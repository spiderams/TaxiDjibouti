# Module Courses — Phase 1 (Cycle de course) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implémenter le cycle de vie d'une course : demande (prix estimé), acceptation par un chauffeur disponible, transitions (arrivé→en cours→terminé) et annulation encadrée, via un aggregate `Ride` qui applique lui-même la machine à états.

**Architecture:** Module `Rides` (Clean Architecture). Aggregate `Ride` riche retournant `Result` sur chaque transition. CQRS sans MediatR (commandes par transition). `IRepository<Ride>`/`IRepository<Driver>` génériques + Specifications. Réutilise `EstimatePrice` (Tarification) et l'entité `Driver` (Drivers).

**Tech Stack:** .NET 10, EF Core 10 + migrations, FluentValidation, Ardalis.Specification, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-16-courses-phase1-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Identité, Tarification, Drivers livrés. 30 tests verts au départ.

> **Portée :** cycle de course uniquement. Notation/Signalement = Phase 2. Pas d'enrichissement des noms, pas de SignalR, pas de matching PostGIS.

> **Choix de réutilisation du prix :** `RequestRideHandler` injecte `IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>` (déjà enregistré) et l'appelle — aucune modification du module Tarification ni de ses tests.

---

## Structure de fichiers cible

```
src/Taxi.SharedKernel/Error.cs                                   — modifié (+ Forbidden)
src/Taxi.Web.Api/Endpoints/ResultExtensions.cs                   — modifié (+ 403)
src/Taxi.Domain/Rides/RideStatus.cs                              — créé
src/Taxi.Domain/Rides/RideErrors.cs                              — créé
src/Taxi.Domain/Rides/Ride.cs                                    — créé (aggregate)
src/Taxi.Application/Rides/RideDto.cs                            — créé
src/Taxi.Application/Rides/RideSpecs.cs                          — créé (4 specs)
src/Taxi.Application/Rides/Request/{RequestRideCommand,Validator,Handler}.cs — créés
src/Taxi.Application/Rides/MyRides/{GetMyRidesQuery,Handler}.cs   — créés
src/Taxi.Application/Rides/Pending/{GetPendingRidesQuery,Handler}.cs — créés
src/Taxi.Application/Rides/Accept/{AcceptRideCommand,Handler}.cs  — créés
src/Taxi.Application/Rides/Transitions/{MarkArrived,Start,Complete}Command(+Handler).cs — créés
src/Taxi.Application/Rides/Cancel/{CancelRideCommand,Handler}.cs  — créés
src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs — créé
src/Taxi.Infrastructure/Persistence/AppDbContext.cs              — modifié (+ DbSet)
src/Taxi.Web.Api/Modules/Rides/*Endpoint.cs                      — créés (8 endpoints)
src/Taxi.Infrastructure/Persistence/Migrations/*                — généré (AddRides)
tests/Taxi.Application.Tests/Rides/RideStateMachineTests.cs      — créé
tests/Taxi.Application.Tests/Rides/*HandlerTests.cs              — créés
```

---

## Task 1: SharedKernel — ErrorType.Forbidden → 403 (TDD)

**Files:**
- Modify: `src/Taxi.SharedKernel/Error.cs`
- Modify: `src/Taxi.Web.Api/Endpoints/ResultExtensions.cs`
- Test: `tests/Taxi.Application.Tests/SharedKernel/ErrorTests.cs`

- [ ] **Step 1: Add the failing test** — append to the existing `tests/Taxi.Application.Tests/SharedKernel/ErrorTests.cs` (inside the `ErrorTests` class):
```csharp
    [Fact]
    public void Forbidden_should_have_forbidden_type()
    {
        var error = Error.Forbidden("X.Forbidden", "Interdit");
        error.Type.Should().Be(ErrorType.Forbidden);
    }
```

