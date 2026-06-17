# Module Dispatch — Phase 1 (moteur de matching PostGIS) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trouver les chauffeurs disponibles les plus proches d'un point via PostGIS (colonne geography + index GiST), exposé en query + endpoint Admin.

**Architecture:** PostGIS + NetTopologySuite. La position chauffeur devient un `geography(Point,4326)` sur `Driver`. La requête spatiale vit derrière `IDriverLocator` (abstraction Application, impl Infrastructure via `AppDbContext` + EF NTS — pattern `IUserDirectory`). Query CQRS `FindNearestDrivers` + endpoint diagnostic.

**Tech Stack:** .NET 10, EF Core 10, Npgsql 10 + NetTopologySuite, PostGIS, Aspire 13.1, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-17-dispatch-phase1-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 70 tests verts au départ.

> **Portée Phase 1 :** infra PostGIS + NTS, `Driver.LastLocation` (geography), migration, `IDriverLocator` + query + endpoint. **Pas** d'auto-assignation (Phase 2). Les tâches de code sont **offline** (build/migration/test) ; la vérif PostGIS réelle est manuelle (Task 6).

---

## Task 1: Packages NTS + câblage Npgsql/EF + extension PostGIS + image AppHost

**Files:**
- Modify: `Directory.Packages.props`, `src/Taxi.Domain/Taxi.Domain.csproj`, `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj`
- Modify: `src/Taxi.Web.Api/Program.cs`, `src/Taxi.Infrastructure/Persistence/AppDbContextFactory.cs`, `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `Taxi.AppHost/AppHost.cs`

- [ ] **Step 1: Add package versions** — dans `Directory.Packages.props`, sous le commentaire `<!-- Infrastructure -->`, ajouter :
```xml
    <PackageVersion Include="NetTopologySuite" Version="2.6.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite" Version="10.0.2" />
```
NOTE: si `dotnet restore` échoue sur une de ces versions, ajuster à la version compatible la plus proche (NTS 2.5.x/2.6.x ; le package Npgsql NTS doit matcher `Npgsql.EntityFrameworkCore.PostgreSQL` = 10.0.2).

- [ ] **Step 2: Reference NetTopologySuite in Domain** — dans `src/Taxi.Domain/Taxi.Domain.csproj`, à l'intérieur d'un `<ItemGroup>`, ajouter :
```xml
    <PackageReference Include="NetTopologySuite" />
```
(Si le fichier n'a pas encore d'`ItemGroup` de `PackageReference`, en créer un.)

- [ ] **Step 3: Reference the Npgsql NTS plugin in Infrastructure** — dans `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj`, dans l'`ItemGroup` des `PackageReference`, ajouter :
```xml
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite" />
```

- [ ] **Step 4: Wire NTS at runtime** — dans `src/Taxi.Web.Api/Program.cs`, remplacer l'appel `builder.AddNpgsqlDbContext<AppDbContext>(...)` par :
```csharp
builder.AddNpgsqlDbContext<AppDbContext>(
    "taxidb",
    configureDbContextOptions: options => options
        .UseNpgsql(npgsql => npgsql.UseNetTopologySuite())
        .UseSnakeCaseNamingConvention());
```
NOTE: l'overload `UseNpgsql(Action<NpgsqlDbContextOptionsBuilder>)` (sans chaîne de connexion) **s'ajoute** à la connexion déjà configurée par Aspire ; il active le mapping NTS côté EF. `UseNetTopologySuite` vient de `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`.

- [ ] **Step 5: Wire NTS at design-time** — dans `src/Taxi.Infrastructure/Persistence/AppDbContextFactory.cs`, modifier l'appel `UseNpgsql` :
```csharp
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=taxidb;Username=postgres;Password=postgres",
                npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;
```
(Indispensable pour que `dotnet ef migrations add` sache mapper `Point` — la génération ne se connecte PAS à la base.)

- [ ] **Step 6: Declare the PostGIS extension** — dans `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`, dans `OnModelCreating`, juste après `base.OnModelCreating(modelBuilder);`, ajouter :
```csharp
        modelBuilder.HasPostgresExtension("postgis");
```

- [ ] **Step 7: Switch AppHost to a PostGIS image** — dans `Taxi.AppHost/AppHost.cs`, remplacer la ligne `var postgres = builder.AddPostgres("postgres").WithDataVolume();` par :
```csharp
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgis/postgis", "17-3.5")
    .WithDataVolume();
