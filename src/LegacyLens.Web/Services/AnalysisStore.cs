using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyLens.Domain;
using LegacyLens.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyLens.Web.Services;

/// <summary>Guarda y recupera análisis completos.</summary>
public sealed class AnalysisStore(AnalysisDbContext db)
{
    /// <summary>
    /// Enumeraciones como texto: el JSON guardado tiene que seguir siendo
    /// legible dentro de un año, y un 3 no dice nada.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<Guid> SaveAsync(AnalysisResult result, string? ownerUserId, CancellationToken ct = default)
    {
        var stored = new StoredAnalysis
        {
            Id = result.Id,
            FileName = result.SourceFileName,
            CreatedAt = result.CreatedAt,
            OwnerUserId = ownerUserId,
            ObjectCount = result.ObjectCount,
            HasAiDocumentation = result.Objects.Any(o => o.Documentation is not null),
            HasPlan = result.Plan is not null,
            Payload = JsonSerializer.Serialize(result, SerializerOptions)
        };

        db.Analyses.Add(stored);
        await db.SaveChangesAsync(ct);

        return stored.Id;
    }

    public async Task<AnalysisResult?> GetAsync(Guid id, string? ownerUserId, CancellationToken ct = default)
    {
        var stored = await db.Analyses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (stored is null) return null;

        // Un usuario no ve los análisis de otro.
        if (stored.OwnerUserId is not null && stored.OwnerUserId != ownerUserId) return null;

        return JsonSerializer.Deserialize<AnalysisResult>(stored.Payload, SerializerOptions);
    }

    public async Task<List<StoredAnalysis>> ListAsync(string? ownerUserId, CancellationToken ct = default) =>
        await db.Analyses
            .AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new StoredAnalysis
            {
                // Sin el Payload: listar no necesita traerse los documentos enteros.
                Id = a.Id,
                FileName = a.FileName,
                CreatedAt = a.CreatedAt,
                ObjectCount = a.ObjectCount,
                HasAiDocumentation = a.HasAiDocumentation,
                HasPlan = a.HasPlan
            })
            .ToListAsync(ct);
}
