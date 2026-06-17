# Module Courses — Phase 2 (Notation & Signalement) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permettre au client de noter une course terminée (upsert, recalcul de la moyenne du chauffeur) et de signaler une course.

**Architecture:** Entités `Rating`/`Report` (Domain/Rides). CQRS sans MediatR. `IRepository<Rating>`/`IRepository<Report>` génériques + Specifications. La notation recalcule `Driver.AverageRating` (nouvelle méthode du module Drivers).

**Tech Stack:** .NET 10, EF Core 10 + migrations, FluentValidation, Ardalis.Specification, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-16-courses-phase2-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Courses Phase 1 + Drivers livrés. 49 tests verts au départ.

> **Portée :** notation + signalement. Modération admin = Administration ; affichage = frontend.

---

## Structure de fichiers cible

```
src/Taxi.Domain/Rides/Rating.cs, Report.cs, RatingErrors.cs        — créés
src/Taxi.Domain/Drivers/Driver.cs                                  — modifié (+ UpdateAverageRating)
src/Taxi.Application/Rides/RatingDto.cs, ReportDto.cs              — créés
src/Taxi.Application/Rides/RatingSpecs.cs                          — créé (RatingByRideSpec, RatingsByDriverSpec)
src/Taxi.Application/Drivers/DriverByIdSpec.cs                     — créé
src/Taxi.Application/Rides/Rate/{RateRideCommand,Validator,Handler}.cs — créés
src/Taxi.Application/Rides/Reporting/{ReportRideCommand,Validator,Handler}.cs — créés (namespace .Reporting pour éviter la collision avec le type Report)
src/Taxi.Infrastructure/Persistence/Configurations/{RatingConfiguration,ReportConfiguration}.cs — créés
src/Taxi.Infrastructure/Persistence/AppDbContext.cs               — modifié (+ 2 DbSet)
src/Taxi.Web.Api/Modules/Rides/{RateRideEndpoint,ReportRideEndpoint}.cs — créés
src/Taxi.Infrastructure/Persistence/Migrations/*                 — généré (AddRatingsAndReports)
tests/Taxi.Application.Tests/Rides/{RatingReportEntityTests,RateRideHandlerTests,ReportRideHandlerTests}.cs — créés
tests/Taxi.Application.Tests/Drivers/DriverTests.cs              — modifié (+ UpdateAverageRating test)
```

---

## Task 1: Domain — Rating, Report, RatingErrors, Driver.UpdateAverageRating (TDD)

**Files:**
- Create: `src/Taxi.Domain/Rides/Rating.cs`, `Report.cs`, `RatingErrors.cs`
- Modify: `src/Taxi.Domain/Drivers/Driver.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RatingReportEntityTests.cs`
- Modify: `tests/Taxi.Application.Tests/Drivers/DriverTests.cs`

- [ ] **Step 1: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RatingReportEntityTests.cs`:
```csharp
using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RatingReportEntityTests
{
    [Fact]
    public void Rating_create_then_update_score()
    {
        var rating = Rating.Create(1, "client-1", 5, 4, "bien");
        rating.RideId.Should().Be(1);
        rating.ClientId.Should().Be("client-1");
        rating.DriverId.Should().Be(5);
        rating.Score.Should().Be(4);
        rating.Comment.Should().Be("bien");

        rating.UpdateScore(2, "moyen");
        rating.Score.Should().Be(2);
        rating.Comment.Should().Be("moyen");
    }

    [Fact]
    public void Report_create_sets_fields()
    {
        var report = Report.Create(1, "client-1", 5, "Retard", "30 min");
        report.RideId.Should().Be(1);
        report.ClientId.Should().Be("client-1");
        report.DriverId.Should().Be(5);
        report.Reason.Should().Be("Retard");
        report.Description.Should().Be("30 min");
    }
}
```
And append to the existing `tests/Taxi.Application.Tests/Drivers/DriverTests.cs` (inside the `DriverTests` class):
```csharp
    [Fact]
    public void UpdateAverageRating_sets_the_average()
    {
        var driver = Driver.Create("u-1", "LIC", "PLATE", "Taxi");
        driver.UpdateAverageRating(4.5);
        driver.AverageRating.Should().Be(4.5);
    }
