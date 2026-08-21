using Microsoft.EntityFrameworkCore;

namespace LegacyLens.Web.Data;

/// <summary>
/// Almacén de análisis, separado del contexto de Identity a propósito.
///
/// Identity trae su propio juego de migraciones con la plantilla y conviene no
/// tocarlo. Este contexto gestiona una única tabla de solo-añadir, así que se
/// crea con EnsureCreated y no necesita historial de migraciones: no hay
/// evolución de esquema que versionar.
/// </summary>
public class AnalysisDbContext(DbContextOptions<AnalysisDbContext> options) : DbContext(options)
{
    public DbSet<StoredAnalysis> Analyses => Set<StoredAnalysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var analysis = modelBuilder.Entity<StoredAnalysis>();

        analysis.HasKey(a => a.Id);
        analysis.Property(a => a.FileName).HasMaxLength(260).IsRequired();
        analysis.Property(a => a.Payload).IsRequired();

        // Se consulta siempre "los análisis de este usuario, del más reciente
        // al más antiguo": ese es el índice que hace falta y no otro.
        analysis.HasIndex(a => new { a.OwnerUserId, a.CreatedAt });
    }
}
