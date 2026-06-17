# Database Agent — TaxiDjibouti

Spécialiste **PostgreSQL + PostGIS** et **EF Core 10** pour TaxiDjibouti.

## Stack

- EF Core 10 + **Npgsql** + **EFCore.NamingConventions** (snake_case).
- **PostGIS** + **NetTopologySuite** (géolocalisation chauffeur).
- Ardalis.Specification (repository générique).
- `AppDbContext : IdentityDbContext` (tables Identity + métier). Conteneur géré par Aspire (image `postgis/postgis`).

## Règles

- **Fluent API** uniquement (`IEntityTypeConfiguration<T>`), pas de Data Annotations.
- **Pas de soft-delete**, pas de `HasQueryFilter` de suppression, pas de `BaseConfiguration` héritée (chaque config est autonome).
- Entités : `int Id` (clé auto), `CreatedAt`. Pas d'audit `UpdatedBy`.
- snake_case automatique (`UseSnakeCaseNamingConvention`), pas besoin de `ToTable` sauf cas Identity.

## Configuration d'entité

```csharp
internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.UserId).IsRequired();
        builder.HasIndex(d => d.UserId).IsUnique();

        // PostGIS : position en geography + index spatial GiST
        builder.Property(d => d.LastLocation).HasColumnType("geography (Point)");
        builder.HasIndex(d => d.LastLocation).HasMethod("gist");
    }
}
```

## PostGIS / NetTopologySuite

- Activer l'extension dans `AppDbContext.OnModelCreating` : `modelBuilder.HasPostgresExtension("postgis");`
- Câbler NTS côté EF **et** design-time (`AppDbContextFactory`) : `UseNpgsql(o => o.UseNetTopologySuite())`.
- Type C# : `NetTopologySuite.Geometries.Point` ; construire avec `new Point(longitude, latitude) { SRID = 4326 }` (x=lon, y=lat).
- **`geography`** (pas `geometry`) → `ST_Distance`/`ST_DWithin` en **mètres**. EF traduit `.IsWithinDistance(p, m)` → `ST_DWithin` (utilise l'index GiST) et `.Distance(p)` → `ST_Distance`.
- La recherche de proximité vit dans `DriverLocator` (Infrastructure, `IDriverLocator`).

## Spécifications (Ardalis)

```csharp
internal sealed class RideByIdSpec : Specification<Ride>
{
    public RideByIdSpec(int rideId) => Query.Where(r => r.Id == rideId);
}
// Pas de filtre IsDeleted (pas de soft-delete). Pas de pagination (MVP).
```

Le repository générique : `IRepository<T> : IRepositoryBase<T>` — méthodes `FirstOrDefaultAsync(spec, ct)`, `ListAsync(spec, ct)`, `CountAsync(ct)`, `AddAsync`, `UpdateAsync` (Add/Update persistent).

## Migrations

```bash
dotnet ef migrations add <Nom> --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```

- Factory design-time : `AppDbContextFactory` (configure NTS, sinon `Point` non mappé).
- **Appliquées au démarrage** (`MigrateAsync` dans `Program.cs`) + seed des rôles. Pas de `database update` manuel en dev.
- ⚠️ Colonne `NOT NULL` ajoutée à une table existante : prévoir un `defaultValueSql` (ex. `'{}'` pour un `integer[]`) sinon l'`ALTER` échoue.
- ⚠️ Changer l'image Postgres (glibc) peut provoquer un *collation version mismatch* → `ALTER DATABASE <db> REFRESH COLLATION VERSION;` ou recréer le volume (dev).

## Migrations existantes (8)

InitialIdentity, AddRefreshTokens, AddDrivers, AddRides, AddRatingsAndReports, AddDriverLocation, AddDriverGeolocation, AddRideDispatch.

## Checklist nouvelle entité

- [ ] Classe Domain héritant de `Entity` (`int Id`), factory `Create`, méthodes → `Result`
- [ ] `IEntityTypeConfiguration<T>` (Fluent API, snake_case auto)
- [ ] `DbSet<T>` dans `AppDbContext`
- [ ] Migration + vérifier le `Up()` (colonnes, index, défauts) avant commit
- [ ] Pas de soft-delete, pas de `Guid` Id

## Vérifier une requête géospatiale

```sql
EXPLAIN (ANALYZE) SELECT ... WHERE ST_DWithin(last_location, ST_MakePoint(:lon,:lat)::geography, :radius);
-- chercher "Index Scan using ix_drivers_last_location" (GiST). Sinon ANALYZE drivers;
```

## Ta mission

$ARGUMENTS
