# Module Drivers (Profil chauffeur) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrer le profil chauffeur (self-service) dans un module `Drivers` : entité `Driver`, upsert de profil, consultation, et gestion de disponibilité, réservés au rôle `Driver`.

**Architecture:** Module `Drivers` (Clean Architecture). Entité riche `Driver : Entity`. CQRS sans MediatR (handlers + Result). `IRepository<Driver>` générique + Specification. Endpoints `IEndpoint` avec rôle `Driver` imposé et `userId` extrait du JWT.

**Tech Stack:** .NET 10, EF Core 10 + migrations, FluentValidation, Ardalis.Specification, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-16-drivers-module-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Identité (Phases 1+2) + Tarification livrés. 21 tests verts au départ.

> **Portée :** profil chauffeur self-service uniquement. Gestion admin, matching PostGIS, documents chauffeur, calcul d'AverageRating = hors périmètre.

---

## Structure de fichiers cible

```
src/Taxi.Domain/Drivers/Driver.cs                                  — créé
src/Taxi.Infrastructure/Persistence/Configurations/DriverConfiguration.cs — créé
src/Taxi.Infrastructure/Persistence/AppDbContext.cs                — modifié (+ DbSet)
src/Taxi.Application/Drivers/DriverDto.cs                          — créé
src/Taxi.Application/Drivers/DriverByUserIdSpec.cs                 — créé
src/Taxi.Application/Drivers/UpsertProfile/{UpsertDriverProfileCommand,Validator,Handler}.cs — créés
src/Taxi.Application/Drivers/GetMyDriver/{GetMyDriverQuery,Handler}.cs — créés
src/Taxi.Application/Drivers/SetAvailability/{SetAvailabilityCommand,Handler}.cs — créés
src/Taxi.Web.Api/Endpoints/ClaimsPrincipalExtensions.cs           — créé (helper GetUserId)
src/Taxi.Web.Api/Endpoints/Tags.cs                                — modifié (+ Drivers)
src/Taxi.Web.Api/Modules/Drivers/{UpsertDriverEndpoint,GetMyDriverEndpoint,SetAvailabilityEndpoint}.cs — créés
src/Taxi.Infrastructure/Persistence/Migrations/*                  — généré (AddDrivers)
tests/Taxi.Application.Tests/Drivers/DriverTests.cs               — créé
tests/Taxi.Application.Tests/Drivers/UpsertDriverProfileHandlerTests.cs — créé
tests/Taxi.Application.Tests/Drivers/DriverQueryHandlersTests.cs  — créé
```

---

## Task 1: Domain — entité Driver (TDD)

**Files:**
- Create: `src/Taxi.Domain/Drivers/Driver.cs`
- Test: `tests/Taxi.Application.Tests/Drivers/DriverTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Taxi.Application.Tests/Drivers/DriverTests.cs`:
```csharp
using FluentAssertions;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class DriverTests
{
    [Fact]
    public void Create_should_set_fields_with_defaults()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.UserId.Should().Be("u-1");
        driver.LicenseNumber.Should().Be("LIC-001");
        driver.VehiclePlate.Should().Be("DJ-1234");
        driver.VehicleType.Should().Be("Taxi");
        driver.IsAvailable.Should().BeFalse();
        driver.AverageRating.Should().Be(0);
    }

    [Fact]
    public void UpdateProfile_should_change_profile_fields()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.UpdateProfile("LIC-002", "DJ-9999", "Minibus");

        driver.LicenseNumber.Should().Be("LIC-002");
        driver.VehiclePlate.Should().Be("DJ-9999");
        driver.VehicleType.Should().Be("Minibus");
    }

    [Fact]
    public void SetAvailability_should_toggle_availability()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");

        driver.SetAvailability(true);
        driver.IsAvailable.Should().BeTrue();

        driver.SetAvailability(false);
        driver.IsAvailable.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (Driver absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 3: Create `Driver.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Drivers;

