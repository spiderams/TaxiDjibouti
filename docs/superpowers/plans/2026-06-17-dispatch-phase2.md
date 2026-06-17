# Module Dispatch — Phase 2 (auto-assignation) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Assigner automatiquement la course au chauffeur disponible le plus proche via une offre séquentielle (timeout 30 s, réattribution), avec retour au flux manuel `Pending` si personne.

**Architecture:** Nouvel état `Offered` + champs d'offre sur `Ride`. Orchestrateur `IRideDispatcher` (Application, sur `IDriverLocator`/`IRepository<Ride>`/`IRealtimeNotifier`). Endpoints accept-offer/decline-offer (Driver). `OfferTimeoutService` (background, Infra) gère les timeouts. Offre ciblée via groupe SignalR `DriverUser_{userId}`. Disponibilité chauffeur liée au cycle.

**Tech Stack:** .NET 10, EF Core 10, SignalR, CQRS maison, Ardalis.Specification, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-17-dispatch-phase2-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 71 tests verts au départ. Dispatch Phase 1 (`IDriverLocator`) livré.

> **Portée :** flux complet offre/timeout/réattribution + disponibilité. Hors périmètre : pondération note/ETA, FCM, limite du nb de réattributions.

---

## Task 1: Domaine — état Offered, champs & méthodes d'offre, erreurs (TDD)

**Files:**
- Modify: `src/Taxi.Domain/Rides/RideStatus.cs`, `src/Taxi.Domain/Rides/RideErrors.cs`, `src/Taxi.Domain/Rides/Ride.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RideOfferTests.cs`

- [ ] **Step 1: Add `Offered` to `RideStatus.cs`**
```csharp
namespace Taxi.Domain.Rides;

public enum RideStatus { Pending, Offered, Accepted, DriverArrived, InProgress, Completed, Cancelled }
```
(Le statut est persisté en **texte** → l'ordre de l'enum est sans impact sur les données existantes.)

- [ ] **Step 2: Add errors to `RideErrors.cs`** — ajouter dans la classe :
```csharp
    public static readonly Error NotOffered = Error.Conflict("Ride.NotOffered", "Cette course n'est pas en cours d'offre.");
    public static readonly Error OfferMismatch = Error.Forbidden("Ride.OfferMismatch", "Cette offre ne vous concerne pas.");
    public static readonly Error OfferExpired = Error.Conflict("Ride.OfferExpired", "Cette offre a expiré.");
```

- [ ] **Step 3: Add fields + methods to `Ride.cs`** — ajouter les propriétés (après `public DateTime? CompletedAt { get; private set; }`) :
```csharp
    public int? OfferedDriverId { get; private set; }
    public DateTime? OfferExpiresAt { get; private set; }
    public List<int> TriedDriverIds { get; private set; } = [];
```
et les méthodes (avant la fin de la classe) :
```csharp
    public Result Offer(int driverId, DateTime expiresAt)
    {
        if (Status != RideStatus.Pending)
            return Result.Failure(RideErrors.NotPending);

        Status = RideStatus.Offered;
        OfferedDriverId = driverId;
        OfferExpiresAt = expiresAt;
        return Result.Success();
    }

    public Result AcceptOffer(int driverId)
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.NotOffered);
        if (OfferedDriverId != driverId)
            return Result.Failure(RideErrors.OfferMismatch);
        if (OfferExpiresAt is null || OfferExpiresAt <= DateTime.UtcNow)
            return Result.Failure(RideErrors.OfferExpired);

        DriverId = driverId;
        Status = RideStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        OfferedDriverId = null;
        OfferExpiresAt = null;
        return Result.Success();
    }

    public Result ReturnToPending()
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.Pending;
        OfferedDriverId = null;
        OfferExpiresAt = null;
        return Result.Success();
    }

    public void MarkDriverTried(int driverId)
    {
        if (!TriedDriverIds.Contains(driverId))
            TriedDriverIds.Add(driverId);
    }
```

- [ ] **Step 4: Allow cancelling an `Offered` ride (client)** — dans `Ride.cs`, méthode `CancelByClient`, élargir la condition pour inclure `Offered` :
```csharp
    public Result CancelByClient()
    {
        if (Status is not (RideStatus.Pending or RideStatus.Offered or RideStatus.Accepted or RideStatus.DriverArrived))
            return Result.Failure(RideErrors.CannotCancel);

        Status = RideStatus.Cancelled;
        return Result.Success();
    }
```

