using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LegacyLens.Web.Data;

/// <summary>
/// Prepara la base de datos y siembra el usuario de prueba.
///
/// El TFM exige entregar unas credenciales de acceso, así que el usuario se
/// crea con el correo ya confirmado: la plantilla exige confirmación para
/// iniciar sesión y sin este paso las credenciales publicadas no servirían.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<AnalysisDbContext>().Database.EnsureCreatedAsync();

        var email = configuration["Demo:Email"];
        var password = configuration["Demo:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Sin credenciales de demo configuradas: no se siembra ningún usuario");
            return;
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByEmailAsync(email) is not null)
        {
            logger.LogInformation("El usuario de demo {Email} ya existe", email);
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
            logger.LogInformation("Usuario de demo {Email} creado", email);
        else
            logger.LogError("No se pudo crear el usuario de demo: {Errores}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
