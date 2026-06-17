# Module Temps réel — Phase 1 (position chauffeur) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Diffuser en temps réel la position du chauffeur pendant une course (SignalR), avec persistance de la dernière position sur le profil chauffeur.

**Architecture:** Hub SignalR in-process (`/hubs/ride`) qui reste **mince** : il délègue validation+persistance à un command handler Application (`UpdateDriverLocationCommand`) et l'autorisation d'accès à une course à un query handler (`RideAccessQuery`), puis diffuse `driverLocationUpdated` aux groupes. Le hub ne touche jamais au `DbContext`. Auth WebSocket via JWT en query string.

**Tech Stack:** .NET 10, ASP.NET Core SignalR (in-process, framework partagé — aucun package à ajouter), EF Core 10, Ardalis.Specification, xUnit/Moq/FluentAssertions.

**Spec :** `docs/superpowers/specs/2026-06-17-realtime-signalr-design.md` (Phase 1 uniquement ; le push des statuts = Phase 2).
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 60 tests verts au départ.

> **Portée Phase 1 :** entité+migration, command handler (TDD), query d'accès (TDD), hub + JWT query string + câblage. **Pas** de `IRealtimeNotifier` ni de modif des handlers Courses (= Phase 2).

---

## Structure de fichiers cible

```
src/Taxi.Domain/Drivers/Driver.cs                                              — modifié (Last* + UpdateLocation)
src/Taxi.Infrastructure/Persistence/Migrations/*_AddDriverLocation.cs          — généré
src/Taxi.Application/Realtime/RealtimeErrors.cs                                — créé
src/Taxi.Application/Realtime/UpdateDriverLocation/UpdateDriverLocationCommand.cs   — créé
src/Taxi.Application/Realtime/UpdateDriverLocation/DriverLocationBroadcast.cs       — créé
src/Taxi.Application/Realtime/UpdateDriverLocation/UpdateDriverLocationCommandHandler.cs — créé
src/Taxi.Application/Realtime/RideAccess/RideAccessQuery.cs                    — créé
src/Taxi.Application/Realtime/RideAccess/RideAccessQueryHandler.cs             — créé
src/Taxi.Web.Api/Realtime/DriverLocationDto.cs                                — créé
src/Taxi.Web.Api/Realtime/RideHub.cs                                          — créé
src/Taxi.Infrastructure/Identity/DependencyInjection.cs                       — modifié (JWT OnMessageReceived)
src/Taxi.Web.Api/Program.cs                                                   — modifié (AddSignalR + MapHub)
tests/Taxi.Application.Tests/Realtime/UpdateDriverLocationCommandHandlerTests.cs — créé
tests/Taxi.Application.Tests/Realtime/RideAccessQueryHandlerTests.cs          — créé
```

---

## Task 1: Domain — Driver.UpdateLocation + migration

**Files:**
- Modify: `src/Taxi.Domain/Drivers/Driver.cs`
- Generate: migration `AddDriverLocation`

> Les 3 propriétés sont des scalaires nullable → EF les mappe automatiquement, **aucune** modif de `DriverConfiguration` nécessaire. Le comportement de `UpdateLocation` est couvert par le test du handler (Task 3).

- [ ] **Step 1: Add fields + method to `Driver.cs`** — ajouter les 3 propriétés après `public double AverageRating { get; private set; }` (ligne 12) :
```csharp
    public double? LastLatitude { get; private set; }
    public double? LastLongitude { get; private set; }
    public DateTime? LastLocationAt { get; private set; }
```
et ajouter la méthode après `UpdateAverageRating` (fin de classe) :
```csharp
    public void UpdateLocation(double latitude, double longitude)
    {
        LastLatitude = latitude;
        LastLongitude = longitude;
        LastLocationAt = DateTime.UtcNow;
    }
```

