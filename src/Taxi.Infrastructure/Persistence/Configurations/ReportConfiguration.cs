using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration EF de l'entité Report : tables, colonnes, contraintes, index.
/// </summary>
internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ClientId).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.HasIndex(r => r.RideId);
    }
}
