using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Pricing;

namespace Taxi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration EF de l'entité ZonePrice : tables, colonnes, contraintes, index.
/// </summary>
internal sealed class ZonePriceConfiguration : IEntityTypeConfiguration<ZonePrice>
{
    public void Configure(EntityTypeBuilder<ZonePrice> builder)
    {
        builder.ToTable("zone_prices");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.FromZone).HasMaxLength(100).IsRequired();
        builder.Property(z => z.ToZone).HasMaxLength(100).IsRequired();
        builder.Property(z => z.Price).HasColumnType("numeric(10,2)");
        builder.HasIndex(z => new { z.FromZone, z.ToZone }).IsUnique();
    }
}