- [ ] **Step 2: Build the solution**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Generate the migration**
Run: `cd /c/prjRecherche/Taxi && dotnet ef migrations add AddDriverLocation --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations`
Expected: nouveau fichier `*_AddDriverLocation.cs` dans `src/Taxi.Infrastructure/Persistence/Migrations`, ajoutant 3 colonnes nullable (`last_latitude`, `last_longitude`, `last_location_at`) à la table `drivers`. (Le `AppDbContextFactory` design-time fournit le contexte ; si `dotnet ef` manque : `dotnet tool install --global dotnet-ef`.)

- [ ] **Step 4: Verify the migration content**
Ouvrir le fichier généré : confirmer `AddColumn` pour les 3 colonnes (snake_case) sur `drivers`, et que `Down()` les supprime. Aucune autre table touchée.

- [ ] **Step 5: Build again (migration compiles)**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: 0 errors.

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): driver last-known location + AddDriverLocation migration"
```

---

## Task 2: Application — messaging types (errors, command, broadcast, query)

**Files:**
- Create: `src/Taxi.Application/Realtime/RealtimeErrors.cs`
- Create: `src/Taxi.Application/Realtime/UpdateDriverLocation/UpdateDriverLocationCommand.cs`
- Create: `src/Taxi.Application/Realtime/UpdateDriverLocation/DriverLocationBroadcast.cs`
- Create: `src/Taxi.Application/Realtime/RideAccess/RideAccessQuery.cs`

- [ ] **Step 1: Create `RealtimeErrors.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Application.Realtime;

public static class RealtimeErrors
{
    public static readonly Error DriverNotFound = Error.NotFound("Realtime.DriverNotFound", "Profil chauffeur introuvable.");
    public static readonly Error RideNotAssigned = Error.Forbidden("Realtime.RideNotAssigned", "Cette course n'est pas assignée à ce chauffeur.");
    public static readonly Error RideNotActive = Error.Conflict("Realtime.RideNotActive", "Cette course est terminée ou annulée.");
}
```

- [ ] **Step 2: Create `UpdateDriverLocationCommand.cs`**
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.UpdateDriverLocation;

public sealed record UpdateDriverLocationCommand(
    string DriverUserId,
    int RideId,
    double Latitude,
    double Longitude,
    double? Heading,
    double? Speed) : ICommand<DriverLocationBroadcast>;
```

- [ ] **Step 3: Create `DriverLocationBroadcast.cs`**
```csharp
namespace Taxi.Application.Realtime.UpdateDriverLocation;

public sealed record DriverLocationBroadcast(
    int RideId,
    string ClientId,
    int DriverId,
    double Latitude,
    double Longitude,
    double? Heading,
    double? Speed,
    DateTime SentAt);
```

- [ ] **Step 4: Create `RideAccessQuery.cs`**
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.RideAccess;

public sealed record RideAccessQuery(int RideId, string UserId, bool IsAdmin) : IQuery<bool>;
```

- [ ] **Step 5: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: 0 errors.

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): realtime messaging types (command, broadcast, access query, errors)"
```

---

## Task 3: Application — UpdateDriverLocationCommandHandler (TDD)

**Files:**
- Create: `src/Taxi.Application/Realtime/UpdateDriverLocation/UpdateDriverLocationCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Realtime/UpdateDriverLocationCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests** — `tests/Taxi.Application.Tests/Realtime/UpdateDriverLocationCommandHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Realtime.UpdateDriverLocation;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Realtime;

public class UpdateDriverLocationCommandHandlerTests
{
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRepository<Ride>> _rides = new();

    private UpdateDriverLocationCommandHandler Handler() => new(_drivers.Object, _rides.Object);

    private static Driver DriverProfile() => Driver.Create("driver-user", "LIC", "PLATE", "Taxi");

