using System.Text;
using LegacyLens.Domain;

namespace LegacyLens.Evals;

/// <summary>
/// Escribe el informe de evaluación a disco.
///
/// Incluye la salida generada íntegra, no solo las métricas. Una cobertura del cien por
/// cien no significa nada si nadie lee el texto: la comprobación por términos detecta
/// ausencias, no verifica que lo escrito tenga sentido. El informe existe para que esa
/// lectura sea posible y quede registrada junto al número.
/// </summary>
internal static class ReportWriter
{
    public static async Task WriteAsync(
        string path,
        List<(EvalResult Result, AnalysisResult Analysis)> runs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Informe de evaluación");
        sb.AppendLine();
        sb.AppendLine("Generado por `tools/LegacyLens.Evals` sobre `samples/legacy-erp.sql`.");
        sb.AppendLine();
        sb.AppendLine("""
            Este informe mide la parte **no determinista** del sistema. El análisis estático
            se verifica con tests unitarios; la documentación generada por el modelo no se
            puede comprobar con asserts, así que se mide contra un conjunto dorado de reglas
            de negocio que sabemos que están en el código, porque el script de ejemplo se
            escribió para este proyecto.

            La métrica de **objetos inventados** merece una nota. Es comprobable de forma
            automática y sin intervención humana gracias a la decisión de arquitectura
            central: el parser produce el inventario exacto del esquema, así que cualquier
            referencia cualificada que el modelo mencione y no esté en ese inventario es,
            por definición, inventada.
            """);
        sb.AppendLine();

        AppendComparison(sb, runs.Select(r => r.Result).ToList());

        foreach (var (result, analysis) in runs)
        {
            sb.AppendLine($"## Detalle: {result.Model}");
            sb.AppendLine();
            AppendScores(sb, result);
            AppendGeneratedOutput(sb, analysis);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private static void AppendComparison(StringBuilder sb, List<EvalResult> results)
    {
        sb.AppendLine("## Comparativa");
        sb.AppendLine();
        sb.AppendLine("| Modelo | Cobertura de reglas | Objetos inventados | Avisos omitidos | Llamadas | Tokens entrada | Tokens salida | Segundos |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var r in results)
            sb.AppendLine($"| `{r.Model}` | {r.RulesCovered}/{r.RulesExpected} ({r.Coverage:F0} %) | " +
                          $"{r.Hallucinations} | {r.DynamicSqlWarningsMissed} | {r.Calls} | " +
                          $"{r.InputTokens} | {r.OutputTokens} | {r.Elapsed.TotalSeconds:F1} |");

        sb.AppendLine();
        sb.AppendLine("Los tokens son acumulados e incluyen la llamada del plan de migración.");
        sb.AppendLine();
    }

    private static void AppendScores(StringBuilder sb, EvalResult result)
    {
        foreach (var score in result.Scores)
        {
            var mark = score.MissingRules.Length == 0 ? "✔" : "◐";
            sb.AppendLine($"- {mark} `{score.ObjectName}` — {score.RulesCovered}/{score.RulesExpected}");

            foreach (var missing in score.MissingRules)
                sb.AppendLine($"  - No cubierta: {missing}");

            foreach (var invented in score.HallucinatedObjects)
                sb.AppendLine($"  - **Objeto inventado:** `{invented}`");

            if (score.DynamicSqlWarningExpected && !score.DynamicSqlWarningPresent)
                sb.AppendLine("  - **No advierte** de que el SQL dinámico oculta dependencias");
        }

        sb.AppendLine();
    }

    private static void AppendGeneratedOutput(StringBuilder sb, AnalysisResult analysis)
    {
        sb.AppendLine("### Salida generada");
        sb.AppendLine();

        foreach (var obj in analysis.Objects
                     .Where(o => o.Documentation is not null)
                     .OrderByDescending(o => o.Risk.Value))
        {
            var doc = obj.Documentation!;

            sb.AppendLine($"#### `{obj.FullName}` (riesgo {obj.Risk.Value})");
            sb.AppendLine();
            sb.AppendLine(doc.Summary);
            sb.AppendLine();

            if (doc.BusinessRules.Count > 0)
            {
                sb.AppendLine("*Reglas de negocio extraídas:*");
                sb.AppendLine();
                foreach (var rule in doc.BusinessRules) sb.AppendLine($"- {rule}");
                sb.AppendLine();
            }

            if (doc.SideEffects.Count > 0)
            {
                sb.AppendLine("*Efectos colaterales:*");
                sb.AppendLine();
                foreach (var effect in doc.SideEffects) sb.AppendLine($"- {effect}");
                sb.AppendLine();
            }

            sb.AppendLine($"*Destino propuesto:* {doc.MigrationTarget}");
            sb.AppendLine();
        }

        if (analysis.Plan is { } plan)
        {
            sb.AppendLine("### Plan de migración generado");
            sb.AppendLine();
            sb.AppendLine(plan.Overview);
            sb.AppendLine();

            foreach (var phase in plan.Phases.OrderBy(p => p.Order))
            {
                sb.AppendLine($"**Fase {phase.Order} — {phase.Title}**");
                sb.AppendLine();
                sb.AppendLine($"- Por qué ahora: {phase.Rationale}");
                sb.AppendLine($"- Objetos: {string.Join(", ", phase.Objects.Select(o => $"`{o}`"))}");
                sb.AppendLine($"- Riesgo: {phase.Risk}");
                sb.AppendLine();
            }

            if (plan.GlobalRisks.Count > 0)
            {
                sb.AppendLine("*Riesgos globales:*");
                sb.AppendLine();
                foreach (var risk in plan.GlobalRisks) sb.AppendLine($"- {risk}");
                sb.AppendLine();
            }
        }
    }
}