public sealed class Driver : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string LicenseNumber { get; private set; } = string.Empty;
    public string VehiclePlate { get; private set; } = string.Empty;
    public string VehicleType { get; private set; } = "Taxi";
    public bool IsAvailable { get; private set; }
    public double AverageRating { get; private set; }

    private Driver() { } // EF

    public static Driver Create(string userId, string licenseNumber, string vehiclePlate, string vehicleType)
        => new()
        {
            UserId = userId,
            LicenseNumber = licenseNumber,
            VehiclePlate = vehiclePlate,
            VehicleType = vehicleType
        };

    public void UpdateProfile(string licenseNumber, string vehiclePlate, string vehicleType)
    {
        LicenseNumber = licenseNumber;
        VehiclePlate = vehiclePlate;
        VehicleType = vehicleType;
    }

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;
}
```

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (Application.Tests +3).

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): Driver entity (profile + availability)"
```

---

## Task 2: Infrastructure — EF config + DbSet

**Files:**
- Create: `src/Taxi.Infrastructure/Persistence/Configurations/DriverConfiguration.cs`
- Modify: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create `DriverConfiguration.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Drivers;

namespace Taxi.Infrastructure.Persistence.Configurations;

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
    }
}
```

- [ ] **Step 2: Add a `DbSet<Driver>` to `AppDbContext.cs`** — read it first, add after the existing `RefreshTokens` DbSet:
```csharp
    public DbSet<Driver> Drivers => Set<Driver>();
```
Add `using Taxi.Domain.Drivers;` at the top of AppDbContext.cs (alongside the existing `using Taxi.Domain.Identity;` / `using Taxi.Domain.Pricing;`).

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): Driver EF configuration and DbSet"
```

---

## Task 3: Application — DriverDto + Specification

**Files:**
- Create: `src/Taxi.Application/Drivers/DriverDto.cs`
- Create: `src/Taxi.Application/Drivers/DriverByUserIdSpec.cs`

- [ ] **Step 1: Create `DriverDto.cs`** (with a mapping factory to avoid duplication across handlers)
```csharp
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

public sealed record DriverDto(
    int Id,
    string UserId,
    string LicenseNumber,
    string VehiclePlate,
    string VehicleType,
    bool IsAvailable,
    double AverageRating)
{
    public static DriverDto From(Driver driver) => new(
        driver.Id, driver.UserId, driver.LicenseNumber, driver.VehiclePlate,
        driver.VehicleType, driver.IsAvailable, driver.AverageRating);
}
```

- [ ] **Step 2: Create `DriverByUserIdSpec.cs`**
```csharp
using Ardalis.Specification;
using Taxi.Domain.Drivers;

namespace Taxi.Application.Drivers;

internal sealed class DriverByUserIdSpec : Specification<Driver>
{
    public DriverByUserIdSpec(string userId)
        => Query.Where(d => d.UserId == userId);
}
```

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): DriverDto and DriverByUserIdSpec"
```

---

## Task 4: Application — UpsertDriverProfile (TDD)

**Files:**
- Create: `src/Taxi.Application/Drivers/UpsertProfile/UpsertDriverProfileCommand.cs`
- Create: `src/Taxi.Application/Drivers/UpsertProfile/UpsertDriverProfileCommandValidator.cs`
- Create: `src/Taxi.Application/Drivers/UpsertProfile/UpsertDriverProfileCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Drivers/UpsertDriverProfileHandlerTests.cs`

- [ ] **Step 1: Create the command + validator**

`UpsertDriverProfileCommand.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.UpsertProfile;

public sealed record UpsertDriverProfileCommand(
    string UserId, string LicenseNumber, string VehiclePlate, string VehicleType)
    : ICommand<DriverDto>;
```

