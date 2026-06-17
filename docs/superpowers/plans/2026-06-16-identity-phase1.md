# Module Identité — Phase 1 (Auth de base) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implémenter l'authentification de base de TaxiDjibouti avec ASP.NET Core Identity (login par téléphone), JWT maison, et endpoints register/login/me — en passant le `AppDbContext` sous Identity et en introduisant les migrations EF.

**Architecture:** Pattern Skoleo adapté mono-tenant. `ApplicationUser : IdentityUser` (clé string/GUID) en Domain ; `ITokenService` abstrait en Application + CQRS (register/login/me) ; `TokenService` (JWT) + `IdentityDbContext` + DI Identity/JWT en Infrastructure ; endpoints `IEndpoint` en Web.Api. Result pattern + décorateur de validation existants.

**Tech Stack:** .NET 10, ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`), JWT (`Microsoft.IdentityModel.JsonWebTokens` / `Microsoft.AspNetCore.Authentication.JwtBearer`), EF Core 10 + Npgsql + migrations, FluentValidation, xUnit.

**Spec :** `docs/superpowers/specs/2026-06-16-identity-module-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Fondations + module Tarification déjà livrés.

> **Portée :** Phase 1 uniquement (auth de base). Refresh tokens = Phase 2, documents chauffeur = Phase 3, profil `Driver` = module Dispatch. Frontend = chantier séparé.

---

## Structure de fichiers cible

```
src/Taxi.SharedKernel/Error.cs                 (+ Unauthorized) — modifié
src/Taxi.Web.Api/Endpoints/ResultExtensions.cs (+ 401) — modifié
src/Taxi.Domain/Identity/ApplicationUser.cs    — créé
src/Taxi.Domain/Identity/RoleNames.cs          — créé
src/Taxi.Application/Identity/Abstractions/ITokenService.cs, AccessToken.cs — créés
src/Taxi.Application/Identity/Auth/AuthResponse.cs, UserInfo.cs — créés
src/Taxi.Application/Identity/Auth/Register/{RegisterCommand,RegisterCommandValidator,RegisterCommandHandler}.cs — créés
src/Taxi.Application/Identity/Auth/Login/{LoginCommand,LoginCommandValidator,LoginCommandHandler}.cs — créés
src/Taxi.Application/Identity/Auth/GetMe/{GetMeQuery,GetMeQueryHandler}.cs — créés
src/Taxi.Infrastructure/Persistence/AppDbContext.cs            — modifié (IdentityDbContext)
src/Taxi.Infrastructure/Persistence/AppDbContextFactory.cs     — créé (design-time)
src/Taxi.Infrastructure/Persistence/Migrations/*              — généré (EF)
src/Taxi.Infrastructure/Identity/{JwtSettings,TokenService,IdentitySeeder,DependencyInjection}.cs — créés
src/Taxi.Web.Api/Modules/Identity/{RegisterEndpoint,LoginEndpoint,MeEndpoint}.cs — créés
src/Taxi.Web.Api/Program.cs                    — modifié (Identity DI, Migrate, seed, auth middleware)
src/Taxi.Web.Api/appsettings.json              — modifié (section Jwt)
tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs — créé
tests/Taxi.Architecture.Tests/LayeringTests.cs — modifié
```

---

## Task 1: SharedKernel — ErrorType.Unauthorized (+ mapping 401)

**Files:**
- Modify: `src/Taxi.SharedKernel/Error.cs`
- Modify: `src/Taxi.Web.Api/Endpoints/ResultExtensions.cs`
- Test: `tests/Taxi.Application.Tests/SharedKernel/ErrorTests.cs`

- [ ] **Step 1: Write the failing test** — create `tests/Taxi.Application.Tests/SharedKernel/ErrorTests.cs`:
```csharp
using FluentAssertions;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.SharedKernel;

public class ErrorTests
{
    [Fact]
    public void Unauthorized_should_have_unauthorized_type()
    {
        var error = Error.Unauthorized("Auth.Invalid", "Identifiants invalides");
        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("Auth.Invalid");
    }
}
```