- [ ] **Step 2: Run — expect FAIL** (Forbidden absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 3: Update `Error.cs`** — add `Forbidden` to the enum and a factory. Final content:
```csharp
namespace Taxi.SharedKernel;

public enum ErrorType { None = 0, Failure = 1, Validation = 2, NotFound = 3, Conflict = 4, Unauthorized = 5, Forbidden = 6 }

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
```

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass.

- [ ] **Step 5: Map Forbidden → 403 in `ResultExtensions.cs`** — add the arm to the `Problem` switch (before `Failure`):
```csharp
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
```
(Keep the existing Validation/NotFound/Conflict arms and the `_ => 500` default.)

- [ ] **Step 6: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: build 0 errors, tests pass.

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(sharedkernel): add ErrorType.Forbidden mapped to 403"
```

---

## Task 2: Domain — RideStatus, RideErrors, Ride aggregate (TDD — la machine à états)

**Files:**
- Create: `src/Taxi.Domain/Rides/RideStatus.cs`
- Create: `src/Taxi.Domain/Rides/RideErrors.cs`
- Create: `src/Taxi.Domain/Rides/Ride.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RideStateMachineTests.cs`

- [ ] **Step 1: Create `RideStatus.cs`**
```csharp
namespace Taxi.Domain.Rides;

public enum RideStatus { Pending, Accepted, DriverArrived, InProgress, Completed, Cancelled }
```

- [ ] **Step 2: Create `RideErrors.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

public static class RideErrors
{
    public static readonly Error NotFound = Error.NotFound("Ride.NotFound", "Course introuvable.");
    public static readonly Error NotPending = Error.Conflict("Ride.NotPending", "Cette course n'est plus disponible.");
    public static readonly Error InvalidTransition = Error.Conflict("Ride.InvalidTransition", "Transition de statut invalide.");
    public static readonly Error CannotCancel = Error.Conflict("Ride.CannotCancel", "Cette course ne peut plus être annulée.");
    public static readonly Error DriverNotAvailable = Error.Conflict("Ride.DriverNotAvailable", "Le chauffeur doit être disponible.");
    public static readonly Error NotAssignedDriver = Error.Forbidden("Ride.NotAssignedDriver", "Cette course n'est pas assignée à ce chauffeur.");
    public static readonly Error NoDriverProfile = Error.NotFound("Ride.NoDriverProfile", "Profil chauffeur introuvable.");
}
```

- [ ] **Step 3: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RideStateMachineTests.cs`:
```csharp
using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideStateMachineTests
{
    private static Ride NewPendingRide() =>
        Ride.Request("client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null, 1500m);

    [Fact]
    public void Request_should_start_pending()
    {
        var ride = NewPendingRide();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.ClientId.Should().Be("client-1");
        ride.EstimatedPrice.Should().Be(1500m);
    }

    [Fact]
    public void Full_happy_path_should_succeed()
    {
        var ride = NewPendingRide();
        ride.Accept(7).IsSuccess.Should().BeTrue();
        ride.DriverId.Should().Be(7);
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.MarkArrived().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.DriverArrived);
        ride.Start().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.InProgress);
        ride.Complete().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Completed);
        ride.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Accept_should_fail_when_not_pending()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        var second = ride.Accept(9);
        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(RideErrors.NotPending);
    }

    [Fact]
    public void Start_should_fail_when_not_arrived()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        var result = ride.Start(); // skips MarkArrived
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.InvalidTransition);
    }

    [Fact]
    public void Complete_should_fail_when_not_in_progress()
    {
        var ride = NewPendingRide();
        var result = ride.Complete();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.InvalidTransition);
    }

    [Fact]
    public void Client_can_cancel_before_in_progress_but_not_after()
    {
        var ride = NewPendingRide();
        ride.Accept(7);
        ride.MarkArrived();
        ride.CancelByClient().IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Cancelled);

        var inProgress = NewPendingRide();
        inProgress.Accept(7); inProgress.MarkArrived(); inProgress.Start();
        inProgress.CancelByClient().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Driver_cannot_cancel_a_pending_ride()
    {
        var ride = NewPendingRide(); // Pending, no driver
        var result = ride.CancelByDriver();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.CannotCancel);
    }
}
```

- [ ] **Step 4: Run — expect FAIL** (Ride absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 5: Create `Ride.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

public sealed class Ride : Entity
{
    public string ClientId { get; private set; } = string.Empty;
    public int? DriverId { get; private set; }
    public string PickupAddress { get; private set; } = string.Empty;
    public string DestinationAddress { get; private set; } = string.Empty;
    public string PickupZone { get; private set; } = string.Empty;
    public string DestinationZone { get; private set; } = string.Empty;
    public double? PickupLatitude { get; private set; }
    public double? PickupLongitude { get; private set; }
    public double? DestinationLatitude { get; private set; }
    public double? DestinationLongitude { get; private set; }
    public decimal EstimatedPrice { get; private set; }
    public RideStatus Status { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private Ride() { } // EF

    public static Ride Request(
        string clientId, string pickupAddress, string destinationAddress,
        string pickupZone, string destinationZone,
        double? pickupLatitude, double? pickupLongitude,
        double? destinationLatitude, double? destinationLongitude,
        decimal estimatedPrice)
        => new()
        {
            ClientId = clientId,
            PickupAddress = pickupAddress,
            DestinationAddress = destinationAddress,
            PickupZone = pickupZone,
            DestinationZone = destinationZone,
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            DestinationLatitude = destinationLatitude,
            DestinationLongitude = destinationLongitude,
            EstimatedPrice = estimatedPrice,
            Status = RideStatus.Pending
        };

    public Result Accept(int driverId)
    {
        if (Status != RideStatus.Pending)
            return Result.Failure(RideErrors.NotPending);

        DriverId = driverId;
        Status = RideStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkArrived()
    {
        if (Status != RideStatus.Accepted)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.DriverArrived;
        return Result.Success();
    }

    public Result Start()
    {
        if (Status != RideStatus.DriverArrived)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.InProgress;
        return Result.Success();
    }

    public Result Complete()
    {
        if (Status != RideStatus.InProgress)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result CancelByClient()
    {
        if (Status is not (RideStatus.Pending or RideStatus.Accepted or RideStatus.DriverArrived))
            return Result.Failure(RideErrors.CannotCancel);

        Status = RideStatus.Cancelled;
        return Result.Success();
    }

    public Result CancelByDriver()
    {
        if (Status is not (RideStatus.Accepted or RideStatus.DriverArrived))
            return Result.Failure(RideErrors.CannotCancel);

        Status = RideStatus.Cancelled;
        return Result.Success();
    }
}
```

- [ ] **Step 6: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (7 new tests).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): Ride aggregate with state machine (TDD)"
```

---

## Task 3: Application — RideDto + Specifications

**Files:**
- Create: `src/Taxi.Application/Rides/RideDto.cs`
- Create: `src/Taxi.Application/Rides/RideSpecs.cs`

- [ ] **Step 1: Create `RideDto.cs`**
```csharp
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

public sealed record RideDto(
    int Id, string ClientId, int? DriverId,
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude,
    decimal EstimatedPrice, string Status,
    DateTime? AcceptedAt, DateTime? CompletedAt, DateTime CreatedAt)
{
    public static RideDto From(Ride r) => new(
        r.Id, r.ClientId, r.DriverId,
        r.PickupAddress, r.DestinationAddress,
        r.PickupZone, r.DestinationZone,
        r.PickupLatitude, r.PickupLongitude,
        r.DestinationLatitude, r.DestinationLongitude,
        r.EstimatedPrice, r.Status.ToString(),
        r.AcceptedAt, r.CompletedAt, r.CreatedAt);
}
```

- [ ] **Step 2: Create `RideSpecs.cs`** (the 4 specifications in one file — they change together)
```csharp
using Ardalis.Specification;
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

internal sealed class RideByIdSpec : Specification<Ride>
{
    public RideByIdSpec(int rideId) => Query.Where(r => r.Id == rideId);
}

internal sealed class RidesByClientSpec : Specification<Ride>
{
    public RidesByClientSpec(string clientId)
        => Query.Where(r => r.ClientId == clientId).OrderByDescending(r => r.CreatedAt);
}

internal sealed class RidesByDriverSpec : Specification<Ride>
{
    public RidesByDriverSpec(int driverId)
        => Query.Where(r => r.DriverId == driverId).OrderByDescending(r => r.CreatedAt);
}

internal sealed class PendingRidesSpec : Specification<Ride>
{
    public PendingRidesSpec()
        => Query.Where(r => r.Status == RideStatus.Pending).OrderByDescending(r => r.CreatedAt);
}
```

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): RideDto and ride specifications"
```

---

## Task 4: Application — RequestRide (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Request/RequestRideCommand.cs`
- Create: `src/Taxi.Application/Rides/Request/RequestRideCommandValidator.cs`
- Create: `src/Taxi.Application/Rides/Request/RequestRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RequestRideHandlerTests.cs`

- [ ] **Step 1: Create command + validator**

`RequestRideCommand.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Request;

public sealed record RequestRideCommand(
    string ClientId,
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude)
    : ICommand<RideDto>;
```

`RequestRideCommandValidator.cs`:
```csharp
using FluentValidation;

namespace Taxi.Application.Rides.Request;

internal sealed class RequestRideCommandValidator : AbstractValidator<RequestRideCommand>
{
    public RequestRideCommandValidator()
    {
        RuleFor(c => c.PickupAddress).NotEmpty();
        RuleFor(c => c.DestinationAddress).NotEmpty();
        RuleFor(c => c.PickupZone).NotEmpty();
        RuleFor(c => c.DestinationZone).NotEmpty();
    }
}
```

- [ ] **Step 2: Write the failing test** — `tests/Taxi.Application.Tests/Rides/RequestRideHandlerTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Request;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RequestRideHandlerTests
{
    [Fact]
    public async Task Should_create_pending_ride_with_estimated_price()
    {
        var rides = new Mock<IRepository<Ride>>();
        var pricing = new Mock<IQueryHandler<EstimatePriceQuery, EstimatePriceResponse>>();
        pricing.Setup(p => p.Handle(It.IsAny<EstimatePriceQuery>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new EstimatePriceResponse("Centre-ville", "Balbala", 1500m)));

        var handler = new RequestRideCommandHandler(rides.Object, pricing.Object);

        var result = await handler.Handle(new RequestRideCommand(
            "client-1", "A", "B", "Centre-ville", "Balbala", null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Pending");
        result.Value.EstimatedPrice.Should().Be(1500m);
        result.Value.ClientId.Should().Be("client-1");
        rides.Verify(r => r.AddAsync(It.IsAny<Ride>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `RequestRideCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Pricing.EstimatePrice;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Request;

internal sealed class RequestRideCommandHandler(
    IRepository<Ride> rides,
    IQueryHandler<EstimatePriceQuery, EstimatePriceResponse> priceEstimator)
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
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (1 new test).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): request ride command reusing EstimatePrice (TDD)"
```

---

## Task 5: Application — GetMyRides + GetPendingRides

**Files:**
- Create: `src/Taxi.Application/Rides/MyRides/GetMyRidesQuery.cs`
- Create: `src/Taxi.Application/Rides/MyRides/GetMyRidesQueryHandler.cs`
- Create: `src/Taxi.Application/Rides/Pending/GetPendingRidesQuery.cs`
- Create: `src/Taxi.Application/Rides/Pending/GetPendingRidesQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RideQueriesTests.cs`

- [ ] **Step 1: Create the queries**

`MyRides/GetMyRidesQuery.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.MyRides;