`UpsertDriverProfileCommandValidator.cs`:
```csharp
using FluentValidation;

namespace Taxi.Application.Drivers.UpsertProfile;

internal sealed class UpsertDriverProfileCommandValidator : AbstractValidator<UpsertDriverProfileCommand>
{
    public UpsertDriverProfileCommandValidator()
    {
        RuleFor(c => c.LicenseNumber).NotEmpty();
        RuleFor(c => c.VehiclePlate).NotEmpty();
        RuleFor(c => c.VehicleType).NotEmpty();
    }
}
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Drivers/UpsertDriverProfileHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.UpsertProfile;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class UpsertDriverProfileHandlerTests
{
    private readonly Mock<IRepository<Driver>> _repo = new();

    [Fact]
    public async Task Should_create_when_no_existing_profile()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new UpsertDriverProfileCommandHandler(_repo.Object);

        var result = await handler.Handle(
            new UpsertDriverProfileCommand("u-1", "LIC-001", "DJ-1234", "Taxi"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LicenseNumber.Should().Be("LIC-001");
        _repo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_update_when_profile_exists()
    {
        var existing = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(existing);
        var handler = new UpsertDriverProfileCommandHandler(_repo.Object);

        var result = await handler.Handle(
            new UpsertDriverProfileCommand("u-1", "LIC-002", "DJ-9999", "Minibus"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LicenseNumber.Should().Be("LIC-002");
        result.Value.VehicleType.Should().Be("Minibus");
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<Driver>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `UpsertDriverProfileCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.UpsertProfile;

internal sealed class UpsertDriverProfileCommandHandler(IRepository<Driver> drivers)
    : ICommandHandler<UpsertDriverProfileCommand, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(UpsertDriverProfileCommand command, CancellationToken cancellationToken)
    {
        var existing = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);

        if (existing is null)
        {
            var created = Driver.Create(command.UserId, command.LicenseNumber, command.VehiclePlate, command.VehicleType);
            await drivers.AddAsync(created, cancellationToken);
            return DriverDto.From(created);
        }

        existing.UpdateProfile(command.LicenseNumber, command.VehiclePlate, command.VehicleType);
        await drivers.UpdateAsync(existing, cancellationToken);
        return DriverDto.From(existing);
    }
}
```

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (Application.Tests +2).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): upsert driver profile command (TDD)"
```

---

## Task 5: Application — GetMyDriver + SetAvailability (TDD)

**Files:**
- Create: `src/Taxi.Application/Drivers/GetMyDriver/GetMyDriverQuery.cs`
- Create: `src/Taxi.Application/Drivers/GetMyDriver/GetMyDriverQueryHandler.cs`
- Create: `src/Taxi.Application/Drivers/SetAvailability/SetAvailabilityCommand.cs`
- Create: `src/Taxi.Application/Drivers/SetAvailability/SetAvailabilityCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Drivers/DriverQueryHandlersTests.cs`

- [ ] **Step 1: Create the query + command**

`GetMyDriverQuery.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.GetMyDriver;

public sealed record GetMyDriverQuery(string UserId) : IQuery<DriverDto>;
```

`SetAvailabilityCommand.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.SetAvailability;

public sealed record SetAvailabilityCommand(string UserId, bool IsAvailable) : ICommand<DriverDto>;
```

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Drivers/DriverQueryHandlersTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers.GetMyDriver;
using Taxi.Application.Drivers.SetAvailability;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.Drivers;

public class DriverQueryHandlersTests
{
    private readonly Mock<IRepository<Driver>> _repo = new();

    [Fact]
    public async Task GetMyDriver_should_return_dto_when_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi"));
        var handler = new GetMyDriverQueryHandler(_repo.Object);

