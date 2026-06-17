# Module Identité — Phase 2 (Refresh tokens) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter des refresh tokens avec rotation + détection de réutilisation, endpoints `/refresh` et `/revoke`, et un service de nettoyage.

**Architecture:** Entité `RefreshToken` (Domain, hachée SHA256, `FamilyId`). `ITokenService` étendu (génération + hachage). Émission centralisée via `AuthTokenIssuer` (Application) réutilisé par Register/Login/Refresh. Repository générique `IRepository<RefreshToken>` + Specifications. `BackgroundService` de nettoyage. Result pattern + décorateurs existants.

**Tech Stack:** .NET 10, ASP.NET Identity, EF Core 10 + migrations, `RandomNumberGenerator`/`SHA256`, FluentValidation, xUnit/Moq.

**Spec :** `docs/superpowers/specs/2026-06-16-identity-phase2-refresh-tokens-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). Phase 1 (auth de base) déjà livrée. 13 tests verts au départ.

> **Portée :** Phase 2 uniquement. Documents chauffeur = Phase 3. Frontend = chantier séparé. Pas d'audit IP/User-Agent.

---

## Structure de fichiers cible

```
src/Taxi.Domain/Identity/RefreshToken.cs                         — créé
src/Taxi.Application/Identity/Abstractions/ITokenService.cs       — modifié (+ refresh)
src/Taxi.Application/Identity/Abstractions/RefreshTokenValue.cs   — créé
src/Taxi.Infrastructure/Identity/TokenService.cs                 — modifié (+ refresh)
src/Taxi.Infrastructure/Identity/JwtSettings.cs                  — modifié (+ RefreshTokenLifetimeDays)
src/Taxi.Web.Api/appsettings.json                               — modifié (+ RefreshTokenLifetimeDays)
src/Taxi.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs — créé
src/Taxi.Infrastructure/Persistence/AppDbContext.cs              — modifié (+ DbSet)
src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenByHashSpec.cs, RefreshTokenByFamilySpec.cs — créés
src/Taxi.Application/Identity/Auth/AuthResponse.cs               — modifié (+ refresh)
src/Taxi.Application/Identity/Auth/AuthTokenIssuer.cs            — créé
src/Taxi.Application/Identity/Auth/Register/RegisterCommandHandler.cs — modifié
src/Taxi.Application/Identity/Auth/Login/LoginCommandHandler.cs  — modifié
src/Taxi.Application/DependencyInjection.cs                      — modifié (register issuer)
src/Taxi.Application/Identity/Auth/Refresh/{RefreshTokenCommand,Validator,Handler}.cs — créés
src/Taxi.Application/Identity/Auth/Revoke/{RevokeTokenCommand,Validator,Handler}.cs   — créés
src/Taxi.Infrastructure/Identity/RefreshTokenCleanupService.cs   — créé
src/Taxi.Infrastructure/Identity/DependencyInjection.cs          — modifié (AddHostedService)
src/Taxi.Web.Api/Modules/Identity/{RefreshEndpoint,RevokeEndpoint}.cs — créés
src/Taxi.Infrastructure/Persistence/Migrations/*                — généré (AddRefreshTokens)
tests/Taxi.Application.Tests/Identity/RefreshTokenTests.cs       — créé
tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs       — modifié (+ refresh tests)
tests/Taxi.Application.Tests/Identity/RefreshTokenCommandHandlerTests.cs — créé
```

---

## Task 1: Domain — entité RefreshToken (TDD)

**Files:**
- Create: `src/Taxi.Domain/Identity/RefreshToken.cs`
- Test: `tests/Taxi.Application.Tests/Identity/RefreshTokenTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Taxi.Application.Tests/Identity/RefreshTokenTests.cs`:
```csharp
using FluentAssertions;
using Taxi.Domain.Identity;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void Create_should_be_active_and_not_revoked()
    {
        var familyId = Guid.NewGuid();
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddDays(7), familyId);

        token.UserId.Should().Be("u-1");
        token.TokenHash.Should().Be("hash-1");
        token.FamilyId.Should().Be(familyId);
        token.IsRevoked.Should().BeFalse();
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Revoke_should_mark_revoked_with_reason_and_replacement()
    {
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddDays(7), Guid.NewGuid());

        token.Revoke("Rotation", replacedByTokenId: 42);

        token.IsRevoked.Should().BeTrue();
        token.RevokedReason.Should().Be("Rotation");
        token.ReplacedByTokenId.Should().Be(42);
        token.RevokedAt.Should().NotBeNull();
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Expired_token_should_not_be_active()
    {
        var token = RefreshToken.Create("u-1", "hash-1", DateTime.UtcNow.AddSeconds(-1), Guid.NewGuid());
        token.IsActive.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (RefreshToken absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 3: Create `RefreshToken.cs`**
```csharp
using Taxi.SharedKernel;

namespace Taxi.Domain.Identity;

public sealed class RefreshToken : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid FamilyId { get; private set; }
    public int? ReplacedByTokenId { get; private set; }

    private RefreshToken() { } // EF

    public static RefreshToken Create(string userId, string tokenHash, DateTime expiresAt, Guid familyId)
        => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            FamilyId = familyId
        };

    public bool IsActive => !IsRevoked && ExpiresAt > DateTime.UtcNow;

    public void Revoke(string reason, int? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
```

- [ ] **Step 4: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (13 in Application.Tests now — was 10 + 3 new).

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(domain): RefreshToken entity with rotation/revocation"
```

---

## Task 2: ITokenService + TokenService — génération & hachage refresh (TDD)

**Files:**
- Create: `src/Taxi.Application/Identity/Abstractions/RefreshTokenValue.cs`
- Modify: `src/Taxi.Application/Identity/Abstractions/ITokenService.cs`
- Modify: `src/Taxi.Infrastructure/Identity/JwtSettings.cs`
- Modify: `src/Taxi.Infrastructure/Identity/TokenService.cs`
- Modify: `src/Taxi.Web.Api/appsettings.json`
- Test: `tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs`

- [ ] **Step 1: Create `RefreshTokenValue.cs`**
```csharp
namespace Taxi.Application.Identity.Abstractions;

public sealed record RefreshTokenValue(string RawToken, string TokenHash, DateTime ExpiresAt);
```

- [ ] **Step 2: Extend `ITokenService.cs`** — final content:
```csharp
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Abstractions;

public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    RefreshTokenValue CreateRefreshToken();
    string HashRefreshToken(string rawToken);
}
```

- [ ] **Step 3: Add `RefreshTokenLifetimeDays` to `JwtSettings.cs`** — final content:
```csharp
namespace Taxi.Infrastructure.Identity;

internal sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "TaxiDjibouti";
    public string Audience { get; init; } = "TaxiDjiboutiApp";
    public int AccessTokenLifetimeMinutes { get; init; } = 60;
    public int RefreshTokenLifetimeDays { get; init; } = 7;
}
```

- [ ] **Step 4: Write the failing tests** — append to `tests/Taxi.Application.Tests/Identity/TokenServiceTests.cs` (inside the class):
```csharp
    [Fact]
    public void HashRefreshToken_should_be_deterministic_lowercase_hex()
    {
        var service = CreateService();
        var hash1 = service.HashRefreshToken("abc");
        var hash2 = service.HashRefreshToken("abc");

        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[0-9a-f]{64}$"); // SHA256 hex
    }

    [Fact]
    public void CreateRefreshToken_should_return_raw_with_matching_hash_and_future_expiry()
    {
        var service = CreateService();
        var rt = service.CreateRefreshToken();

        rt.RawToken.Should().NotBeNullOrWhiteSpace();
        rt.TokenHash.Should().Be(service.HashRefreshToken(rt.RawToken));
        rt.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(10));
    }
```

- [ ] **Step 5: Run — expect FAIL** (methods absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 6: Implement in `TokenService.cs`** — add `using System.Security.Cryptography;` and these members (keep the existing `CreateAccessToken` + the static `Handler` field unchanged):
```csharp
    public RefreshTokenValue CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashRefreshToken(raw);
        var expiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenLifetimeDays);
        return new RefreshTokenValue(raw, hash, expiresAt);
    }

    public string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
```
(`Encoding` is `System.Text` — already imported in TokenService.cs from Phase 1; if not, add `using System.Text;`.)

- [ ] **Step 7: Add the config key to `appsettings.json`** — inside the existing `"Jwt"` object, add:
```json
    "RefreshTokenLifetimeDays": 7
```
(Read appsettings.json first; add it as a sibling of `AccessTokenLifetimeMinutes`, keeping valid JSON.)

- [ ] **Step 8: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (15 in Application.Tests).

- [ ] **Step 9: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(identity): refresh token generation and SHA256 hashing in TokenService"
```

---

## Task 3: Infrastructure — EF config + DbSet pour RefreshToken

**Files:**
- Create: `src/Taxi.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`
- Modify: `src/Taxi.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create `RefreshTokenConfiguration.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taxi.Domain.Identity;

namespace Taxi.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(50);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
    }
}
```

- [ ] **Step 2: Add a `DbSet<RefreshToken>` to `AppDbContext.cs`** — add the property next to `ZonePrices`:
```csharp
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```
(The `using Taxi.Domain.Identity;` is already present in AppDbContext.cs from Phase 1.)

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (RefreshToken is now in the EF model via the configuration + DbSet — the migration in Task 10 will create the table.)

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): RefreshToken EF configuration and DbSet"
```

---

## Task 4: Application — Specifications RefreshToken

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenByHashSpec.cs`
- Create: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenByFamilySpec.cs`

- [ ] **Step 1: Create `RefreshTokenByHashSpec.cs`**
```csharp
using Ardalis.Specification;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Refresh;

internal sealed class RefreshTokenByHashSpec : Specification<RefreshToken>
{
    public RefreshTokenByHashSpec(string tokenHash)
        => Query.Where(t => t.TokenHash == tokenHash);
}
```

- [ ] **Step 2: Create `RefreshTokenByFamilySpec.cs`**
```csharp
using Ardalis.Specification;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth.Refresh;

internal sealed class RefreshTokenByFamilySpec : Specification<RefreshToken>
{
    public RefreshTokenByFamilySpec(Guid familyId)
        => Query.Where(t => t.FamilyId == familyId);
}
```

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): RefreshToken specifications (by hash, by family)"
```

---

## Task 5: Application — AuthResponse enrichi + AuthTokenIssuer + refacto Register/Login

**Files:**
- Modify: `src/Taxi.Application/Identity/Auth/AuthResponse.cs`
- Create: `src/Taxi.Application/Identity/Auth/AuthTokenIssuer.cs`
- Modify: `src/Taxi.Application/Identity/Auth/Register/RegisterCommandHandler.cs`
- Modify: `src/Taxi.Application/Identity/Auth/Login/LoginCommandHandler.cs`
- Modify: `src/Taxi.Application/DependencyInjection.cs`

> This task changes `AuthResponse`'s shape, which breaks the Register/Login handlers' construction — so all of it is done together so the project compiles.

- [ ] **Step 1: Extend `AuthResponse.cs`** — final content:
```csharp
namespace Taxi.Application.Identity.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string TokenType,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserInfo User);
```

- [ ] **Step 2: Create `AuthTokenIssuer.cs`** (centralises access+refresh issuance)
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Domain.Identity;

namespace Taxi.Application.Identity.Auth;

internal sealed class AuthTokenIssuer(
    ITokenService tokenService,
    IRepository<RefreshToken> refreshTokens)
{
    public async Task<(AuthResponse Response, RefreshToken Token)> IssueAsync(
        ApplicationUser user, IReadOnlyList<string> roles, Guid familyId, CancellationToken cancellationToken)
    {
        var access = tokenService.CreateAccessToken(user, roles);
        var refresh = tokenService.CreateRefreshToken();

        var entity = RefreshToken.Create(user.Id, refresh.TokenHash, refresh.ExpiresAt, familyId);
        await refreshTokens.AddAsync(entity, cancellationToken); // Ardalis AddAsync persists and sets Id

        var response = new AuthResponse(
            access.Token, access.ExpiresAt, "Bearer",
            refresh.RawToken, refresh.ExpiresAt,
            new UserInfo(user.Id, user.FullName, user.PhoneNumber!, roles));

        return (response, entity);
    }
}
```

- [ ] **Step 3: Refactor `RegisterCommandHandler.cs`** to use the issuer (inject `AuthTokenIssuer` instead of `ITokenService`):
```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Application.Identity.Auth;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Register;

internal sealed class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthTokenIssuer issuer)
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

        var (response, _) = await issuer.IssueAsync(user, roles.ToList(), Guid.NewGuid(), cancellationToken);
        return response;
    }
}
```

- [ ] **Step 4: Refactor `LoginCommandHandler.cs`** to use the issuer:
```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Application.Identity.Auth;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Login;

internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthTokenIssuer issuer)
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
            await userManager.AccessFailedAsync(user);
            return Result.Failure<AuthResponse>(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var roles = await userManager.GetRolesAsync(user);

        var (response, _) = await issuer.IssueAsync(user, roles.ToList(), Guid.NewGuid(), cancellationToken);
        return response;
    }
}
```

- [ ] **Step 5: Register `AuthTokenIssuer` in `DependencyInjection.cs`** — in `AddApplication`, add this line before `return services;`:
```csharp
        services.AddScoped<AuthTokenIssuer>();
```
Add `using Taxi.Application.Identity.Auth;` at the top if not present.

- [ ] **Step 6: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass (the existing TokenService test still green; no behavioral test for handlers — manual later).

- [ ] **Step 7: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): issue refresh tokens on register/login via AuthTokenIssuer"
```

---

## Task 6: Application — Refresh (command + validator + handler, TDD)

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenCommand.cs`
- Create: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenCommandValidator.cs`
- Create: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Identity/RefreshTokenCommandHandlerTests.cs`

- [ ] **Step 1: Create `RefreshTokenCommand.cs`**
```csharp
using Taxi.Application.Identity.Auth;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;
```

- [ ] **Step 2: Create `RefreshTokenCommandValidator.cs`**
```csharp
using FluentValidation;

namespace Taxi.Application.Identity.Auth.Refresh;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty();
    }
}
```

- [ ] **Step 3: Write the failing tests** — `tests/Taxi.Application.Tests/Identity/RefreshTokenCommandHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.Application.Abstractions;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Xunit;

namespace Taxi.Application.Tests.Identity;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IRepository<RefreshToken>> _repo = new();
    private readonly Mock<ITokenService> _tokens = new();

    private RefreshTokenCommandHandler CreateHandler()
    {
        _tokens.Setup(t => t.HashRefreshToken(It.IsAny<string>())).Returns("hashed");
        var userManager = IdentityMocks.UserManager();
        var issuer = new AuthTokenIssuer(_tokens.Object, _repo.Object);
        return new RefreshTokenCommandHandler(_repo.Object, userManager.Object, _tokens.Object, issuer);
    }

    [Fact]
    public async Task Should_fail_when_token_not_found()
    {
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((RefreshToken?)null);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.InvalidToken");
    }

    [Fact]
    public async Task Should_detect_reuse_and_revoke_family_when_token_already_revoked()
    {
        var revoked = RefreshToken.Create("u-1", "hashed", DateTime.UtcNow.AddDays(7), Guid.NewGuid());
        revoked.Revoke("Rotation");
        var familyMember = RefreshToken.Create("u-1", "other", DateTime.UtcNow.AddDays(7), revoked.FamilyId);

        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(revoked);
        _repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<RefreshToken> { revoked, familyMember });

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.TokenReuse");
        familyMember.IsRevoked.Should().BeTrue(); // family revoked
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Should_fail_when_token_expired()
    {
        var expired = RefreshToken.Create("u-1", "hashed", DateTime.UtcNow.AddSeconds(-1), Guid.NewGuid());
        _repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<RefreshToken>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(expired);

        var result = await CreateHandler().Handle(new RefreshTokenCommand("raw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.ExpiredToken");
    }
}
```

- [ ] **Step 4: Create the UserManager mock helper** — `tests/Taxi.Application.Tests/Identity/IdentityMocks.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Taxi.Domain.Identity;

namespace Taxi.Application.Tests.Identity;

internal static class IdentityMocks
{
    public static Mock<UserManager<ApplicationUser>> UserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
```

- [ ] **Step 5: Run — expect FAIL** (handler absent)
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: compilation failure.

- [ ] **Step 6: Create `RefreshTokenCommandHandler.cs`**
```csharp
using Microsoft.AspNetCore.Identity;
using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Refresh;

internal sealed class RefreshTokenCommandHandler(
    IRepository<RefreshToken> refreshTokens,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    AuthTokenIssuer issuer)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    private static readonly Error Invalid = Error.Unauthorized("Auth.InvalidToken", "Refresh token invalide.");

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(command.RefreshToken);
        var stored = await refreshTokens.FirstOrDefaultAsync(new RefreshTokenByHashSpec(hash), cancellationToken);

        if (stored is null)
            return Result.Failure<AuthResponse>(Invalid);

        if (stored.IsRevoked)
        {
            // Reuse of an already-rotated token → revoke the whole family.
            var family = await refreshTokens.ListAsync(new RefreshTokenByFamilySpec(stored.FamilyId), cancellationToken);
            foreach (var token in family)
                token.Revoke("TokenReuse");
            await refreshTokens.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.TokenReuse", "Réutilisation de token détectée."));
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.ExpiredToken", "Refresh token expiré."));

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null)
            return Result.Failure<AuthResponse>(Invalid);

        var roles = await userManager.GetRolesAsync(user);
        var (response, newToken) = await issuer.IssueAsync(user, roles.ToList(), stored.FamilyId, cancellationToken);

        stored.Revoke("Rotation", newToken.Id);
        await refreshTokens.UpdateAsync(stored, cancellationToken);

        return response;
    }
}
```

- [ ] **Step 7: Run — expect PASS**
Run: `cd /c/prjRecherche/Taxi && dotnet test tests/Taxi.Application.Tests`
Expected: all pass (18 in Application.Tests: 15 + 3 new refresh-handler tests).

- [ ] **Step 8: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): refresh token rotation with reuse detection (TDD)"
```

---

## Task 7: Application — Revoke (command + validator + handler)

**Files:**
- Create: `src/Taxi.Application/Identity/Auth/Revoke/RevokeTokenCommand.cs`
- Create: `src/Taxi.Application/Identity/Auth/Revoke/RevokeTokenCommandValidator.cs`
- Create: `src/Taxi.Application/Identity/Auth/Revoke/RevokeTokenCommandHandler.cs`

- [ ] **Step 1: Create `RevokeTokenCommand.cs`**
```csharp
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Revoke;

public sealed record RevokeTokenCommand(string RefreshToken) : ICommand<bool>;
```

- [ ] **Step 2: Create `RevokeTokenCommandValidator.cs`**
```csharp
using FluentValidation;

namespace Taxi.Application.Identity.Auth.Revoke;

internal sealed class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty();
    }
}
```

- [ ] **Step 3: Create `RevokeTokenCommandHandler.cs`** (idempotent — no info leak on unknown/already-revoked)
```csharp
using Taxi.Application.Abstractions;
using Taxi.Application.Identity.Abstractions;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.Domain.Identity;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Identity.Auth.Revoke;

internal sealed class RevokeTokenCommandHandler(
    IRepository<RefreshToken> refreshTokens,
    ITokenService tokenService)
    : ICommandHandler<RevokeTokenCommand, bool>
{
    public async Task<Result<bool>> Handle(RevokeTokenCommand command, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(command.RefreshToken);
        var stored = await refreshTokens.FirstOrDefaultAsync(new RefreshTokenByHashSpec(hash), cancellationToken);

        if (stored is not null && !stored.IsRevoked)
        {
            stored.Revoke("Logout");
            await refreshTokens.UpdateAsync(stored, cancellationToken);
        }

        return true;
    }
}
```

- [ ] **Step 4: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors. (`RefreshTokenByHashSpec` is reused from the Refresh folder — it's internal in the same assembly, accessible.)

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(application): revoke (logout) refresh token command"
```

---

## Task 8: Infrastructure — service de nettoyage

**Files:**
- Create: `src/Taxi.Infrastructure/Identity/RefreshTokenCleanupService.cs`
- Modify: `src/Taxi.Infrastructure/Identity/DependencyInjection.cs`

- [ ] **Step 1: Create `RefreshTokenCleanupService.cs`**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taxi.Infrastructure.Persistence;

namespace Taxi.Infrastructure.Identity;

internal sealed class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;
                var revokedThreshold = now.AddDays(-7);

                var deleted = await db.RefreshTokens
                    .Where(t => t.ExpiresAt < now || (t.IsRevoked && t.RevokedAt < revokedThreshold))
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    logger.LogInformation("Nettoyage refresh tokens : {Count} supprimé(s)", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec du nettoyage des refresh tokens");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
```

- [ ] **Step 2: Register it in `DependencyInjection.cs`** — in `AddIdentityInfrastructure`, add before `return services;`:
```csharp
        services.AddHostedService<RefreshTokenCleanupService>();
```

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): background service cleaning up expired/revoked refresh tokens"
```

---

## Task 9: Web.Api — endpoints refresh + revoke

**Files:**
- Create: `src/Taxi.Web.Api/Modules/Identity/RefreshEndpoint.cs`
- Create: `src/Taxi.Web.Api/Modules/Identity/RevokeEndpoint.cs`

- [ ] **Step 1: Create `RefreshEndpoint.cs`**
```csharp
using Taxi.Application.Identity.Auth;
using Taxi.Application.Identity.Auth.Refresh;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

public sealed class RefreshEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/refresh", async (
            RefreshTokenCommand command,
            ICommandHandler<RefreshTokenCommand, AuthResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithName("Refresh")
        .WithTags(Tags.Identity)
        .WithSummary("Renouveler les tokens")
        .WithDescription("Échange un refresh token valide contre un nouvel access token + refresh token (rotation).");
    }
}
```

- [ ] **Step 2: Create `RevokeEndpoint.cs`** (success → 204 No Content)
```csharp
using Taxi.Application.Identity.Auth.Revoke;
using Taxi.SharedKernel.Messaging;
using Taxi.Web.Api.Endpoints;

namespace Taxi.Web.Api.Modules.Identity;

public sealed class RevokeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/revoke", async (
            RevokeTokenCommand command,
            ICommandHandler<RevokeTokenCommand, bool> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("Revoke")
        .WithTags(Tags.Identity)
        .WithSummary("Révoquer un refresh token (logout)")
        .WithDescription("Révoque le refresh token fourni pour déconnecter l'appareil.");
    }
}
```

- [ ] **Step 3: Build + tests**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors; all tests pass.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(api): refresh and revoke endpoints"
```

---

## Task 10: Migration EF AddRefreshTokens

**Files:**
- Create (generated): `src/Taxi.Infrastructure/Persistence/Migrations/*_AddRefreshTokens.cs`

- [ ] **Step 1: Generate the migration**
Run:
```bash
cd /c/prjRecherche/Taxi && dotnet ef migrations add AddRefreshTokens --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: `Done.` New `*_AddRefreshTokens.cs`. Its `Up()` creates the `refresh_tokens` table (snake_case columns: `id`, `user_id`, `token_hash`, `expires_at`, `is_revoked`, `revoked_at`, `revoked_reason`, `family_id`, `replaced_by_token_id`, `created_at`) with a unique index on `token_hash` and an index on `family_id`. It must NOT recreate Identity/zone_prices tables (those are in the prior migration). Confirm by opening the file.

> The `dotnet-ef` tool was updated to v10 in Phase 1; `AppDbContextFactory` enables design-time generation without a running DB.

- [ ] **Step 2: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(infra): AddRefreshTokens EF migration (refresh_tokens table)"
```

---

## Task 11: Suite complète + vérification manuelle

- [ ] **Step 1: Run the whole suite**
Run: `cd /c/prjRecherche/Taxi && dotnet test Taxi.slnx`
Expected: all pass. (Application.Tests grew by RefreshToken entity (3) + TokenService refresh (2) + refresh handler (3) = +8 → 18; Architecture.Tests 3 → total 21.)

- [ ] **Step 2: Manual verification (USER — Docker running)**

> No volume wipe needed — `MigrateAsync` applies `AddRefreshTokens` on top of the existing schema at startup.

Start the AppHost: `dotnet run --project C:\prjRecherche\Taxi\Taxi.AppHost`. Then in Scalar:
  1. `POST /api/auth/login` `{ "phoneNumber":"77000002", "password":"123456" }` → **200**, note `accessToken` AND `refreshToken`.
  2. `POST /api/auth/refresh` `{ "refreshToken":"<le refreshToken>" }` → **200**, new `accessToken` + a NEW `refreshToken` (rotation).
  3. `POST /api/auth/refresh` with the **OLD** refreshToken (the one from step 1, now rotated) → **401** `Auth.TokenReuse` (reuse detected; the family is revoked — the step-2 token is now also dead).
  4. `POST /api/auth/login` again → fresh tokens. `POST /api/auth/revoke` (with `Authorization: Bearer <access>`) `{ "refreshToken":"<refresh>" }` → **204**. Then `/refresh` with that revoked token → **401**.

- [ ] **Step 3: Confirm results to the user.** No commit (verification).

---

## Definition of Done (Phase 2)

- [ ] `dotnet build Taxi.slnx` : 0 erreur.
- [ ] `dotnet test Taxi.slnx` : tous verts (≈21).
- [ ] Migration `AddRefreshTokens` présente.
- [ ] login/register renvoient un `refreshToken` ; `/refresh` effectue la rotation ; rejouer un token tourné → 401 + famille révoquée ; `/revoke` → 204 puis 401.
- [ ] Tout committé sur `main`.

## Phase suivante (hors périmètre)

Phase 3 : `DriverDocument` + upload Azure Blob. Frontend : `api.ts` (lire `refreshToken`, auto-refresh sur 401).
