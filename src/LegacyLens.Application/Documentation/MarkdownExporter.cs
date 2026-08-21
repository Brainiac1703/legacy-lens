using System.Text;
using LegacyLens.Domain;

namespace LegacyLens.Application.Documentation;

/// <summary>
/// Genera el paquete de documentación en Markdown.
///
/// El objetivo es que el resultado sea entregable: algo que se pueda meter en
/// el repositorio del cliente y leerse sin la aplicación delante. Por eso se
/// separa siempre lo verificado de lo interpretado, y cada afirmación del
/// modelo va acompañada del modelo que la produjo.
/// </summary>
public static class MarkdownExporter
{
    public static string Export(AnalysisResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Análisis de `{result.SourceFileName}`");
        sb.AppendLine();
        sb.AppendLine($"Generado el {result.CreatedAt:dd/MM/yyyy HH:mm} UTC por Legacy Lens.");
        sb.AppendLine();

        AppendSummary(sb, result);
        AppendPlan(sb, result);
        AppendCallGraph(sb, result);
        AppendObjects(sb, result);
        AppendMethodology(sb);

        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, AnalysisResult result)
    {
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine("| Concepto | Valor |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Objetos totales | {result.ObjectCount} |");

        foreach (var group in result.Objects.GroupBy(o => o.Kind).OrderBy(g => g.Key.ToString()))
            sb.AppendLine($"| {group.Key} | {group.Count()} |");

        sb.AppendLine($"| Dependencias detectadas | {result.Dependencies.Count} |");

        var programmable = result.Objects.Where(o => o.IsProgrammable).ToList();
        if (programmable.Count > 0)
        {
            var critical = programmable.Count(o => o.Risk.Level is RiskLevel.High or RiskLevel.Critical);
            sb.AppendLine($"| Objetos de riesgo alto o crítico | {critical} de {programmable.Count} |");
        }

        if (result.Usage.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Consumo");
            sb.AppendLine();
            sb.AppendLine("| Modelo | Llamadas | Tokens entrada | Tokens salida |");
            sb.AppendLine("| --- | --- | --- | --- |");

            foreach (var usage in result.Usage)
                sb.AppendLine($"| `{usage.Model}` | {usage.Calls} | " +
                              $"{usage.InputTokens:N0} | {usage.OutputTokens:N0} |");
        }

        if (result.ParseErrors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"> El script contiene {result.ParseErrors.Count} error(es) de sintaxis. Los objetos");
            sb.AppendLine("> afectados no han podido analizarse:");
            sb.AppendLine();
            foreach (var error in result.ParseErrors)
                sb.AppendLine($"> - {error}");
        }

