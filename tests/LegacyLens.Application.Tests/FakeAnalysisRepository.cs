using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;

namespace LegacyLens.Application.Tests;

/// <summary>
/// Repositorio en memoria que respeta la regla que importa: un análisis solo se
/// devuelve a su propietario.
///
/// No es un doble sin comportamiento. La firma de <see cref="IAnalysisRepository"/>
/// existe precisamente para que esa regla viva en el método y no en que cada
/// consulta se acuerde de filtrar, así que un falso que ignorara el propietario
/// haría pasar unos tests que no demuestran nada.
/// </summary>
internal sealed class FakeAnalysisRepository : IAnalysisRepository
{
    private readonly Dictionary<Guid, (AnalysisResult Result, string Owner)> _stored = [];

    /// <summary>Propietarios por los que se ha preguntado, en orden.</summary>
    public List<string> RequestedOwners { get; } = [];

    public void Add(AnalysisResult result, string ownerUserId) =>
        _stored[result.Id] = (result, ownerUserId);

    public Task<AnalysisResult?> GetAsync(
        Guid id, string ownerUserId, CancellationToken cancellationToken = default)
    {
        RequestedOwners.Add(ownerUserId);

        if (!_stored.TryGetValue(id, out var entry)) return Task.FromResult<AnalysisResult?>(null);

        return Task.FromResult(entry.Owner == ownerUserId ? entry.Result : null);
    }

    public Task<Guid> SaveAsync(
        AnalysisResult result, string ownerUserId, CancellationToken cancellationToken = default)
    {
        Add(result, ownerUserId);
        return Task.FromResult(result.Id);
    }

    public Task<IReadOnlyList<AnalysisSummary>> ListAsync(
        string ownerUserId, CancellationToken cancellationToken = default)
    {
        RequestedOwners.Add(ownerUserId);

        IReadOnlyList<AnalysisSummary> summaries =
        [
            .. _stored.Values
                .Where(e => e.Owner == ownerUserId)
                .OrderByDescending(e => e.Result.CreatedAt)
                .Select(e => new AnalysisSummary(
                    e.Result.Id,
                    e.Result.SourceFileName,
                    e.Result.CreatedAt,
                    e.Result.Objects.Count,
                    e.Result.Objects.Any(o => o.Documentation is not null),
                    e.Result.Plan is not null))
        ];

        return Task.FromResult(summaries);
    }
}