    // Ride assignée au driver (Id=0) et active (Accepted)
    private static Ride AssignedActiveRide()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(0); // DriverId = 0 (= driver.Id par défaut), Status = Accepted
        return ride;
    }

    private static UpdateDriverLocationCommand Command()
        => new("driver-user", 1, 11.58, 43.14, 90.0, 30.0);

    [Fact]
    public async Task Should_persist_location_and_return_broadcast()
    {
        var driver = DriverProfile();
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(driver);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AssignedActiveRide());

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientId.Should().Be("client-1");
        result.Value.Latitude.Should().Be(11.58);
        result.Value.Longitude.Should().Be(43.14);
        driver.LastLatitude.Should().Be(11.58);
        driver.LastLongitude.Should().Be(43.14);
        driver.LastLocationAt.Should().NotBeNull();
        _drivers.Verify(d => d.UpdateAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_fail_when_driver_profile_missing()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.DriverNotFound);
    }

    [Fact]
    public async Task Should_fail_when_ride_missing()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Ride?)null);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotFound);
    }

    [Fact]
    public async Task Should_fail_when_ride_not_assigned_to_driver()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(5); // DriverId = 5 != driver.Id (0)
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.RideNotAssigned);
    }

    [Fact]
    public async Task Should_fail_when_ride_completed()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        ride.Accept(0);
        ride.MarkArrived();
        ride.Start();
        ride.Complete(); // Status = Completed
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverProfile());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RealtimeErrors.RideNotActive);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 3: Create `UpdateDriverLocationCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.UpdateDriverLocation;

internal sealed class UpdateDriverLocationCommandHandler(
    IRepository<Driver> drivers,
    IRepository<Ride> rides)
    : ICommandHandler<UpdateDriverLocationCommand, DriverLocationBroadcast>
{
    public async Task<Result<DriverLocationBroadcast>> Handle(
        UpdateDriverLocationCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.DriverNotFound);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<DriverLocationBroadcast>(RideErrors.NotFound);

        if (ride.DriverId != driver.Id)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.RideNotAssigned);

        if (ride.Status is RideStatus.Completed or RideStatus.Cancelled)
            return Result.Failure<DriverLocationBroadcast>(RealtimeErrors.RideNotActive);

        driver.UpdateLocation(command.Latitude, command.Longitude);
        await drivers.UpdateAsync(driver, cancellationToken);

        return new DriverLocationBroadcast(
            ride.Id, ride.ClientId, driver.Id,
            command.Latitude, command.Longitude, command.Heading, command.Speed,
            DateTime.UtcNow);
    }
}
```
NOTE: `DriverByUserIdSpec` (namespace `Taxi.Application.Drivers`) et `RideByIdSpec` (namespace `Taxi.Application.Rides`) sont `internal` mais dans le même assembly `Taxi.Application` → accessibles. La conversion implicite `DriverLocationBroadcast` → `Result<DriverLocationBroadcast>` fonctionne (valeur unique, comme `RatingDto` dans `RateRideCommandHandler`).

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (5 nouveaux tests).

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): UpdateDriverLocation command handler (TDD)"
```

---

## Task 4: Application — RideAccessQueryHandler (TDD)

**Files:**
- Create: `src/Taxi.Application/Realtime/RideAccess/RideAccessQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Realtime/RideAccessQueryHandlerTests.cs`

- [ ] **Step 1: Write the failing tests** — `tests/Taxi.Application.Tests/Realtime/RideAccessQueryHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime.RideAccess;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Realtime;

