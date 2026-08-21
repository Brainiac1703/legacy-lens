using LegacyLens.Persistence.EF.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LegacyLens.Persistence.EF;

/// <summary>
/// Contexto único de la aplicación: identidades y análisis.
///
/// Antes había dos contextos con criterios distintos —uno con migraciones para
/// Identity y otro con EnsureCreated para los análisis— y era una consecuencia
/// de la plantilla, no una decisión. Con una base de datos servidor detrás,
/// tener dos contextos sobre el mismo servidor solo complicaría el despliegue:
/// habría dos historiales de esquema que aplicar y mantener en orden.
///
/// Un solo contexto significa un solo juego de migraciones, que es exactamente
/// lo que necesita el paso de actualización de base de datos del pipeline.
/// </summary>
public class LegacyLensDbContext(DbContextOptions<LegacyLensDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<StoredAnalysis> Analyses => Set<StoredAnalysis>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Las configuraciones van en clases aparte y se descubren por
        // ensamblado: así este fichero no crece con cada entidad nueva.
        builder.ApplyConfigurationsFromAssembly(typeof(LegacyLensDbContext).Assembly);
    }
}
