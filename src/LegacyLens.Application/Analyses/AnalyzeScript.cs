using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentValidation;
using LegacyLens.Application.Abstractions;
using LegacyLens.Application.Costing;
using MediatR;
using Microsoft.Extensions.Localization;
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

/// <summary>
/// Un paso del análisis, tal como se emite al consumidor.
///
/// No lleva ningún texto para el usuario, y es deliberado: la fase y el nombre
/// del objeto en curso son datos, y con ellos la presentación compone el mensaje
/// en el idioma que toque. Antes este registro llevaba un Message en español, con
/// lo que un caso de uso decidía la redacción de la interfaz y hacía imposible
/// traducirla.
/// </summary>
public sealed record AnalysisProgress(
    AnalysisPhase Phase,
    int Completed = 0,
    int Total = 0,
    Guid? AnalysisId = null,
    string? CurrentObject = null)
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
    private const int MaxCharacters = 8 * 1024 * 1024;

    public AnalyzeScriptValidator(IStringLocalizer<ValidationText> localizer)
    {
        RuleFor(x => x.Script)
            .NotEmpty().WithMessage(_ => localizer["Script_Empty"])
            .MaximumLength(MaxCharacters)
            .WithMessage(_ => localizer["Script_TooLarge", MaxCharacters / 1024 / 1024]);

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage(_ => localizer["FileName_Missing"])
            .MaximumLength(260);

        // Sin propietario el análisis quedaría accesible para cualquiera. La
        // regla está aquí, y no en el repositorio, para que el fallo salte antes
        // de tocar la base de datos.
        RuleFor(x => x.OwnerUserId)
            .NotEmpty().WithMessage(_ => localizer["Owner_Missing"]);
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
        yield return new AnalysisProgress(AnalysisPhase.Parsing);

        // Etapa determinista: rápida y sin dependencias externas que puedan fallar.
        var result = analyzer.Analyze(request.Script, request.FileName);
        var programmable = result.Objects.Count(o => o.IsProgrammable);

        logger.LogInformation(
            "Analizado {Fichero}: {Objetos} objetos, {Programables} programables, {Dependencias} dependencias",
            request.FileName, result.ObjectCount, programmable, result.Dependencies.Count);

        if (ai.IsAvailable && programmable > 0)
        {
            var usage = new ModelUsageCollector();

            await foreach (var step in DocumentAsync(result, programmable, usage, cancellationToken))
                yield return step;

            yield return new AnalysisProgress(
                AnalysisPhase.Planning, programmable, programmable);

            result.Plan = await ai.BuildPlanAsync(result, usage, cancellationToken);
            result.Usage.AddRange(usage.Snapshot());
        }
        else if (!ai.IsAvailable)
        {
            logger.LogInformation("IA no disponible: se entrega solo el análisis estático");
        }

        yield return new AnalysisProgress(AnalysisPhase.Saving);

        var id = await repository.SaveAsync(result, request.OwnerUserId, cancellationToken);

        yield return new AnalysisProgress(AnalysisPhase.Done, AnalysisId: id);
    }

    /// <summary>
    /// Puentea el <see cref="IProgress{T}"/> de la capa de IA a un flujo.
    ///
    /// La capa de IA documenta en paralelo y notifica por callback, que es su
    /// forma natural de trabajar. Un canal sin límite traduce esos avisos —que
    /// llegan desde hilos del pool— en elementos de un flujo que el consumidor
    /// recorre a su ritmo, sin que ninguna de las dos capas conozca a la otra.
    /// </summary>
    private async IAsyncEnumerable<AnalysisProgress> DocumentAsync(
        Domain.AnalysisResult result,
        int total,
        IModelUsageCollector usage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AnalysisProgress>();

        var progress = new Progress<AiProgress>(p =>
            channel.Writer.TryWrite(new AnalysisProgress(
                AnalysisPhase.Documenting,
                p.Completed,
                p.Total,
                CurrentObject: p.CurrentObject)));

        async Task RunDocumentation()
        {
            try
            {
                await ai.DocumentAllAsync(result, progress, usage, cancellationToken);
            }
            finally
            {
                // Cerrar el canal en finally garantiza que el consumidor sale del
                // bucle también cuando la documentación falla o se cancela.
                channel.Writer.TryComplete();
            }
        }

        yield return new AnalysisProgress(AnalysisPhase.Documenting, 0, total);

        var documentationTask = RunDocumentation();

        await foreach (var step in channel.Reader.ReadAllAsync(cancellationToken))
            yield return step;

        // Se espera la tarea para que cualquier excepción se propague en lugar
        // de quedarse en un Task abandonado.
        await documentationTask;
    }
}
