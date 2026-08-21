using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentValidation;
using LegacyLens.Application.Abstractions;
using LegacyLens.Application.Costing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LegacyLens.Application.Analyses;

public enum AnalysisPhase
{
    Parsing,
    Documenting,
    Planning,
    Saving,
    Done
}

/// <summary>Un paso del análisis, tal como se emite al consumidor.</summary>
public sealed record AnalysisProgress(
    AnalysisPhase Phase,
    string Message,
    int Completed = 0,
    int Total = 0,
    Guid? AnalysisId = null)
{
    public int Percentage => Total == 0 ? 0 : (int)(100.0 * Completed / Total);
}

/// <summary>
/// Analiza un script y emite el avance a medida que ocurre.
///
/// Es un <see cref="IStreamRequest{T}"/> y no un comando normal porque el
/// análisis dura decenas de segundos y su valor para el usuario está tanto en el
/// resultado como en ver que avanza. Con un comando corriente habría que colar
/// un delegate de progreso dentro de la propia petición, que es exactamente el
/// tipo de acoplamiento entre caso de uso y presentación que este refactor
/// pretende quitar. El último elemento emitido lleva el identificador del
/// análisis guardado.
/// </summary>
public sealed record AnalyzeScriptRequest(string Script, string FileName, string OwnerUserId)
    : IStreamRequest<AnalysisProgress>;

public sealed class AnalyzeScriptValidator : AbstractValidator<AnalyzeScriptRequest>
{
    /// <summary>
    /// Ocho megas de script. Por encima de eso deja de ser un caso de uso
    /// interactivo y toca resolverlo con proceso en segundo plano.
    /// </summary>
    private const int MaximoCaracteres = 8 * 1024 * 1024;

    public AnalyzeScriptValidator()
    {
        RuleFor(x => x.Script)
            .NotEmpty().WithMessage("El script está vacío.")
            .MaximumLength(MaximoCaracteres)
            .WithMessage($"El script supera el límite de {MaximoCaracteres / 1024 / 1024} MB.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Falta el nombre del fichero.")
            .MaximumLength(260);

        // Sin propietario el análisis quedaría accesible para cualquiera. La
        // regla está aquí, y no en el repositorio, para que el fallo salte antes
        // de tocar la base de datos.
        RuleFor(x => x.OwnerUserId)
            .NotEmpty().WithMessage("No se ha identificado al usuario propietario.");
    }
}

public sealed class AnalyzeScriptHandler(
    ITSqlAnalyzer analyzer,
    IAiEnrichmentService ai,
    IAnalysisRepository repository,
    ILogger<AnalyzeScriptHandler> logger)
    : IStreamRequestHandler<AnalyzeScriptRequest, AnalysisProgress>
{
    public async IAsyncEnumerable<AnalysisProgress> Handle(
        AnalyzeScriptRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AnalysisProgress(AnalysisPhase.Parsing, "Analizando el script...");

        // Etapa determinista: rápida y sin dependencias externas que puedan fallar.
        var result = analyzer.Analyze(request.Script, request.FileName);
        var programables = result.Objects.Count(o => o.IsProgrammable);

        logger.LogInformation(
            "Analizado {Fichero}: {Objetos} objetos, {Programables} programables, {Dependencias} dependencias",
            request.FileName, result.ObjectCount, programables, result.Dependencies.Count);

        if (ai.IsAvailable && programables > 0)
        {
            var consumo = new ModelUsageCollector();

            await foreach (var paso in Documentar(result, programables, consumo, cancellationToken))
                yield return paso;

            yield return new AnalysisProgress(
                AnalysisPhase.Planning, "Generando el plan de migración...", programables, programables);

            result.Plan = await ai.BuildPlanAsync(result, consumo, cancellationToken);
            result.Usage.AddRange(consumo.Snapshot());
        }
        else if (!ai.IsAvailable)
        {
            logger.LogInformation("IA no disponible: se entrega solo el análisis estático");
        }

        yield return new AnalysisProgress(AnalysisPhase.Saving, "Guardando...");

        var id = await repository.SaveAsync(result, request.OwnerUserId, cancellationToken);

        yield return new AnalysisProgress(AnalysisPhase.Done, "Listo", AnalysisId: id);
    }

    /// <summary>
    /// Puentea el <see cref="IProgress{T}"/> de la capa de IA a un flujo.
    ///
    /// La capa de IA documenta en paralelo y notifica por callback, que es su
    /// forma natural de trabajar. Un canal sin límite traduce esos avisos —que
    /// llegan desde hilos del pool— en elementos de un flujo que el consumidor
    /// recorre a su ritmo, sin que ninguna de las dos capas conozca a la otra.
    /// </summary>
    private async IAsyncEnumerable<AnalysisProgress> Documentar(
        Domain.AnalysisResult result,
        int total,
        IModelUsageCollector consumo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var canal = Channel.CreateUnbounded<AnalysisProgress>();

        var progreso = new Progress<AiProgress>(p =>
            canal.Writer.TryWrite(new AnalysisProgress(
                AnalysisPhase.Documenting,
                $"Documentando {p.CurrentObject}",
                p.Completed,
                p.Total)));

        async Task Documentacion()
        {
            try
            {
                await ai.DocumentAllAsync(result, progreso, consumo, cancellationToken);
            }
            finally
            {
                // Cerrar el canal en finally garantiza que el consumidor sale del
                // bucle también cuando la documentación falla o se cancela.
                canal.Writer.TryComplete();
            }
        }

        yield return new AnalysisProgress(
            AnalysisPhase.Documenting, "Documentando objetos...", 0, total);

        var tarea = Documentacion();

        await foreach (var paso in canal.Reader.ReadAllAsync(cancellationToken))
            yield return paso;

        // Se espera la tarea para que cualquier excepción se propague en lugar
        // de quedarse en un Task abandonado.
        await tarea;
    }
}