public sealed record GetMyRidesQuery(string UserId, bool AsDriver) : IQuery<IReadOnlyList<RideDto>>;
```

`Pending/GetPendingRidesQuery.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Pending;

public sealed record GetPendingRidesQuery : IQuery<IReadOnlyList<RideDto>>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RideQueriesTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.MyRides;
using Taxi.Application.Rides.Pending;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideQueriesTests
{
    private static Ride Pending() =>
        Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);

    [Fact]
    public async Task GetMyRides_as_client_returns_client_rides()
    {
        var rides = new Mock<IRepository<Ride>>();
        var drivers = new Mock<IRepository<Driver>>();
        rides.Setup(r => r.ListAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Ride> { Pending() });
        var handler = new GetMyRidesQueryHandler(rides.Object, drivers.Object);

        var result = await handler.Handle(new GetMyRidesQuery("client-1", AsDriver: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMyRides_as_driver_without_profile_returns_empty()
    {
        var rides = new Mock<IRepository<Ride>>();
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Driver?)null);
        var handler = new GetMyRidesQueryHandler(rides.Object, drivers.Object);

        var result = await handler.Handle(new GetMyRidesQuery("u-x", AsDriver: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingRides_returns_pending_list()
    {
        var rides = new Mock<IRepository<Ride>>();
        rides.Setup(r => r.ListAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Ride> { Pending(), Pending() });
        var handler = new GetPendingRidesQueryHandler(rides.Object);

        var result = await handler.Handle(new GetPendingRidesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }
}
```

- [ ] **Step 3: Run — expect FAIL**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `MyRides/GetMyRidesQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.MyRides;

internal sealed class GetMyRidesQueryHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : IQueryHandler<GetMyRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetMyRidesQuery query, CancellationToken cancellationToken)
    {
        List<Ride> list;
        if (query.AsDriver)
        {
            var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
            list = driver is null
                ? new List<Ride>()
                : await rides.ListAsync(new RidesByDriverSpec(driver.Id), cancellationToken);
        }
        else
        {
            list = await rides.ListAsync(new RidesByClientSpec(query.UserId), cancellationToken);
        }

        return list.Select(RideDto.From).ToList();
    }
}
```
NOTE: `DriverByUserIdSpec` is `internal` in `Taxi.Application.Drivers` — accessible from the same assembly via `using Taxi.Application.Drivers;`. `RidesByDriverSpec`/`RidesByClientSpec` are in the parent namespace `Taxi.Application.Rides` (visible without using).

- [ ] **Step 5: Create `Pending/GetPendingRidesQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Pending;

internal sealed class GetPendingRidesQueryHandler(IRepository<Ride> rides)
    : IQueryHandler<GetPendingRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetPendingRidesQuery query, CancellationToken cancellationToken)
    {
        var list = await rides.ListAsync(new PendingRidesSpec(), cancellationToken);
        return list.Select(RideDto.From).ToList();
    }
}
```
NOTE: `PendingRidesSpec` is in the parent namespace `Taxi.Application.Rides` — visible from `Taxi.Application.Rides.Pending` without a using.

- [ ] **Step 6: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (3 new tests).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): get-my-rides and get-pending-rides queries (TDD)"
```

