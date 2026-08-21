using LegacyLens.Domain;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace LegacyLens.Analysis;

/// <summary>
/// Recorre el árbol sintáctico de un objeto programable y acumula todo lo que
/// se puede afirmar con certeza: qué tablas toca, a quién llama y qué
/// construcciones problemáticas usa.
/// </summary>
internal sealed class ObjectAnalysisVisitor : TSqlFragmentVisitor
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public HashSet<string> TablesWritten { get; } = new(NameComparer);
    public HashSet<string> TablesReferenced { get; } = new(NameComparer);
    public HashSet<string> ProceduresCalled { get; } = new(NameComparer);
    public HashSet<string> FunctionsCalled { get; } = new(NameComparer);

    /// <summary>
    /// Todo objeto programable invocado, sea con EXEC o dentro de una expresión.
    /// Las funciones escalares son dependencias igual de reales que los
    /// procedimientos, y condicionan el orden de migración lo mismo.
    /// </summary>
    public IEnumerable<string> CalledObjects => ProceduresCalled.Concat(FunctionsCalled);

    /// <summary>Tablas temporales distintas, no número de veces que se usan.</summary>
    private readonly HashSet<string> _tempTables = new(NameComparer);

    public int StatementCount { get; private set; }
    public int CursorCount { get; private set; }
    public int DynamicSqlCount { get; private set; }
    public int TransactionCount { get; private set; }
    public bool HasErrorHandling { get; private set; }
    public int ControlFlowNodes { get; private set; }

    public int TempTableCount => _tempTables.Count;

    /// <summary>Tablas leídas: las referenciadas que no son destino de escritura.</summary>
    public IEnumerable<string> TablesRead => TablesReferenced.Except(TablesWritten, NameComparer);

    public override void Visit(TSqlStatement node) => StatementCount++;

    public override void Visit(NamedTableReference node)
    {
        var name = NameResolver.Resolve(node.SchemaObject);
        if (name is null) return;

        // Las tablas temporales no son objetos del esquema: se cuentan como
        // señal de complejidad, pero no entran en el grafo de dependencias.
        if (NameResolver.IsTemporary(node.SchemaObject))
        {
            _tempTables.Add(name);
            return;
        }

        // inserted/deleted dentro de un disparador no son tablas reales.
        if (NameResolver.IsPseudoTable(node.SchemaObject)) return;

        TablesReferenced.Add(name);
    }

    public override void Visit(InsertSpecification node) => RegisterWrite(node.Target);

    public override void Visit(UpdateSpecification node) => RegisterWrite(node.Target);

    public override void Visit(DeleteSpecification node) => RegisterWrite(node.Target);

    public override void Visit(MergeSpecification node) => RegisterWrite(node.Target);

    public override void Visit(SelectStatement node)
    {
        // SELECT ... INTO nueva_tabla también es una escritura.
        if (node.Into is not null)
        {
            var name = NameResolver.Resolve(node.Into);
            if (name is not null && !NameResolver.IsTemporary(node.Into))
                TablesWritten.Add(name);
        }
    }

    public override void Visit(DeclareCursorStatement node) => CursorCount++;

    public override void Visit(BeginTransactionStatement node) => TransactionCount++;

    public override void Visit(TryCatchStatement node) => HasErrorHandling = true;

    public override void Visit(IfStatement node) => ControlFlowNodes++;

    public override void Visit(WhileStatement node) => ControlFlowNodes++;

    public override void Visit(SearchedCaseExpression node) => ControlFlowNodes++;

    public override void Visit(SimpleCaseExpression node) => ControlFlowNodes++;

    public override void Visit(FunctionCall node)
    {
        // Solo las llamadas cualificadas con esquema pueden ser funciones de
        // usuario. GETDATE(), COUNT() y compañía no llevan CallTarget, así que
        // esta comprobación descarta por sí sola todo lo integrado en el motor.
        if (node.CallTarget is not MultiPartIdentifierCallTarget target) return;

        var schema = target.MultiPartIdentifier?.Identifiers?.LastOrDefault()?.Value;
        var name = node.FunctionName?.Value;

        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name)) return;

        var qualified = $"{schema}.{name}";
        if (!NameResolver.IsSystemObject(qualified))
            FunctionsCalled.Add(qualified);
    }

    public override void Visit(ExecuteStatement node)
    {
        switch (node.ExecuteSpecification?.ExecutableEntity)
        {
            // EXEC ('SELECT ...') o EXEC (@sql): SQL construido en tiempo de ejecución.
            case ExecutableStringList:
                DynamicSqlCount++;
                break;

            case ExecutableProcedureReference procRef:
                var name = NameResolver.Resolve(procRef.ProcedureReference?.ProcedureReference?.Name);
                if (name is null) break;

                // sp_executesql es SQL dinámico disfrazado de llamada a procedimiento.
                if (name.EndsWith("sp_executesql", StringComparison.OrdinalIgnoreCase))
                    DynamicSqlCount++;
                else if (!NameResolver.IsSystemObject(name))
                    ProceduresCalled.Add(name);
                break;
        }
    }

    private void RegisterWrite(TableReference? target)
    {
        if (target is not NamedTableReference named) return;

        var name = NameResolver.Resolve(named.SchemaObject);
        if (name is null) return;

        if (NameResolver.IsTemporary(named.SchemaObject))
        {
            _tempTables.Add(name);
            return;
        }

        if (NameResolver.IsPseudoTable(named.SchemaObject)) return;

        TablesWritten.Add(name);
    }
}
