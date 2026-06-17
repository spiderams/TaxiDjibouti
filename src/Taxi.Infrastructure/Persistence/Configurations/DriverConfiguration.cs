using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Drivers;

namespace Taxi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration EF de l'entité Driver : tables, colonnes, contraintes, index.
/// </summary>
internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.LicenseNumber).HasMaxLength(50);
        builder.Property(d => d.VehiclePlate).HasMaxLength(20);
        builder.Property(d => d.VehicleType).HasMaxLength(50).IsRequired();
        builder.HasIndex(d => d.UserId).IsUnique();
        builder.Property(d => d.LastLocation).HasColumnType("geography (Point)");
        builder.HasIndex(d => d.LastLocation).HasMethod("gist");
    }
}