---

## Task 6: Application — AcceptRide (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Accept/AcceptRideCommand.cs`
- Create: `src/Taxi.Application/Rides/Accept/AcceptRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/AcceptRideHandlerTests.cs`

- [ ] **Step 1: Create `AcceptRideCommand.cs`**
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Accept;

public sealed record AcceptRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/AcceptRideHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Accept;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class AcceptRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private AcceptRideCommandHandler Handler() => new(_rides.Object, _drivers.Object);

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
        var unavailable = Driver.Create("driver-user", "LIC", "PLATE", "Taxi"); // IsAvailable = false
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(unavailable);

        var result = await Handler().Handle(new AcceptRideCommand(1, "driver-user"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.DriverNotAvailable);
    }
}
```

- [ ] **Step 3: Run — expect FAIL**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `AcceptRideCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Accept;

internal sealed class AcceptRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<AcceptRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(AcceptRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);
        if (!driver.IsAvailable)
            return Result.Failure<RideDto>(RideErrors.DriverNotAvailable);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        var accepted = ride.Accept(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}
```
NOTE: `RideByIdSpec` (parent namespace `Taxi.Application.Rides`) and `DriverByUserIdSpec` (`using Taxi.Application.Drivers;`).

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (3 new tests).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): accept ride command (driver availability) (TDD)"
```

