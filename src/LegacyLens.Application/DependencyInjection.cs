using System.Reflection;
using FluentValidation;
using LegacyLens.Application.Common.Behaviours;
using LegacyLens.Application.Costing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyLens.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la capa de aplicación.
    ///
    /// Cada capa expone su propio método de registro y la presentación solo los
    /// invoca. Así el proyecto web no necesita conocer MediatR, FluentValidation
    /// ni ninguna de las piezas internas de esta capa: si mañana cambia el
    /// despachador, Program.cs no se toca.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var ensamblado = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(ensamblado);

            // El orden de registro es el orden de ejecución, y aquí importa: el
            // log envuelve a la validación para que un rechazo por validación
            // también quede registrado. Al revés, las peticiones inválidas
            // desaparecerían del rastro.
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));

            cfg.AddOpenStreamBehavior(typeof(StreamLoggingBehaviour<,>));
            cfg.AddOpenStreamBehavior(typeof(StreamValidationBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(ensamblado, includeInternalTypes: true);

        services.Configure<PricingOptions>(configuration.GetSection(PricingOptions.SectionName));
        services.AddSingleton<CostEstimator>();

        return services;
    }
}