- [ ] **Step 2: Run the test — expect FAIL (Unauthorized missing)**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure (`ErrorType.Unauthorized` / `Error.Unauthorized` absent).

- [ ] **Step 3: Add Unauthorized to `Error.cs`**

In `src/Taxi.SharedKernel/Error.cs`, add `Unauthorized` to the enum and a factory. Final content:
```csharp
namespace Taxi.SharedKernel;

public enum ErrorType { None = 0, Failure = 1, Validation = 2, NotFound = 3, Conflict = 4, Unauthorized = 5 }

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
}
```

- [ ] **Step 4: Run the test — expect PASS**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (11 tests total).

- [ ] **Step 5: Map Unauthorized → 401 in `ResultExtensions.cs`**

In `src/Taxi.Web.Api/Endpoints/ResultExtensions.cs`, add the `Unauthorized` arm to the switch. Final `Problem` method:
```csharp
    private static IResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Problem(statusCode: status, title: error.Code, detail: error.Description);
    }
```

- [ ] **Step 6: Build + tests**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: build 0 errors, tests pass.

- [ ] **Step 7: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(sharedkernel): add ErrorType.Unauthorized mapped to 401"
```

---

## Task 2: Enregistrer les packages Identity/JWT en CPM

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add the package versions**

In `Directory.Packages.props`, inside the existing `<ItemGroup>`, add these entries (aligned on .NET 10):
```xml
    <PackageVersion Include="Microsoft.Extensions.Identity.Stores" Version="10.0.8" />
    <PackageVersion Include="Microsoft.Extensions.Identity.Core" Version="10.0.8" />
    <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.8" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.8" />
    <PackageVersion Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.14.0" />
    <PackageVersion Include="Microsoft.IdentityModel.Tokens" Version="8.14.0" />
```

> Note exécutant : si `Microsoft.IdentityModel.JsonWebTokens`/`.Tokens` 8.14.0 ne restaure pas, aligner sur la version réellement tirée transitivement par `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.8 (`dotnet list src/Taxi.Web.Api package --include-transitive` après l'avoir ajouté) et corriger ici. Ne pas laisser une version qui ne restaure pas.

- [ ] **Step 2: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "chore: register Identity/JWT package versions (CPM)"
```

---

## Task 3: Domain — ApplicationUser + RoleNames

**Files:**
- Create: `src/Taxi.Domain/Identity/ApplicationUser.cs`
- Create: `src/Taxi.Domain/Identity/RoleNames.cs`
- Modify: `src/Taxi.Domain/Taxi.Domain.csproj` (add package)

- [ ] **Step 1: Add the Identity stores package to Domain**

Run: `cd /c/prjRecherche/Taxi && dotnet add src/Taxi.Domain package Microsoft.Extensions.Identity.Stores`
Expected: succès (no inline version — CPM provides 10.0.8).

- [ ] **Step 2: Create `ApplicationUser.cs`**

```csharp
using Microsoft.AspNetCore.Identity;

namespace Taxi.Domain.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: Create `RoleNames.cs`**

```csharp
namespace Taxi.Domain.Identity;

public static class RoleNames
{
    public const string Client = "Client";
    public const string Driver = "Driver";
    public const string Admin = "Admin";

    public static readonly string[] All = [Client, Driver, Admin];
}
```

- [ ] **Step 4: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Domain`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): ApplicationUser (IdentityUser) and RoleNames"
```

---

## Task 4: Infrastructure — AppDbContext devient IdentityDbContext + factory design-time

**Files:**
- Modify: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/Taxi.Infrastructure/Persistence/AppDbContextFactory.cs`
- Modify: `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj` (add package)

- [ ] **Step 1: Add the Identity EF Core package**

Run: `cd /c/prjRecherche/Taxi && dotnet add src/Taxi.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore`
Expected: succès (CPM provides 10.0.8).

- [ ] **Step 2: Replace `AppDbContext.cs`** (inherit IdentityDbContext, call base FIRST)

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Taxi.Domain.Identity;
using Taxi.Domain.Pricing;

namespace Taxi.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<ZonePrice> ZonePrices => Set<ZonePrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

- [ ] **Step 3: Create `AppDbContextFactory.cs`** (lets `dotnet ef` build the context at design time without the running app)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Taxi.Infrastructure.Persistence;

internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=taxidb;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
```

- [ ] **Step 4: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (Tests still build; existing 11 tests unaffected.)

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AppDbContext becomes IdentityDbContext + design-time factory"
```

---

## Task 5: Application — ITokenService, AccessToken, DTOs

**Files:**
- Create: `src/Taxi.Application/Identity/Abstractions/AccessToken.cs`
- Create: `src/Taxi.Application/Identity/Abstractions/ITokenService.cs`
- Create: `src/Taxi.Application/Identity/Auth/UserInfo.cs`
- Create: `src/Taxi.Application/Identity/Auth/AuthResponse.cs`

- [ ] **Step 1: Create `AccessToken.cs`**

```csharp
namespace Taxi.Application.Identity.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiresAt);
```

- [ ] **Step 2: Create `ITokenService.cs`**

```csharp
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Abstractions;