---

## Task 7: Application — Transitions (MarkArrived / Start / Complete) (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Transitions/MarkArrivedCommand.cs`
- Create: `src/Taxi.Application/Rides/Transitions/StartRideCommand.cs`
- Create: `src/Taxi.Application/Rides/Transitions/CompleteRideCommand.cs`
- Create: `src/Taxi.Application/Rides/Transitions/MarkArrivedCommandHandler.cs`
- Create: `src/Taxi.Application/Rides/Transitions/StartRideCommandHandler.cs`
- Create: `src/Taxi.Application/Rides/Transitions/CompleteRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RideTransitionsHandlerTests.cs`

> The 3 transition handlers share the same shape: resolve the driver, load the ride, verify the ride is assigned to that driver (`ride.DriverId == driver.Id`, else `NotAssignedDriver`), call the aggregate transition, persist.

- [ ] **Step 1: Create the 3 commands**

`MarkArrivedCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

public sealed record MarkArrivedCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```
`StartRideCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

public sealed record StartRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```
`CompleteRideCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

public sealed record CompleteRideCommand(int RideId, string DriverUserId) : ICommand<RideDto>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RideTransitionsHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Transitions;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideTransitionsHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private static Driver DriverWithId(int id)
    {
        var d = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        typeof(Taxi.SharedKernel.Entity).GetProperty("Id")!.SetValue(d, id);
        d.SetAvailability(true);
        return d;
    }

    private static Ride AcceptedRide(int driverId)
    {
        var r = Ride.Request("c", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        r.Accept(driverId);
        return r;
    }

    [Fact]
    public async Task MarkArrived_should_succeed_for_assigned_driver()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverWithId(7));
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AcceptedRide(7));
        var handler = new MarkArrivedCommandHandler(_rides.Object, _drivers.Object);

        var result = await handler.Handle(new MarkArrivedCommand(1, "driver-user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("DriverArrived");
    }

    [Fact]
    public async Task MarkArrived_should_forbid_when_not_assigned_driver()
    {
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DriverWithId(9)); // different driver
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(AcceptedRide(7));
        var handler = new MarkArrivedCommandHandler(_rides.Object, _drivers.Object);

        var result = await handler.Handle(new MarkArrivedCommand(1, "driver-user"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }
}
```
NOTE: the reflection `SetValue` on `Id` is a test-only shortcut to give the in-memory Driver a known Id (the `Entity.Id` setter is `protected`). This keeps the test self-contained without a DB.

