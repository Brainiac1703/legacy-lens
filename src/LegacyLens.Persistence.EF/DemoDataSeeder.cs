using LegacyLens.Persistence.EF.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyLens.Persistence.EF;

/// <summary>
/// Aplica las migraciones pendientes y siembra el usuario de prueba.
///
/// El usuario se crea con el correo ya confirmado: la plantilla exige
/// confirmación para iniciar sesión y sin este paso las credenciales publicadas
/// en el README no servirían.
///
/// Sobre aplicar migraciones al arrancar: se mantiene porque hace que levantar
/// el proyecto en local o en un contenedor no requiera ningún paso previo. En el
/// despliegue real, el pipeline las aplica antes de publicar la nueva revisión,
/// así que cuando la aplicación arranca ya no queda nada por aplicar y esto es
/// una comprobación inocua. El orden importa: primero el esquema, después el
/// código que lo usa.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task MigrateAndSeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<LegacyLensDbContext>()
            .Database.MigrateAsync(cancellationToken);

        var email = configuration["Demo:Email"];
        var password = configuration["Demo:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Sin credenciales de demo configuradas: no se siembra ningún usuario");
            return;
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Los mensajes de abajo no incluyen el correo. Aquí es el del usuario de
        // demostración, publicado en el README, así que registrarlo no filtraría
        // nada — pero el patrón sí es malo: este método solo sabe que le han dado
        // un correo, no de quién es, y basta reutilizarlo con datos reales para
        // acabar volcando direcciones a un registro que suele guardarse más
        // tiempo y con menos control que la base de datos. Tampoco aporta nada al
        // diagnóstico: el correo ya está en la configuración.
        if (await users.FindByEmailAsync(email) is not null)
        {
            logger.LogInformation("El usuario de demo ya existe");
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, password);

        if (result.Succeeded)
            logger.LogInformation("Usuario de demo creado");
        else
            logger.LogError("No se pudo crear el usuario de demo: {Errores}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
