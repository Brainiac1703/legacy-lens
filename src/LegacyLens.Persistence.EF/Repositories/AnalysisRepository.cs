using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;
using LegacyLens.Persistence.EF.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyLens.Persistence.EF.Repositories;

/// <summary>
/// Implementación del repositorio de análisis.
///
/// Aquí vive la decisión de guardar el análisis serializado en una columna en
/// lugar de repartirlo por seis tablas, y es importante que viva **aquí**: es un
/// detalle de cómo se almacena, y la capa de aplicación no debería saberlo. Con
/// el diseño anterior esa serialización estaba en un servicio del proyecto web,
/// que es justo lo que este refactor corrige.
///
/// El criterio sigue siendo el mismo: el análisis se escribe una vez y se lee
/// entero, nunca se consulta por partes ni se actualiza campo a campo.
/// </summary>
public sealed class AnalysisRepository(LegacyLensDbContext db) : IAnalysisRepository
{
    /// <summary>
    /// Enumeraciones como texto: el documento guardado tiene que seguir siendo
    /// legible dentro de un año, y un 3 no dice nada.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<Guid> SaveAsync(
        AnalysisResult result, string ownerUserId, CancellationToken cancellationToken = default)
    {
        var fila = new StoredAnalysis
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

        db.Analyses.Add(fila);
        await db.SaveChangesAsync(cancellationToken);

        return fila.Id;
    }

    public async Task<AnalysisResult?> GetAsync(
        Guid id, string ownerUserId, CancellationToken cancellationToken = default)
    {
        // El filtro por propietario va en la consulta, no en una comprobación
        // posterior: así no hay forma de leer la fila de otro usuario ni por
        // error de programación.
        var payload = await db.Analyses
            .AsNoTracking()
            .Where(a => a.Id == id && a.OwnerUserId == ownerUserId)
            .Select(a => a.Payload)
            .FirstOrDefaultAsync(cancellationToken);

        return payload is null
            ? null
            : JsonSerializer.Deserialize<AnalysisResult>(payload, SerializerOptions);
    }

    public async Task<IReadOnlyList<AnalysisSummary>> ListAsync(
        string ownerUserId, CancellationToken cancellationToken = default) =>
        await db.Analyses
            .AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderByDescending(a => a.CreatedAt)
            // La proyección deja el Payload fuera de la consulta: listar no
            // necesita traerse los documentos enteros desde el servidor.
            .Select(a => new AnalysisSummary(
                a.Id,
                a.FileName,
                a.CreatedAt,
                a.ObjectCount,
                a.HasAiDocumentation,
                a.HasPlan))
            .ToListAsync(cancellationToken);
}