- [ ] **Step 3: Run — expect FAIL**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `MarkArrivedCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

internal sealed class MarkArrivedCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<MarkArrivedCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(MarkArrivedCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.MarkArrived();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 5: Create `StartRideCommandHandler.cs`** (identical shape, calls `ride.Start()`)
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

internal sealed class StartRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<StartRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(StartRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.Start();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 6: Create `CompleteRideCommandHandler.cs`** (identical shape, calls `ride.Complete()`)
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Transitions;

internal sealed class CompleteRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<CompleteRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(CompleteRideCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);
        if (ride.DriverId != driver.Id)
            return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);

        var transition = ride.Complete();
        if (transition.IsFailure)
            return Result.Failure<RideDto>(transition.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 7: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (2 new tests).

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): ride transition commands (arrived/start/complete) (TDD)"
```

---

## Task 8: Application — CancelRide (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Cancel/CancelRideCommand.cs`
- Create: `src/Taxi.Application/Rides/Cancel/CancelRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/CancelRideHandlerTests.cs`

- [ ] **Step 1: Create `CancelRideCommand.cs`**
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Cancel;

public sealed record CancelRideCommand(int RideId, string UserId, bool IsDriver) : ICommand<RideDto>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/CancelRideHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Cancel;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class CancelRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private CancelRideCommandHandler Handler() => new(_rides.Object, _drivers.Object);

    [Fact]
    public async Task Client_can_cancel_own_pending_ride()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new CancelRideCommand(1, "client-1", IsDriver: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Client_cannot_cancel_someone_elses_ride()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new CancelRideCommand(1, "intruder", IsDriver: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }
}
```

- [ ] **Step 3: Run — expect FAIL**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `CancelRideCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Cancel;

internal sealed class CancelRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Driver> drivers)
    : ICommandHandler<CancelRideCommand, RideDto>
{
    public async Task<Result<RideDto>> Handle(CancelRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        Result outcome;
        if (command.IsDriver)
        {
            var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);
            if (driver is null)
                return Result.Failure<RideDto>(RideErrors.NoDriverProfile);
            if (ride.DriverId != driver.Id)
                return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);
            outcome = ride.CancelByDriver();
        }
        else
        {
            if (ride.ClientId != command.UserId)
                return Result.Failure<RideDto>(RideErrors.NotAssignedDriver);
            outcome = ride.CancelByClient();
        }

        if (outcome.IsFailure)
            return Result.Failure<RideDto>(outcome.Error);

        await rides.UpdateAsync(ride, cancellationToken);
        return RideDto.From(ride);
    }
}
```

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (2 new tests).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): cancel ride command (client/driver rules) (TDD)"
```

---

## Task 9: Infrastructure — RideConfiguration + DbSet