public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}
```

- [ ] **Step 3: Create `UserInfo.cs`**

```csharp
namespace Taxi.Application.Identity.Auth;

public sealed record UserInfo(string Id, string FullName, string PhoneNumber, IReadOnlyList<string> Roles);
```

- [ ] **Step 4: Create `AuthResponse.cs`**

```csharp
namespace Taxi.Application.Identity.Auth;

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, string TokenType, UserInfo User);
```

- [ ] **Step 5: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 6: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): ITokenService abstraction and auth DTOs"
```

---

## Task 6: Infrastructure — JwtSettings + TokenService (TDD)

**Files:**
- Create: `src/Taxi.Infrastructure/Identity/JwtSettings.cs`
- Create: `src/Taxi.Infrastructure/Identity/TokenService.cs`
- Modify: `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj` (add JWT packages)
- Test: `tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs`
- Modify: `tests/Taxi.Application.Tests/Taxi.Application.Tests.csproj` (reference Infrastructure)

- [ ] **Step 1: Add JWT packages to Infrastructure**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Infrastructure package Microsoft.IdentityModel.JsonWebTokens
dotnet add src/Taxi.Infrastructure package Microsoft.IdentityModel.Tokens
dotnet add src/Taxi.Infrastructure package Microsoft.Extensions.Options
```
Expected: succès (CPM provides versions; `Microsoft.Extensions.Options` may need adding to Directory.Packages.props at version 10.0.8 if absent — add it there if `dotnet add` complains, then re-run).

- [ ] **Step 2: Create `JwtSettings.cs`**

```csharp
namespace Taxi.Infrastructure.Identity;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "TaxiDjibouti";
    public string Audience { get; init; } = "TaxiDjiboutiApp";
    public int AccessTokenLifetimeMinutes { get; init; } = 60;
}
```

- [ ] **Step 3: Make the test project reference Infrastructure** (to test TokenService directly)

Run: `cd /c/prjRecherche/Taxi && dotnet add tests/Taxi.Application.Tests reference src/Taxi.Infrastructure`
Expected: succès.

Then add `InternalsVisibleTo` so the internal `TokenService` is testable. In `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj`, add inside a `<ItemGroup>`:
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Taxi.Application.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Write the failing test** — `tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs`:
```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Taxi.Domain.Identity;
using Taxi.Infrastructure.Identity;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class TokenServiceTests
{
    private static TokenService CreateService() =>
        new(Options.Create(new JwtSettings
        {
            Secret = "taxi-djibouti-dev-secret-key-minimum-32-characters!",
            Issuer = "TaxiDjibouti",
            Audience = "TaxiDjiboutiApp",
            AccessTokenLifetimeMinutes = 60
        }));

    [Fact]
    public void CreateAccessToken_should_embed_sub_phone_and_role_claims()
    {
        var service = CreateService();
        var user = new ApplicationUser { Id = "u-1", UserName = "77000002", PhoneNumber = "77000002", FullName = "Client Test" };

        var token = service.CreateAccessToken(user, new[] { RoleNames.Client });

        token.Token.Should().NotBeNullOrWhiteSpace();
        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value.Should().Be("u-1");
        jwt.GetClaim("phone").Value.Should().Be("77000002");
        jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).Should().Contain(RoleNames.Client);
    }
}
```

- [ ] **Step 5: Run the test — expect FAIL (TokenService absent)**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 6: Create `TokenService.cs`**

```csharp
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Identity;

