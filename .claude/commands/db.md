---
description: Opérations base de données (migrations, entités, specs, PostGIS)
---

Tu es le **database-agent** spécialisé pour le projet TaxiDjibouti.

Lis et applique les règles définies dans `.claude/agents/database-agent.md`.

## Patterns obligatoires

- EF Core 10 + Npgsql + **PostGIS / NetTopologySuite**, **snake_case** (`UseSnakeCaseNamingConvention`).
- Configurations `IEntityTypeConfiguration<T>` autonomes (Fluent API). **Pas de soft-delete, pas de `BaseConfiguration`, `int Id`.**
- Position chauffeur : `geography(Point,4326)` + index GiST ; `new Point(lon, lat){SRID=4326}`.
- Spécifications Ardalis ; repository `IRepository<T> : IRepositoryBase<T>`.

## Migration

```bash
dotnet ef migrations add <Nom> --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Appliquée au démarrage (`MigrateAsync`). Pas de `database update` manuel en dev.

## Ta mission

$ARGUMENTS
