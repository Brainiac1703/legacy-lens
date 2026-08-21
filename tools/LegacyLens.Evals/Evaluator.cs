using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LegacyLens.Domain;

namespace LegacyLens.Evals;

internal sealed record ObjectScore(
    string ObjectName,
    int RulesCovered,
    int RulesExpected,
    string[] MissingRules,
    string[] HallucinatedObjects,
    bool DynamicSqlWarningExpected,
    bool DynamicSqlWarningPresent)
{
    public bool Documented => RulesExpected == 0 || RulesCovered > 0 || MissingRules.Length < RulesExpected;
}

internal sealed record EvalResult(
    string Model,
    ObjectScore[] Scores,
    int UndocumentedObjects,
    long InputTokens,
    long OutputTokens,
    int Calls,
    TimeSpan Elapsed)
{
    public int RulesCovered => Scores.Sum(s => s.RulesCovered);
    public int RulesExpected => Scores.Sum(s => s.RulesExpected);

    public double Coverage => RulesExpected == 0 ? 0 : 100.0 * RulesCovered / RulesExpected;

    public int Hallucinations => Scores.Sum(s => s.HallucinatedObjects.Length);

    public int DynamicSqlWarningsMissed =>
        Scores.Count(s => s.DynamicSqlWarningExpected && !s.DynamicSqlWarningPresent);
}

/// <summary>
/// Compara la documentación generada contra el conjunto dorado.
///
/// La comprobación de alucinación es la parte más valiosa y solo es posible por la decisión
/// de arquitectura del proyecto: como el parser produce el inventario exacto del esquema,
/// cualquier objeto cualificado que el modelo mencione y que no esté en ese inventario es,
/// por definición, inventado. No hace falta juicio humano ni otro modelo para detectarlo.
/// </summary>
internal static class Evaluator
{
    /// <summary>Referencias tipo <c>esquema.objeto</c> en el texto generado.</summary>
    private static readonly Regex QualifiedName = new(
        @"\b([a-zA-Z_][a-zA-Z0-9_]*)\.([a-zA-Z_][a-zA-Z0-9_]{2,})\b",
        RegexOptions.Compiled);

    private static readonly string[] DynamicSqlHints =
    [
        "dinamic", "dinámic", "tiempo de ejecucion", "tiempo de ejecución",
        "no se pueden conocer", "no pueden conocerse", "incomplet", "sin ejecutar"
    ];

    public static EvalResult Evaluate(
        AnalysisResult analysis,
        string model,
        long inputTokens,
        long outputTokens,
        int calls,
        TimeSpan elapsed)
    {
        // Inventario real: la fuente de verdad contra la que se mide la alucinación.
        var known = new HashSet<string>(
            analysis.Objects.Select(o => o.FullName),
            StringComparer.OrdinalIgnoreCase);

        var scores = new List<ObjectScore>();

        foreach (var expectation in GoldenSet.Expectations)
        {
            var obj = analysis.Objects
                .FirstOrDefault(o => o.FullName.Equals(expectation.ObjectName, StringComparison.OrdinalIgnoreCase));

            if (obj?.Documentation is not { } doc)
            {
                scores.Add(new ObjectScore(
                    expectation.ObjectName, 0, expectation.Rules.Length,
                    expectation.Rules.Select(r => r.Description).ToArray(),
                    [], expectation.MustWarnAboutDynamicSql, false));
                continue;
            }

            var text = Normalize(Flatten(doc));

            var missing = expectation.Rules
                .Where(rule => !rule.AnyOf.Any(term => text.Contains(Normalize(term))))
                .Select(rule => rule.Description)
                .ToArray();

            scores.Add(new ObjectScore(
                ObjectName: expectation.ObjectName,
                RulesCovered: expectation.Rules.Length - missing.Length,
                RulesExpected: expectation.Rules.Length,
                MissingRules: missing,
                HallucinatedObjects: FindHallucinations(Flatten(doc), known),
                DynamicSqlWarningExpected: expectation.MustWarnAboutDynamicSql,
                DynamicSqlWarningPresent: DynamicSqlHints.Any(h => text.Contains(Normalize(h)))));
        }

        var undocumented = analysis.Objects.Count(o => o.IsProgrammable && o.Documentation is null);

        return new EvalResult(model, [.. scores], undocumented, inputTokens, outputTokens, calls, elapsed);
    }

    private static string[] FindHallucinations(string text, HashSet<string> known) =>
        QualifiedName.Matches(text)
            .Select(m => m.Value)
            // Solo interesan las referencias que parecen objetos de base de datos.
            .Where(name => LooksLikeDatabaseObject(name))
            .Where(name => !known.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool LooksLikeDatabaseObject(string qualified)
    {
        var parts = qualified.Split('.');
        if (parts.Length != 2) return false;

        // El esquema en el script de ejemplo es dbo. Cualquier otro prefijo suele ser
        // prosa ("apartado.siguiente") o una referencia a .NET, no un objeto SQL.
        return parts[0].Equals("dbo", StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(ObjectDocumentation doc) => string.Join(
        " \n ",
        [doc.Summary, doc.MigrationTarget, .. doc.BusinessRules, .. doc.SideEffects]);

    /// <summary>
    /// Compara sin acentos ni mayúsculas: el modelo escribe «antigüedad» o «antiguedad»
    /// según le convenga, y eso no debe alterar la medida.
    /// </summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