```
NOTE: `17-3.5` = Postgres 17 + PostGIS 3.5. Si l'instance Postgres existante n'est pas en v17, ajuster le tag majeur pour matcher (sinon le volume devra être recréé — voir Task 6).

- [ ] **Step 8: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (Aucun changement de schéma encore ; `HasPostgresExtension` sera matérialisé par la migration de la Task 2.)

- [ ] **Step 9: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): PostGIS + NetTopologySuite wiring (packages, EF/design-time, extension, AppHost image)"
```

---

## Task 2: Driver.LastLocation (geography) + config + migration

**Files:**
- Modify: `src/Taxi.Domain/Drivers/Driver.cs`
- Modify: `src/Taxi.Infrastructure/Persistence/Configurations/DriverConfiguration.cs`
- Generate: migration `AddDriverGeolocation`

- [ ] **Step 1: Replace the location fields in `Driver.cs`** — supprimer les 3 propriétés `LastLatitude`/`LastLongitude`/`LastLocationAt` actuelles **et** la méthode `UpdateLocation`, et mettre à la place :
  - en tête du fichier, ajouter le using : `using NetTopologySuite.Geometries;`
  - propriétés (à l'emplacement des anciennes `Last*`) :
```csharp
    public Point? LastLocation { get; private set; }
    public DateTime? LastLocationAt { get; private set; }

    public double? LastLatitude => LastLocation?.Y;
    public double? LastLongitude => LastLocation?.X;
```
  - méthode (à l'emplacement de l'ancienne `UpdateLocation`) :
```csharp
    public void UpdateLocation(double latitude, double longitude)
    {
        LastLocation = new Point(longitude, latitude) { SRID = 4326 };
        LastLocationAt = DateTime.UtcNow;
    }
```
NOTE: `LastLatitude`/`LastLongitude` sont désormais des propriétés **calculées** (en lecture seule, sans setter) → EF les ignore automatiquement (pas de setter mappable), et le handler/test du temps réel qui lisent `driver.LastLatitude` continuent de fonctionner. `Point(x=longitude, y=latitude)`.

- [ ] **Step 2: Configure `LastLocation` in `DriverConfiguration.cs`** — ajouter dans la méthode `Configure`, avant la fermeture :
```csharp
        builder.Property(d => d.LastLocation).HasColumnType("geography (Point)");
        builder.HasIndex(d => d.LastLocation).HasMethod("gist");
```
(`geography` → `ST_Distance`/`ST_DWithin` raisonnent en **mètres**. Index GiST pour la recherche spatiale.)

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: 0 errors.

- [ ] **Step 4: Generate the migration**
Run: `cd /c/prjRecherche/Taxi && dotnet ef migrations add AddDriverGeolocation --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations`
Expected: nouveau fichier `*_AddDriverGeolocation.cs`.

- [ ] **Step 5: Verify the migration content** — le `Up()` doit :
  - matérialiser l'extension : un `migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:postgis", ...)` (= `CREATE EXTENSION postgis`) ;
  - **DropColumn** `last_latitude` et `last_longitude` sur `drivers` ;
  - **AddColumn** `last_location` de type `geography (Point)` (nullable) sur `drivers` ;
  - **CreateIndex** sur `last_location` avec `.Annotation("Npgsql:IndexMethod", "gist")`.
  Si le `last_location` n'est pas en `geography` ou si l'extension n'apparaît pas, STOP et reporter (ne pas committer).

- [ ] **Step 6: Build again (migration compiles)**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: 0 errors.

- [ ] **Step 7: Run the test suite (no regression)**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: tous verts (70). En particulier `UpdateDriverLocationCommandHandlerTests` reste vert (lecture de `LastLatitude`/`LastLongitude` via les propriétés calculées ; `LastLocationAt` toujours renseigné).

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): driver geography location (PostGIS Point) + GiST index + migration"
```

---

## Task 3: IDriverLocator + FindNearestDrivers query (Application, TDD)

**Files:**
- Create: `src/Taxi.Application/Dispatch/IDriverLocator.cs`, `src/Taxi.Application/Dispatch/FindNearestDrivers/FindNearestDriversQuery.cs`, `FindNearestDriversQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Dispatch/FindNearestDriversQueryHandlerTests.cs`

- [ ] **Step 1: Create `IDriverLocator.cs`**
```csharp
namespace Taxi.Application.Dispatch;

public interface IDriverLocator
{
    Task<IReadOnlyList<NearbyDriver>> FindNearestAsync(
        double latitude, double longitude, double radiusMeters, int max, CancellationToken cancellationToken);
}

