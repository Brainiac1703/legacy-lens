using System.Diagnostics;
using LegacyLens.Ai;
using LegacyLens.Analysis;
using LegacyLens.Domain;
using LegacyLens.Application.Abstractions;
using LegacyLens.Application.Costing;
using LegacyLens.Evals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------------------
// Arnés de evaluación.
//
// Ejecuta el análisis completo sobre el script de ejemplo y mide la calidad de
// la parte no determinista: cuántas de las reglas de negocio que sabemos que
// están en el código aparecen en la documentación generada, y si el modelo
// menciona objetos que no existen.
//
// Uso:
//   dotnet run --project tools/LegacyLens.Evals
//   dotnet run --project tools/LegacyLens.Evals -- --models gpt-4.1-mini,gpt-4o
//
// Configuración por variables de entorno: Ai__Endpoint y, opcionalmente,
// Ai__ApiKey. Sin clave se usa la identidad de la sesión de az login.
// ---------------------------------------------------------------------------

var models = ParseModels(args);

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var scriptPath = FindSampleScript();
var script = await File.ReadAllTextAsync(scriptPath);

Console.WriteLine($"Script: {scriptPath}");
Console.WriteLine();

var runs = new List<(EvalResult Result, AnalysisResult Analysis)>();
var results = new List<EvalResult>();

foreach (var model in models)
{
    Console.WriteLine($"── Evaluando con {model} ".PadRight(70, '─'));

    var services = new ServiceCollection();

    services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true)
                              .SetMinimumLevel(LogLevel.Warning));

    services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

    // El modelo de documentación se sobrescribe por ejecución para poder comparar.
    services.PostConfigure<AiOptions>(o => o.DocumentationDeployment = model);

    services.AddSingleton<IAiEnrichmentService, AiEnrichmentService>();

    await using var provider = services.BuildServiceProvider();

    var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

    if (!options.IsConfigured)
    {
        Console.Error.WriteLine(
            "Falta Ai__Endpoint. Configúralo con:\n" +
            "  export Ai__Endpoint=$(cd infra && terraform output -raw openai_endpoint)");
        return 1;
    }

    var ai = provider.GetRequiredService<IAiEnrichmentService>();

    // El consumo se acumula por ejecucion, no en un singleton global: cada
    // modelo evaluado tiene que dar su propia cifra.
    var usage = new ModelUsageCollector();

    var analysis = new TSqlAnalyzer().Analyze(script, Path.GetFileName(scriptPath));

    if (analysis.ParseErrors.Count > 0)
    {
        Console.Error.WriteLine($"El script tiene {analysis.ParseErrors.Count} error(es) de parseo.");
        foreach (var error in analysis.ParseErrors) Console.Error.WriteLine($"  {error}");
        return 1;
    }

    var stopwatch = Stopwatch.StartNew();

    var progress = new Progress<AiProgress>(p =>
        Console.Write($"\r  documentando {p.Completed}/{p.Total}...   "));

    await ai.DocumentAllAsync(analysis, progress, usage);
    Console.Write("\r  generando el plan...                \r");

    analysis.Plan = await ai.BuildPlanAsync(analysis, usage);
    stopwatch.Stop();

    Console.WriteLine("  completado.                          ");
    Console.WriteLine();

    var snapshot = usage.Snapshot();
    analysis.Usage.AddRange(snapshot);

    var result = Evaluator.Evaluate(
        analysis,
        model,
        snapshot.Sum(u => u.InputTokens),
        snapshot.Sum(u => u.OutputTokens),
        snapshot.Sum(u => u.Calls),
        stopwatch.Elapsed);

    results.Add(result);
    runs.Add((result, analysis));
    PrintDetail(result, analysis.Plan is not null);
}

PrintComparison(results);

var outPath = ParseOut(args);
if (outPath is not null)
{
    await ReportWriter.WriteAsync(outPath, runs);
    Console.WriteLine($"Informe escrito en {outPath}");
}

return 0;

// ---------------------------------------------------------------------------

static string[] ParseModels(string[] args)
{
    var index = Array.IndexOf(args, "--models");

    return index >= 0 && index + 1 < args.Length
        ? args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : ["gpt-4.1-mini"];
}

static string? ParseOut(string[] args)
{
    var index = Array.IndexOf(args, "--out");
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string FindSampleScript()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);

    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
        dir = dir.Parent;

    if (dir is null) throw new InvalidOperationException("No se encontró el directorio samples.");

    return Path.Combine(dir.FullName, "samples", "legacy-erp.sql");
}

static void PrintDetail(EvalResult result, bool planGenerated)
{
    Console.WriteLine($"  Cobertura de reglas    {result.RulesCovered}/{result.RulesExpected} " +
                      $"({result.Coverage:F0} %)");
    Console.WriteLine($"  Objetos sin documentar {result.UndocumentedObjects}");
    Console.WriteLine($"  Objetos inventados     {result.Hallucinations}");
    Console.WriteLine($"  Avisos de SQL dinámico omitidos  {result.DynamicSqlWarningsMissed}");
    Console.WriteLine($"  Plan de migración      {(planGenerated ? "generado" : "NO generado")}");
    Console.WriteLine();

    foreach (var score in result.Scores)
    {
        var mark = score.MissingRules.Length == 0 ? "✔" : "◐";
        Console.WriteLine($"  {mark} {score.ObjectName}  {score.RulesCovered}/{score.RulesExpected}");

        foreach (var missing in score.MissingRules)
            Console.WriteLine($"      falta: {missing}");

        foreach (var invented in score.HallucinatedObjects)
            Console.WriteLine($"      INVENTADO: {invented}");

        if (score.DynamicSqlWarningExpected && !score.DynamicSqlWarningPresent)
            Console.WriteLine("      no advierte de que el SQL dinámico oculta dependencias");
    }

    Console.WriteLine();
}

static void PrintComparison(List<EvalResult> results)
{
    if (results.Count == 0) return;

    Console.WriteLine();
    Console.WriteLine("═══ Comparativa ".PadRight(70, '═'));
    Console.WriteLine();
    Console.WriteLine($"{"Modelo",-18} {"Cobertura",10} {"Inventados",11} {"Llamadas",9} " +
                      $"{"Tok. ent.",10} {"Tok. sal.",10} {"Segundos",9}");
    Console.WriteLine(new string('─', 82));

    foreach (var r in results)
        Console.WriteLine($"{r.Model,-18} {r.Coverage,9:F0}% {r.Hallucinations,11} {r.Calls,9} " +
                          $"{r.InputTokens,10} {r.OutputTokens,10} {r.Elapsed.TotalSeconds,9:F1}");

    Console.WriteLine();
    Console.WriteLine("Los tokens son acumulados e incluyen la llamada del plan de migración.");
    Console.WriteLine("El coste se calcula a partir de ellos con los precios vigentes del despliegue.");
}
