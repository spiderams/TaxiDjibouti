# Module Temps réel — Phase 2 (push des statuts) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pousser en temps réel les changements de statut de course (nouvelle course en attente → chauffeurs ; transitions → client/course/admin) via SignalR.

**Architecture:** Abstraction `IRealtimeNotifier` (Application, sans dépendance SignalR) implémentée en Web.Api (`SignalRRealtimeNotifier` via `IHubContext<RideHub>`). Les handlers Courses existants reçoivent `IRealtimeNotifier` et notifient **après** persistance réussie. La notification est **best-effort** : l'implémentation avale les exceptions (log) pour ne jamais faire échouer la commande.

**Tech Stack:** .NET 10, ASP.NET Core SignalR (in-process), CQRS maison, xUnit/Moq/FluentAssertions.

**Spec :** `docs/superpowers/specs/2026-06-17-realtime-signalr-design.md` (Phase 2).
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 69 tests verts au départ. Phase 1 (hub + position) déjà livrée.

> **Portée Phase 2 :** `IRealtimeNotifier` + impl + DI, câblage des 5 handlers (RequestRide, Accept, MarkArrived, Start, Complete, Cancel), mise à jour des tests. Pas de migration, pas de nouvelle entité.

---

## Structure de fichiers cible

```
src/Taxi.Application/Realtime/IRealtimeNotifier.cs                       — créé
src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs                     — créé
src/Taxi.Web.Api/Program.cs                                             — modifié (DI notifier)
src/Taxi.Application/Rides/Request/RequestRideCommandHandler.cs          — modifié
src/Taxi.Application/Rides/Accept/AcceptRideCommandHandler.cs            — modifié
src/Taxi.Application/Rides/Transitions/MarkArrivedCommandHandler.cs      — modifié
src/Taxi.Application/Rides/Transitions/StartRideCommandHandler.cs        — modifié
src/Taxi.Application/Rides/Transitions/CompleteRideCommandHandler.cs     — modifié
src/Taxi.Application/Rides/Cancel/CancelRideCommandHandler.cs            — modifié
tests/Taxi.Application.Tests/Rides/RequestRideHandlerTests.cs            — modifié
tests/Taxi.Application.Tests/Rides/AcceptRideHandlerTests.cs             — modifié
tests/Taxi.Application.Tests/Rides/RideTransitionsHandlerTests.cs        — modifié
tests/Taxi.Application.Tests/Rides/CancelRideHandlerTests.cs            — modifié
```

---

## Task 1: IRealtimeNotifier + SignalRRealtimeNotifier + DI

**Files:**
- Create: `src/Taxi.Application/Realtime/IRealtimeNotifier.cs`
- Create: `src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs`
- Modify: `src/Taxi.Web.Api/Program.cs`

- [ ] **Step 1: Create `IRealtimeNotifier.cs`**
```csharp
namespace Taxi.Application.Realtime;

public interface IRealtimeNotifier
{
    Task RideStatusChangedAsync(int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken);
    Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create `SignalRRealtimeNotifier.cs`** (best-effort : try/catch + log)
```csharp
using Microsoft.AspNetCore.SignalR;
using Taxi.Application.Realtime;

namespace Taxi.Web.Api.Realtime;

internal sealed class SignalRRealtimeNotifier(
    IHubContext<RideHub> hub,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task RideStatusChangedAsync(
        int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { rideId, status, driverId };
            await hub.Clients.Group($"Client_{clientId}").SendAsync("rideStatusChanged", payload, cancellationToken);
            await hub.Clients.Group($"Ride_{rideId}").SendAsync("rideStatusChanged", payload, cancellationToken);
            await hub.Clients.Group("Admins").SendAsync("rideStatusChanged", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (rideStatusChanged) failed for ride {RideId}", rideId);
        }
    }

    public async Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group("Drivers").SendAsync("newPendingRide", new { rideId }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (newPendingRide) failed for ride {RideId}", rideId);
        }
    }
}
```
NOTE: `IHubContext<RideHub>` est singleton-friendly ; `RideHub` est dans le même projet (`Taxi.Web.Api.Realtime`). L'impl est `internal` → enregistrée explicitement (étape suivante).

- [ ] **Step 3: Register the notifier in `Program.cs`** — après la ligne `builder.Services.AddSignalR();`, ajouter :
```csharp
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
```
Et ajouter le using en tête si absent : `using Taxi.Application.Realtime;`

- [ ] **Step 4: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (Les tests restent verts : aucun handler ne dépend encore du notifier.)

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(realtime): IRealtimeNotifier abstraction + SignalR implementation (best-effort) + DI"
```