internal sealed class TokenService(IOptions<JwtSettings> options) : ITokenService
{
    private readonly JwtSettings _settings = options.Value;

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("phone", user.PhoneNumber ?? string.Empty),
            new("fullName", user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }
}
```

- [ ] **Step 7: Run tests — expect PASS**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (12 tests total).

- [ ] **Step 8: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): JwtSettings and TokenService (JWT generation) with tests"
```

---

## Task 7: Application — Register (command + validator + handler)

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/Register/RegisterCommand.cs`
- Create: `src/Taxi.Application/Identity/Auth/Register/RegisterCommandValidator.cs`
- Create: `src/Taxi.Application/Identity/Auth/Register/RegisterCommandHandler.cs`
- Modify: `src/Taxi.Application/Taxi.Application.csproj` (add Identity package for UserManager)

- [ ] **Step 1: Add the Identity Core package to Application** (for `UserManager` — note: NOT `SignInManager`, which lives in the ASP.NET framework; the Application layer must stay framework-free)

Run: `cd /c/prjRecherche/Taxi && dotnet add src/Taxi.Application package Microsoft.Extensions.Identity.Core`
Expected: succès (CPM 10.0.8).

- [ ] **Step 2: Create `RegisterCommand.cs`**

```csharp
using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Register;

public sealed record RegisterCommand(string FullName, string PhoneNumber, string Password, string Role)
    : ICommand<AuthResponse>;
```

- [ ] **Step 3: Create `RegisterCommandValidator.cs`**

```csharp
using FluentValidation;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Register;

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.FullName).NotEmpty();
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(6);
        RuleFor(c => c.Role).Must(role => RoleNames.All.Contains(role))
            .WithMessage("Rôle invalide (attendu: Client, Driver ou Admin).");
    }
}
```

- [ ] **Step 4: Create `RegisterCommandHandler.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Register;

internal sealed class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService)
    : ICommandHandler<RegisterCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByNameAsync(command.PhoneNumber);
        if (existing is not null)
            return Result.Failure<AuthResponse>(Error.Conflict("Auth.PhoneTaken", "Ce numéro est déjà utilisé."));

        var user = new ApplicationUser
        {
            UserName = command.PhoneNumber,
            PhoneNumber = command.PhoneNumber,
            FullName = command.FullName
        };

        var created = await userManager.CreateAsync(user, command.Password);
        if (!created.Succeeded)
        {
            var first = created.Errors.FirstOrDefault();
            return Result.Failure<AuthResponse>(
                Error.Validation("Auth.RegisterFailed", first?.Description ?? "Inscription impossible."));
        }

        await userManager.AddToRoleAsync(user, command.Role);
        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.CreateAccessToken(user, roles);

        return new AuthResponse(token.Token, token.ExpiresAt, "Bearer",
            new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles.ToList()));
    }
}
```

- [ ] **Step 5: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (Scrutor will discover this internal handler at runtime.)

- [ ] **Step 6: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): Register command, validator and handler"
```

---