public sealed record NearbyDriver(
    int DriverId, string UserId, double DistanceMeters,
    double Latitude, double Longitude, string VehicleType);
```

- [ ] **Step 2: Create `FindNearestDriversQuery.cs`**
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.FindNearestDrivers;

public sealed record FindNearestDriversQuery(double Lat, double Lon, double RadiusMeters, int Max)
    : IQuery<IReadOnlyList<NearbyDriver>>;
```

- [ ] **Step 3: Write the failing test** — `tests/Taxi.Application.Tests/Dispatch/FindNearestDriversQueryHandlerTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.FindNearestDrivers;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class FindNearestDriversQueryHandlerTests
{
    [Fact]
    public async Task Should_return_drivers_from_locator()
    {
        var locator = new Mock<IDriverLocator>();
        locator.Setup(l => l.FindNearestAsync(11.58, 43.14, 5000, 10, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<NearbyDriver>
               {
                   new(1, "u-1", 120.5, 11.581, 43.141, "Taxi"),
                   new(2, "u-2", 800.0, 11.59, 43.15, "VTC"),
               });
        var handler = new FindNearestDriversQueryHandler(locator.Object);

        var result = await handler.Handle(new FindNearestDriversQuery(11.58, 43.14, 5000, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].DriverId.Should().Be(1);
        result.Value[0].DistanceMeters.Should().Be(120.5);
    }
}
```

- [ ] **Step 4: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 5: Create `FindNearestDriversQueryHandler.cs`**
```csharp
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.FindNearestDrivers;

internal sealed class FindNearestDriversQueryHandler(IDriverLocator locator)
    : IQueryHandler<FindNearestDriversQuery, IReadOnlyList<NearbyDriver>>
{
    public async Task<Result<IReadOnlyList<NearbyDriver>>> Handle(
        FindNearestDriversQuery query, CancellationToken cancellationToken)
    {
        var drivers = await locator.FindNearestAsync(
            query.Lat, query.Lon, query.RadiusMeters, query.Max, cancellationToken);
        return Result.Success(drivers);
    }
}
```

- [ ] **Step 6: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (1 nouveau test).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): IDriverLocator abstraction + FindNearestDrivers query (TDD)"
```

---

## Task 4: DriverLocator (Infra) + DI + endpoint

**Files:**
- Create: `src/Taxi.Infrastructure/Dispatch/DriverLocator.cs`
- Modify: `src/Taxi.Infrastructure/DependencyInjection.cs`
- Modify: `src/Taxi.Web.Api/Endpoints/Tags.cs`
- Create: `src/Taxi.Web.Api/Modules/Dispatch/DispatchEndpoints.cs`

- [ ] **Step 1: Create `DriverLocator.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Taxi.Application.Dispatch;
using Taxi.Infrastructure.Persistence;

namespace Taxi.Infrastructure.Dispatch;

internal sealed class DriverLocator(AppDbContext db) : IDriverLocator
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<NearbyDriver>> FindNearestAsync(
        double latitude, double longitude, double radiusMeters, int max, CancellationToken cancellationToken)
    {
        var pickup = new Point(longitude, latitude) { SRID = 4326 };
        var cutoff = DateTime.UtcNow - FreshnessWindow;

        return await db.Drivers
            .Where(d => d.IsAvailable
                && d.LastLocation != null
                && d.LastLocationAt >= cutoff
                && d.LastLocation.IsWithinDistance(pickup, radiusMeters))
            .OrderBy(d => d.LastLocation!.Distance(pickup))
            .Take(max)
            .Select(d => new NearbyDriver(
                d.Id,
                d.UserId,
                d.LastLocation!.Distance(pickup),
                d.LastLocation!.Y,
                d.LastLocation!.X,
                d.VehicleType))
            .ToListAsync(cancellationToken);
    }
}
```
NOTE: `IsWithinDistance` → `ST_DWithin` (utilise l'index GiST) ; `Distance` → `ST_Distance` en mètres (geography). Le `!` après le filtre `!= null` est sûr.

- [ ] **Step 2: Register `IDriverLocator` in `DependencyInjection.cs`** — `src/Taxi.Infrastructure/DependencyInjection.cs`, dans `AddInfrastructure`, avant `return services;` :
```csharp
        services.AddScoped<IDriverLocator, DriverLocator>();
```
Ajouter les usings : `using Taxi.Application.Dispatch;` et `using Taxi.Infrastructure.Dispatch;`

- [ ] **Step 3: Add `Dispatch` to `Tags.cs`** — `src/Taxi.Web.Api/Endpoints/Tags.cs`, ajouter la constante :
```csharp
    public const string Dispatch = "Dispatch";