---

## Task 2: Wire RequestRide → newPendingRide (TDD)

**Files:**
- Modify: `src/Taxi.Application/Rides/Request/RequestRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RequestRideHandlerTests.cs`

- [ ] **Step 1: Update the test** — remplacer **tout** le contenu de `tests/Taxi.Application.Tests/Rides/RequestRideHandlerTests.cs` par :
```csharp
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Request;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RequestRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>> _pricing = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private RequestRideCommandHandler Handler() => new(_rides.Object, _pricing.Object, _notifier.Object);

    private void PriceReturns(decimal price) =>
        _pricing.Setup(p => p.Handle(It.IsAny<EstimatePriceQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(new EstimatePriceResponse("Centre-ville", "Balbala", price)));

    [Fact]
    public async Task Should_create_pending_ride_with_estimated_price()
    {
        PriceReturns(1500m);

        var result = await Handler().Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Pending");
        result.Value.EstimatedPrice.Should().Be(1500m);
        result.Value.ClientId.Should().Be("client-1");
        _rides.Verify(r => r.AddAsync(It.IsAny<Ride>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_notify_drivers_of_new_pending_ride()
    {
        PriceReturns(1500m);

        await Handler().Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null), CancellationToken.None);

        _notifier.Verify(n => n.NewPendingRideAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (constructeur à 2 args + appel notifier absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 3: Update `RequestRideCommandHandler.cs`** — remplacer le constructeur et l'appel `AddAsync` :
  - Ajouter le using : `using Taxi.Application.Realtime;`
  - Constructeur :
```csharp
internal sealed class RequestRideCommandHandler(
    IRepository<Ride> rides,
    IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> priceEstimator,
    IRealtimeNotifier notifier)
    : ICommandHandler<RequestRideCommand, RideDto>
```
  - Remplacer la fin du `Handle` (le bloc `await rides.AddAsync(...); return RideDto.From(ride);`) par :
```csharp
        await rides.AddAsync(ride, cancellationToken);
        await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
        return RideDto.From(ride);
```

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (1 nouveau test).

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(rides): notify drivers group on new pending ride"
```

---

## Task 3: Wire transitions → rideStatusChanged (TDD)

**Files:**
- Modify: `src/Taxi.Application/Rides/Accept/AcceptRideCommandHandler.cs`, `Transitions/MarkArrivedCommandHandler.cs`, `Transitions/StartRideCommandHandler.cs`, `Transitions/CompleteRideCommandHandler.cs`, `Cancel/CancelRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/AcceptRideHandlerTests.cs`, `RideTransitionsHandlerTests.cs`, `CancelRideHandlerTests.cs`

> Les 5 handlers reçoivent le même nouveau paramètre `IRealtimeNotifier notifier` et le même appel après `await rides.UpdateAsync(ride, cancellationToken);` :
> ```csharp
>         await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
> ```
> et le using `using Taxi.Application.Realtime;`.