**Files:**
- Create: `src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs`
- Modify: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create `RideConfiguration.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Persistence.Configurations;

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
```

- [ ] **Step 2: Add a `DbSet<Ride>` to `AppDbContext.cs`** — add after the `Drivers` DbSet and add `using Taxi.Domain.Rides;`:
```csharp
    public DbSet<Ride> Rides => Set<Ride>();
```

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): Ride EF configuration (enum->string) and DbSet"
```

---

## Task 10: Web.Api — endpoints (8)

**Files:**
- Create: `src/Taxi.Web.Api/Modules/Rides/RequestRideEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Rides/MyRidesEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Rides/PendingRidesEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Rides/AcceptRideEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Rides/RideTransitionEndpoints.cs` (arrived/start/complete in one file)
- Create: `src/Taxi.Web.Api/Modules/Rides/CancelRideEndpoint.cs`
- Modify: `src/Taxi.Web.Api/Endpoints/Tags.cs` (+ Rides)

- [ ] **Step 1: Add a `Rides` tag to `Tags.cs`** — final content:
```csharp
namespace Taxi.Web.Api.Endpoints;

public static class Tags
{
    public const string Identity = "Auth";
    public const string Pricing = "Pricing";
    public const string Drivers = "Drivers";
    public const string Rides = "Rides";
}
```

- [ ] **Step 2: Create `RequestRideEndpoint.cs`** (role Client)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Request;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record RequestRideRequest(
    string PickupAddress, string DestinationAddress,
    string PickupZone, string DestinationZone,
    double? PickupLatitude, double? PickupLongitude,
    double? DestinationLatitude, double? DestinationLongitude);

public sealed class RequestRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/request", async (
            RequestRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<RequestRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new RequestRideCommand(
                userId, body.PickupAddress, body.DestinationAddress, body.PickupZone, body.DestinationZone,
                body.PickupLatitude, body.PickupLongitude, body.DestinationLatitude, body.DestinationLongitude), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RequestRide").WithTags(Tags.Rides)
        .WithSummary("Demander une course").WithDescription("Crée une course en attente avec un prix estimé.");
    }
}
```

- [ ] **Step 3: Create `MyRidesEndpoint.cs`** (Client or Driver — asDriver computed from role)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.MyRides;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed class MyRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rides/my-rides", async (
            ClaimsPrincipal principal,
            IQueryHandler<GetMyRidesQuery, IReadOnlyList<RideDto>> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var asDriver = principal.IsInRole(RoleNames.Driver);
            var result = await handler.Handle(new GetMyRidesQuery(userId, asDriver), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("MyRides").WithTags(Tags.Rides)
        .WithSummary("Mes courses").WithDescription("Client : ses courses ; Chauffeur : les courses qui lui sont assignées.");
    }
}
```

- [ ] **Step 4: Create `PendingRidesEndpoint.cs`** (role Driver)
```csharp
using Taxi.Application.Rides;
using Taxi.Application.Rides.Pending;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed class PendingRidesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rides/pending", async (
            IQueryHandler<GetPendingRidesQuery, IReadOnlyList<RideDto>> handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new GetPendingRidesQuery(), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
        .WithName("PendingRides").WithTags(Tags.Rides)
        .WithSummary("Courses en attente").WithDescription("Liste des courses en attente d'un chauffeur.");
    }
}
```

- [ ] **Step 5: Create `AcceptRideEndpoint.cs`** (role Driver)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Accept;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed class AcceptRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/accept", async (
            int id, ClaimsPrincipal principal,
            ICommandHandler<AcceptRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new AcceptRideCommand(id, userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
        .WithName("AcceptRide").WithTags(Tags.Rides)
        .WithSummary("Accepter une course").WithDescription("Le chauffeur disponible accepte une course en attente.");
    }
}
```

