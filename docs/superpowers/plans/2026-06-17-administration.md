# Module Administration — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Back-office lecture seule (rôle Admin) : statistiques (compteurs) + listes des utilisateurs, chauffeurs, courses et signalements.

**Architecture:** Module `Administration`. Queries CQRS. `IUserDirectory` (Application) pour lire les utilisateurs, implémenté en Infrastructure via `UserManager`/EF. Les entités du domaine passent par `IRepository<T>` (Ardalis `CountAsync`/`ListAsync`). Réutilise `DriverDto`/`RideDto`/`ReportDto`.

**Tech Stack:** .NET 10, EF Core 10 (Infra only), ASP.NET Identity, FluentValidation (n/a ici), xUnit/Moq. **Pas de migration** (lecture seule).

**Spec :** `docs/superpowers/specs/2026-06-17-administration-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Tous les modules précédents livrés. 57 tests verts au départ.

> **Portée :** stats + 5 listes en lecture seule. Pas de modération, pas de gestion users, pas de pagination.

---

## Structure de fichiers cible

```
src/Taxi.Application/Administration/IUserDirectory.cs, UserSummary.cs, AdminStatsDto.cs — créés
src/Taxi.Application/Administration/Stats/{GetAdminStatsQuery,Handler}.cs              — créés
src/Taxi.Application/Administration/Listing/{GetUsersQuery,GetUsersQueryHandler,
    GetDriversQuery,GetDriversQueryHandler,GetAllRidesQuery,GetAllRidesQueryHandler,
    GetReportsQuery,GetReportsQueryHandler}.cs                                          — créés
src/Taxi.Infrastructure/Identity/UserDirectory.cs                                      — créé
src/Taxi.Infrastructure/Identity/DependencyInjection.cs                                — modifié (register IUserDirectory)
src/Taxi.Web.Api/Endpoints/Tags.cs                                                     — modifié (+ Admin)
src/Taxi.Web.Api/Modules/Admin/AdminEndpoints.cs                                       — créé (5 GET via group)
tests/Taxi.Application.Tests/Administration/GetAdminStatsQueryHandlerTests.cs          — créé
tests/Taxi.Application.Tests/Administration/AdminListingHandlersTests.cs               — créé
```

---

## Task 1: Application — IUserDirectory, UserSummary, AdminStatsDto

**Files:**
- Create: `src/Taxi.Application/Administration/UserSummary.cs`, `IUserDirectory.cs`, `AdminStatsDto.cs`

- [ ] **Step 1: Create `UserSummary.cs`**
```csharp
namespace Taxi.Application.Administration;

public sealed record UserSummary(string Id, string FullName, string PhoneNumber, IReadOnlyList<string> Roles);
```

- [ ] **Step 2: Create `IUserDirectory.cs`**
```csharp
namespace Taxi.Application.Administration;

public interface IUserDirectory
{
    Task<int> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Create `AdminStatsDto.cs`**
```csharp
namespace Taxi.Application.Administration;

public sealed record AdminStatsDto(int Users, int Drivers, int Rides, int Reports);
```

- [ ] **Step 4: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): admin user directory abstraction and stats DTO"
```

---

## Task 2: Application — GetAdminStats (TDD)

**Files:**
- Create: `src/Taxi.Application/Administration/Stats/GetAdminStatsQuery.cs`, `GetAdminStatsQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Administration/GetAdminStatsQueryHandlerTests.cs`

- [ ] **Step 1: Create `GetAdminStatsQuery.cs`**
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Stats;

public sealed record GetAdminStatsQuery : IQuery<AdminStatsDto>;
```

