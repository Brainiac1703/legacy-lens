using System.Text;
using LegacyLens.Domain;

namespace LegacyLens.Ai;

/// <summary>
/// Construcción de los prompts.
///
/// El principio que sostiene todo el proyecto está aquí: al modelo se le
/// entregan los hechos que el parser ya ha verificado (qué tablas toca, a quién
/// llama, qué construcciones usa) y se le pide explícitamente que no invente
/// dependencias. Su trabajo es interpretar intención y proponer diseño, no
/// deducir estructura que ya conocemos con certeza.
/// </summary>
internal static class Prompts
{
    public const string DocumentationSystem = """
        Eres un arquitecto de software especializado en modernizar sistemas
        heredados de SQL Server hacia .NET.

        Recibirás el código de un objeto de base de datos junto con un análisis
        estático ya verificado de ese objeto. El análisis estático es la verdad:
        se ha obtenido del árbol sintáctico real del SQL.

        Reglas estrictas:
        - No inventes tablas, procedimientos ni columnas que no aparezcan en el
          código o en los hechos verificados.
        - Si el código construye SQL dinámico, di explícitamente que hay
          dependencias que no pueden conocerse sin ejecutarlo.
        - Las reglas de negocio deben ser afirmaciones concretas y verificables
          en el código, no generalidades.
        - Si algo no se puede determinar, dilo en lugar de rellenar.

        Escribe en español, en lenguaje de negocio comprensible para alguien que
        no sabe leer T-SQL.
        """;

    public const string PlanningSystem = """
        Eres un arquitecto de software que planifica la migración de la lógica
        de negocio alojada en una base de datos SQL Server hacia una aplicación
        .NET moderna.

        Recibirás el inventario completo de objetos con su riesgo calculado y el
        grafo de dependencias real.

        Aplica el patrón strangler fig: se migra primero lo autocontenido y de
        bajo riesgo, y se dejan para el final los objetos de los que dependen
        muchos otros. Nunca propongas migrar un objeto antes que sus propias
        dependencias.

        Sé concreto y honesto sobre los riesgos. Escribe en español.
        """;

    /// <summary>Prompt de documentación de un objeto, con sus hechos verificados.</summary>
    public static string ForObject(SqlObject obj, AnalysisResult result, int maxBodyChars)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Objeto: {obj.FullName} ({obj.Kind})");
        sb.AppendLine();
        sb.AppendLine("## Hechos verificados por análisis estático");
        sb.AppendLine();

        var reads = Edges(result, obj, DependencyKind.Reads);
        var writes = Edges(result, obj, DependencyKind.Writes);
        var calls = Edges(result, obj, DependencyKind.Calls);

        sb.AppendLine($"- Tablas que lee: {Format(reads)}");
        sb.AppendLine($"- Tablas en las que escribe: {Format(writes)}");
        sb.AppendLine($"- Objetos que invoca: {Format(calls)}");

        var m = obj.Metrics;
        sb.AppendLine($"- Líneas: {m.Lines}, sentencias: {m.StatementCount}");
        sb.AppendLine($"- Cursores: {m.CursorCount}, SQL dinámico: {m.DynamicSqlCount}");
        sb.AppendLine($"- Transacciones explícitas: {m.TransactionCount}, TRY/CATCH: {(m.HasErrorHandling ? "sí" : "no")}");
        sb.AppendLine($"- Tablas temporales: {m.TempTableCount}, complejidad de control: {m.ControlFlowComplexity}");

        if (obj.Risk.Factors.Count > 0)
        {
            sb.AppendLine($"- Riesgo calculado: {obj.Risk.Value}/100 ({obj.Risk.Level}) por estos motivos:");
            foreach (var f in obj.Risk.Factors)
                sb.AppendLine($"    - {f.Description}");
        }

        if (m.DynamicSqlCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine("> Atención: este objeto construye SQL en tiempo de ejecución, así que la");
            sb.AppendLine("> lista de tablas de arriba está incompleta por definición. Menciónalo.");
        }

