# Extension : logs métier ciblés + couverture XML complète — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter des logs `[LoggerMessage]` métier là où il y a une vraie décision/sécurité (sans dupliquer le décorateur), documenter une convention, et donner une couverture de commentaires XML complète (tous les types publics + interfaces, format multi-ligne) — un code « livré par un senior ».

**Architecture:** Le `LoggingDecorator` trace déjà le cycle de vie de chaque commande/requête → les logs ajoutés ici sont **uniquement** des événements métier (sécurité, décisions, effets). Les commentaires XML couvrent tout le code public, en format multi-ligne FR, expliquant l'intention.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Logging` source generators, xUnit.

**Répertoire :** `C:\prjRecherche\Taxi` (branche `main`). 86 tests verts au départ.

> **Référence faite :** plan logging `2026-06-17-logging-observability.md` (RequestLog + décorateurs + démo RideDispatcher/OfferTimeoutService déjà livrés). Ne PAS recommenter ce qui l'est déjà.

---

## Convention de commentaires & logs (à respecter dans toutes les tâches)

**Format XML (multi-ligne, FR, explique l'INTENTION pas la syntaxe) :**
```csharp
/// <summary>
/// Phrase expliquant à quoi sert le type et pourquoi il existe.
/// </summary>
```
- Sur **tout** type public/interne public-visible : `class`, `interface`, `record`, `enum`, `struct`.
- Sur les **méthodes publiques non triviales** : `/// <summary>`, + `/// <param name="x">…</param>` et `/// <returns>…</returns>` quand utile.
- **Ne PAS** commenter : getters/setters auto, constructeurs triviaux, membres d'enum évidents, code généré (migrations), fichiers déjà commentés.
- Toujours **multi-ligne** (jamais `/// <summary>…</summary>` sur une seule ligne).

**Logs `[LoggerMessage]` métier :** uniquement pour un **événement métier / décision / sécurité / effet** que le décorateur ne voit pas. Jamais de « started/succeeded » (déjà fait par le décorateur). Classe `partial`, méthode `private static partial void Log...(ILogger logger, …)`.

---

## Task 1: Convention écrite (doc onboarding)

**Files:**
- Create: `docs/conventions-logging-et-commentaires.md`

- [ ] **Step 1: Create the convention doc**
```markdown
# Conventions — Logs & commentaires

## Logs
- **Ne jamais logguer le cycle de vie d'un handler** (début/succès/échec) : le `LoggingDecorator`
  (commande ET requête) le fait automatiquement pour tous les handlers, via `RequestLog` (source-generated).
- **Logguer un événement MÉTIER** uniquement quand il apporte une info que le décorateur ignore :
  décision, branche importante, sécurité, effet de bord. Exemples : réutilisation de token détectée,
  chauffeur rendu indisponible, offre faite à un chauffeur, moyenne recalculée.
- **Pattern** (performant, source-generated) :
  ```csharp
  internal sealed partial class MonHandler(..., ILogger<MonHandler> logger) : ICommandHandler<...>
  {
      // ... après l'action métier ...
      LogQuelqueChose(logger, id);

      [LoggerMessage(Level = LogLevel.Information, Message = "Quelque chose est arrivé pour {Id}")]
      private static partial void LogQuelqueChose(ILogger logger, int id);
  }
  ```
- **Niveaux** : `Information` = événement normal notable ; `Warning` = anormal récupérable (échec attendu,
  sécurité) ; `Error` = exception/incident. Mettre un `if (logger.IsEnabled(LogLevel.X))` UNIQUEMENT
  quand le message implique une construction coûteuse (`string.Join`, concat).
- **Sink** : OpenTelemetry d'Aspire (dashboard Aspire). Pas de Serilog.

## Commentaires XML
- Tout type public + interface porte un `/// <summary>` **multi-ligne** en français expliquant son INTENTION
  (à quoi il sert, pourquoi il existe), pas sa syntaxe.
- Méthodes publiques non triviales : `<summary>` + `<param>`/`<returns>` si utile.
- Ne pas commenter le trivial (getters auto, ctors triviaux) ni le code généré (migrations).
```

- [ ] **Step 2: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs: logging & commenting conventions for the team"
```

---

## Task 2: Logs métier — Identité (sécurité + inscription)

**Files:**
- Modify: `src/Taxi.Application/Identity/Auth/Refresh/RefreshTokenCommandHandler.cs`
- Modify: `src/Taxi.Application/Identity/Auth/Register/RegisterCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Identity/RefreshTokenCommandHandlerTests.cs`

- [ ] **Step 1: `RefreshTokenCommandHandler` — log de sécurité.** Rendre la classe `partial`, ajouter `ILogger<RefreshTokenCommandHandler> logger` comme **dernier** paramètre du constructeur, ajouter `using Microsoft.Extensions.Logging;`. Dans la branche `if (stored.IsRevoked)`, juste après le `foreach (...) token.Revoke("TokenReuse");` et avant `SaveChangesAsync`, ajouter l'appel ; et ajouter la méthode `[LoggerMessage]` :
```csharp
        // dans la branche reuse, après la boucle de révocation :
        LogTokenReuseDetected(logger, stored.FamilyId);
```
```csharp
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Réutilisation de refresh token détectée → révocation de toute la famille {FamilyId}")]
    private static partial void LogTokenReuseDetected(ILogger logger, Guid familyId);
```
(`FamilyId` est un `Guid` — vérifier le type dans `RefreshToken` ; si c'est un autre type, adapter le paramètre.)

- [ ] **Step 2: `RegisterCommandHandler` — log d'inscription.** Rendre `partial`, ajouter `ILogger<RegisterCommandHandler> logger` en dernier paramètre, `using Microsoft.Extensions.Logging;`. Après `await userManager.AddToRoleAsync(user, command.Role);`, ajouter :
```csharp
        LogUserRegistered(logger, user.Id, command.Role);
```
et la méthode :
```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Utilisateur {UserId} inscrit avec le rôle {Role}")]
    private static partial void LogUserRegistered(ILogger logger, string userId, string role);
```

- [ ] **Step 3: Fix `RefreshTokenCommandHandlerTests`.** Lire le fichier : il construit `new RefreshTokenCommandHandler(...)`. Ajouter `using Microsoft.Extensions.Logging.Abstractions;` et passer `NullLogger<RefreshTokenCommandHandler>.Instance` comme dernier argument à chaque construction du handler. (Register n'a pas de test dédié → rien à corriger de ce côté.)

- [ ] **Step 4: Build + test**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test tests/Taxi.Application.Tests`
Expected: build 0 errors, tous verts.

- [ ] **Step 5: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(logging): business logs for token-reuse detection and user registration"
```

---

## Task 3: Logs métier — Courses & Dispatch

**Files:**
- Modify: `src/Taxi.Application/Rides/Accept/AcceptRideCommandHandler.cs`
- Modify: `src/Taxi.Application/Rides/Rate/RateRideCommandHandler.cs`
- Modify: `src/Taxi.Application/Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs`
- Test: `tests/Taxi.Application.Tests/Rides/AcceptRideHandlerTests.cs`, `RateRideHandlerTests.cs`, `tests/Taxi.Application.Tests/Dispatch/OfferHandlersTests.cs`

> Pour chaque handler : `using Microsoft.Extensions.Logging;`, classe `partial`, `ILogger<X> logger` en **dernier** paramètre du constructeur, appel après l'action métier, méthode `[LoggerMessage]`.

- [ ] **Step 1: `AcceptRideCommandHandler`.** Après `driver.SetAvailability(false); await drivers.UpdateAsync(driver, cancellationToken);` (avant la notif), ajouter `LogRideAccepted(logger, ride.Id, driver.Id);` et la méthode :
```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} acceptée par le chauffeur {DriverId} (rendu indisponible)")]
    private static partial void LogRideAccepted(ILogger logger, int rideId, int driverId);
```

- [ ] **Step 2: `RateRideCommandHandler`.** Après le recalcul de la moyenne et `drivers.UpdateAsync(driver, …)` (avant `return RatingDto.From(rating);`), ajouter `LogRideRated(logger, ride.Id, driverId, average);` et :
```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} notée ; moyenne du chauffeur {DriverId} recalculée à {Average:0.0}")]
    private static partial void LogRideRated(ILogger logger, int rideId, int driverId, double average);
```
(`driverId` et `average` sont des variables locales déjà présentes dans le handler — vérifier leurs noms exacts et adapter l'appel.)

- [ ] **Step 3: `AcceptOfferCommandHandler`.** Après `driver.SetAvailability(false); await drivers.UpdateAsync(...)` (avant la notif/return), ajouter `LogOfferAccepted(logger, ride.Id, driver.Id);` et :
```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Offre de la course {RideId} acceptée par le chauffeur {DriverId}")]
    private static partial void LogOfferAccepted(ILogger logger, int rideId, int driverId);
```

- [ ] **Step 4: Fix the 3 tests.** Dans chacun, ajouter `using Microsoft.Extensions.Logging.Abstractions;` et passer `NullLogger<TheHandler>.Instance` comme dernier argument :
  - `AcceptRideHandlerTests` : `Handler()` → `new(_rides.Object, _drivers.Object, _notifier.Object, NullLogger<AcceptRideCommandHandler>.Instance)`.
  - `RateRideHandlerTests` : lire la construction de `RateRideCommandHandler` et ajouter `NullLogger<RateRideCommandHandler>.Instance` en dernier argument.
  - `OfferHandlersTests` : la construction de `AcceptOfferCommandHandler` → ajouter `NullLogger<AcceptOfferCommandHandler>.Instance` en dernier (la construction de `DeclineOfferCommandHandler` est inchangée — on ne l'a pas modifié).

- [ ] **Step 5: Build + test**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors, tous verts.

- [ ] **Step 6: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "feat(logging): business logs for ride accept / rating / offer acceptance"
```

---

## Task 4: Couverture XML — Domain

**Files:** tous les `.cs` sous `src/Taxi.Domain/` (hors `obj`/`bin`), SAUF ceux déjà commentés (`Drivers/Driver.cs` est partiellement à compléter ; `Rides/Ride.cs` ; etc. — commenter ce qui ne l'est pas encore).

- [ ] **Step 1: Comment all public types in Domain** — pour CHAQUE type public (`class`/`record`/`enum`) de `Taxi.Domain` sans `/// <summary>`, ajouter un summary multi-ligne FR (cf. convention). Couvrir notamment :
  - Entités : `Driver`, `Ride`, `Rating`, `Report`, `ZonePrice`, `ApplicationUser`, `RefreshToken` — décrire le rôle métier de l'entité.
  - Enums : `RideStatus` (« étapes du cycle de vie d'une course »), erreurs statiques `RideErrors`/`RatingErrors` (« catalogue des erreurs métier de … »), `RoleNames`.
  - Méthodes de transition publiques des agrégats (`Ride.Accept/Offer/AcceptOffer/Complete/…`, `Driver.UpdateLocation/SetAvailability/…`) : un `/// <summary>` court décrivant la règle métier appliquée.
  Exemple attendu :
```csharp
/// <summary>
/// Course demandée par un client : porte les adresses/zones, le prix estimé, le chauffeur assigné
/// et l'état courant du cycle de vie (<see cref="RideStatus"/>). Agrégat riche : toutes les
/// transitions passent par ses méthodes qui renvoient un <c>Result</c>.
/// </summary>
public sealed class Ride : Entity
```

- [ ] **Step 2: Build** : `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Domain` → 0 errors.

- [ ] **Step 3: Commit** : `cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs(domain): XML summaries on all public domain types"`

---

## Task 5: Couverture XML — Application (Identity, Pricing, Drivers)

**Files:** `src/Taxi.Application/Identity/**`, `src/Taxi.Application/Pricing/**`, `src/Taxi.Application/Drivers/**` (hors fichiers déjà commentés).

- [ ] **Step 1: Comment all public types** — commands/queries (records), handlers, validators, DTOs, services (`AuthTokenIssuer`, `ITokenService`, specs). Pour chaque : summary multi-ligne FR décrivant l'intention. Exemples :
  - Sur une commande : `/// <summary>\n/// Commande d'inscription d'un nouvel utilisateur (téléphone + mot de passe + rôle).\n/// </summary>`
  - Sur un handler : `/// <summary>\n/// Gère <see cref="RegisterCommand"/> : crée l'utilisateur, lui attribue le rôle, émet les jetons.\n/// </summary>`
  - Sur un validator : `/// <summary>\n/// Règles de validation de <see cref="RegisterCommand"/> (téléphone requis, mot de passe min., etc.).\n/// </summary>`
  - Sur une spec : `/// <summary>\n/// Spécification : sélectionne … .\n/// </summary>`

- [ ] **Step 2: Build** : `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application` → 0 errors.

- [ ] **Step 3: Commit** : `cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs(application): XML summaries — Identity, Pricing, Drivers"`

---

## Task 6: Couverture XML — Application (Rides, Dispatch, Realtime, Administration, Abstractions)

**Files:** `src/Taxi.Application/Rides/**`, `Dispatch/**`, `Realtime/**`, `Administration/**`, `Abstractions/**` (hors déjà commentés : `RideDispatcher`, `LoggingDecorator`, `RequestLog`, `ValidationDecorator`, `IDriverLocator`, `IRealtimeNotifier`, `IRepository`, `RequestRideCommandHandler`).

- [ ] **Step 1: Comment all remaining public types** — commands/queries, handlers, DTOs (`RideDto`, `RatingDto`, `ReportDto`, `AdminStatsDto`, `UserSummary`, `NearbyDriver`, `DriverLocationBroadcast`), specs (`RideSpecs`/`RatingSpecs` classes), `IUserDirectory`, `IRideDispatcher`, erreurs (`RealtimeErrors`). Format multi-ligne FR, intention. Suivre les exemples de la Task 5.

- [ ] **Step 2: Build** : `cd /c/prjRecherche/Taxi && dotnet build src/Taxi.Application` → 0 errors.

- [ ] **Step 3: Commit** : `cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs(application): XML summaries — Rides, Dispatch, Realtime, Administration"`

---

## Task 7: Couverture XML — Infrastructure & Web.Api

**Files:** `src/Taxi.Infrastructure/**` (hors `Persistence/Migrations/**` = code généré, et hors `OfferTimeoutService`/`DriverLocator` déjà partiellement), et `src/Taxi.Web.Api/**` (hors déjà commentés : endpoints `IEndpoint`/`ResultExtensions`, `RideHub`, middlewares).

- [ ] **Step 1: Infrastructure** — commenter : `AppDbContext` (« contexte EF : Identity + tables métier, snake_case, extension PostGIS »), `AppDbContextFactory`, chaque `*Configuration` (« configuration EF de l'entité X : tables/colonnes/index »), `Repository<T>`, services Identity (`TokenService`, `JwtSettings`, `IdentitySeeder`, `RefreshTokenCleanupService`, `UserDirectory`), `DependencyInjection` (les deux : « enregistre les services d'infrastructure / d'identité »). **NE PAS** toucher aux migrations.

- [ ] **Step 2: Web.Api** — commenter les classes d'endpoints restantes (`*Endpoints`/`*Endpoint` : « endpoints REST du module X »), `EndpointExtensions`, `Tags`, `ClaimsPrincipalExtensions`, `BearerSecuritySchemeTransformer`, DTOs (`DriverLocationDto`), `SignalRRealtimeNotifier`. Sur les classes d'endpoints, un summary de classe suffit (pas besoin de commenter chaque lambda de route).

- [ ] **Step 3: Build + full test**
Run: `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx`
Expected: build 0 errors, tous verts.

- [ ] **Step 4: Commit**
```bash
cd /c/prjRecherche/Taxi && git add -A && git commit -m "docs(infra,api): XML summaries on infrastructure and web API types"
```

---

## Task 8: Vérification finale

- [ ] **Step 1: Build + full suite** : `cd /c/prjRecherche/Taxi && dotnet build Taxi.slnx && dotnet test Taxi.slnx` → 0 errors, tous verts.
- [ ] **Step 2: Format check** — `grep -rnE '///\s*<summary>.+</summary>' src --include=*.cs | grep -vE '/obj/|/bin/'` doit être **vide** (aucun summary mono-ligne — tout est multi-ligne).
- [ ] **Step 3: Vérif manuelle (USER, dashboard Aspire)** — déclencher : une **réutilisation de refresh token** (rejouer un vieux token) → log Warning `Réutilisation de refresh token détectée …` ; une **inscription** → `Utilisateur … inscrit avec le rôle …` ; une **acceptation de course** → `Course … acceptée par le chauffeur …`. Confirmer aussi qu'un handler trivial (ex. liste admin) ne produit QUE les logs du décorateur (pas de doublon).
- [ ] **Step 4: Confirmer à l'utilisateur.** Aucun commit.

---

## Definition of Done

- [ ] `dotnet build Taxi.slnx` : 0 erreur ; `dotnet test Taxi.slnx` : tous verts.
- [ ] Logs métier `[LoggerMessage]` présents sur les handlers à vraie logique (token reuse, register, accept ride/offer, rate) — sans doublonner le décorateur.
- [ ] Convention écrite disponible pour le binôme.
- [ ] Tous les types publics + interfaces de tous les modules portent un `/// <summary>` multi-ligne FR (hors code généré).
- [ ] Aucun summary mono-ligne ne subsiste.
- [ ] Tout committé sur `main`.