```

- [ ] **Step 2: Run — expect FAIL** (Rating/Report/UpdateAverageRating absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 3: Create `Rating.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

public sealed class Rating : Entity
{
    public int RideId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public int DriverId { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }

    private Rating() { } // EF

    public static Rating Create(int rideId, string clientId, int driverId, int score, string? comment)
        => new() { RideId = rideId, ClientId = clientId, DriverId = driverId, Score = score, Comment = comment };

    public void UpdateScore(int score, string? comment)
    {
        Score = score;
        Comment = comment;
    }
}
```

- [ ] **Step 4: Create `Report.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

public sealed class Report : Entity
{
    public int RideId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public int? DriverId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Report() { } // EF

    public static Report Create(int rideId, string clientId, int? driverId, string reason, string? description)
        => new() { RideId = rideId, ClientId = clientId, DriverId = driverId, Reason = reason, Description = description };
}
```

- [ ] **Step 5: Create `RatingErrors.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Rides;

public static class RatingErrors
{
    public static readonly Error RideNotCompleted = Error.Conflict("Rating.RideNotCompleted", "On ne peut noter qu'une course terminée.");
    public static readonly Error NoDriver = Error.Conflict("Rating.NoDriver", "Aucun chauffeur associé à cette course.");
}
```

- [ ] **Step 6: Add `UpdateAverageRating` to `Driver.cs`** — add this method to the `Driver` class (after `SetAvailability`):
```csharp
    public void UpdateAverageRating(double average) => AverageRating = average;
```

- [ ] **Step 7: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (3 new tests).

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): Rating/Report entities, RatingErrors, Driver.UpdateAverageRating (TDD)"
```

---

## Task 2: Application — DTOs + Specifications

**Files:**
- Create: `src/Taxi.Application/Rides/RatingDto.cs`, `ReportDto.cs`
- Create: `src/Taxi.Application/Rides/RatingSpecs.cs`
- Create: `src/Taxi.Application/Drivers/DriverByIdSpec.cs`

- [ ] **Step 1: Create `RatingDto.cs`**
```csharp
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

public sealed record RatingDto(int Id, int RideId, string ClientId, int DriverId, int Score, string? Comment, DateTime CreatedAt)
{
    public static RatingDto From(Rating r) => new(r.Id, r.RideId, r.ClientId, r.DriverId, r.Score, r.Comment, r.CreatedAt);
}
```

- [ ] **Step 2: Create `ReportDto.cs`**
```csharp
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

public sealed record ReportDto(int Id, int RideId, string ClientId, int? DriverId, string Reason, string? Description, DateTime CreatedAt)
{
    public static ReportDto From(Report r) => new(r.Id, r.RideId, r.ClientId, r.DriverId, r.Reason, r.Description, r.CreatedAt);
}
```

- [ ] **Step 3: Create `RatingSpecs.cs`**
```csharp
using Ardalis.Specification;
using Taxi.Domain.Rides;

namespace Taxi.Application.Rides;

internal sealed class RatingByRideSpec : Specification<Rating>
{
    public RatingByRideSpec(int rideId) => Query.Where(r => r.RideId == rideId);
}

internal sealed class RatingsByDriverSpec : Specification<Rating>
{
    public RatingsByDriverSpec(int driverId) => Query.Where(r => r.DriverId == driverId);
}
```

- [ ] **Step 4: Create `DriverByIdSpec.cs`**
```csharp
using Ardalis.Specification;
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

internal sealed class DriverByIdSpec : Specification<Driver>
{
    public DriverByIdSpec(int id) => Query.Where(d => d.Id == id);
}
```

- [ ] **Step 5: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): Rating/Report DTOs and specifications"
```

---

## Task 3: Application — RateRide (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Rate/RateRideCommand.cs`, `RateRideCommandValidator.cs`, `RateRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RateRideHandlerTests.cs`

- [ ] **Step 1: Create command + validator**

`RateRideCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Rate;

public sealed record RateRideCommand(int RideId, string ClientId, int Score, string? Comment) : ICommand<RatingDto>;
```

