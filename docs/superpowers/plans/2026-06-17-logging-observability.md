# Observabilité (logs source-generated) & commentaires — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tracer automatiquement chaque commande/requête via le pattern `[LoggerMessage]` source-generated, démontrer le pattern sur 2 endroits métier, et commenter (XML `///`, FR) le cœur architectural pour le binôme.

**Architecture:** Catalogue `RequestLog` source-generated appelé par les décorateurs (commande + nouveau décorateur requête, via `TryDecorate`). Sink = OpenTelemetry d'Aspire (déjà en place ; Serilog retiré). Démo `[LoggerMessage]` sur `RideDispatcher` + `OfferTimeoutService`. Commentaires sur une liste bornée de classes.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Logging` source generators, Aspire OTel, xUnit.

**Spec :** `docs/superpowers/specs/2026-06-17-logging-observability-design.md`
**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 84 tests verts au départ.

> **Ordre des décorateurs** (existant) : Validation enregistré avant Logging → **Logging est le plus externe** (il voit le résultat d'échec de validation). On conserve cet ordre pour les requêtes.

---

## Task 1: Catalogue RequestLog + décorateurs source-generated (commande + requête)

**Files:**
- Create: `src/Taxi.Application/Abstractions/Behaviors/RequestLog.cs`
- Modify: `src/Taxi.Application/Abstractions/Behaviors/LoggingDecorator.cs`
- Modify: `src/Taxi.Application/DependencyInjection.cs`
- Test: `tests/Taxi.Application.Tests/Abstractions/LoggingDecoratorTests.cs`

- [ ] **Step 1: Create `RequestLog.cs`**
```csharp
using Microsoft.Extensions.Logging;

namespace Taxi.Application.Abstractions.Behaviors;

/// <summary>
/// Catalogue centralisé des messages de log du cycle de vie des requêtes (commandes et requêtes CQRS).
/// Utilise les générateurs de source de Microsoft.Extensions.Logging (<c>[LoggerMessage]</c>) :
/// les méthodes sont générées à la compilation, sans allocation quand le niveau est désactivé,
/// et les paramètres ({RequestName}, {ErrorCode}) deviennent des propriétés structurées exploitables
/// dans le dashboard Aspire / OpenTelemetry.
/// </summary>
internal static partial class RequestLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Traitement de {RequestName}")]
    public static partial void LogStarted(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} traitée avec succès")]
    public static partial void LogSucceeded(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} en échec : {ErrorCode}")]
    public static partial void LogFailed(ILogger logger, string requestName, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "{RequestName} a levé une exception")]
    public static partial void LogException(ILogger logger, Exception exception, string requestName);
}
```

- [ ] **Step 2: Rewrite `LoggingDecorator.cs`** (command upgraded + new query decorator) — remplacer tout le contenu par :
```csharp
using Microsoft.Extensions.Logging;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;

namespace Taxi.Application.Abstractions.Behaviors;

/// <summary>
/// Décorateurs transverses qui tracent automatiquement le cycle de vie de CHAQUE commande et requête
/// (début, succès, échec métier, exception). Branchés via Scrutor (<c>TryDecorate</c>) : aucun handler
/// n'a besoin d'écrire ces logs lui-même. S'appuie sur <see cref="RequestLog"/> (source-generated).
/// </summary>
public static class LoggingDecorator
{
    /// <summary>Enveloppe un <see cref="ICommandHandler{TCommand,TResponse}"/> pour le tracer.</summary>
    public sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var name = typeof(TCommand).Name;
            RequestLog.LogStarted(logger, name);
            try
            {
                var result = await inner.Handle(command, cancellationToken);
                if (result.IsSuccess)
                    RequestLog.LogSucceeded(logger, name);
                else
                    RequestLog.LogFailed(logger, name, result.Error.Code);
                return result;
            }
            catch (Exception ex)
            {
                RequestLog.LogException(logger, ex, name);
                throw;
            }
        }
    }

    /// <summary>Enveloppe un <see cref="IQueryHandler{TQuery,TResponse}"/> pour le tracer (symétrique des commandes).</summary>
    public sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var name = typeof(TQuery).Name;
            RequestLog.LogStarted(logger, name);
            try
            {
                var result = await inner.Handle(query, cancellationToken);
                if (result.IsSuccess)
                    RequestLog.LogSucceeded(logger, name);
                else
                    RequestLog.LogFailed(logger, name, result.Error.Code);
                return result;
            }
            catch (Exception ex)
            {
                RequestLog.LogException(logger, ex, name);
                throw;
            }
        }
    }
}
```

- [ ] **Step 3: Register the query logging decorator** — dans `src/Taxi.Application/DependencyInjection.cs`, juste après la ligne `services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));` ajouter :
```csharp
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
```
(Placé après le décorateur de validation des requêtes → Logging reste le plus externe pour les requêtes aussi.)

- [ ] **Step 4: Write the decorator pass-through test** — `tests/Taxi.Application.Tests/Abstractions/LoggingDecoratorTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taxi.Application.Abstractions.Behaviors;
using Taxi.SharedKernel;
using Taxi.SharedKernel.Messaging;
using Xunit;

namespace Taxi.Application.Tests.Abstractions;

public class LoggingDecoratorTests
{
    private sealed record DummyQuery : IQuery<int>;

    private sealed class OkHandler : IQueryHandler<DummyQuery, int>
    {
        public Task<Result<int>> Handle(DummyQuery query, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(42));
    }

    private sealed class ThrowHandler : IQueryHandler<DummyQuery, int>
    {
        public Task<Result<int>> Handle(DummyQuery query, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task QueryDecorator_passes_through_success()
    {
        var decorator = new LoggingDecorator.QueryHandler<DummyQuery, int>(
            new OkHandler(), NullLogger<LoggingDecorator.QueryHandler<DummyQuery, int>>.Instance);

        var result = await decorator.Handle(new DummyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task QueryDecorator_rethrows_on_exception()
    {
        var decorator = new LoggingDecorator.QueryHandler<DummyQuery, int>(
            new ThrowHandler(), NullLogger<LoggingDecorator.QueryHandler<DummyQuery, int>>.Instance);

        var act = () => decorator.Handle(new DummyQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

- [ ] **Step 5: Build + test**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: build 0 errors (la source-gen `[LoggerMessage]` compile) ; tests verts (2 nouveaux).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(logging): source-generated request log catalog + command/query logging decorators"
```

---

## Task 2: Démo du pattern [LoggerMessage] (RideDispatcher + OfferTimeoutService)

**Files:**
- Modify: `src/Taxi.Application/Dispatch/RideDispatcher.cs`
- Modify: `src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs`

- [ ] **Step 1: Add logging to `RideDispatcher.cs`** — rendre la classe `partial`, injecter `ILogger`, ajouter les méthodes `[LoggerMessage]` et les appels. Remplacer le fichier par :
```csharp
using Microsoft.Extensions.Logging;
using Taxi.Application.Abstractions;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;

namespace Taxi.Application.Dispatch;

/// <summary>
/// Orchestre l'attribution automatique d'une course : trouve le chauffeur disponible le plus proche
/// (via <see cref="IDriverLocator"/>) qui n'a pas déjà été sollicité, lui fait une offre temporisée,
/// et le notifie en temps réel. Sans candidat ou sans coordonnées, la course retombe dans le flux manuel.
/// </summary>
internal sealed partial class RideDispatcher(
    IDriverLocator locator,
    IRepository<Ride> rides,
    IRealtimeNotifier notifier,
    ILogger<RideDispatcher> logger)
    : IRideDispatcher
{
    private static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(30);
    private const double RadiusMeters = 5000;
    private const int MaxCandidates = 20;

    /// <summary>Tente d'offrir la course <paramref name="rideId"/> au prochain chauffeur le plus proche.</summary>
    public async Task DispatchAsync(int rideId, CancellationToken cancellationToken)
    {
        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(rideId), cancellationToken);
        if (ride is null || ride.Status != RideStatus.Pending)
            return;

        if (ride.PickupLatitude is null || ride.PickupLongitude is null)
        {
            LogNoCoordinates(logger, ride.Id);
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var candidates = await locator.FindNearestAsync(
            ride.PickupLatitude.Value, ride.PickupLongitude.Value, RadiusMeters, MaxCandidates, cancellationToken);

        var next = candidates.FirstOrDefault(c => !ride.TriedDriverIds.Contains(c.DriverId));

        if (next is null)
        {
            LogNoCandidate(logger, ride.Id);
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var expiresAt = DateTime.UtcNow + OfferTtl;
        ride.Offer(next.DriverId, expiresAt);
        await rides.UpdateAsync(ride, cancellationToken);
        await notifier.RideOfferedAsync(next.UserId, ride.Id, expiresAt, cancellationToken);
        LogOfferMade(logger, ride.Id, next.DriverId, expiresAt);
    }

    // --- Logs métier (pattern [LoggerMessage] : à reproduire dans les autres handlers au besoin) ---

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} offerte au chauffeur {DriverId} (expire à {ExpiresAt:o})")]
    private static partial void LogOfferMade(ILogger logger, int rideId, int driverId, DateTime expiresAt);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Aucun chauffeur disponible pour la course {RideId} → retour en attente (flux manuel)")]
    private static partial void LogNoCandidate(ILogger logger, int rideId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} sans coordonnées de prise en charge → flux manuel")]
    private static partial void LogNoCoordinates(ILogger logger, int rideId);
}
```

- [ ] **Step 2: Add logging to `OfferTimeoutService.cs`** — rendre `partial`, ajouter un compteur + un log si > 0. Modifier la boucle `foreach` pour compter, et logguer après. Remplacer le corps du `try { using var scope ... }` (la partie qui liste + boucle) par :
```csharp
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

                if (expired.Count > 0)
                    LogOffersExpired(logger, expired.Count);
```
Et changer la déclaration de classe en `internal sealed partial class OfferTimeoutService(...)`, puis ajouter à la fin de la classe :
```csharp
    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} offre(s) expirée(s) réattribuée(s)")]
    private static partial void LogOffersExpired(ILogger logger, int count);
```
(`ListAsync` renvoie un `IReadOnlyList` ou `List` → `.Count` est disponible. Si c'est un `IEnumerable`, matérialiser avec `.ToList()` d'abord — vérifier le type de retour de `ListAsync`.)

- [ ] **Step 3: Fix `RideDispatcherTests` constructor FIRST** — `RideDispatcher` reçoit un nouveau paramètre `ILogger` ; le test le construit à la main et ne compilerait plus. Dans `tests/Taxi.Application.Tests/Dispatch/RideDispatcherTests.cs`, ajouter `using Microsoft.Extensions.Logging.Abstractions;` et changer le helper :
```csharp
    private RideDispatcher Dispatcher() => new(
        _locator.Object, _rides.Object, _notifier.Object,
        NullLogger<RideDispatcher>.Instance);
```
(En DI réel, `ILogger<RideDispatcher>` est résolu automatiquement — seul le test construit à la main.)

- [ ] **Step 4: Build + test**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: tous verts (RideDispatcherTests passent avec le logger nul).

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(logging): demonstrate [LoggerMessage] business logs in RideDispatcher + OfferTimeoutService"
```

---

## Task 3: Retrait de Serilog + niveaux de log

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Taxi.Web.Api/appsettings.json`

- [ ] **Step 1: Remove the unused Serilog package** — dans `Directory.Packages.props`, supprimer la ligne :
```xml
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
```
(Aucun `.csproj` ne le référence — vérifier par `grep -ri serilog src/` qui ne doit rien renvoyer dans le code.)

- [ ] **Step 2: Tidy log levels** — lire `src/Taxi.Web.Api/appsettings.json`. S'assurer que la section `Logging:LogLevel` contient (créer/fusionner sans casser les autres clés comme `ConnectionStrings`) :
```json
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
```
(Réduit le bruit framework/SQL tout en gardant nos logs applicatifs en Information.)

- [ ] **Step 3: Build**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: 0 errors (le retrait du PackageVersion ne casse rien car non référencé).

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "chore(logging): drop unused Serilog package, tidy log levels (rely on Aspire OTel)"
```

---

## Task 4: Commentaires XML (section 8 du spec) — pour le binôme

**Objectif :** sur chaque fichier listé, ajouter un `/// <summary>` (FR, 1-3 phrases) expliquant **l'intention** de la classe/interface, et `/// <summary>`/`/// <param>`/`/// <returns>` sur les **méthodes publiques non triviales**. Ne PAS commenter les getters/ctors triviaux. Les commentaires expliquent le « pourquoi », pas la syntaxe.

> Le `RideDispatcher`, `LoggingDecorator` et `RequestLog` sont déjà commentés (Tasks 1-2). Cette tâche couvre le reste.

- [ ] **Step 1: SharedKernel** — ajouter les summaries :
  - `src/Taxi.SharedKernel/Result.cs` — sur `Result` : « Représente l'issue d'une opération (succès/échec) sans lever d'exception pour les erreurs *attendues* (métier). Porte une <see cref="Error"/> en cas d'échec. » ; sur `Result<T>` : « Résultat portant une valeur en cas de succès. Conversion implicite depuis <c>T</c>. »
  - `src/Taxi.SharedKernel/Error.cs` — sur `Error` : « Erreur métier typée (code + description + <see cref="ErrorType"/>). Convertie en code HTTP par la couche Web. » ; sur `ErrorType` : « Catégorie d'erreur, mappée vers un statut HTTP (Validation→400, NotFound→404, … ). »
  - `src/Taxi.SharedKernel/Entity.cs` — « Classe de base des entités persistées : identité technique (<c>Id</c>) et date de création. »
  - `src/Taxi.SharedKernel/Messaging/ICommand.cs` — « Marqueur d'une commande CQRS (écriture) renvoyant <c>TResponse</c>. »
  - `IQuery.cs` — « Marqueur d'une requête CQRS (lecture seule) renvoyant <c>TResponse</c>. »
  - `ICommandHandler.cs` — « Gère une commande et renvoie un <see cref="Result{TResponse}"/>. »
  - `IQueryHandler.cs` — « Gère une requête et renvoie un <see cref="Result{TResponse}"/>. »

- [ ] **Step 2: Application** — ajouter les summaries :
  - `Abstractions/IRepository.cs` — « Dépôt générique (pattern Repository + Specification d'Ardalis) pour une entité du domaine. Abstrait l'accès aux données : la couche Application ne connaît pas EF. »
  - `Abstractions/Behaviors/ValidationDecorator.cs` — sur la classe : « Décorateurs qui valident la commande/requête (FluentValidation) AVANT le handler ; renvoient une erreur de validation sans exécuter le handler si invalide. »
  - `Dispatch/IDriverLocator.cs` — sur l'interface : « Recherche géospatiale (PostGIS) des chauffeurs disponibles les plus proches d'un point. Implémentée en Infrastructure (accès EF). » ; sur `NearbyDriver` : « Chauffeur proche + distance en mètres, renvoyé par la recherche. »
  - `Realtime/IRealtimeNotifier.cs` — « Notifications temps réel (SignalR) émises par la couche Application sans en connaître l'implémentation : statut de course, nouvelle course en attente, offre ciblée à un chauffeur. »
  - `Rides/Request/RequestRideCommandHandler.cs` — « Crée une course (prix estimé via la tarification) puis déclenche l'auto-dispatch vers le chauffeur le plus proche. »

- [ ] **Step 3: Web.Api** — ajouter les summaries :
  - `Endpoints/IEndpoint.cs` — « Contrat d'un endpoint Minimal API auto-découvert : chaque endpoint déclare ses routes dans <c>MapEndpoint</c>. »
  - `Endpoints/ResultExtensions.cs` — sur `ToHttpResult` : « Convertit un <see cref="Result{T}"/> en réponse HTTP : la valeur en 200, ou le bon code d'erreur (400/401/403/404/409/500) selon l'<see cref="ErrorType"/>. »
  - `Realtime/RideHub.cs` — sur la classe : « Hub SignalR du suivi de course : les clients/chauffeurs rejoignent des groupes (course, chauffeur, admin) et reçoivent position et statuts en temps réel. Le chauffeur diffuse sa position via <c>SendDriverLocation</c>. » ; un `/// <summary>` court sur chaque méthode `Join*` et `SendDriverLocation`.
  - `Middleware/GlobalExceptionHandler.cs` — « Filet de sécurité : transforme toute exception *non gérée* en réponse <c>ProblemDetails</c> 500 propre (détail masqué hors développement). Les erreurs métier, elles, passent par le pattern Result. »
  - `Middleware/SecurityHeadersMiddleware.cs` — « Ajoute les en-têtes de sécurité OWASP aux réponses (hors Scalar/OpenAPI/health). »

- [ ] **Step 4: Build (les commentaires ne changent pas le comportement)**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx`
Expected: 0 errors.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs(code): XML summaries on core architecture classes for onboarding"
```

---

## Task 5: Build complet + vérification manuelle

- [ ] **Step 1: Build + full suite**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors ; tous les tests verts (84 + 2 ≈ 86).

- [ ] **Step 2: Manual verification (USER — dashboard Aspire)**
Démarrer l'AppHost. Ouvrir le **dashboard Aspire** (onglet Logs / Structured logs du projet `api`).
  1. Appeler une requête simple (ex. `POST /api/auth/login`, ou `GET /api/dispatch/nearest-drivers` en Admin) → voir
     `Traitement de <Nom>Query/Command` puis `<Nom> traitée avec succès`, avec `RequestName` en propriété structurée.
  2. Provoquer un **échec métier** (ex. login mauvais mot de passe, ou demander une course depuis une zone inconnue)
     → voir un log **Warning** `... en échec : <ErrorCode>`.
  3. Demander une course **avec coordonnées** (chauffeur dispo proche) → voir le log métier
     `Course {RideId} offerte au chauffeur {DriverId} ...` du `RideDispatcher`.
  4. (Optionnel) Laisser une offre expirer → log `N offre(s) expirée(s) réattribuée(s)`.

- [ ] **Step 3: Confirmer à l'utilisateur.** Aucun commit (vérification).

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts (~86).
- [ ] Toute commande ET requête est tracée (début/succès/échec/exception) via `[LoggerMessage]` source-generated, visible dans le dashboard Aspire.
- [ ] `RideDispatcher` + `OfferTimeoutService` montrent le pattern `[LoggerMessage]` métier (référence binôme).
- [ ] Serilog retiré, niveaux de log nettoyés ; sink = Aspire OTel.
- [ ] Les classes architecturales listées (section 8) portent des `/// <summary>` FR expliquant leur intention.
- [ ] Tout committé sur `main`.

## Suite

Reprise des modules fonctionnels après la pause : Identité Phase 3 (documents/Blob), stubs Paiement/Notifications, puis frontend.