- [ ] **Step 2: Write the failing test** — `tests/Taxi.Application.Tests/Administration/GetAdminStatsQueryHandlerTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Administration;
using Taxi.Application.Administration.Stats;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Administration;

public class GetAdminStatsQueryHandlerTests
{
    [Fact]
    public async Task Should_aggregate_counts()
    {
        var users = new Mock<IUserDirectory>();
        users.Setup(u => u.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10);
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var rides = new Mock<IRepository<Ride>>();
        rides.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var reports = new Mock<IRepository<Report>>();
        reports.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var handler = new GetAdminStatsQueryHandler(users.Object, drivers.Object, rides.Object, reports.Object);

        var result = await handler.Handle(new GetAdminStatsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new AdminStatsDto(10, 3, 7, 2));
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 4: Create `GetAdminStatsQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Domain.Drivers;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Stats;

internal sealed class GetAdminStatsQueryHandler(
    IUserDirectory users,
    IRepository<Driver> drivers,
    IRepository<Ride> rides,
    IRepository<Report> reports)
    : IQueryHandler<GetAdminStatsQuery, AdminStatsDto>
{
    public async Task<Result<AdminStatsDto>> Handle(GetAdminStatsQuery query, CancellationToken cancellationToken)
    {
        var userCount = await users.CountAsync(cancellationToken);
        var driverCount = await drivers.CountAsync(cancellationToken);
        var rideCount = await rides.CountAsync(cancellationToken);
        var reportCount = await reports.CountAsync(cancellationToken);

        return new AdminStatsDto(userCount, driverCount, rideCount, reportCount);
    }
}
```
NOTE: `IUserDirectory`/`AdminStatsDto` are in the parent namespace `Taxi.Application.Administration` (visible from `.Stats` without a using). `IRepository<T>.CountAsync(CancellationToken)` is the Ardalis "count all" overload.

- [ ] **Step 5: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (1 new test).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): GetAdminStats query (TDD)"
```

---

## Task 3: Application — listing queries (users/drivers/rides/reports)

**Files:**
- Create: `src/Taxi.Application/Administration/Listing/GetUsersQuery.cs`, `GetUsersQueryHandler.cs`, `GetDriversQuery.cs`, `GetDriversQueryHandler.cs`, `GetAllRidesQuery.cs`, `GetAllRidesQueryHandler.cs`, `GetReportsQuery.cs`, `GetReportsQueryHandler.cs`
- Test: `tests/Taxi.Application.Tests/Administration/AdminListingHandlersTests.cs`

- [ ] **Step 1: Create the 4 queries**

`GetUsersQuery.cs`:
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

public sealed record GetUsersQuery : IQuery<IReadOnlyList<UserSummary>>;
```
`GetDriversQuery.cs`:
```csharp
using Taxi.Application.Drivers;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

public sealed record GetDriversQuery : IQuery<IReadOnlyList<DriverDto>>;
```
`GetAllRidesQuery.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

public sealed record GetAllRidesQuery : IQuery<IReadOnlyList<RideDto>>;
```
`GetReportsQuery.cs`:
```csharp
using Taxi.Application.Rides;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

public sealed record GetReportsQuery : IQuery<IReadOnlyList<ReportDto>>;
```
NOTE: `UserSummary` is in the parent namespace `Taxi.Application.Administration`. `DriverDto` is in `Taxi.Application.Drivers`; `RideDto`/`ReportDto` in `Taxi.Application.Rides` (hence the usings).

- [ ] **Step 2: Write the failing tests** — `tests/Taxi.Application.Tests/Administration/AdminListingHandlersTests.cs`:
```csharp
using FluentAssertions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Administration;
using Taxi.Application.Administration.Listing;
using Taxi.Domain.Drivers;
using Xunit;

namespace Taxi.Application.Tests.Administration;

public class AdminListingHandlersTests
{
    [Fact]
    public async Task GetUsers_returns_directory_list()
    {
        var users = new Mock<IUserDirectory>();
        users.Setup(u => u.ListAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<UserSummary> { new("u-1", "Client Test", "77000002", new[] { "Client" }) });
        var handler = new GetUsersQueryHandler(users.Object);

        var result = await handler.Handle(new GetUsersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].FullName.Should().Be("Client Test");
    }