## Task 8: Application — Login (command + validator + handler)

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/Login/LoginCommand.cs`
- Create: `src/Taxi.Application/Identity/Auth/Login/LoginCommandValidator.cs`
- Create: `src/Taxi.Application/Identity/Auth/Login/LoginCommandHandler.cs`

> Note: the login handler uses **only `UserManager`** (no `SignInManager`) so the Application layer stays free of the ASP.NET framework. Lockout is handled manually via `IsLockedOutAsync` / `AccessFailedAsync` / `ResetAccessFailedCountAsync`.

- [ ] **Step 1: Create `LoginCommand.cs`**

```csharp
using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Login;

public sealed record LoginCommand(string PhoneNumber, string Password) : ICommand<AuthResponse>;
```

- [ ] **Step 2: Create `LoginCommandValidator.cs`**

```csharp
using FluentValidation;

namespace Taxi.Application.Identity.Auth.Login;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}
```

- [ ] **Step 3: Create `LoginCommandHandler.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Login;

internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "Téléphone ou mot de passe incorrect.");

    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(command.PhoneNumber);
        if (user is null)
            return Result.Failure<AuthResponse>(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            return Result.Failure<AuthResponse>(
                Error.Unauthorized("Auth.LockedOut", "Compte temporairement verrouillé."));

        if (!await userManager.CheckPasswordAsync(user, command.Password))
        {
            await userManager.AccessFailedAsync(user); // incrémente le compteur de lockout
            return Result.Failure<AuthResponse>(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.CreateAccessToken(user, roles);

        return new AuthResponse(token.Token, token.ExpiresAt, "Bearer",
            new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles.ToList()));
    }
}
```
> `UserManager` provient de `Microsoft.Extensions.Identity.Core` (ajouté à Application en Task 7) — pas de dépendance au framework ASP.NET. Le `using Microsoft.AspNetCore.Identity;` est le namespace de `UserManager` (qui vit dans le package Core), pas une référence au framework web.

- [ ] **Step 4: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): Login command, validator and handler"
```

---

## Task 9: Application — GetMe (query + handler)

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/GetMe/GetMeQuery.cs`
- Create: `src/Taxi.Application/Identity/Auth/GetMe/GetMeQueryHandler.cs`

- [ ] **Step 1: Create `GetMeQuery.cs`**

```csharp
using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.GetMe;

public sealed record GetMeQuery(string UserId) : IQuery<UserInfo>;
```

- [ ] **Step 2: Create `GetMeQueryHandler.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.GetMe;

internal sealed class GetMeQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetMeQuery, UserInfo>
{
    public async Task<Result<UserInfo>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId);
        if (user is null)
            return Result.Failure<UserInfo>(Error.NotFound("Auth.UserNotFound", "Utilisateur introuvable."));

        var roles = await userManager.GetRolesAsync(user);
        return new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles.ToList());
    }
}
```

- [ ] **Step 3: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): GetMe query and handler"
```

---

## Task 10: Infrastructure — IdentitySeeder + DI (Identity Core + JWT bearer + TokenService)

**Files:**
- Create: `src/Taxi.Infrastructure/Identity/IdentitySeeder.cs`
- Create: `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`
- Modify: `src/Taxi.Infrastructure/Taxi.Infrastructure.csproj` (add JwtBearer + Configuration abstractions)

- [ ] **Step 1: Add packages to Infrastructure**

Run:
```bash
cd /c/prjRecherche/Taxi
dotnet add src/Taxi.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Taxi.Infrastructure package Microsoft.Extensions.Configuration.Abstractions
```
Expected: succès. (`Microsoft.Extensions.Configuration.Abstractions` may need adding to Directory.Packages.props at 10.0.8 if `dotnet add` complains — add it there then re-run.)

- [ ] **Step 2: Create `IdentitySeeder.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}
```

- [ ] **Step 3: Create `DependencyInjection.cs`** (Identity Core + JWT bearer + TokenService)

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;
using Taxi.Infrastructure.Persistence;

namespace Taxi.Infrastructure.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<ITokenService, TokenService>();

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireDigit = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ClockSkew = TimeSpan.Zero
            };
        });

        return services;
    }
}
```

- [ ] **Step 4: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): identity seeder and Identity/JWT DI registration"
```