- [ ] **Step 6: Create `RideTransitionEndpoints.cs`** (arrived/start/complete — role Driver)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Transitions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed class RideTransitionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides/{id:int}")
            .RequireAuthorization(p => p.RequireRole(RoleNames.Driver))
            .WithTags(Tags.Rides);

        group.MapPost("/arrived", async (int id, ClaimsPrincipal principal,
            ICommandHandler<MarkArrivedCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new MarkArrivedCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideArrived").WithSummary("Chauffeur arrivé");

        group.MapPost("/start", async (int id, ClaimsPrincipal principal,
            ICommandHandler<StartRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new StartRideCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideStart").WithSummary("Démarrer la course");

        group.MapPost("/complete", async (int id, ClaimsPrincipal principal,
            ICommandHandler<CompleteRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            return (await handler.Handle(new CompleteRideCommand(id, userId), ct)).ToHttpResult();
        }).WithName("RideComplete").WithSummary("Terminer la course");
    }
}
```

- [ ] **Step 7: Create `CancelRideEndpoint.cs`** (Client or Driver)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Cancel;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed class CancelRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/cancel", async (
            int id, ClaimsPrincipal principal,
            ICommandHandler<CancelRideCommand, RideDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var isDriver = principal.IsInRole(RoleNames.Driver);
            var result = await handler.Handle(new CancelRideCommand(id, userId, isDriver), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("CancelRide").WithTags(Tags.Rides)
        .WithSummary("Annuler une course").WithDescription("Client ou chauffeur assigné, selon la règle d'annulation.");
    }
}
```

- [ ] **Step 8: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass.

- [ ] **Step 9: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): ride lifecycle endpoints (request/my-rides/pending/accept/transitions/cancel)"
```

---

## Task 11: Migration EF AddRides

**Files:**
- Create (generated): `src/Taxi.Infrastructure/Persistence/Migrations/*_AddRides.cs`

- [ ] **Step 1: Generate the migration**
Run:
```bash
cd /c/prjRecherche/Taxi && dotnet ef migrations add AddRides --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: `Done.` New `*_AddRides.cs`. Its `Up()` creates ONLY the `rides` table (snake_case columns incl. `status` as text, `estimated_price` numeric, the coordinate columns nullable) + indexes on `client_id`, `driver_id`, `status`. It must NOT recreate other tables. Confirm by opening the file.

- [ ] **Step 2: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AddRides EF migration (rides table)"
```

---

## Task 12: Suite + vérification manuelle

- [ ] **Step 1: Run the whole suite**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass (≈ 30 + 1 Forbidden + 7 aggregate + 1 request + 3 queries + 3 accept + 2 transitions + 2 cancel = ~49).

- [ ] **Step 2: Manual verification (USER — Docker running)**

> No volume wipe — `MigrateAsync` applies `AddRides` at startup. Start the AppHost (or F5).

In Scalar (use the **Authorize** button with each token):
  1. Log in as a **Client** (`77000002`) → token. `POST /api/rides/request` `{ "pickupAddress":"Place Menelik","destinationAddress":"Balbala","pickupZone":"Centre-ville","destinationZone":"Balbala" }` → **200**, `status:"Pending"`, `estimatedPrice` set, note the `id`.
  2. `GET /api/rides/my-rides` (Client token) → **200**, the ride listed.
  3. Log in as a **Driver** (`77000003`) → token. Ensure the driver is **available**: `POST /api/drivers/set-availability {"isAvailable":true}`.
  4. (Driver token) `GET /api/rides/pending` → **200**, the ride present. `POST /api/rides/{id}/accept` → **200**, `status:"Accepted"`.
  5. `POST /api/rides/{id}/arrived` → **200** `DriverArrived` ; `/start` → **200** `InProgress` ; `/complete` → **200** `Completed`.
  6. Invalid transition check: on the completed ride, `POST /api/rides/{id}/start` → **409**. A second driver accepting an already-accepted ride → **409**.
  7. Cancellation: new ride (Client), `POST /api/rides/{id}/cancel` (Client token) → **200** `Cancelled`. A Client cancelling someone else's ride → **403**.

- [ ] **Step 3: Confirm results to the user.** No commit (verification).

---

## Definition of Done (Phase 1)

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] Migration `AddRides` présente.
- [ ] Parcours complet request→accept→arrived→start→complete OK ; transitions invalides → 409 ; mauvais acteur → 403 ; annulation selon règle A.
- [ ] Tout committé sur `main`.

## Phase suivante (hors périmètre)

Courses Phase 2 : `Rating` + `Report`. Puis Dispatch (matching PostGIS), Administration, temps réel SignalR.