`RateRideCommandValidator.cs`:
```csharp
using FluentValidation;

namespace Taxi.Application.Rides.Rate;

internal sealed class RateRideCommandValidator : AbstractValidator<RateRideCommand>
{
    public RateRideCommandValidator()
    {
        RuleFor(c => c.Score).InclusiveBetween(1, 5);
    }
}
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/RateRideHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Rate;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RateRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Rating>> _ratings = new();
    private readonly Mock<IRepository<Driver>> _drivers = new();

    private RateRideCommandHandler Handler() => new(_rides.Object, _ratings.Object, _drivers.Object);

    private static Ride CompletedRideOwnedBy(string clientId, int driverId)
    {
        var r = Ride.Request(clientId, "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        r.Accept(driverId); r.MarkArrived(); r.Start(); r.Complete();
        return r;
    }

    [Fact]
    public async Task Should_forbid_when_not_owner()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CompletedRideOwnedBy("client-1", 5));

        var result = await Handler().Handle(new RateRideCommand(1, "intruder", 4, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver); // 403, reused for "not owner"
    }

    [Fact]
    public async Task Should_conflict_when_not_completed()
    {
        var pending = Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m);
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(pending);

        var result = await Handler().Handle(new RateRideCommand(1, "client-1", 4, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RatingErrors.RideNotCompleted);
    }

    [Fact]
    public async Task Should_create_rating_and_update_driver_average()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CompletedRideOwnedBy("client-1", 5));
        _ratings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Rating>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Rating?)null); // no existing rating → create
        _ratings.Setup(r => r.ListAsync(It.IsAny<ISpecification<Rating>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Rating> { Rating.Create(1, "client-1", 5, 4, null) });
        var driver = Driver.Create("driver-user", "LIC", "PLATE", "Taxi");
        _drivers.Setup(d => d.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(driver);

        var result = await Handler().Handle(new RateRideCommand(1, "client-1", 4, "ok"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Score.Should().Be(4);
        driver.AverageRating.Should().Be(4);
        _ratings.Verify(r => r.AddAsync(It.IsAny<Rating>(), It.IsAny<CancellationToken>()), Times.Once);
        _drivers.Verify(d => d.UpdateAsync(driver, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 4: Create `RateRideCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Rate;

internal sealed class RateRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Rating> ratings,
    IRepository<Driver> drivers)
    : ICommandHandler<RateRideCommand, RatingDto>
{
    public async Task<Result<RatingDto>> Handle(RateRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RatingDto>(RideErrors.NotFound);
        if (ride.ClientId != command.ClientId)
            return Result.Failure<RatingDto>(RideErrors.NotAssignedDriver);
        if (ride.Status != RideStatus.Completed)
            return Result.Failure<RatingDto>(RatingErrors.RideNotCompleted);
        if (ride.DriverId is null)
            return Result.Failure<RatingDto>(RatingErrors.NoDriver);

        var driverId = ride.DriverId.Value;

        var existing = await ratings.FirstOrDefaultAsync(new RatingByRideSpec(command.RideId), cancellationToken);
        Rating rating;
        if (existing is null)
        {
            rating = Rating.Create(ride.Id, command.ClientId, driverId, command.Score, command.Comment);
            await ratings.AddAsync(rating, cancellationToken);
        }
        else
        {
            existing.UpdateScore(command.Score, command.Comment);
            await ratings.UpdateAsync(existing, cancellationToken);
            rating = existing;
        }

        var driverRatings = await ratings.ListAsync(new RatingsByDriverSpec(driverId), cancellationToken);
        var average = driverRatings.Average(r => r.Score);

        var driver = await drivers.FirstOrDefaultAsync(new DriverByIdSpec(driverId), cancellationToken);
        if (driver is not null)
        {
            driver.UpdateAverageRating(average);
            await drivers.UpdateAsync(driver, cancellationToken);
        }

        return RatingDto.From(rating);
    }
}
```
NOTE: `RideByIdSpec`, `RatingByRideSpec`, `RatingsByDriverSpec` are in the parent namespace `Taxi.Application.Rides` (visible from `.Rate` without a using). `DriverByIdSpec` is internal in `Taxi.Application.Drivers` (brought by `using Taxi.Application.Drivers;`). `RideStatus`/`RideErrors`/`RatingErrors`/`Rating` in `Taxi.Domain.Rides`.

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (3 new tests).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): rate ride command with driver-average recompute (TDD)"
```

---

## Task 4: Application — ReportRide (TDD)