- [ ] **Step 5: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RideOfferTests.cs`:
```csharp
using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideOfferTests
{
    private static Ride PendingRide()
        => Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);

    [Fact]
    public void Offer_moves_pending_to_offered()
    {
        var ride = PendingRide();
        var result = ride.Offer(7, DateTime.UtcNow.AddSeconds(30));

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverId.Should().Be(7);
        ride.OfferExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void AcceptOffer_succeeds_for_offered_driver()
    {
        var ride = PendingRide();
        ride.Offer(7, DateTime.UtcNow.AddSeconds(30));

        var result = ride.AcceptOffer(7);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.DriverId.Should().Be(7);
        ride.OfferedDriverId.Should().BeNull();
    }

    [Fact]
    public void AcceptOffer_fails_for_wrong_driver()
    {
        var ride = PendingRide();
        ride.Offer(7, DateTime.UtcNow.AddSeconds(30));

        var result = ride.AcceptOffer(9);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferMismatch);
    }

    [Fact]
    public void AcceptOffer_fails_when_expired()
    {
        var ride = PendingRide();
        ride.Offer(7, DateTime.UtcNow.AddSeconds(-1));

        var result = ride.AcceptOffer(7);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferExpired);
    }

    [Fact]
    public void AcceptOffer_fails_when_not_offered()
    {
        var ride = PendingRide();

        var result = ride.AcceptOffer(7);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotOffered);
    }

    [Fact]
    public void ReturnToPending_clears_the_offer()
    {
        var ride = PendingRide();
        ride.Offer(7, DateTime.UtcNow.AddSeconds(30));

        var result = ride.ReturnToPending();

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.OfferedDriverId.Should().BeNull();
    }

    [Fact]
    public void MarkDriverTried_is_idempotent()
    {
        var ride = PendingRide();
        ride.MarkDriverTried(7);
        ride.MarkDriverTried(7);

        ride.TriedDriverIds.Should().BeEquivalentTo(new[] { 7 });
    }
}
```

- [ ] **Step 6: Run — expect FAIL then implement is already in steps 1-4. Run tests**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: les 7 nouveaux tests passent (les méthodes/erreurs sont déjà écrites aux steps 1-4).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): ride offer state machine (Offered, Offer/AcceptOffer/ReturnToPending/MarkDriverTried)"
```

---

## Task 2: Migration AddRideDispatch

**Files:** Generate migration.

- [ ] **Step 1: Build** : `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx` → 0 errors.

- [ ] **Step 2: Generate**
Run: `cd /c/prjRecherche/Taxi && dotnet ef migrations add AddRideDispatch --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations`

- [ ] **Step 3: Verify** — le `Up()` ajoute sur `rides` : `offered_driver_id` (int, nullable), `offer_expires_at` (timestamptz, nullable), et `tried_driver_ids` (collection primitive — `integer[]` ou `jsonb` selon EF/Npgsql). Aucune autre table touchée. Si autre chose apparaît, STOP et reporter.

- [ ] **Step 4: Build** : `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx` → 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AddRideDispatch migration (offer fields + tried drivers)"
```

---

## Task 3: Temps réel — offre ciblée

**Files:**
- Modify: `src/Taxi.Application/Realtime/IRealtimeNotifier.cs`
- Modify: `src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs`
- Modify: `src/Taxi.Web.Api/Realtime/RideHub.cs`

- [ ] **Step 1: Add to `IRealtimeNotifier.cs`** — ajouter la méthode dans l'interface :
```csharp
    Task RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken cancellationToken);
```

- [ ] **Step 2: Implement in `SignalRRealtimeNotifier.cs`** — ajouter la méthode (best-effort, même style try/catch) :
```csharp
    public async Task RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group($"DriverUser_{driverUserId}")
                .SendAsync("rideOffered", new { rideId, expiresAt }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (rideOffered) failed for ride {RideId}", rideId);
        }
    }
```

- [ ] **Step 3: Add `JoinMyDriverGroup` to `RideHub.cs`** — ajouter la méthode dans le hub :
```csharp
    public Task JoinMyDriverGroup()
    {
        var userId = Context.User!.GetUserId();
        if (userId is null)
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, $"DriverUser_{userId}");
    }
```

- [ ] **Step 4: Build** : `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx` → 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(realtime): targeted rideOffered event + per-driver hub group"
```

---