```

- [ ] **Step 4: Create `DispatchEndpoints.cs`**
```csharp
using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.FindNearestDrivers;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Dispatch;

public sealed class DispatchEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dispatch/nearest-drivers", async (
            double lat, double lon,
            IQueryHandler<FindNearestDriversQuery, IReadOnlyList<NearbyDriver>> handler,
            CancellationToken ct,
            double? radius = null,
            int? max = null) =>
        {
            var query = new FindNearestDriversQuery(lat, lon, radius ?? 5000, max ?? 10);
            return (await handler.Handle(query, ct)).ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin))
        .WithName("NearestDrivers")
        .WithTags(Tags.Dispatch)
        .WithSummary("Chauffeurs disponibles les plus proches")
        .WithDescription("Renvoie les chauffeurs disponibles dans le rayon (m, défaut 5000), triés par distance, limités à max (défaut 10).");
    }
}
```

- [ ] **Step 5: Build + full test suite**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; tous les tests verts (71).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(dispatch): DriverLocator (PostGIS proximity) + DI + nearest-drivers endpoint"
```

---

## Task 5: Vérification manuelle (USER — PostGIS + Docker)

> Cette phase change l'image Postgres (PostGIS) → **le volume Taxi doit probablement être recréé**. La migration applique `CREATE EXTENSION postgis` + la colonne geography au démarrage (`MigrateAsync`).

- [ ] **Step 1: Recréer le volume Taxi si nécessaire**
Au démarrage de l'AppHost, si le conteneur Postgres ne démarre pas (incompatibilité d'image) ou si la migration échoue : arrêter l'AppHost, supprimer **uniquement** le volume de données Taxi (vérifier son nom : `docker volume ls | grep -i taxi`), puis redémarrer. ⚠️ Ne pas toucher aux volumes des autres projets (skoleo, iqrainstitut) qui partagent la même instance Docker.

- [ ] **Step 2: Démarrer + appliquer la migration**
F5 sur Taxi.AppHost (ou `dotnet run`). Au démarrage, `MigrateAsync` applique `AddDriverGeolocation` (CREATE EXTENSION postgis + colonne `last_location` + index GiST). Vérifier l'absence d'erreur de mapping NTS au démarrage.

- [ ] **Step 3: Préparer des chauffeurs avec positions (REST + hub)**
  1. Register 3 Drivers + profils ; en rendre 2 **disponibles** (`POST /api/drivers/set-availability`), 1 indisponible.
  2. Via le client SignalR (`SendDriverLocation`), envoyer des positions connues autour de Djibouti :
     - Driver A (dispo) : `lat 11.585, lon 43.145` (proche du point de test).
     - Driver B (dispo) : `lat 11.610, lon 43.160` (plus loin).
     - Driver C (indispo) : `lat 11.586, lon 43.146` (très proche mais indisponible).

- [ ] **Step 4: Interroger le moteur de matching**
`GET /api/dispatch/nearest-drivers?lat=11.588&lon=43.145&radius=5000&max=10` (token **Admin**).
**Attendu :**
  - Driver A et B renvoyés, **A avant B** (tri par `distanceMeters` croissant), `distanceMeters` cohérents (ordre de grandeur en mètres : A ~ quelques centaines de m).
  - Driver C **absent** (indisponible).
  - Avec `radius=300` → seul A reste (B hors rayon).

- [ ] **Step 5: Fraîcheur (optionnel)**
Si une position date de plus de 10 min, le chauffeur correspondant n'apparaît plus (filtre de fraîcheur). Vérifiable en attendant ou en notant le comportement.

- [ ] **Step 6: Confirmer les résultats à l'utilisateur.** Aucun commit (vérification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : 71 verts.
- [ ] Migration `AddDriverGeolocation` (CREATE EXTENSION postgis + `last_location` geography + index GiST) appliquée au démarrage.
- [ ] `GET /api/dispatch/nearest-drivers` renvoie les chauffeurs **disponibles** dans le rayon, triés par distance croissante, en mètres ; exclut indisponibles / hors rayon / positions périmées.
- [ ] Tout committé sur `main`.

## Suite (Phase 2 — auto-assignation)

États d'offre (`OfferedDriverId`/`OfferExpiresAt`), offre au plus proche via `IDriverLocator`, endpoint de refus, timeout + réattribution (background service), notifications SignalR d'offre, cas « aucun chauffeur ».
