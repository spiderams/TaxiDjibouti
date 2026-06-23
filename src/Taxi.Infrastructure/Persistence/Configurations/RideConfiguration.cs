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

        // Vague de chauffeurs offerts : champ privé _offeredDriverIds exposé via OfferedDriverIds.
        builder.Property<List<int>>("_offeredDriverIds")
            .HasColumnName("offered_driver_ids")
            .HasDefaultValueSql("'{}'::integer[]");

        // Verrou optimiste : la colonne système PostgreSQL xmin sert de token de concurrence.
        // Deux acceptations simultanées → un seul UPDATE matche xmin, l'autre lève DbUpdateConcurrencyException.
        // Note : UseXminAsConcurrencyToken() a été supprimé dans Npgsql EF Core v10 ; on déclare la propriété ombre manuellement.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