## Task 4: IRideDispatcher + RideDispatcher (Application, TDD)

**Files:**
- Create: `src/Taxi.Application/Dispatch/IRideDispatcher.cs`, `src/Taxi.Application/Dispatch/RideDispatcher.cs`
- Modify: `src/Taxi.Application/Rides/RideSpecs.cs` (ExpiredOffersSpec)
- Modify: `src/Taxi.Application/DependencyInjection.cs` (register IRideDispatcher)
- Test: `tests/Taxi.Application.Tests/Dispatch/RideDispatcherTests.cs`

- [ ] **Step 1: Add `ExpiredOffersSpec` to `RideSpecs.cs`**
```csharp
internal sealed class ExpiredOffersSpec : Specification<Ride>
{
    public ExpiredOffersSpec(DateTime now)
        => Query.Where(r => r.Status == RideStatus.Offered && r.OfferExpiresAt != null && r.OfferExpiresAt <= now);
}
```

- [ ] **Step 2: Create `IRideDispatcher.cs`**
```csharp
namespace Taxi.Application.Dispatch;

public interface IRideDispatcher
{
    Task DispatchAsync(int rideId, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing test** — `tests/Taxi.Application.Tests/Dispatch/RideDispatcherTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Realtime;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class RideDispatcherTests
{
    private readonly Mock<IDriverLocator> _locator = new();
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();

    private RideDispatcher Dispatcher() => new(_locator.Object, _rides.Object, _notifier.Object);

    private static Ride PendingRideWithCoords()
        => Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);

    [Fact]
    public async Task Offers_to_nearest_untried_driver()
    {
        var ride = PendingRideWithCoords();
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver> { new(5, "driver-5", 100, 11.581, 43.141, "Taxi") });

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverId.Should().Be(5);
        _notifier.Verify(n => n.RideOfferedAsync("driver-5", It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_already_tried_driver_and_offers_next()
    {
        var ride = PendingRideWithCoords();
        ride.MarkDriverTried(5);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver>
                {
                    new(5, "driver-5", 100, 11.581, 43.141, "Taxi"),
                    new(8, "driver-8", 200, 11.582, 43.142, "VTC"),
                });

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.OfferedDriverId.Should().Be(8);
    }

    [Fact]
    public async Task Returns_to_pending_and_notifies_when_no_candidate()
    {
        var ride = PendingRideWithCoords();
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);
        _locator.Setup(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NearbyDriver>());

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Pending);
        _notifier.Verify(n => n.NewPendingRideAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_when_no_pickup_coordinates()
    {
        var ride = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ride);

        await Dispatcher().DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Pending);
        _locator.Verify(l => l.FindNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NewPendingRideAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 4: Run — expect FAIL** (RideDispatcher absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 5: Create `RideDispatcher.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;

namespace Taxi.Application.Dispatch;

internal sealed class RideDispatcher(
    IDriverLocator locator,
    IRepository<Ride> rides,
    IRealtimeNotifier notifier)
    : IRideDispatcher
{
    private static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(30);
    private const double RadiusMeters = 5000;
    private const int MaxCandidates = 20;

    public async Task DispatchAsync(int rideId, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(rideId), cancellationToken);
        if (ride is null || ride.Status != RideStatus.Pending)
            return;

        if (ride.PickupLatitude is null || ride.PickupLongitude is null)
        {
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken); // pas de coords → flux manuel
            return;
        }

        var candidates = await locator.FindNearestAsync(
            ride.PickupLatitude.Value, ride.PickupLongitude.Value, RadiusMeters, MaxCandidates, cancellationToken);

        var next = candidates.FirstOrDefault(c => !ride.TriedDriverIds.Contains(c.DriverId));

        if (next is null)
        {
            // la course est déjà Pending (garde en tête de méthode) → on notifie juste le flux manuel
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var expiresAt = DateTime.UtcNow + OfferTtl;
        ride.Offer(next.DriverId, expiresAt);
        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideOfferedAsync(next.UserId, ride.Id, expiresAt, cancellationToken);
    }
}
```
NOTE: le dispatcher ne traite que des courses `Pending` (garde) ; appelé depuis decline/timeout, la course a déjà été repassée en `Pending` au préalable. La branche « aucun candidat » laisse donc la course `Pending` et notifie `newPendingRide` (filet manuel).

- [ ] **Step 6: Register in `src/Taxi.Application/DependencyInjection.cs`** — dans `AddApplication`, ajouter (avant le `return`) :
```csharp
        services.AddScoped<IRideDispatcher, RideDispatcher>();
```
Ajouter `using Taxi.Application.Dispatch;` en tête si absent.

- [ ] **Step 7: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: 4 nouveaux tests verts.

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(dispatch): RideDispatcher (sequential nearest-driver offer) + ExpiredOffersSpec + DI (TDD)"
```

---

## Task 5: Commandes accept-offer / decline-offer + endpoints (TDD)

**Files:**
- Create: `src/Taxi.Application/Dispatch/AcceptOffer/{AcceptOfferCommand,AcceptOfferCommandHandler}.cs`
- Create: `src/Taxi.Application/Dispatch/DeclineOffer/{DeclineOfferCommand,DeclineOfferCommandHandler}.cs`
- Create: `src/Taxi.Web.Api/Modules/Dispatch/OfferEndpoints.cs`
- Test: `tests/Taxi.Application.Tests/Dispatch/OfferHandlersTests.cs`

- [ ] **Step 1: Create the two commands**

`AcceptOfferCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.AcceptOffer;

public sealed record AcceptOfferCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```
`DeclineOfferCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.DeclineOffer;

public sealed record DeclineOfferCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Dispatch/OfferHandlersTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Dispatch.AcceptOffer;
using Taxi.Application.Dispatch.DeclineOffer;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class OfferHandlersTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();
    private readonly Mock<IRideDispatcher> _dispatcher = new();

    private static Driver DriverWithId(int id)
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        typeof(Taxi.SharedKernel.Entity).GetProperty("Id")!.SetValue(d, id);
        d.SetAvailability(true);
        return d;
    }

    private static Ride OfferedRideTo(int driverId)
    {
        var r = Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.6, 43.16, 1000m);
        r.Offer(driverId, DateTime.UtcNow.AddSeconds(30));
        return r;
    }

    [Fact]
    public async Task AcceptOffer_assigns_and_makes_driver_unavailable()
    {
        var driver = DriverWithId(5);
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>())).ReturnsAsync(driver);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>())).ReturnsAsync(OfferedRideTo(5));

        var handler = new AcceptOfferCommandHandler(_rides.Object, _drivers.Object, _notifier.Object);
        var result = await handler.Handle(new AcceptOfferCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Accepted");
        driver.IsAvailable.Should().BeFalse();
        _notifier.Verify(n => n.RideStatusChangedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), "Accepted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclineOffer_marks_tried_returns_pending_and_redispatches()
    {
        var driver = DriverWithId(5);
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>())).ReturnsAsync(driver);
        var ride = OfferedRideTo(5);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ride);

        var handler = new DeclineOfferCommandHandler(_rides.Object, _drivers.Object, _dispatcher.Object);
        var result = await handler.Handle(new DeclineOfferCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ride.TriedDriverIds.Should().Contain(5);
        _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handlers absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 4: Create `AcceptOfferCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.AcceptOffer;

internal sealed class AcceptOfferCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRealtimeNotifier notifier)
    : ICommandHandler<AcceptOfferCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(AcceptOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        var accepted = ride.AcceptOffer(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        await rides.UpdateAsync(ride, cancellationToken);

        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);

        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 5: Create `DeclineOfferCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Dispatch.DeclineOffer;

internal sealed class DeclineOfferCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers,
    IRideDispatcher dispatcher)
    : ICommandHandler<DeclineOfferCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(DeclineOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        if (ride.Status != RideStatus.Offered || ride.OfferedDriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.OfferMismatch);

        ride.MarkDriverTried(driver.Id);
        ride.ReturnToPending();
        await rides.UpdateAsync(ride, cancellationToken);

        await dispatcher.DispatchAsync(ride.Id, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 6: Create `OfferEndpoints.cs`**
```csharp
using System.Security.Claims;
using Taxi.Application.Dispatch.AcceptOffer;
using Taxi.Application.Dispatch.DeclineOffer;
using Taxi.Application.Rides;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Dispatch;

public sealed class OfferEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides/{id:int}")
            .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
            .WithTags(Tags.Dispatch);

        group.MapPost("/accept-offer", async (int id, ClaimsPrincipal principal,
            ICommandHandler<AcceptOfferCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new AcceptOfferCommand(id, userId), ct)).ToHttpResult();
        }).WithName("AcceptOffer").WithSummary("Accepter l'offre de course");

        group.MapPost("/decline-offer", async (int id, ClaimsPrincipal principal,
            ICommandHandler<DeclineOfferCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new DeclineOfferCommand(id, userId), ct)).ToHttpResult();
        }).WithName("DeclineOffer").WithSummary("Refuser l'offre de course");
    }
}
```

- [ ] **Step 7: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: 2 nouveaux tests verts.

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(dispatch): accept-offer / decline-offer commands + endpoints (TDD)"
```

---

## Task 6: Disponibilité liée au cycle + RequestRide → dispatcher

**Files:**
- Modify: `Accept/AcceptRideCommandHandler.cs`, `Transitions/CompleteRideCommandHandler.cs`, `Cancel/CancelRideCommandHandler.cs`
- Modify: `Request/RequestRideCommandHandler.cs`
- Modify tests: `AcceptRideHandlerTests.cs`, `RequestRideHandlerTests.cs`

- [ ] **Step 1: `AcceptRideCommandHandler` — rendre indisponible.** Après `await rides.UpdateAsync(ride, cancellationToken);`, ajouter :
```csharp
        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);
```
(Le handler a déjà `driver` et `drivers`.)

- [ ] **Step 2: `CompleteRideCommandHandler` — rendre disponible.** Après `await rides.UpdateAsync(ride, cancellationToken);` (et avant la notif/`return`), ajouter :
```csharp
        driver.SetAvailability(true);
        await drivers.UpdateAsync(driver, cancellationToken);
```
(Le handler a déjà `driver`.)

- [ ] **Step 3: `CancelRideCommandHandler` — remettre dispo le chauffeur assigné.** Après `await rides.UpdateAsync(ride, cancellationToken);`, ajouter :
```csharp
        if (ride.DriverId is not null)
        {
            var assigned = await drivers.FirstOrDefaultAsync(new DriverByIdSpec(ride.DriverId.Value), cancellationToken);
            if (assigned is not null)
            {
                assigned.SetAvailability(true);
                await drivers.UpdateAsync(assigned, cancellationToken);
            }
        }
```
Ajouter `using Taxi.Application.Drivers;` si absent (pour `DriverByIdSpec`). NOTE : `DriverByIdSpec` existe déjà (`src/Taxi.Application/Drivers/DriverByIdSpec.cs`).

- [ ] **Step 4: `RequestRideCommandHandler` → dispatcher.** Remplacer la dépendance `IRealtimeNotifier notifier` par `IRideDispatcher dispatcher`, le using `using Taxi.Application.Realtime;` par `using Taxi.Application.Dispatch;`, et l'appel `await notifier.NewPendingRideAsync(ride.Id, cancellationToken);` par `await dispatcher.DispatchAsync(ride.Id, cancellationToken);`. Résultat :
```csharp
internal sealed class RequestRideCommandHandler(
    IRepository<Ride> rides,
    IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> priceEstimator,
    IRideDispatcher dispatcher)
    : ICommandHandler<RequestRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(RequestRideCommand command, CancellationToken cancellationToken)
    {
        var price = await priceEstimator.Handle(
            new EstimatePriceQuery(command.PickupZone, command.DestinationZone), cancellationToken);
        if (price.IsFailure)
            return Result.Failure<RideDto>(price.Error);

        var ride = Ride.Request(
            command.ClientId, command.PickupAddress, command.DestinationAddress,
            command.PickupZone, command.DestinationZone,
            command.PickupLatitude, command.PickupLongitude,
            command.DestinationLatitude, command.DestinationLongitude,
            price.Value.Price);

        await rides.AddAsync(ride, cancellationToken);
        await dispatcher.DispatchAsync(ride.Id, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 5: Update `AcceptRideHandlerTests.cs`** — dans le test `Should_accept_when_driver_available_and_ride_pending`, ajouter à la fin l'assertion que le chauffeur est persisté indisponible (le handler appelle désormais `drivers.UpdateAsync` une fois) :
```csharp
        _drivers.Verify(d => d.UpdateAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 6: Update `RequestRideHandlerTests.cs`** — remplacer le mock notifier par un mock dispatcher : remplacer `using Taxi.Application.Realtime;` par `using Taxi.Application.Dispatch;`, le champ `_notifier` par `private readonly Mock<IRideDispatcher> _dispatcher = new();`, le `Handler()` par `new(_rides.Object, _pricing.Object, _dispatcher.Object)`, et le test `Should_notify_drivers_of_new_pending_ride` par :
```csharp
    [Fact]
    public async Task Should_dispatch_the_new_ride()
    {
        PriceReturns(1500m);

        await Handler().Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", 11.58, 43.14, 11.6, 43.16), CancellationToken.None);

        _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 7: Run**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (tests Accept/Request mis à jour).

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(rides): driver availability lifecycle + RequestRide auto-dispatch"
```

---

## Task 7: OfferTimeoutService (background) + build complet

**Files:**
- Create: `src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs`
- Modify: `src/Taxi.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create `OfferTimeoutService.cs`**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Dispatch;

internal sealed class OfferTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<OfferTimeoutService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var rides = scope.ServiceProvider.GetRequiredService<IRepository<Ride>>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRideDispatcher>();

                var expired = await rides.ListAsync(new ExpiredOffersSpec(DateTime.UtcNow), stoppingToken);
                foreach (var ride in expired)
                {
                    if (ride.OfferedDriverId is not null)
                        ride.MarkDriverTried(ride.OfferedDriverId.Value);
                    ride.ReturnToPending();
                    await rides.UpdateAsync(ride, stoppingToken);
                    await dispatcher.DispatchAsync(ride.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec du traitement des offres expirées");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```
NOTE: `ExpiredOffersSpec` est `internal` dans `Taxi.Application.Rides` ; Infrastructure référence Application → accessible. `DispatchAsync` rechargera la course (même scope/DbContext → instance suivie) maintenant en `Pending` et offrira au suivant.

- [ ] **Step 2: Register in `DependencyInjection.cs`** — dans `AddInfrastructure`, avant `return services;` :
```csharp
        services.AddHostedService<OfferTimeoutService>();
```
Ajouter `using Microsoft.Extensions.Hosting;` et `using Taxi.Infrastructure.Dispatch;` si absents.

- [ ] **Step 3: Build + full test suite**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; tous les tests verts.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(dispatch): offer timeout background service + registration"
```

---

## Task 8: Vérification manuelle (USER — Docker + client Node)

> Pas de reset de volume nécessaire (migration additive). Démarrer l'AppHost.

- [ ] **Step 1: Préparer** — 1 Client (token + clientId), 2 Drivers (A proche, B plus loin) dispos avec positions récentes (via `SendDriverLocation`), un point de prise en charge connu. Chaque Driver, sur le hub, appelle `JoinMyDriverGroup()` et écoute `rideOffered`.

- [ ] **Step 2: Offre initiale** — le client demande une course **avec coordonnées pickup** (`POST /api/rides/request` avec `pickupLatitude/Longitude`). **Attendu :** le Driver **A** (le plus proche) reçoit `rideOffered { rideId, expiresAt }` ; la course est en statut `Offered` ; B ne reçoit rien.

- [ ] **Step 3: Accept** — A appelle `POST /api/rides/{id}/accept-offer`. **Attendu :** course `Accepted`, `DriverId = A`, A devient indisponible (`GET /api/drivers/me`), event `rideStatusChanged`.

- [ ] **Step 4: Decline → réattribution** — refaire une course ; A reçoit l'offre, appelle `POST /api/rides/{id}/decline-offer`. **Attendu :** A est marqué tenté, **B** reçoit `rideOffered` (offre au suivant).

- [ ] **Step 5: Timeout → réattribution** — refaire une course ; A reçoit l'offre et **ne répond pas**. Après ~30-35 s, **attendu :** l'offre expire (background), A marqué tenté, B reçoit `rideOffered`. Si plus aucun candidat → course revient `Pending` (visible dans `GET /api/rides/pending`).

- [ ] **Step 6: Sans coords** — demander une course **sans** `pickupLatitude/Longitude` → reste `Pending`, notifiée à tous (`newPendingRide`), acceptable via `/accept` manuel.

- [ ] **Step 7: Confirmer à l'utilisateur.** Aucun commit.

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] Une course avec coords est offerte au plus proche ; accept → Accepted + chauffeur indisponible ; decline/timeout → réattribution au suivant ; épuisement/sans-coords → retour Pending (flux manuel).
- [ ] Complete/Cancel remettent le chauffeur disponible.
- [ ] Tout committé sur `main`. **→ Dispatch complet (matching + auto-assignation).**

## Suite (au-delà du legacy)

Identité Phase 3 (documents chauffeur/Azure Blob), stubs Paiement (D-Money) + Notifications (FCM/SMS), puis **frontend React** (dernière phase).