---

## Task 11: Web.Api — endpoints + Program.cs wiring + appsettings

**Files:**
- Create: `src/Taxi.Web.Api/Modules/Identity/RegisterEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Identity/LoginEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Identity/MeEndpoint.cs`
- Modify: `src/Taxi.Web.Api/Program.cs`
- Modify: `src/Taxi.Web.Api/appsettings.json`

- [ ] **Step 1: Create `RegisterEndpoint.cs`**

```csharp
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Register;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

public sealed class RegisterEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", async (
            RegisterCommand command,
            ICommandHandler<RegisterCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithTags("Auth");
    }
}
```

- [ ] **Step 2: Create `LoginEndpoint.cs`**

```csharp
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Login;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

public sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            LoginCommand command,
            ICommandHandler<LoginCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithTags("Auth");
    }
}
```

- [ ] **Step 3: Create `MeEndpoint.cs`**

```csharp
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.GetMe;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

public sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", async (
            ClaimsPrincipal principal,
            IQueryHandler<GetMeQuery, UserInfo> handler,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                         ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await handler.Handle(new GetMeQuery(userId), ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithTags("Auth");
    }
}
```

- [ ] **Step 4: Add the `Jwt` section to `appsettings.json`**

In `src/Taxi.Web.Api/appsettings.json`, add a top-level `"Jwt"` object (alongside the existing `"Logging"` etc.):
```json
  "Jwt": {
    "Secret": "taxi-djibouti-dev-secret-key-minimum-32-characters!",
    "Issuer": "TaxiDjibouti",
    "Audience": "TaxiDjiboutiApp",
    "AccessTokenLifetimeMinutes": 60
  }
```
> Note: dev secret only. En production, le `Secret` viendra d'Azure Key Vault (cf. doc d'architecture), pas du fichier.

- [ ] **Step 5: Replace `Program.cs`** (add Identity DI, auth middleware, Migrate + seed)

```csharp
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Taxi.Application;
using Taxi.Infrastructure;
using Taxi.Infrastructure.Identity;
using Taxi.Infrastructure.Persistence;
using Taxi.Web.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<AppDbContext>(
    "taxidb",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddEndpoints();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

app.Run();
```

