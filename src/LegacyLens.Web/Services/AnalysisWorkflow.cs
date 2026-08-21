using LegacyLens.Ai;
using LegacyLens.Analysis;
using LegacyLens.Domain;

namespace LegacyLens.Web.Services;

public enum AnalysisPhase
{
    Parsing,
    Documenting,
    Planning,
    Saving,
    Done
}

/// <summary>Estado observable del análisis, para pintarlo en vivo.</summary>
public sealed record AnalysisProgressState(
    AnalysisPhase Phase,
    string Message,
    int Completed = 0,
    int Total = 0)
{
    public int Percentage => Total == 0 ? 0 : (int)(100.0 * Completed / Total);
}

/// <summary>
/// Orquesta las tres etapas del análisis: primero los hechos, después la
/// interpretación de cada objeto y por último el plan global.
///
/// El orden importa. El plan se genera al final porque su prompt incluye los
/// resúmenes ya producidos en la etapa anterior: razonar sobre el sistema
/// entero funciona mucho mejor sabiendo qué hace cada pieza.
/// </summary>
public sealed class AnalysisWorkflow(
    TSqlAnalyzer analyzer,
    AiEnrichmentService ai,
    AnalysisStore store,
    ILogger<AnalysisWorkflow> logger)
{
    public async Task<AnalysisResult> RunAsync(
        string script,
        string fileName,
        string? ownerUserId,
        Func<AnalysisProgressState, Task> onProgress,
        CancellationToken cancellationToken = default)
    {
        await onProgress(new AnalysisProgressState(AnalysisPhase.Parsing, "Analizando el script..."));

        // Etapa determinista. Es rápida y no puede fallar por causas externas.
        var result = analyzer.Analyze(script, fileName);

        var programmable = result.Objects.Count(o => o.IsProgrammable);
        logger.LogInformation(
            "Analizado {Fichero}: {Objetos} objetos, {Programables} programables, {Dependencias} dependencias",
            fileName, result.ObjectCount, programmable, result.Dependencies.Count);

        if (ai.IsAvailable && programmable > 0)
        {
            var runUsage = new AiRunUsage();

            await onProgress(new AnalysisProgressState(
                AnalysisPhase.Documenting, "Documentando objetos...", 0, programmable));

            var progress = new Progress<AiProgress>(p =>
            {
                // Progress<T> ya vuelve al contexto capturado; el componente se
                // encarga de refrescar la interfaz.
                _ = onProgress(new AnalysisProgressState(
                    AnalysisPhase.Documenting,
                    $"Documentando {p.CurrentObject}",
                    p.Completed,
                    p.Total));
            });

            await ai.DocumentAllAsync(result, progress, runUsage, cancellationToken);

            await onProgress(new AnalysisProgressState(
                AnalysisPhase.Planning, "Generando el plan de migración...", programmable, programmable));

            result.Plan = await ai.BuildPlanAsync(result, runUsage, cancellationToken);

            result.Usage.AddRange(runUsage.Snapshot());
        }
        else if (!ai.IsAvailable)
        {
            logger.LogInformation("IA no disponible: se entrega solo el análisis estático");
        }

        await onProgress(new AnalysisProgressState(AnalysisPhase.Saving, "Guardando..."));
        await store.SaveAsync(result, ownerUserId, cancellationToken);

        await onProgress(new AnalysisProgressState(AnalysisPhase.Done, "Listo"));
        return result;
    }
}
