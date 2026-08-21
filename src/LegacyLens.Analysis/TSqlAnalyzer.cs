using System.Text;
using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace LegacyLens.Analysis;

/// <summary>
/// Analiza un script T-SQL usando el parser oficial de Microsoft.
///
/// Todo lo que produce esta clase es determinista: sale del árbol sintáctico
/// real del SQL, no de una interpretación. Es la base de hechos verificados
/// sobre la que después trabaja el modelo de lenguaje.
/// </summary>
public sealed class TSqlAnalyzer : ITSqlAnalyzer
{
    /// <summary>Analiza un script completo y devuelve el inventario con su grafo.</summary>
    public AnalysisResult Analyze(string script, string sourceFileName)
    {
        var result = new AnalysisResult { SourceFileName = sourceFileName };

        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(script);
        var fragment = parser.Parse(reader, out var errors);

        foreach (var error in errors)
            result.ParseErrors.Add($"Línea {error.Line}: {error.Message}");

        if (fragment is not TSqlScript tsqlScript)
            return result;

        foreach (var statement in tsqlScript.Batches.SelectMany(b => b.Statements))
            AddObject(result, statement);

        return result;
    }

    private void AddObject(AnalysisResult result, TSqlStatement statement)
    {
        var (name, kind) = Identify(statement);
        if (name is null || kind is null) return;

        var parts = name.Split('.', 2);
        var sqlObject = new SqlObject
        {
            Schema = parts[0],
            Name = parts[1],
            Kind = kind.Value,
            Body = GetSourceText(statement)
        };

        // Las tablas no tienen cuerpo ejecutable que analizar.
        if (kind is SqlObjectKind.Table)
        {
            sqlObject.Metrics = CodeMetrics.Empty with { Lines = CountLines(sqlObject.Body) };
            result.Objects.Add(sqlObject);
            return;
        }

        var visitor = new ObjectAnalysisVisitor();
        statement.Accept(visitor);

        var tablesRead = visitor.TablesRead.ToList();

        sqlObject.Metrics = new CodeMetrics(
            Lines: CountLines(sqlObject.Body),
            StatementCount: visitor.StatementCount,
            CursorCount: visitor.CursorCount,
            DynamicSqlCount: visitor.DynamicSqlCount,
            TransactionCount: visitor.TransactionCount,
            TempTableCount: visitor.TempTableCount,
            HasErrorHandling: visitor.HasErrorHandling,
            ControlFlowComplexity: visitor.ControlFlowNodes,
            TablesRead: tablesRead.Count,
            TablesWritten: visitor.TablesWritten.Count,
            ObjectsCalled: visitor.CalledObjects.Count());

        sqlObject.Risk = RiskScorer.Score(sqlObject.Metrics);
        result.Objects.Add(sqlObject);

        var from = sqlObject.FullName;
        foreach (var table in tablesRead)
            result.Dependencies.Add(new Dependency(from, table, DependencyKind.Reads));
        foreach (var table in visitor.TablesWritten)
            result.Dependencies.Add(new Dependency(from, table, DependencyKind.Writes));
        foreach (var called in visitor.CalledObjects)
            result.Dependencies.Add(new Dependency(from, called, DependencyKind.Calls));
    }

    /// <summary>Determina qué objeto define una sentencia, si define alguno.</summary>
    private static (string? Name, SqlObjectKind? Kind) Identify(TSqlStatement statement) => statement switch
    {
        // Los tipos base cubren CREATE, ALTER y CREATE OR ALTER en los cuatro casos.
        ProcedureStatementBody p => (NameResolver.Resolve(p.ProcedureReference?.Name), SqlObjectKind.Procedure),
        FunctionStatementBody f => (NameResolver.Resolve(f.Name), SqlObjectKind.Function),
        ViewStatementBody v => (NameResolver.Resolve(v.SchemaObjectName), SqlObjectKind.View),
        TriggerStatementBody t => (NameResolver.Resolve(t.Name), SqlObjectKind.Trigger),
        CreateTableStatement c => (NameResolver.Resolve(c.SchemaObjectName), SqlObjectKind.Table),
        _ => (null, null)
    };

    /// <summary>
    /// Recupera el texto original de un nodo desde el flujo de tokens, para
    /// mostrar y documentar el código tal como lo escribieron.
    /// </summary>
    private static string GetSourceText(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null) return string.Empty;

        var builder = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
            builder.Append(fragment.ScriptTokenStream[i].Text);

        return builder.ToString().Trim();
    }

    private static int CountLines(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
}
