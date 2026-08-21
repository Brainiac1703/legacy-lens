using LegacyLens.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyLens.Analysis;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el análisis estático.
    ///
    /// Singleton porque el analizador no guarda estado entre llamadas: cada
    /// invocación parsea un script y devuelve un resultado nuevo.
    /// </summary>
    public static IServiceCollection AddAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<ITSqlAnalyzer, TSqlAnalyzer>();

        return services;
    }
}
