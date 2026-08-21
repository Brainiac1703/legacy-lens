using LegacyLens.Domain;

namespace LegacyLens.Application.Abstractions;

/// <summary>
/// Acceso a los análisis guardados.
///
/// Se eligió un repositorio con métodos de dominio en lugar de exponer el
/// contexto de Entity Framework detrás de un interface. La aplicación solo hace
/// tres cosas con los datos —guardar, recuperar uno y listar los de un usuario—
/// y expresarlas así consigue dos cosas que el otro camino no da: que Entity
/// Framework quede encerrado por completo en la capa de persistencia, y que la
/// regla de que un usuario solo ve lo suyo viva en la firma del método en lugar
/// de depender de que cada consulta se acuerde de filtrar.
/// </summary>
public interface IAnalysisRepository
{
    /// <summary>Guarda un análisis y devuelve su identificador.</summary>
    Task<Guid> SaveAsync(AnalysisResult result, string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera un análisis del usuario indicado. Devuelve nulo si no existe o
    /// si pertenece a otro: no se distingue entre ambos casos a propósito, para
    /// no revelar la existencia de análisis ajenos.
    /// </summary>
    Task<AnalysisResult?> GetAsync(Guid id, string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Resumen de los análisis de un usuario, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<AnalysisSummary>> ListAsync(string ownerUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Modelo de lectura para el listado.
///
/// Existe para no tener que traerse el análisis completo —que incluye el código
/// fuente y la documentación de cada objeto— solo para pintar una tabla.
/// </summary>
public sealed record AnalysisSummary(
    Guid Id,
    string FileName,
    DateTimeOffset CreatedAt,
    int ObjectCount,
    bool HasAiDocumentation,
    bool HasPlan);