- [ ] **Step 6: Build**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): auth endpoints (register/login/me), Identity DI, migrate+seed on startup"
```

---

## Task 12: Migration EF InitialIdentity

**Files:**
- Create: `src/Taxi.Infrastructure/Persistence/Migrations/*` (généré)
- Modify: `src/Taxi.Web.Api/Taxi.Web.Api.csproj` (add EF Design)

- [ ] **Step 1: Ensure the EF tools are available**

Run: `cd /c/prjRecherche/Taxi && dotnet ef --version`
Expected: prints a version. If it errors ("command not found"), install: `dotnet tool install --global dotnet-ef` then retry.

- [ ] **Step 2: Add `Microsoft.EntityFrameworkCore.Design` to the startup project**

Run: `cd /c/prjRecherche/Taxi && dotnet add src/Taxi.Web.Api package Microsoft.EntityFrameworkCore.Design`
Expected: succès (CPM provides 10.0.8).

- [ ] **Step 3: Generate the migration**

Run:
```bash
cd /c/prjRecherche/Taxi && dotnet ef migrations add InitialIdentity \
  --project src/Taxi.Infrastructure \
  --startup-project src/Taxi.Web.Api \
  --output-dir Persistence/Migrations
```
Expected: `Done.` Files appear under `src/Taxi.Infrastructure/Persistence/Migrations/` (e.g. `*_InitialIdentity.cs`, `AppDbContextModelSnapshot.cs`). The migration must contain the Identity tables (`asp_net_users`, `asp_net_roles`, …) AND `zone_prices`.

> If `dotnet ef` cannot create the context, the `AppDbContextFactory` (Task 4) is used — confirm it exists. The factory's hardcoded connection string is only used to build the model, not to connect.

- [ ] **Step 4: Build to confirm the generated code compiles**

Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): InitialIdentity EF migration (Identity tables + zone_prices)"
```

---

## Task 13: Tests d'architecture + suite complète

**Files:**
- Modify: `tests/Taxi.Architecture.Tests/LayeringTests.cs`

- [ ] **Step 1: Add an Identity-aware rule** — append this test method inside the `LayeringTests` class:
```csharp
    [Fact]
    public void Identity_user_should_live_in_Domain()
    {
        var result = NetArchTest.Rules.Types.InAssembly(typeof(Taxi.Domain.Identity.ApplicationUser).Assembly)
            .That().HaveNameStartingWith("ApplicationUser")
            .Should().ResideInNamespaceStartingWith("Taxi.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
```
(The existing two rules — Domain not depending on Application/Infrastructure, Application not depending on Infrastructure — still apply and must remain green even though Domain now references the Identity package, because the Identity package is not the Application/Infrastructure assemblies.)

- [ ] **Step 2: Run the architecture tests**

Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Architecture.Tests`
Expected: `Passed!` (3 tests).

- [ ] **Step 3: Run the WHOLE suite**

Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass (12 in Application.Tests + 3 in Architecture.Tests = 15 total).

- [ ] **Step 4: Commit**

```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "test(arch): assert ApplicationUser lives in Domain"
```

---

## Task 14: Vérification manuelle (utilisateur — Docker requis)

> Étape manuelle. **Docker Desktop doit tourner.** ⚠️ Comme on passe de `EnsureCreated` aux migrations, **supprimer d'abord le volume Postgres existant** pour repartir d'une base vierge (sinon conflit « zone_prices existe déjà »). Au dashboard Aspire précédent le volume s'appelle d'après la ressource `postgres` ; le plus simple : `docker volume ls` puis `docker volume rm <volume_taxi_postgres>` pendant que l'AppHost est arrêté. (Ou, en dev, supprimer le conteneur+volume via Docker Desktop.)

- [ ] **Step 1: Démarrer l'AppHost**

Run (utilisateur, terminal dédié): `dotnet run --project C:\prjRecherche\Taxi\Taxi.AppHost`
Expected: `postgres` et `api` en `Running` ; au démarrage, `MigrateAsync` crée le schéma et les 3 rôles sont seedés.

- [ ] **Step 2: Tester via Scalar** (`https://<api>/scalar/v1`)
  1. `POST /api/auth/register` `{ "fullName":"Client Test", "phoneNumber":"77000002", "password":"123456", "role":"Client" }` → **200**, `accessToken` + `user.roles:["Client"]`.
  2. `POST /api/auth/register` même numéro → **409** (numéro déjà pris).
  3. `POST /api/auth/login` `{ "phoneNumber":"77000002", "password":"123456" }` → **200** + token.
  4. `POST /api/auth/login` mauvais mot de passe → **401**.
  5. `GET /api/auth/me` avec en-tête `Authorization: Bearer <token>` → **200** + `UserInfo`. Sans token → **401**.

- [ ] **Step 3: Confirmer le résultat** des 5 tests à l'utilisateur. Aucun commit (vérification).

---

## Definition of Done (Phase 1)

- [ ] `dotnet build Taxi.slnx` : 0 erreur.
- [ ] `dotnet test Taxi.slnx` : 15 tests verts.
- [ ] Migration `InitialIdentity` présente et compilée.
- [ ] AppHost démarre, applique la migration, seed les rôles ; register/login/me se comportent comme attendu (200/409/401).
- [ ] Tout committé sur `main`.

## Phases suivantes (hors périmètre)

- **Phase 2** : refresh tokens (rotation + détection de réutilisation), `revoke`/logout, service de nettoyage.
- **Phase 3** : entité `DriverDocument` + upload Azure Blob.
- **Frontend** : adapter `api.ts` (`user.id` string, `accessToken`) — chantier séparé.
