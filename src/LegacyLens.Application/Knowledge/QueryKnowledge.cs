using FluentValidation;
using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;
using MediatR;

namespace LegacyLens.Application.Knowledge;

// ---------------------------------------------------------------------------
// Consultas sobre el conocimiento de un sistema heredado ya analizado.
//
// Existen para el servidor MCP, pero no dependen de él: son consultas de la
// capa de aplicación como cualquier otra, y la web podría usarlas mañana. Esa
// es la razón de que vivan aquí y no dentro del proyecto del servidor.
//
// Todas se resuelven sobre un único análisis. No es una limitación: un análisis
// es un sistema heredado, y preguntar «de quién depende esta tabla» solo tiene
// sentido dentro de un sistema. Además acota el coste: se carga un análisis, no
// el histórico entero del usuario.
// ---------------------------------------------------------------------------

/// <summary>
/// Ficha de un objeto con lo que se sabe de él, separando lo calculado de lo
/// generado por el modelo. La distinción no es decorativa: es la tesis del
/// proyecto, y un agente que consuma esto por MCP necesita saber de cuál de las
/// dos naturalezas es cada dato antes de confiar en él.
/// </summary>
public sealed record ObjectCard(
    string FullName,
    SqlObjectKind Kind,
    int Lines,
    CodeMetrics Metrics,
    RiskScore Risk,
    ObjectDocumentation? Documentation,
    IReadOnlyList<Dependency> Reads,
    IReadOnlyList<Dependency> Writes,
    IReadOnlyList<Dependency> Calls,
    IReadOnlyList<string> CalledBy);

/// <summary>Radio de impacto de cambiar un objeto.</summary>
public sealed record ChangeImpact(
    string FullName,
    RiskScore Risk,
    IReadOnlyList<string> DirectDependents,
    IReadOnlyList<string> TransitiveDependents,
    IReadOnlyList<string> Blockers);

// ---------------------------------------------------------------------------
// Buscar un objeto
// ---------------------------------------------------------------------------

/// <summary>
/// Busca por nombre, con o sin esquema. La coincidencia es exacta sobre el
/// nombre cualificado y, si no hay, sobre el nombre suelto: quien pregunta
/// desde un agente escribe «Facturas», no «dbo.Facturas».
/// </summary>
public sealed record FindObjectQuery(Guid AnalysisId, string OwnerUserId, string Name)
    : IRequest<ObjectCard?>;

