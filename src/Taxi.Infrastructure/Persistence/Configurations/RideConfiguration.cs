using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration EF de l'entité Ride : tables, colonnes, contraintes, index.
/// </summary>
internal sealed class RideConfiguration : IEntityTypeConfiguration<Ride>
{
    public void Configure(EntityTypeBuilder<Ride> builder)
    {
        builder.ToTable("rides");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ClientId).IsRequired();
        builder.Property(r => r.PickupAddress).HasMaxLength(200);
        builder.Property(r => r.DestinationAddress).HasMaxLength(200);
        builder.Property(r => r.PickupZone).HasMaxLength(100);
        builder.Property(r => r.DestinationZone).HasMaxLength(100);
        builder.Property(r => r.EstimatedPrice).HasColumnType("numeric(10,2)");
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => r.ClientId);
        builder.HasIndex(r => r.DriverId);
        builder.HasIndex(r => r.Status);
    }
}