        var result = await handler.Handle(new GetMyDriverQuery("u-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("u-1");
    }

    [Fact]
    public async Task GetMyDriver_should_fail_notfound_when_absent()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new GetMyDriverQueryHandler(_repo.Object);

        var result = await handler.Handle(new GetMyDriverQuery("u-x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task SetAvailability_should_toggle_and_update_when_found()
    {
        var driver = Driver.Create("u-1", "LIC-001", "DJ-1234", "Taxi");
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(driver);
        var handler = new SetAvailabilityCommandHandler(_repo.Object);

        var result = await handler.Handle(new SetAvailabilityCommand("u-1", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAvailable.Should().BeTrue();
        _repo.Verify(r => r.UpdateAsync(driver, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAvailability_should_fail_notfound_when_absent()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Driver>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Driver?)null);
        var handler = new SetAvailabilityCommandHandler(_repo.Object);

        var result = await handler.Handle(new SetAvailabilityCommand("u-x", true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handlers absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 4: Create `GetMyDriverQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.GetMyDriver;

internal sealed class GetMyDriverQueryHandler(IRepository<Driver> drivers)
    : IQueryHandler<GetMyDriverQuery, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(GetMyDriverQuery query, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(query.UserId), cancellationToken);
        return driver is null
            ? Result.Failure<DriverDto>(Error.NotFound("Driver.NotFound", "Profil chauffeur introuvable."))
            : DriverDto.From(driver);
    }
}
```

- [ ] **Step 5: Create `SetAvailabilityCommandHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Drivers.SetAvailability;

internal sealed class SetAvailabilityCommandHandler(IRepository<Driver> drivers)
    : ICommandHandler<SetAvailabilityCommand, DriverDto>
{
    public async Task<Result<DriverDto>> Handle(SetAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.UserId), cancellationToken);
        if (driver is null)
            return Result.Failure<DriverDto>(Error.NotFound("Driver.NotFound", "Profil chauffeur introuvable."));

        driver.SetAvailability(command.IsAvailable);
        await drivers.UpdateAsync(driver, cancellationToken);
        return DriverDto.From(driver);
    }
}
```
NOTE: `DriverByUserIdSpec` is `internal` in namespace `Taxi.Application.Drivers`; both handlers reference it via `using Taxi.Application.Drivers;` (already present for `DriverDto`). Same assembly → accessible.

- [ ] **Step 6: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (Application.Tests +4).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): get-my-driver query and set-availability command (TDD)"
```

---

## Task 6: Web.Api — endpoints + helper + Tags

**Files:**
- Create: `src/Taxi.Web.Api/Endpoints/ClaimsPrincipalExtensions.cs`
- Modify: `src/Taxi.Web.Api/Endpoints/Tags.cs`
- Create: `src/Taxi.Web.Api/Modules/Drivers/UpsertDriverEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Drivers/GetMyDriverEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Drivers/SetAvailabilityEndpoint.cs`

- [ ] **Step 1: Create `ClaimsPrincipalExtensions.cs`** (DRY userId extraction)
```csharp
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Taxi.Web.Api.Endpoints;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
           ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
}
```

- [ ] **Step 2: Add a `Drivers` tag to `Tags.cs`** — final content:
```csharp
namespace Taxi.Web.Api.Endpoints;

public static class Tags
{
    public const string Identity = "Auth";
    public const string Pricing = "Pricing";
    public const string Drivers = "Drivers";
}
```

- [ ] **Step 3: Create `UpsertDriverEndpoint.cs`**
```csharp
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.UpsertProfile;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

public sealed record UpsertDriverRequest(string LicenseNumber, string VehiclePlate, string VehicleType);

public sealed class UpsertDriverEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/drivers", async (
            UpsertDriverRequest body,
            System.Security.Claims.ClaimsPrincipal principal,
            ICommandHandler<UpsertDriverProfileCommand, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(
                new UpsertDriverProfileCommand(userId, body.LicenseNumber, body.VehiclePlate, body.VehicleType), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("UpsertDriverProfile")
        .WithTags(Tags.Drivers)
        .WithSummary("Créer ou mettre à jour mon profil chauffeur")
        .WithDescription("Self-service : crée le profil s'il n'existe pas pour l'utilisateur courant, sinon le met à jour.");
    }
}
```

- [ ] **Step 4: Create `GetMyDriverEndpoint.cs`**
```csharp
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.GetMyDriver;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

public sealed class GetMyDriverEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/drivers/me", async (
            System.Security.Claims.ClaimsPrincipal principal,
            IQueryHandler<GetMyDriverQuery, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new GetMyDriverQuery(userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("GetMyDriver")
        .WithTags(Tags.Drivers)
        .WithSummary("Consulter mon profil chauffeur")
        .WithDescription("Renvoie le profil chauffeur de l'utilisateur courant (404 s'il n'existe pas).");
    }
}
```

- [ ] **Step 5: Create `SetAvailabilityEndpoint.cs`**
```csharp
using Taxi.Application.Drivers;
using Taxi.Application.Drivers.SetAvailability;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Drivers;

public sealed record SetAvailabilityRequest(bool IsAvailable);

public sealed class SetAvailabilityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/drivers/set-availability", async (
            SetAvailabilityRequest body,
            System.Security.Claims.ClaimsPrincipal principal,
            ICommandHandler<SetAvailabilityCommand, DriverDto> handler,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new SetAvailabilityCommand(userId, body.IsAvailable), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization(policy => policy.RequireRole(RoleNames.Driver))
        .WithName("SetDriverAvailability")
        .WithTags(Tags.Drivers)
        .WithSummary("Définir ma disponibilité")
        .WithDescription("Bascule la disponibilité du chauffeur courant.");
    }
}
```

- [ ] **Step 6: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass.

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): driver profile endpoints (upsert, me, set-availability) with Driver role"
```

---

## Task 7: Migration EF AddDrivers

**Files:**
- Create (generated): `src/Taxi.Infrastructure/Persistence/Migrations/*_AddDrivers.cs`

- [ ] **Step 1: Generate the migration**
Run:
```bash
cd /c/prjRecherche/Taxi && dotnet ef migrations add AddDrivers --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: `Done.` New `*_AddDrivers.cs`. Its `Up()` creates ONLY the `drivers` table (snake_case columns: `id`, `user_id`, `license_number`, `vehicle_plate`, `vehicle_type`, `is_available`, `average_rating`, `created_at`) with a UNIQUE index on `user_id`. It must NOT recreate `asp_net_*`, `zone_prices`, or `refresh_tokens`. Confirm by opening the file.

- [ ] **Step 2: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AddDrivers EF migration (drivers table)"
```

---

## Task 8: Suite + vérification manuelle

- [ ] **Step 1: Run the whole suite**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass (Application.Tests grew by entity 3 + upsert 2 + get/set 4 = +9; plus Architecture 3).

- [ ] **Step 2: Manual verification (USER — Docker running)**

> No volume wipe — `MigrateAsync` applies `AddDrivers` at startup. Start the AppHost (or F5 in VS to debug): `dotnet run --project C:\prjRecherche\Taxi\Taxi.AppHost`.

In Scalar:
  1. Register/login a **Driver** (e.g. `POST /api/auth/register` `{ "fullName":"Chauffeur Test","phoneNumber":"77000003","password":"123456","role":"Driver" }`), copy the `accessToken`.
  2. With `Authorization: Bearer <token>`: `POST /api/drivers` `{ "licenseNumber":"LIC-001","vehiclePlate":"DJ-1234","vehicleType":"Taxi" }` → **200** `DriverDto` (création).
  3. `GET /api/drivers/me` → **200** the same profile.
  4. `POST /api/drivers` again with different values → **200**, profile updated (NOT duplicated).
  5. `POST /api/drivers/set-availability` `{ "isAvailable": true }` → **200**, `isAvailable: true`.
  6. Log in as a **Client** and call `GET /api/drivers/me` with that token → **403 Forbidden** (role check).

- [ ] **Step 3: Confirm results to the user.** No commit (verification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur.
- [ ] `dotnet test Taxi.slnx` : tous verts.
- [ ] Migration `AddDrivers` présente (table `drivers`, index unique sur `user_id`).
- [ ] upsert crée puis met à jour (pas de doublon) ; `me` renvoie le profil / 404 ; `set-availability` bascule ; un Client reçoit 403.
- [ ] Tout committé sur `main`.

## Suite (hors périmètre)

Module Courses (réutilisera `Driver` pour l'assignation), Dispatch (matching PostGIS lisant `Driver`), Administration
(gestion des chauffeurs), Identité Phase 3 (documents chauffeur sur `Driver`).
