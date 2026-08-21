using LegacyLens.Persistence.EF.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyLens.Persistence.EF.Configurations;

public sealed class StoredAnalysisConfiguration : IEntityTypeConfiguration<StoredAnalysis>
{
    public void Configure(EntityTypeBuilder<StoredAnalysis> builder)
    {
        builder.ToTable("Analyses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(a => a.OwnerUserId)
            .HasMaxLength(450);   // el mismo ancho que la clave de Identity

        // El documento completo. En SQL Server, nvarchar(max): un análisis con
        // el código fuente de cien procedimientos supera con holgura cualquier
        // límite razonable de columna corta.
        builder.Property(a => a.Payload)
            .IsRequired();

        // La consulta real siempre es "los análisis de este usuario, del más
        // reciente al más antiguo". Ese es el índice que hace falta, y el orden
        // descendente de la fecha evita una ordenación extra.
        builder.HasIndex(a => new { a.OwnerUserId, a.CreatedAt })
            .IsDescending(false, true);
    }
}