    [Fact]
    public async Task GetDrivers_maps_to_dtos()
    {
        var drivers = new Mock<IRepository<Driver>>();
        drivers.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<Driver> { Driver.Create("u-1", "LIC", "PLATE", "Taxi") });
        var handler = new GetDriversQueryHandler(drivers.Object);

        var result = await handler.Handle(new GetDriversQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }
}
```

- [ ] **Step 3: Run — expect FAIL** (handlers absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`

- [ ] **Step 4: Create `GetUsersQueryHandler.cs`**
```csharp
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

internal sealed class GetUsersQueryHandler(IUserDirectory users)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<UserSummary>>
{
    public async Task<Result<IReadOnlyList<UserSummary>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var list = await users.ListAsync(cancellationToken);
        return Result.Success(list);
    }
}
```

- [ ] **Step 5: Create `GetDriversQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Drivers;
using Taxi.Domain.Drivers;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

internal sealed class GetDriversQueryHandler(IRepository<Driver> drivers)
    : IQueryHandler<GetDriversQuery, IReadOnlyList<DriverDto>>
{
    public async Task<Result<IReadOnlyList<DriverDto>>> Handle(GetDriversQuery query, CancellationToken cancellationToken)
    {
        var list = await drivers.ListAsync(cancellationToken);
        return list.Select(DriverDto.From).ToList();
    }
}
```

- [ ] **Step 6: Create `GetAllRidesQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

internal sealed class GetAllRidesQueryHandler(IRepository<Ride> rides)
    : IQueryHandler<GetAllRidesQuery, IReadOnlyList<RideDto>>
{
    public async Task<Result<IReadOnlyList<RideDto>>> Handle(GetAllRidesQuery query, CancellationToken cancellationToken)
    {
        var list = await rides.ListAsync(cancellationToken);
        return list.Select(RideDto.From).ToList();
    }
}
```

- [ ] **Step 7: Create `GetReportsQueryHandler.cs`**
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Administration.Listing;

internal sealed class GetReportsQueryHandler(IRepository<Report> reports)
    : IQueryHandler<GetReportsQuery, IReadOnlyList<ReportDto>>
{
    public async Task<Result<IReadOnlyList<ReportDto>>> Handle(GetReportsQuery query, CancellationToken cancellationToken)
    {
        var list = await reports.ListAsync(cancellationToken);
        return list.Select(ReportDto.From).ToList();
    }
}
```
NOTE: `IRepository<T>.ListAsync(CancellationToken)` is the Ardalis "list all" overload. `DriverDto.From`/`RideDto.From`/`ReportDto.From` are the existing mapping factories.

- [ ] **Step 8: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (2 new tests).

- [ ] **Step 9: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): admin listing queries (users/drivers/rides/reports)"
```

---

## Task 4: Infrastructure — UserDirectory + DI

**Files:**
- Create: `src/Taxi.Infrastructure/Identity/UserDirectory.cs`
- Modify: `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`

- [ ] **Step 1: Create `UserDirectory.cs`**
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Taxi.Application.Administration;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Identity;

internal sealed class UserDirectory(UserManager<ApplicationUser> userManager) : IUserDirectory
{
    public Task<int> CountAsync(CancellationToken cancellationToken)
        => userManager.Users.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.ToListAsync(cancellationToken);
        var summaries = new List<UserSummary>(users.Count);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummary(user.Id, user.FullName, user.PhoneNumber ?? string.Empty, roles.ToList()));
        }

        return summaries;
    }
}
```

- [ ] **Step 2: Register it in `DependencyInjection.cs`** — read `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`. In `AddIdentityInfrastructure`, add before `return services;`:
```csharp
        services.AddScoped<IUserDirectory, UserDirectory>();
```
Add `using Taxi.Application.Administration;` at the top if not present.

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (`CountAsync`/`ToListAsync` resolve from `Microsoft.EntityFrameworkCore` — Infrastructure references EF Core.)

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): UserDirectory (user count/list via UserManager) + DI"
```

---

## Task 5: Web.Api — admin endpoints