**Files:**
- Create: `src/Taxi.Application/Rides/Reporting/ReportRideCommand.cs`, `ReportRideCommandValidator.cs`, `ReportRideCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/ReportRideHandlerTests.cs`

> The folder/namespace is **`Reporting`** (not `Report`) on purpose: a namespace named `Report` would collide with the domain type `Taxi.Domain.Rides.Report` (CS0118).

- [ ] **Step 1: Create command + validator**

`ReportRideCommand.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Reporting;

public sealed record ReportRideCommand(int RideId, string ClientId, string Reason, string? Description) : ICommand<ReportDto>;
```

`ReportRideCommandValidator.cs`:
```csharp
using FluentValidation;

namespace Taxi.Application.Rides.Reporting;

internal sealed class ReportRideCommandValidator : AbstractValidator<ReportRideCommand>
{
    public ReportRideCommandValidator()
    {
        RuleFor(c => c.Reason).NotEmpty();
    }
}
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Rides/ReportRideHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Rides.Reporting;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class ReportRideHandlerTests
{
    private readonly Mock<IRepository<Ride>> _rides = new();
    private readonly Mock<IRepository<Taxi.Domain.Rides.Report>> _reports = new();

    private ReportRideCommandHandler Handler() => new(_rides.Object, _reports.Object);

    [Fact]
    public async Task Should_forbid_when_not_owner()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new ReportRideCommand(1, "intruder", "Retard", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotAssignedDriver);
    }

    [Fact]
    public async Task Should_create_report_for_own_ride()
    {
        _rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Ride>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Ride.Request("client-1", "A", "B", "Z1", "Z2", null, null, null, null, 1000m));

        var result = await Handler().Handle(new ReportRideCommand(1, "client-1", "Retard", "30 min"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reason.Should().Be("Retard");
        _reports.Verify(r => r.AddAsync(It.IsAny<Taxi.Domain.Rides.Report>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 4: Create `ReportRideCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Rides.Reporting;

internal sealed class ReportRideCommandHandler(
    IRepository<Ride> rides,
    IRepository<Report> reports)
    : ICommandHandler<ReportRideCommand, ReportDto>
{
    public async Task<Result<ReportDto>> Handle(ReportRideCommand command, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<ReportDto>(RideErrors.NotFound);
        if (ride.ClientId != command.ClientId)
            return Result.Failure<ReportDto>(RideErrors.NotAssignedDriver);

        var report = Report.Create(ride.Id, command.ClientId, ride.DriverId, command.Reason, command.Description);
        await reports.AddAsync(report, cancellationToken);

        return ReportDto.From(report);
    }
}
```
NOTE: namespace `Taxi.Application.Rides.Reporting` does NOT clash with the type `Report`, so `Report` resolves cleanly to `Taxi.Domain.Rides.Report` via `using Taxi.Domain.Rides;`. `ReportDto`/`RideByIdSpec` are in the parent namespace `Taxi.Application.Rides`.

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (2 new tests).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): report ride command (TDD)"
```

---

## Task 5: Infrastructure — EF configs + DbSets

**Files:**
- Create: `src/Taxi.Infrastructure/Persistence/Configurations/RatingConfiguration.cs`, `ReportConfiguration.cs`
- Modify: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create `RatingConfiguration.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Persistence.Configurations;

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("ratings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ClientId).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(500);
        builder.HasIndex(r => r.RideId).IsUnique();
        builder.HasIndex(r => r.DriverId);
    }
}
```

- [ ] **Step 2: Create `ReportConfiguration.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Rides;

namespace Taxi.Infrastructure.Persistence.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ClientId).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.HasIndex(r => r.RideId);
    }
}
```

- [ ] **Step 3: Add the 2 DbSets to `AppDbContext.cs`** — add after the `Rides` DbSet (the `using Taxi.Domain.Rides;` is already present from Phase 1):
```csharp
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Report> Reports => Set<Report>();
```