        sb.AppendLine();
    }

    private static void AppendPlan(StringBuilder sb, AnalysisResult result)
    {
        if (result.Plan is null) return;

        var plan = result.Plan;

        sb.AppendLine("## Plan de migración propuesto");
        sb.AppendLine();
        sb.AppendLine(plan.Overview);
        sb.AppendLine();

        foreach (var phase in plan.Phases.OrderBy(p => p.Order))
        {
            sb.AppendLine($"### Fase {phase.Order}: {phase.Title}");
            sb.AppendLine();
            sb.AppendLine($"**Por qué en este momento:** {phase.Rationale}");
            sb.AppendLine();

            if (phase.Objects.Count > 0)
            {
                sb.AppendLine("Objetos:");
                sb.AppendLine();
                foreach (var obj in phase.Objects)
                    sb.AppendLine($"- `{obj}`");
                sb.AppendLine();
            }

            sb.AppendLine($"**Riesgo:** {phase.Risk}");
            sb.AppendLine();
        }

        if (plan.GlobalRisks.Count > 0)
        {
            sb.AppendLine("### Riesgos que afectan a todo el proyecto");
            sb.AppendLine();
            foreach (var risk in plan.GlobalRisks)
                sb.AppendLine($"- {risk}");
            sb.AppendLine();
        }

        sb.AppendLine($"_Plan generado con {plan.ModelUsed}._");
        sb.AppendLine();
    }

    private static void AppendCallGraph(StringBuilder sb, AnalysisResult result)
    {
        sb.AppendLine("## Grafo de dependencias");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(MermaidGraphBuilder.BuildCallGraph(result).TrimEnd());
        sb.AppendLine("```");
        sb.AppendLine();
    }

    private static void AppendObjects(StringBuilder sb, AnalysisResult result)
    {
        sb.AppendLine("## Objetos");
        sb.AppendLine();

        var ordered = result.Objects
            .Where(o => o.IsProgrammable)
            .OrderByDescending(o => o.Risk.Value)
            .ThenBy(o => o.FullName);

        foreach (var obj in ordered)
        {
            sb.AppendLine($"### `{obj.FullName}`");
            sb.AppendLine();
            sb.AppendLine($"**{obj.Kind}** · riesgo **{obj.Risk.Value}/100** ({obj.Risk.Level})");
            sb.AppendLine();

            if (obj.Documentation is { } doc)
            {
                sb.AppendLine(doc.Summary);
                sb.AppendLine();

                if (doc.BusinessRules.Count > 0)
                {
                    sb.AppendLine("**Reglas de negocio**");
                    sb.AppendLine();
                    foreach (var rule in doc.BusinessRules)
                        sb.AppendLine($"- {rule}");
                    sb.AppendLine();
                }

                if (doc.SideEffects.Count > 0)
                {
                    sb.AppendLine("**Efectos colaterales**");
                    sb.AppendLine();
                    foreach (var effect in doc.SideEffects)
                        sb.AppendLine($"- {effect}");
                    sb.AppendLine();
                }

                sb.AppendLine($"**Destino propuesto en .NET:** {doc.MigrationTarget}");
                sb.AppendLine();
                sb.AppendLine($"_Interpretación generada con {doc.ModelUsed}._");
                sb.AppendLine();
            }

            AppendVerifiedFacts(sb, obj, result);
        }
    }

    private static void AppendVerifiedFacts(StringBuilder sb, SqlObject obj, AnalysisResult result)
    {
        sb.AppendLine("**Hechos verificados por análisis estático**");
        sb.AppendLine();

        var m = obj.Metrics;
        sb.AppendLine($"- {m.Lines} líneas, {m.StatementCount} sentencias, complejidad de control {m.ControlFlowComplexity}");
        sb.AppendLine($"- Cursores: {m.CursorCount} · SQL dinámico: {m.DynamicSqlCount} · " +
                      $"Transacciones: {m.TransactionCount} · TRY/CATCH: {(m.HasErrorHandling ? "sí" : "no")}");

        AppendEdges(sb, result, obj, DependencyKind.Reads, "Lee");
        AppendEdges(sb, result, obj, DependencyKind.Writes, "Escribe en");
        AppendEdges(sb, result, obj, DependencyKind.Calls, "Invoca");

        if (obj.Risk.Factors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Desglose del riesgo**");
            sb.AppendLine();
            foreach (var factor in obj.Risk.Factors)
                sb.AppendLine($"- `+{factor.Points}` **{factor.Code}** — {factor.Description}");
        }

        sb.AppendLine();
    }

    private static void AppendEdges(
        StringBuilder sb, AnalysisResult result, SqlObject obj, DependencyKind kind, string label)
    {
        var targets = result.Dependencies
            .Where(d => d.From == obj.FullName && d.Kind == kind)
            .Select(d => $"`{d.To}`")
            .ToList();

        if (targets.Count > 0)
            sb.AppendLine($"- {label}: {string.Join(", ", targets)}");
    }

    private static void AppendMethodology(StringBuilder sb)
    {
        sb.AppendLine("## Cómo leer este documento");
        sb.AppendLine();
        sb.AppendLine("""
            Este informe combina dos fuentes de naturaleza distinta, y conviene no
            confundirlas:

            - Los **hechos verificados** salen del árbol sintáctico real del T-SQL,
              obtenido con el parser oficial de Microsoft. Son exactos y
              reproducibles: dos ejecuciones sobre el mismo script dan lo mismo.
            - Los **resúmenes, reglas de negocio y propuestas de migración** los
              genera un modelo de lenguaje a partir de esos hechos. Son una
              interpretación fundamentada, no una verdad demostrada, y hay que
              revisarlos antes de tomar decisiones a partir de ellos.

            Donde un objeto construya SQL dinámico, la lista de dependencias está
            incompleta por definición: esas referencias solo existen en tiempo de
            ejecución y ningún análisis estático puede verlas.
            """);
        sb.AppendLine();
    }
}