**Files:**
- Modify: `src/Taxi.Web.Api/Endpoints/Tags.cs`
- Create: `src/Taxi.Web.Api/Modules/Admin/AdminEndpoints.cs`

- [ ] **Step 1: Add `Admin` to `Tags.cs`** — final content:
```csharp
namespace Taxi.Web.Api.Endpoints;

public static class Tags
{
    public const string Identity = "Auth";
    public const string Pricing = "Pricing";
    public const string Drivers = "Drivers";
    public const string Rides = "Rides";
    public const string Admin = "Admin";
}
```

- [ ] **Step 2: Create `AdminEndpoints.cs`** (5 GETs under one group, role Admin)
```csharp
using Taxi.Application.Administration;
using Taxi.Application.Administration.Listing;
using Taxi.Application.Administration.Stats;
using Taxi.Application.Drivers;
using Taxi.Application.Rides;
using Taxi.Domain.Identity;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Admin;

public sealed class AdminEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(p => p.RequireRole(RoleNames.Admin))
            .WithTags(Tags.Admin);

        group.MapGet("/stats", async (
            IQueryHandler<GetAdminStatsQuery, AdminStatsDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAdminStatsQuery(), ct)).ToHttpResult())
            .WithName("AdminStats").WithSummary("Statistiques globales");

        group.MapGet("/users", async (
            IQueryHandler<GetUsersQuery, IReadOnlyList<UserSummary>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetUsersQuery(), ct)).ToHttpResult())
            .WithName("AdminUsers").WithSummary("Liste des utilisateurs");

        group.MapGet("/drivers", async (
            IQueryHandler<GetDriversQuery, IReadOnlyList<DriverDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetDriversQuery(), ct)).ToHttpResult())
            .WithName("AdminDrivers").WithSummary("Liste des chauffeurs");

        group.MapGet("/rides", async (
            IQueryHandler<GetAllRidesQuery, IReadOnlyList<RideDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetAllRidesQuery(), ct)).ToHttpResult())
            .WithName("AdminRides").WithSummary("Liste des courses");

        group.MapGet("/reports", async (
            IQueryHandler<GetReportsQuery, IReadOnlyList<ReportDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetReportsQuery(), ct)).ToHttpResult())
            .WithName("AdminReports").WithSummary("Liste des signalements");
    }
}
```

- [ ] **Step 3: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): admin read-only endpoints (stats/users/drivers/rides/reports)"
```

---

## Task 6: Suite + vérification manuelle

- [ ] **Step 1: Run the whole suite**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass (57 + stats 1 + listing 2 = ~60).

- [ ] **Step 2: Manual verification (USER — Docker running)**

> No migration / no volume wipe (read-only feature). Start the AppHost (or F5).

In Scalar (Authorize with the right token):
  1. Register an **Admin**: `POST /api/auth/register` `{ "fullName":"Admin Taxi","phoneNumber":"77000001","password":"123456","role":"Admin" }` → copy `accessToken`.
  2. (Admin token) `GET /api/admin/stats` → **200**, counts reflecting the data created so far (users ≥ 3, drivers ≥ 1, rides ≥ 1, reports per what you created).
  3. `GET /api/admin/users` → **200**, list incl. the Admin/Client/Driver with their roles.
  4. `GET /api/admin/drivers` → **200**, the driver profile(s). `GET /api/admin/rides` → **200**, the rides. `GET /api/admin/reports` → **200**, the report(s) created in Courses Phase 2.
  5. With a **Client** or **Driver** token, call any `/api/admin/*` → **403 Forbidden**.

- [ ] **Step 3: Confirm results to the user.** No commit (verification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] `stats` renvoie des compteurs cohérents ; les 4 listes renvoient les données ; un non-Admin reçoit 403.
- [ ] Tout committé sur `main`.

## Suite (hors périmètre)

Modération des signalements, gestion des utilisateurs, pagination, Dispatch (PostGIS), temps réel SignalR, Identité Phase 3 (Blob), frontend.
