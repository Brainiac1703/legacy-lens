using LegacyLens.Application.Abstractions;
using LegacyLens.Persistence.EF.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyLens.Persistence.EF;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la persistencia.
    ///
    /// Lo único que la presentación necesita saber es que existe este método:
    /// ni el proveedor, ni el contexto, ni el repositorio salen de aquí.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cadena = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'DefaultConnection'.");

        services.AddDbContext<LegacyLensDbContext>(options =>
            options.UseSqlServer(cadena, sql =>
            {
                // Azure SQL corta conexiones por mantenimiento o por límites del
                // plan, y son fallos transitorios de los que merece la pena
                // reintentar. Sin esto, un error recuperable llega al usuario
                // como una excepción.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                sql.MigrationsHistoryTable("__EFMigrationsHistory");
            }));

        services.AddScoped<IAnalysisRepository, AnalysisRepository>();

        return services;
    }
}