public sealed class FindObjectValidator : AbstractValidator<FindObjectQuery>
{
    public FindObjectValidator()
    {
        RuleFor(x => x.AnalysisId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

public sealed class FindObjectHandler(IAnalysisRepository repository)
    : IRequestHandler<FindObjectQuery, ObjectCard?>
{
    public async Task<ObjectCard?> Handle(FindObjectQuery request, CancellationToken cancellationToken)
    {
        var analysis = await repository.GetAsync(request.AnalysisId, request.OwnerUserId, cancellationToken);
        if (analysis is null) return null;

        var objeto = KnowledgeLookup.Resolve(analysis, request.Name);
        if (objeto is null) return null;

        var salientes = DependencyGraph.DirectNeighbours(
            analysis.Dependencies, objeto.FullName, DependencyGraph.Direction.Downstream);

        return new ObjectCard(
            objeto.FullName,
            objeto.Kind,
            objeto.Metrics.Lines,
            objeto.Metrics,
            objeto.Risk,
            objeto.Documentation,
            Reads: [.. salientes.Where(d => d.Kind == DependencyKind.Reads)],
            Writes: [.. salientes.Where(d => d.Kind == DependencyKind.Writes)],
            Calls: [.. salientes.Where(d => d.Kind == DependencyKind.Calls)],
            CalledBy: [.. DependencyGraph
                .DirectNeighbours(analysis.Dependencies, objeto.FullName,
                    DependencyGraph.Direction.Upstream, DependencyKind.Calls)
                .Select(d => d.From)
                .Distinct(StringComparer.OrdinalIgnoreCase)]);
    }
}

// ---------------------------------------------------------------------------
// Quién usa un objeto
// ---------------------------------------------------------------------------

/// <summary>Una referencia a un objeto, con la naturaleza de la relación.</summary>
public sealed record Usage(string From, DependencyKind Kind);

/// <summary>
/// Qué habría que revisar al cambiar una tabla o un objeto. Es la pregunta que
/// más veces se hace de verdad antes de tocar un sistema heredado.
/// </summary>
public sealed record WhereUsedQuery(Guid AnalysisId, string OwnerUserId, string Name)
    : IRequest<IReadOnlyList<Usage>?>;

public sealed class WhereUsedValidator : AbstractValidator<WhereUsedQuery>
{
    public WhereUsedValidator()
    {
        RuleFor(x => x.AnalysisId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

public sealed class WhereUsedHandler(IAnalysisRepository repository)
    : IRequestHandler<WhereUsedQuery, IReadOnlyList<Usage>?>
{
    public async Task<IReadOnlyList<Usage>?> Handle(WhereUsedQuery request, CancellationToken cancellationToken)
    {
        var analysis = await repository.GetAsync(request.AnalysisId, request.OwnerUserId, cancellationToken);
        if (analysis is null) return null;

        // Una tabla puede no aparecer como objeto del script —es habitual que el
        // esquema esté en otro fichero— y aun así tener referencias. Por eso se
        // resuelve contra el grafo y no solo contra la lista de objetos.
        var nombre = KnowledgeLookup.Resolve(analysis, request.Name)?.FullName
                     ?? KnowledgeLookup.ResolveInGraph(analysis, request.Name);

        if (nombre is null) return [];

        return [.. DependencyGraph
            .DirectNeighbours(analysis.Dependencies, nombre, DependencyGraph.Direction.Upstream)
            .Select(d => new Usage(d.From, d.Kind))
            .DistinctBy(u => (u.From, u.Kind), TupleComparer.Instance)];
    }
}

// ---------------------------------------------------------------------------
// Riesgo de cambiar un objeto
// ---------------------------------------------------------------------------

/// <summary>
/// Riesgo propio del objeto más su radio de impacto. Son dos cosas distintas y
/// se devuelven separadas: un procedimiento sencillo al que llaman veinte
/// sitios es poco riesgo de traducir y mucho de tocar.
/// </summary>
public sealed record ChangeRiskQuery(Guid AnalysisId, string OwnerUserId, string Name)
    : IRequest<ChangeImpact?>;

public sealed class ChangeRiskValidator : AbstractValidator<ChangeRiskQuery>
{
    public ChangeRiskValidator()
    {
        RuleFor(x => x.AnalysisId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

public sealed class ChangeRiskHandler(IAnalysisRepository repository)
    : IRequestHandler<ChangeRiskQuery, ChangeImpact?>
{
    public async Task<ChangeImpact?> Handle(ChangeRiskQuery request, CancellationToken cancellationToken)
    {
        var analysis = await repository.GetAsync(request.AnalysisId, request.OwnerUserId, cancellationToken);
        if (analysis is null) return null;

        var objeto = KnowledgeLookup.Resolve(analysis, request.Name);
        if (objeto is null) return null;

        var directos = DependencyGraph
            .DirectNeighbours(analysis.Dependencies, objeto.FullName, DependencyGraph.Direction.Upstream)
            .Select(d => d.From)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var transitivos = DependencyGraph
            .TransitiveClosure(analysis.Dependencies, objeto.FullName, DependencyGraph.Direction.Upstream)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Lo que hay que migrar antes: de esto depende el objeto, así que
        // moverlo sin haber resuelto estos deja el sistema a medias.
        var bloqueantes = DependencyGraph
            .DirectNeighbours(analysis.Dependencies, objeto.FullName,
                DependencyGraph.Direction.Downstream, DependencyKind.Calls)
            .Select(d => d.To)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ChangeImpact(objeto.FullName, objeto.Risk, directos, transitivos, bloqueantes);
    }
}

// ---------------------------------------------------------------------------
// Resolución de nombres
// ---------------------------------------------------------------------------

/// <summary>
/// Traduce lo que escribe una persona —o un agente— al nombre cualificado que
/// usa el grafo.
/// </summary>
internal static class KnowledgeLookup
{
    public static SqlObject? Resolve(AnalysisResult analysis, string name)
    {
        var buscado = name.Trim();

        return analysis.Objects.FirstOrDefault(o =>
                   string.Equals(o.FullName, buscado, StringComparison.OrdinalIgnoreCase))
               ?? analysis.Objects.FirstOrDefault(o =>
                   string.Equals(o.Name, buscado, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Busca el nombre entre los extremos de las aristas. Sirve para objetos
    /// referenciados que no están definidos en el script analizado.
    /// </summary>
    public static string? ResolveInGraph(AnalysisResult analysis, string name)
    {
        var buscado = name.Trim();

        return analysis.Dependencies
            .SelectMany(d => new[] { d.From, d.To })
            .FirstOrDefault(n =>
                string.Equals(n, buscado, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Split('.').Last(), buscado, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Comparador para deduplicar pares (origen, tipo) ignorando mayúsculas en el
/// nombre. Un comparador propio y no un <c>DistinctBy</c> sobre una cadena
/// concatenada, porque concatenar nombres con un separador puede colisionar.
/// </summary>
internal sealed class TupleComparer : IEqualityComparer<(string From, DependencyKind Kind)>
{
    public static readonly TupleComparer Instance = new();

    public bool Equals((string From, DependencyKind Kind) x, (string From, DependencyKind Kind) y) =>
        x.Kind == y.Kind && string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string From, DependencyKind Kind) obj) =>
        HashCode.Combine(obj.From.ToUpperInvariant(), obj.Kind);
}
