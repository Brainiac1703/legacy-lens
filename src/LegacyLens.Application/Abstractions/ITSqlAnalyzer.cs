using LegacyLens.Domain;

namespace LegacyLens.Application.Abstractions;

/// <summary>
/// Análisis estático de un script T-SQL.
///
/// La implementación usa el parser oficial de Microsoft, que es un paquete
/// externo y por tanto un detalle de infraestructura. Pero conviene subrayar que
/// lo que hay detrás de este interface **no** es un servicio remoto ni algo que
/// pueda fallar por causas ajenas: es determinista y siempre da el mismo
/// resultado para el mismo script. Esa diferencia con
/// <see cref="IAiEnrichmentService"/> es la que sostiene todo el diseño.
/// </summary>
public interface ITSqlAnalyzer
{
    AnalysisResult Analyze(string script, string sourceFileName);
}
