using LegacyLens.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyLens.Ai;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la capa de IA.
    ///
    /// Singleton para que la caché de documentación por contenido sobreviva
    /// entre análisis: repetir el mismo script no debe volver a pagarse.
    /// </summary>
    public static IServiceCollection AddAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddSingleton<IAiEnrichmentService, AiEnrichmentService>();

        return services;
    }
}
