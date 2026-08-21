using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyLens.Persistence.EF;

/// <summary>
/// Contexto para las herramientas de línea de comandos de Entity Framework.
///
/// Sin esta clase, «dotnet ef» arrancaría el proyecto web para descubrir cómo se
/// construye el contexto, y por el camino ejecutaría el sembrado de datos, que
/// intenta aplicar migraciones y conectarse. Es decir: generar una migración
/// exigiría tener ya la base de datos creada.
///
/// Con la factoría, las herramientas construyen el contexto directamente:
///
///   cd src/LegacyLens.Persistence.EF
///   dotnet ef migrations add Nombre
///
/// La cadena de conexión de aquí solo sirve para que el proveedor sepa que es
/// SQL Server. Generar una migración no requiere conectarse a ningún sitio; para
/// aplicarla, la cadena real llega por variable de entorno.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LegacyLensDbContext>
{
    private const string CadenaDeDiseño =
        "Server=(localdb)\\mssqllocaldb;Database=LegacyLens;Trusted_Connection=True";

    public LegacyLensDbContext CreateDbContext(string[] args)
    {
        var cadena =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? CadenaDeDiseño;

        var options = new DbContextOptionsBuilder<LegacyLensDbContext>()
            .UseSqlServer(cadena, sql => sql.MigrationsAssembly(
                typeof(LegacyLensDbContext).Assembly.FullName))
            // IdentityDbContext lee la versión del esquema de las opciones de
            // Identity, y las busca en el proveedor de servicios de la
            // aplicación. En tiempo de diseño no hay ninguno, así que hay que
            // darle uno mínimo: sin esto, el esquema generado se queda en la
            // versión por omisión y la migración sale sin la tabla de passkeys.
            .UseApplicationServiceProvider(ProveedorMinimo())
            .Options;

        return new LegacyLensDbContext(options);
    }

    private static IServiceProvider ProveedorMinimo()
    {
        var servicios = new ServiceCollection();

        servicios.AddOptions<IdentityOptions>()
            .Configure(o => o.Stores.SchemaVersion = IdentityDefaults.SchemaVersion);

        return servicios.BuildServiceProvider();
    }
}