public class RideAccessQueryHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private RideAccessQueryHandler Handler() => new(_rides.Object, _drivers.Object);

    [Fact]
    public async Task Admin_always_has_access()
    {
        var result = await Handler().Handle(new RideAccessQuery(1, "anyone", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _rides.Verify(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Client_of_the_ride_has_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new RideAccessQuery(1, "client-1", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Unrelated_user_has_no_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(new RideAccessQuery(1, "stranger", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_ride_means_no_access()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Ride?)null);

        var result = await Handler().Handle(new RideAccessQuery(1, "client-1", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 3: Create `RideAccessQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Realtime.RideAccess;

internal sealed class RideAccessQueryHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : IQueryHandler<RideAccessQuery, bool>
{
    public async Task<Result<bool>> Handle(RideAccessQuery query, CancellationToken cancellationToken)
    {
        if (query.IsAdmin)
            return true;

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(query.RideId), cancellationToken);
        if (ride is null)
            return false;

        if (ride.ClientId == query.UserId)
            return true;

        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
        return driver is not null && ride.DriverId == driver.Id;
    }
}
```
NOTE: conversion implicite `bool` → `Result<bool>` (valeur unique). Specs `internal` accessibles (même assembly).

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (4 nouveaux tests).

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): RideAccess query handler for hub group authorization (TDD)"
```

---

## Task 5: Web.Api — RideHub + JWT query string + câblage

**Files:**
- Create: `src/Taxi.Web.Api/Realtime/DriverLocationDto.cs`
- Create: `src/Taxi.Web.Api/Realtime/RideHub.cs`
- Modify: `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`
- Modify: `src/Taxi.Web.Api/Program.cs`

- [ ] **Step 1: Create `DriverLocationDto.cs`**
```csharp
namespace Taxi.Web.Api.Realtime;

public sealed record DriverLocationDto(int RideId, double Latitude, double Longitude, double? Heading, double? Speed);
```

- [ ] **Step 2: Create `RideHub.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Taxi.Application.Realtime.RideAccess;
using Taxi.Application.Realtime.UpdateDriverLocation;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Realtime;

[Authorize]
public sealed class RideHub(
    ICommandHandler<UpdateDriverLocationCommand, DriverLocationBroadcast> locationHandler,
    IQueryHandler<RideAccessQuery, bool> accessHandler) : Hub
{
    public Task JoinDriversGroup()
        => Groups.AddToGroupAsync(Context.ConnectionId, "Drivers");

    public Task JoinAdminsGroup()
    {
        if (!Context.User!.IsInRole(RoleNames.Admin))
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
    }

    public Task JoinClientGroup(string clientId)
    {
        var userId = Context.User!.GetUserId();
        if (!Context.User.IsInRole(RoleNames.Admin) && clientId != userId)
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, $"Client_{clientId}");
    }

    public async Task JoinRideGroup(int rideId)
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            return;

        var isAdmin = Context.User.IsInRole(RoleNames.Admin);
        var access = await accessHandler.Handle(new RideAccessQuery(rideId, userId, isAdmin), Context.ConnectionAborted);
        if (access.IsSuccess && access.Value)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Ride_{rideId}");
    }

    public async Task SendDriverLocation(DriverLocationDto location)
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            throw new HubException("Utilisateur non authentifié.");

        var command = new UpdateDriverLocationCommand(
            userId, location.RideId, location.Latitude, location.Longitude, location.Heading, location.Speed);

        var result = await locationHandler.Handle(command, Context.ConnectionAborted);
        if (!result.IsSuccess)
            throw new HubException(result.Error.Description);

        var payload = result.Value;
        await Clients.Group($"Client_{payload.ClientId}").SendAsync("driverLocationUpdated", payload);
        await Clients.Group($"Ride_{payload.RideId}").SendAsync("driverLocationUpdated", payload);
        await Clients.Group("Admins").SendAsync("driverLocationUpdated", payload);
    }
}
```
NOTE: `GetUserId()` est l'extension existante (`Taxi.Web.Api.Endpoints.ClaimsPrincipalExtensions`, renvoie `string?` depuis le claim `sub`). `IsInRole` fonctionne car `TokenService` émet le rôle en `ClaimTypes.Role`. `RoleNames` est dans `Taxi.Domain.Identity`. Le `userId` du chauffeur vient **toujours** du token (`Context.User`), jamais du payload.

- [ ] **Step 3: Add JWT query-string auth in `DependencyInjection.cs`** — dans `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`, sur le `AddJwtBearer(options => { ... })`, ajouter (après l'affectation de `options.TokenValidationParameters`, avant la fermeture du lambda) :
```csharp
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/ride"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
```
`JwtBearerEvents` est dans `Microsoft.AspNetCore.Authentication.JwtBearer` (déjà importé en tête du fichier).

- [ ] **Step 4: Wire SignalR in `Program.cs`**
  - Ajouter le using en tête : `using Taxi.Web.Api.Realtime;`
  - Après `builder.Services.AddEndpoints();`, ajouter : `builder.Services.AddSignalR();`
  - Après `app.MapEndpoints();` (avant `app.Run();`), ajouter : `app.MapHub<RideHub>("/hubs/ride");`

- [ ] **Step 5: Build + full test suite**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; tous les tests verts (60 + 5 + 4 = 69).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): RideHub (driver location broadcast) + JWT query-string auth + SignalR wiring"
```

---

## Task 6: Vérification manuelle

> La migration `AddDriverLocation` ne casse rien (colonnes nullable) ; `MigrateAsync` au démarrage l'applique automatiquement. Démarrer l'AppHost (F5 sur Taxi.AppHost, ou `dotnet run`). Tester le hub avec un client SignalR — un script Node `@microsoft/signalr` ou un client .NET console. (Pas besoin du frontend.)

- [ ] **Step 1: Préparer les données via REST (Scalar)**
  1. Register un **Client** (`POST /api/auth/register` role=Client) → garder son `accessToken` (token CLIENT) et son `user.id` (= clientId).
  2. Register un **Driver** (role=Driver), créer son profil (`POST /api/drivers`), le rendre dispo. Garder son `accessToken` (token DRIVER).
  3. Le client demande une course (`POST /api/rides/request`) → noter le `rideId`. Le driver l'accepte (`POST /api/rides/{rideId}/accept`) → la course est `Accepted` (active, assignée).

- [ ] **Step 2: Le client s'abonne**
Connecter un client SignalR à `/hubs/ride?access_token=<token CLIENT>`, puis appeler `JoinRideGroup(rideId)` (et/ou `JoinClientGroup(<clientId>)`). Enregistrer le handler `driverLocationUpdated`.

- [ ] **Step 3: Le chauffeur envoie sa position**
Connecter un 2e client SignalR avec `?access_token=<token DRIVER>`, appeler `SendDriverLocation({ rideId, latitude: 11.58, longitude: 43.14, heading: 90, speed: 30 })`.
**Attendu :** le client (Step 2) reçoit `driverLocationUpdated` avec le payload (rideId, clientId, driverId, coords, sentAt).

- [ ] **Step 4: Persistance**
`GET /api/drivers/me` (token DRIVER) **ou** inspecter la table `drivers` : `last_latitude=11.58`, `last_longitude=43.14`, `last_location_at` renseigné.
*(Si `GET /api/drivers/me` n'expose pas ces champs, vérifier directement en base — c'est suffisant.)*

- [ ] **Step 5: Sécurité**
  - Avec le token CLIENT, tenter `SendDriverLocation(...)` → **HubException** (« Profil chauffeur introuvable » ou « non assignée »).
  - Avec un 2e client (autre user), `JoinRideGroup(rideId)` d'une course qui n'est pas la sienne → pas ajouté (ne reçoit aucun `driverLocationUpdated` quand le chauffeur envoie).

- [ ] **Step 6: Confirmer les résultats à l'utilisateur.** Aucun commit (vérification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : 69 tests verts.
- [ ] Migration `AddDriverLocation` générée et appliquée au démarrage.
- [ ] Un chauffeur connecté au hub diffuse sa position → le client abonné la reçoit (`driverLocationUpdated`).
- [ ] La dernière position est persistée sur `Driver` (`last_latitude/longitude/at`).
- [ ] Contrôles d'accès : un non-chauffeur ne peut pas envoyer de position ; un tiers ne rejoint pas un `Ride_{id}` qui n'est pas le sien.
- [ ] Tout committé sur `main`.

## Suite (Phase 2)

`IRealtimeNotifier` + `SignalRRealtimeNotifier`, câblage des handlers Courses (`newPendingRide` au groupe `Drivers`, `rideStatusChanged` aux abonnés sur chaque transition).
