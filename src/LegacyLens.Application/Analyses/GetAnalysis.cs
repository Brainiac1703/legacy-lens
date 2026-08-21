using FluentValidation;
using LegacyLens.Application.Abstractions;
using LegacyLens.Application.Documentation;
using LegacyLens.Domain;
using MediatR;

namespace LegacyLens.Application.Analyses;

// ---------------------------------------------------------------------------
// Recuperar un análisis
// ---------------------------------------------------------------------------

/// <summary>
/// Devuelve nulo si el análisis no existe o si pertenece a otro usuario. No se
/// distinguen los dos casos a propósito: hacerlo revelaría qué identificadores
/// existen.
/// </summary>
public sealed record GetAnalysisQuery(Guid Id, string OwnerUserId) : IRequest<AnalysisResult?>;

public sealed class GetAnalysisValidator : AbstractValidator<GetAnalysisQuery>
{
    public GetAnalysisValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
    }
}

public sealed class GetAnalysisHandler(IAnalysisRepository repository)
    : IRequestHandler<GetAnalysisQuery, AnalysisResult?>
{
    public Task<AnalysisResult?> Handle(GetAnalysisQuery request, CancellationToken cancellationToken) =>
        repository.GetAsync(request.Id, request.OwnerUserId, cancellationToken);
}

// ---------------------------------------------------------------------------
// Listar los análisis de un usuario
// ---------------------------------------------------------------------------

public sealed record ListAnalysesQuery(string OwnerUserId) : IRequest<IReadOnlyList<AnalysisSummary>>;

public sealed class ListAnalysesValidator : AbstractValidator<ListAnalysesQuery>
{
    public ListAnalysesValidator() => RuleFor(x => x.OwnerUserId).NotEmpty();
}

public sealed class ListAnalysesHandler(IAnalysisRepository repository)
    : IRequestHandler<ListAnalysesQuery, IReadOnlyList<AnalysisSummary>>
{
    public Task<IReadOnlyList<AnalysisSummary>> Handle(
        ListAnalysesQuery request, CancellationToken cancellationToken) =>
        repository.ListAsync(request.OwnerUserId, cancellationToken);
}

// ---------------------------------------------------------------------------
// Exportar la documentación
// ---------------------------------------------------------------------------

/// <summary>Documento listo para descargar, con el nombre que debe llevar.</summary>
public sealed record MarkdownExport(string FileName, string Content);

public sealed record ExportAnalysisMarkdownQuery(Guid Id, string OwnerUserId) : IRequest<MarkdownExport?>;

public sealed class ExportAnalysisMarkdownValidator : AbstractValidator<ExportAnalysisMarkdownQuery>
{
    public ExportAnalysisMarkdownValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
    }
}

/// <summary>
/// La generación del documento es una operación de la aplicación, no de la
/// presentación: el mismo informe debe salir igual desde la web, desde una
/// futura API o desde una herramienta de línea de comandos.
/// </summary>
public sealed class ExportAnalysisMarkdownHandler(IAnalysisRepository repository)
    : IRequestHandler<ExportAnalysisMarkdownQuery, MarkdownExport?>
{
    public async Task<MarkdownExport?> Handle(
        ExportAnalysisMarkdownQuery request, CancellationToken cancellationToken)
    {
        var result = await repository.GetAsync(request.Id, request.OwnerUserId, cancellationToken);
        if (result is null) return null;

        var nombre = $"{Path.GetFileNameWithoutExtension(result.SourceFileName)}-legacy-lens.md";

        return new MarkdownExport(nombre, MarkdownExporter.Export(result));
    }
}