- [ ] **Step 1: Update `AcceptRideHandlerTests.cs`** — remplacer le contenu par :
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides.Accept;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class AcceptRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private AcceptRideCommandHandler Handler() => new(_rides.Object, _drivers.Object, _notifier.Object);

    private static Driver AvailableDriver()
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        d.SetAvailability(true);
        return d;
    }

    [Fact]
    public async Task Should_accept_when_driver_available_and_ride_pending()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AvailableDriver());
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("c", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new AcceptRideCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");
        _rides.Verify(r => r.UpdateAsync(It.IsAny<Ride>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.RideStatusChangedAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "Accepted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_fail_when_no_driver_profile()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Driver?)null);

        var result = await Handler().Handle(new AcceptRideCommand(1, "x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NoDriverProfile);
    }

    [Fact]
    public async Task Should_fail_when_driver_not_available()
    {
        var unavailable = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(unavailable);

        var result = await Handler().Handle(new AcceptRideCommand(1, "driver-user"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.DriverNotAvailable);
    }
}
```

- [ ] **Step 2: Update `RideTransitionsHandlerTests.cs`** — ajouter le notifier. Ajouter le using `using Taxi.Application.Realtime;`, ajouter le champ `private readonly Mock<IRealtimeNotifier> _notifier = new();` après `_drivers`, et remplacer les **deux** `new MarkArrivedCommandHandler(_rides.Object, _drivers.Object)` par `new MarkArrivedCommandHandler(_rides.Object, _drivers.Object, _notifier.Object)`. Dans le test `MarkArrived_should_succeed_for_assigned_driver`, ajouter avant la fin :
```csharp
        _notifier.Verify(n => n.RideStatusChangedAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "DriverArrived", It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 3: Update `CancelRideHandlerTests.cs`** — ajouter le using `using Taxi.Application.Realtime;`, ajouter le champ `private readonly Mock<IRealtimeNotifier> _notifier = new();` après `_drivers`, et remplacer `private CancelRideCommandHandler Handler() => new(_rides.Object, _drivers.Object);` par `private CancelRideCommandHandler Handler() => new(_rides.Object, _drivers.Object, _notifier.Object);`

- [ ] **Step 4: Run — expect FAIL** (constructeurs et appels notifier pas encore en place)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 5: Update the 5 handlers.** Pour CHACUN, ajouter `using Taxi.Application.Realtime;`, ajouter `IRealtimeNotifier notifier` au constructeur, et insérer l'appel notifier juste après `await rides.UpdateAsync(ride, cancellationToken);`.

`AcceptRideCommandHandler.cs` — constructeur :
```csharp
internal sealed class AcceptRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<AcceptRideCommand, RideDto>
```
fin du `Handle` :
```csharp
        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
```

`MarkArrivedCommandHandler.cs`, `StartRideCommandHandler.cs`, `CompleteRideCommandHandler.cs` — même modification : ajouter `IRealtimeNotifier notifier` comme **3e** paramètre du constructeur (après `IRepository<Driver> drivers`), `using Taxi.Application.Realtime;`, et insérer l'appel notifier après `await rides.UpdateAsync(ride, cancellationToken);` (avant `return RideDto.From(ride);`) :
```csharp
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
```

`CancelRideCommandHandler.cs` — constructeur :
```csharp
internal sealed class CancelRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<CancelRideCommand, RideDto>
```
fin du `Handle` :
```csharp
        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
```

- [ ] **Step 6: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts.

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(rides): push rideStatusChanged on accept/arrived/start/complete/cancel"
```

---

## Task 4: Build complet + vérification manuelle

- [ ] **Step 1: Build + full suite**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; tous les tests verts (69 + 2 nouveaux ≈ 71).

- [ ] **Step 2: Manual verification (USER — Docker + client SignalR Node de la Phase 1)**

> Pas de migration. Démarrer l'AppHost. Réutiliser le script Node `@microsoft/signalr` de la Phase 1.

  1. **`rideStatusChanged`** : un client connecté qui a fait `JoinRideGroup(rideId)` (ou `JoinClientGroup(clientId)`) enregistre `connection.on("rideStatusChanged", ...)`. Déclencher une transition via REST : `POST /api/rides/{rideId}/accept` (token Driver) → le client reçoit `rideStatusChanged` avec `{ rideId, status: "Accepted", driverId }`. Idem pour `/arrived`, `/start`, `/complete`, `/cancel`.
  2. **`newPendingRide`** : un client connecté en `JoinDriversGroup()` enregistre `connection.on("newPendingRide", ...)`. Un client demande une course (`POST /api/rides/request`) → le groupe Drivers reçoit `newPendingRide` avec `{ rideId }`.
  3. **Best-effort** : optionnel — vérifier qu'une commande REST réussit (200) même sans aucun client SignalR connecté (la notification ne casse rien).

- [ ] **Step 3: Confirmer les résultats à l'utilisateur.** Aucun commit (vérification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] Une nouvelle course en attente notifie le groupe `Drivers` (`newPendingRide`).
- [ ] Chaque transition (accept/arrived/start/complete/cancel) pousse `rideStatusChanged` aux abonnés (client/course/admin).
- [ ] Une commande REST reste fonctionnelle même si la notification échoue (best-effort).
- [ ] Tout committé sur `main`. **→ portage backend du legacy 100 % terminé + extension temps réel.**

## Suite (au-delà du legacy)

Dispatch (matching proximité PostGIS, s'appuiera sur la position persistée en Phase 1), Identité Phase 3 (documents chauffeur/Blob), stubs Paiement+Notifications, puis **frontend React** (dernière phase : contrat auth + endpoints + branchement carte SignalR + CORS).