        sb.AppendLine();
        sb.AppendLine("## Código fuente");
        sb.AppendLine();
        sb.AppendLine("```sql");
        sb.AppendLine(Truncate(obj.Body, maxBodyChars));
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("""
            ## Lo que tienes que producir

            - summary: qué hace este objeto, en dos o tres frases de negocio.
            - businessRules: reglas de negocio concretas que impone el código.
              Cada una verificable leyendo el SQL. Lista vacía si no hay ninguna.
            - sideEffects: efectos colaterales relevantes (qué datos modifica, qué
              puede quedar inconsistente si falla a mitad).
            - migrationTarget: a qué debería convertirse en .NET y por qué.
              Sé específico: comando, consulta, servicio de dominio, trabajo en
              segundo plano, y qué habría que sacar de la base de datos.
            """);

        return sb.ToString();
    }

    /// <summary>Prompt del plan global, con el inventario y el grafo completos.</summary>
    public static string ForPlan(AnalysisResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Inventario de {result.SourceFileName}");
        sb.AppendLine();
        sb.AppendLine($"Objetos totales: {result.ObjectCount}");
        sb.AppendLine();

        sb.AppendLine("## Objetos programables, de mayor a menor riesgo");
        sb.AppendLine();
        foreach (var o in result.Objects.Where(o => o.IsProgrammable)
                                        .OrderByDescending(o => o.Risk.Value))
        {
            sb.AppendLine($"### {o.FullName} ({o.Kind}) - riesgo {o.Risk.Value}/100 ({o.Risk.Level})");
            sb.AppendLine($"- {o.Metrics.Lines} líneas, lee {o.Metrics.TablesRead} tabla(s), " +
                          $"escribe {o.Metrics.TablesWritten}, invoca {o.Metrics.ObjectsCalled}");
            foreach (var f in o.Risk.Factors)
                sb.AppendLine($"- {f.Description}");

            if (o.Documentation is not null)
                sb.AppendLine($"- Función: {o.Documentation.Summary}");

            sb.AppendLine();
        }

        sb.AppendLine("## Grafo de llamadas entre objetos programables");
        sb.AppendLine();
        var calls = result.Dependencies.Where(d => d.Kind == DependencyKind.Calls).ToList();
        if (calls.Count == 0)
            sb.AppendLine("(ningún objeto llama a otro)");
        else
            foreach (var c in calls)
                sb.AppendLine($"- {c.From} invoca {c.To}");

        sb.AppendLine();
        sb.AppendLine("## Posición en el grafo");
        sb.AppendLine();
        sb.AppendLine($"- Autocontenidos (no invocan nada): {Format(result.Leaves.Select(o => o.FullName))}");
        sb.AppendLine($"- Puntos de entrada (nadie los invoca): {Format(result.EntryPoints.Select(o => o.FullName))}");
        sb.AppendLine($"- Más referenciados: {Format(result.Hubs.Select(h => $"{h.Object.FullName} ({h.Referrers})"))}");

        sb.AppendLine();
        sb.AppendLine("""
            ## Lo que tienes que producir

            - overview: diagnóstico general del sistema en un párrafo. Qué tipo de
              lógica vive en la base de datos y cuál es la dificultad principal.
            - phases: fases de migración ordenadas. Para cada una, un título, la
              razón por la que va en ese momento, la lista de objetos por su
              nombre completo y el riesgo concreto de esa fase.
              Entre tres y cinco fases. Todo objeto programable debe aparecer en
              exactamente una fase.
            - globalRisks: riesgos que afectan al proyecto entero, no a una fase.
            """);

        return sb.ToString();
    }

    private static IEnumerable<string> Edges(AnalysisResult result, SqlObject obj, DependencyKind kind) =>
        result.Dependencies.Where(d => d.From == obj.FullName && d.Kind == kind).Select(d => d.To);

    private static string Format(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "(ninguna)" : string.Join(", ", list);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n-- [recortado por longitud]";
}
