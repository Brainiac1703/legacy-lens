using LegacyLens.Domain;

namespace LegacyLens.Application.Abstractions;

/// <summary>Avance de la fase de interpretación.</summary>
public sealed record AiProgress(int Completed, int Total, string CurrentObject);

/// <summary>
/// Interpretación de un análisis con un modelo de lenguaje.
///
/// Es la única dependencia del sistema que puede no estar disponible, y el
/// diseño lo asume: <see cref="IsAvailable"/> permite que los casos de uso
/// entreguen el análisis estático cuando no hay IA configurada, en lugar de
/// fallar.
/// </summary>
public interface IAiEnrichmentService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Documenta los objetos programables del análisis, modificándolo en sitio.
    /// Un objeto que falle queda sin documentar sin interrumpir el resto.
    /// </summary>
    Task DocumentAllAsync(
        AnalysisResult result,
        IProgress<AiProgress>? progress = null,
        IModelUsageCollector? usage = null,
        CancellationToken cancellationToken = default);

    /// <summary>Genera el plan de migración. Devuelve nulo si no se pudo generar.</summary>
    Task<MigrationPlan?> BuildPlanAsync(
        AnalysisResult result,
        IModelUsageCollector? usage = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Acumula el consumo de un análisis concreto.
///
/// Está en la capa de aplicación y no en la de IA porque el consumo es un dato
/// del caso de uso: la aplicación necesita poder decir cuánto costó *este*
/// análisis, y pueden ejecutarse varios a la vez.
/// </summary>
public interface IModelUsageCollector
{
    void Add(string model, long inputTokens, long outputTokens);

    IReadOnlyList<ModelUsage> Snapshot();
}