- [ ] **Step 4: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): Rating/Report EF configurations and DbSets"
```

---

## Task 6: Web.Api — rate + report endpoints

**Files:**
- Create: `src/Taxi.Web.Api/Modules/Rides/RateRideEndpoint.cs`, `ReportRideEndpoint.cs`

- [ ] **Step 1: Create `RateRideEndpoint.cs`** (role Client)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Rate;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record RateRideRequest(int Score, string? Comment);

public sealed class RateRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/rate", async (
            int id, RateRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<RateRideCommand, RatingDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new RateRideCommand(id, userId, body.Score, body.Comment), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("RateRide").WithTags(Tags.Rides)
        .WithSummary("Noter une course").WithDescription("Note (1-5) une course terminée ; met à jour la moyenne du chauffeur.");
    }
}
```

- [ ] **Step 2: Create `ReportRideEndpoint.cs`** (role Client)
```csharp
using System.Security.Claims;
using Taxi.Application.Rides;
using Taxi.Application.Rides.Reporting;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Rides;

public sealed record ReportRideRequest(string Reason, string? Description);

public sealed class ReportRideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/rides/{id:int}/report", async (
            int id, ReportRideRequest body, ClaimsPrincipal principal,
            ICommandHandler<ReportRideCommand, ReportDto> handler, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var result = await handler.Handle(new ReportRideCommand(id, userId, body.Reason, body.Description), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(p => p.RequireRole(RoleNames.Client))
        .WithName("ReportRide").WithTags(Tags.Rides)
        .WithSummary("Signaler une course").WithDescription("Signale une course (raison + description).");
    }
}
```
NOTE: `RateRideCommand` (`Taxi.Application.Rides.Rate`), `ReportRideCommand` (`Taxi.Application.Rides.Reporting`), `RatingDto`/`ReportDto` (`Taxi.Application.Rides`). `WithName` "RateRide"/"ReportRide" are unique.

- [ ] **Step 3: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): rate and report ride endpoints"
```

---

## Task 7: Migration EF AddRatingsAndReports

**Files:**
- Create (generated): `src/Taxi.Infrastructure/Persistence/Migrations/*_AddRatingsAndReports.cs`

- [ ] **Step 1: Generate the migration**
Run:
```bash
cd /c/prjRecherche/Taxi && dotnet ef migrations add AddRatingsAndReports --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: `Done.` New `*_AddRatingsAndReports.cs`. Its `Up()` creates ONLY `ratings` (unique index on `ride_id`, index on `driver_id`) and `reports` (index on `ride_id`), snake_case. It must NOT recreate other tables. Confirm by opening the file.

- [ ] **Step 2: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AddRatingsAndReports EF migration"
```

---

## Task 8: Suite + vérification manuelle

- [ ] **Step 1: Run the whole suite**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass (49 + entities 3 + rate 3 + report 2 = ~57).

- [ ] **Step 2: Manual verification (USER — Docker running)**

> No volume wipe — `MigrateAsync` applies `AddRatingsAndReports` at startup. Start the AppHost (or F5).

In Scalar (Authorize with the right token per role):
  1. Complete a ride end-to-end (Phase 1 flow: Client requests → Driver accepts/arrived/start/complete). Note the ride `id` and the driver's user.
  2. **Client**: `POST /api/rides/{id}/rate` `{ "score": 4, "comment": "Bien" }` → **200** `RatingDto`.
  3. **Driver**: `GET /api/drivers/me` → `averageRating` is now `4`.
  4. **Client**: `POST /api/rides/{id}/rate` `{ "score": 2 }` again → **200** (upsert — same rating updated, not duplicated); driver `averageRating` becomes `2`.
  5. **Client**: rate a NON-completed ride → **409** `Rating.RideNotCompleted`. Rate someone else's ride → **403**. Rate with score 6 → **400** (validation).
  6. **Client**: `POST /api/rides/{id}/report` `{ "reason": "Retard", "description": "30 min" }` → **200** `ReportDto`. Report someone else's ride → **403**.

- [ ] **Step 3: Confirm results to the user.** No commit (verification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] Migration `AddRatingsAndReports` présente.
- [ ] Notation : 200 sur course terminée propre, upsert (pas de doublon), moyenne chauffeur mise à jour ; 409 si non terminée ; 403 si pas propriétaire ; 400 si score hors 1-5. Signalement : 200 sur course propre, 403 sinon.
- [ ] Tout committé sur `main`.

## Suite (hors périmètre)

Administration (stats + listes + modération des signalements), Dispatch (PostGIS), temps réel SignalR, Identité Phase 3 (documents/Blob), frontend.
