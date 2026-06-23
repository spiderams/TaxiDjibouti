# Wave Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer le dispatch séquentiel (1 chauffeur à la fois, TTL 30 s) par des vagues parallèles de `min(3, candidats)` chauffeurs en « premier-arrivé-gagne », avec verrou optimiste `xmin` et révocation d'offre temps réel.

**Architecture:** Le `Ride` (Domain) porte désormais une vague de chauffeurs (`OfferedDriverIds`) au lieu d'un seul. Le `RideDispatcher` (Application) offre à toute la vague d'un coup. L'unicité du gagnant est garantie par l'optimistic concurrency PostgreSQL (`xmin`) configurée sur le `Ride` : deux `UPDATE ... WHERE xmin=N` concurrents, un seul réussit, l'autre lève `DbUpdateConcurrencyException` → mappé en HTTP 409. Les perdants de la vague reçoivent l'événement SignalR `rideOfferRevoked`.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql 10 (NetTopologySuite), Ardalis.Specification 9, SignalR, CQRS maison (sans MediatR), xUnit 2.9 + Moq 4.20 + FluentAssertions 6.12 + NetArchTest 1.3.

## Global Constraints

- **Pas de MediatR / IMediator** — handlers injectés directement.
- **Result pattern** — jamais d'exceptions pour erreurs métier ; mapping HTTP via `ToHttpResult()` (déjà en place). `Error.Conflict(...)` → HTTP 409.
- **Id = `int`**, pas de Guid ; pas de soft-delete ; pas d'audit `UpdatedBy/At`.
- **Commentaires XML multi-lignes en français** sur tous les types/membres publics.
- **`[LoggerMessage]` source-generated** pour les logs (pas d'interpolation directe). Ne pas re-logger le cycle de vie (décorateurs s'en chargent).
- **`file-scoped namespaces`, primary constructors, `record` pour commands/queries/DTOs, suffixe `Async`, `CancellationToken cancellationToken` partout.**
- **Repository Ardalis** : `UpdateAsync(entity, ct)` appelle `SaveChangesAsync` immédiatement → c'est l'appel où `DbUpdateConcurrencyException` est levé.
- **Migration EF** (commande exacte) :
  ```bash
  dotnet ef migrations add <Nom> --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
  ```
- **Répertoire de travail backend** : `c:/prjRecherche/TaxiBackEnd`. Tous les chemins ci-dessous sont relatifs à cette racine.
- **Branche** : on travaille sur `fix/frontend-backend-integration` (branche courante du repo).

---

## File Structure

**Domain** (`src/Taxi.Domain/Rides/Ride.cs`)
- Remplace `int? OfferedDriverId` par `List<int> OfferedDriverIds`.
- `Offer(int, DateTime)` → `OfferWave(IEnumerable<int>, DateTime)`.
- `AcceptOffer(int)` / `DeclineOffer(int)` / `ReturnToPending()` adaptés à la vague.

**Persistence** (`src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs` + migration)
- Colonne `offered_driver_ids integer[]` + `UseXminAsConcurrencyToken()`.

**Application**
- `Dispatch/RideDispatcher.cs` — logique de vague.
- `Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs` — catch `DbUpdateConcurrencyException`.
- `Dispatch/DeclineOffer/DeclineOfferCommandHandler.cs` — retrait de la vague.
- `Realtime/IRealtimeNotifier.cs` — `+ RideOfferRevokedAsync`.
- `Rides/RideErrors.cs` — `+ OfferTaken`.

**Web.Api** (`src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs`)
- Implémente `RideOfferRevokedAsync` (émet `rideOfferRevoked`).

**Infrastructure** (`src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs`)
- Révoque la vague à l'expiration.

**Tests** (`tests/Taxi.Application.Tests/`)
- `Rides/RideWaveTests.cs` — transitions Domain.
- `Dispatch/RideDispatcherWaveTests.cs` — logique de vague (Moq).

> **Note `DbUpdateConcurrencyException`** : Ardalis `RepositoryBase.UpdateAsync` fait `SaveChangesAsync` en interne. L'exception de concurrence est donc levée DANS `rides.UpdateAsync(ride, ct)`. Le test de race condition réel nécessite une vraie base PostgreSQL (xmin n'existe pas en InMemory) → il est documenté comme test d'intégration manuel en fin de plan, hors suite unitaire automatique (voir Task 7).

---

### Task 1: Modèle d'état du Ride — passage à la vague (Domain)

**Files:**
- Modify: `src/Taxi.Domain/Rides/Ride.cs`
- Modify: `src/Taxi.Domain/Rides/RideErrors.cs`
- Test: `tests/Taxi.Application.Tests/Rides/RideWaveTests.cs` (create)

**Interfaces:**
- Produces:
  - `Ride.OfferedDriverIds` : `IReadOnlyCollection<int>` (exposition publique en lecture).
  - `Result Ride.OfferWave(IEnumerable<int> driverIds, DateTime expiresAt)`
  - `Result Ride.AcceptOffer(int driverId)` (signature inchangée, logique adaptée)
  - `Result Ride.DeclineOffer(int driverId)` (nouveau)
  - `Result Ride.ReturnToPending()` (inchangé)
  - `void Ride.MarkDriverTried(int driverId)` (inchangé)
  - `RideErrors.OfferTaken` : `Error` de type Conflict.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `tests/Taxi.Application.Tests/Rides/RideWaveTests.cs` :

```csharp
using FluentAssertions;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Rides;

public class RideWaveTests
{
    private static Ride NewPendingRide() => Ride.Request(
        clientId: "client-1",
        pickupAddress: "A", destinationAddress: "B",
        pickupZone: "Z1", destinationZone: "Z2",
        pickupLatitude: 11.58, pickupLongitude: 43.14,
        destinationLatitude: 11.60, destinationLongitude: 43.15,
        estimatedPrice: 1000m);

    [Fact]
    public void OfferWave_passe_en_Offered_et_remplit_la_vague_et_les_essayes()
    {
        var ride = NewPendingRide();

        var result = ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered);
        ride.OfferedDriverIds.Should().BeEquivalentTo([10, 20, 30]);
        ride.TriedDriverIds.Should().BeEquivalentTo([10, 20, 30]);
    }

    [Fact]
    public void OfferWave_echoue_si_pas_pending()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10], DateTime.UtcNow.AddSeconds(15));

        var result = ride.OfferWave([20], DateTime.UtcNow.AddSeconds(15));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.NotPending);
    }

    [Fact]
    public void AcceptOffer_reussit_pour_un_chauffeur_de_la_vague()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.AcceptOffer(20);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Accepted);
        ride.DriverId.Should().Be(20);
        ride.OfferedDriverIds.Should().BeEmpty();
    }

    [Fact]
    public void AcceptOffer_echoue_pour_un_chauffeur_hors_vague()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.AcceptOffer(99);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferMismatch);
        ride.Status.Should().Be(RideStatus.Offered);
    }

    [Fact]
    public void AcceptOffer_echoue_si_offre_expiree()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20], DateTime.UtcNow.AddSeconds(-1));

        var result = ride.AcceptOffer(10);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RideErrors.OfferExpired);
    }

    [Fact]
    public void AcceptOffer_echoue_si_deja_accepte_premier_gagne()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20], DateTime.UtcNow.AddSeconds(15));
        ride.AcceptOffer(10); // premier gagne

        var second = ride.AcceptOffer(20); // doit échouer : statut n'est plus Offered

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(RideErrors.NotOffered);
    }

    [Fact]
    public void DeclineOffer_retire_de_la_vague_sans_la_vider()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10, 20, 30], DateTime.UtcNow.AddSeconds(15));

        var result = ride.DeclineOffer(20);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Offered); // encore 10 et 30
        ride.OfferedDriverIds.Should().BeEquivalentTo([10, 30]);
    }

    [Fact]
    public void DeclineOffer_vide_la_vague_remet_en_pending()
    {
        var ride = NewPendingRide();
        ride.OfferWave([10], DateTime.UtcNow.AddSeconds(15));

        var result = ride.DeclineOffer(10);

        result.IsSuccess.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Pending);
        ride.OfferedDriverIds.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test tests/Taxi.Application.Tests --filter "FullyQualifiedName~RideWaveTests"`
Expected: échec de compilation (`OfferWave`, `DeclineOffer`, `OfferedDriverIds`, `RideErrors.OfferTaken` inexistants).

- [ ] **Step 3: Ajouter l'erreur OfferTaken**

Dans `src/Taxi.Domain/Rides/RideErrors.cs`, ajouter après la ligne `OfferExpired` (ligne 20) :

```csharp
    public static readonly Error OfferTaken = Error.Conflict("Ride.OfferTaken", "Cette course vient d'être prise par un autre chauffeur.");
```

- [ ] **Step 4: Adapter le Ride à la vague**

Dans `src/Taxi.Domain/Rides/Ride.cs` :

Remplacer la ligne 26 :
```csharp
    public int? OfferedDriverId { get; private set; }
```
par :
```csharp
    private readonly List<int> _offeredDriverIds = [];

    /// <summary>
    /// Chauffeurs de la vague en cours auxquels la course est actuellement offerte (premier arrivé gagne).
    /// </summary>
    public IReadOnlyCollection<int> OfferedDriverIds => _offeredDriverIds.AsReadOnly();
```

Remplacer la méthode `Offer` (lignes 140-154) par :
```csharp
    /// <summary>
    /// Propose la course à une vague de chauffeurs simultanément avec une fenêtre d'expiration commune :
    /// passe au statut <see cref="RideStatus.Offered"/>, enregistre la vague et marque ces chauffeurs comme essayés.
    /// </summary>
    public Result OfferWave(IEnumerable<int> driverIds, DateTime expiresAt)
    {
        if (Status != RideStatus.Pending)
            return Result.Failure(RideErrors.NotPending);

        _offeredDriverIds.Clear();
        foreach (var id in driverIds)
        {
            if (!_offeredDriverIds.Contains(id))
                _offeredDriverIds.Add(id);
            MarkDriverTried(id);
        }

        Status = RideStatus.Offered;
        OfferExpiresAt = expiresAt;
        return Result.Success();
    }
```

Remplacer la méthode `AcceptOffer` (lignes 156-176) par :
```csharp
    /// <summary>
    /// Confirme l'acceptation d'une offre par un chauffeur de la vague : vérifie qu'il fait partie de la vague
    /// en cours et que celle-ci n'a pas expiré, puis fait passer la course à <see cref="RideStatus.Accepted"/>.
    /// Le premier chauffeur à accepter gagne ; les suivants échoueront car le statut n'est plus <c>Offered</c>.
    /// </summary>
    public Result AcceptOffer(int driverId)
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.NotOffered);
        if (!_offeredDriverIds.Contains(driverId))
            return Result.Failure(RideErrors.OfferMismatch);
        if (OfferExpiresAt is null || OfferExpiresAt <= DateTime.UtcNow)
            return Result.Failure(RideErrors.OfferExpired);

        DriverId = driverId;
        Status = RideStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        _offeredDriverIds.Clear();
        OfferExpiresAt = null;
        return Result.Success();
    }

    /// <summary>
    /// Refus d'une offre par un chauffeur de la vague : le retire de la vague en cours. Si la vague devient vide,
    /// la course retourne au statut <see cref="RideStatus.Pending"/> afin d'être réattribuée.
    /// </summary>
    public Result DeclineOffer(int driverId)
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.NotOffered);
        if (!_offeredDriverIds.Contains(driverId))
            return Result.Failure(RideErrors.OfferMismatch);

        _offeredDriverIds.Remove(driverId);
        if (_offeredDriverIds.Count == 0)
        {
            Status = RideStatus.Pending;
            OfferExpiresAt = null;
        }
        return Result.Success();
    }
```

Remplacer la méthode `ReturnToPending` (lignes 178-191) par :
```csharp
    /// <summary>
    /// Remet la course au statut <see cref="RideStatus.Pending"/> lorsqu'une vague expire,
    /// permettant au système de proposer une nouvelle vague à d'autres chauffeurs.
    /// </summary>
    public Result ReturnToPending()
    {
        if (Status != RideStatus.Offered)
            return Result.Failure(RideErrors.InvalidTransition);

        Status = RideStatus.Pending;
        _offeredDriverIds.Clear();
        OfferExpiresAt = null;
        return Result.Success();
    }
```

- [ ] **Step 5: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test tests/Taxi.Application.Tests --filter "FullyQualifiedName~RideWaveTests"`
Expected: PASS (8 tests). Le projet Domain compile.

> Note : ce step casse temporairement la compilation de `RideDispatcher`, `OfferTimeoutService`, `DeclineOfferCommandHandler` (références à `Offer`/`OfferedDriverId`). C'est attendu — ils sont corrigés aux tasks 3-6. La commande `dotnet test` ci-dessus ne compile que `Taxi.Application.Tests` + ses dépendances ; si elle échoue à cause de l'Infrastructure, lancer plutôt `dotnet build src/Taxi.Domain` pour valider le Domain isolément, puis poursuivre.

- [ ] **Step 6: Commit**

```bash
git add src/Taxi.Domain/Rides/Ride.cs src/Taxi.Domain/Rides/RideErrors.cs tests/Taxi.Application.Tests/Rides/RideWaveTests.cs
git commit -m "feat(domain): Ride porte une vague de chauffeurs (OfferWave/AcceptOffer/DeclineOffer)"
```

---

### Task 2: Persistance — colonne tableau + verrou optimiste xmin (Infrastructure)

**Files:**
- Modify: `src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs`
- Create: migration EF sous `src/Taxi.Infrastructure/Persistence/Migrations/`

**Interfaces:**
- Consumes: `Ride._offeredDriverIds` / `OfferedDriverIds` (Task 1).
- Produces: colonne `offered_driver_ids integer[]` mappée sur le champ `_offeredDriverIds` ; `xmin` configuré comme concurrency token sur `rides`.

- [ ] **Step 1: Configurer la colonne et le verrou**

Dans `src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs`, ajouter dans `Configure`, après la ligne 25 (`builder.HasIndex(r => r.Status);`) :

```csharp

        // Vague de chauffeurs offerts : champ privé _offeredDriverIds exposé via OfferedDriverIds.
        builder.Property<List<int>>("_offeredDriverIds")
            .HasColumnName("offered_driver_ids")
            .HasDefaultValueSql("'{}'::integer[]");

        // Verrou optimiste : la colonne système PostgreSQL xmin sert de token de concurrence.
        // Deux acceptations simultanées → un seul UPDATE matche xmin, l'autre lève DbUpdateConcurrencyException.
        builder.UseXminAsConcurrencyToken();
```

> Note : `UseXminAsConcurrencyToken()` ne crée PAS de colonne (xmin est une colonne système PostgreSQL). La migration ne contiendra donc que `offered_driver_ids` et la suppression de l'ancienne colonne `offered_driver_id`.

- [ ] **Step 2: Vérifier que le projet compile**

Run: `dotnet build src/Taxi.Infrastructure`
Expected: échec attendu UNIQUEMENT si les tasks 3-6 ne sont pas faites (références à `Offer`/`OfferedDriverId` dans RideDispatcher/OfferTimeoutService/DeclineHandler). Si c'est le seul type d'erreur, continuer ; la migration sera générée après les tasks 3-6. **Réordonnancement : faire cette Step 3 (migration) APRÈS la Task 6.**

- [ ] **Step 3: Générer la migration (à exécuter APRÈS la Task 6, quand tout compile)**

Run:
```bash
dotnet ef migrations add WaveDispatch --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: création de `*_WaveDispatch.cs`. Vérifier qu'elle :
- `DropColumn("offered_driver_id", "rides")`
- `AddColumn<int[]>("offered_driver_ids", "rides", defaultValueSql: "'{}'::integer[]")`
- ajoute `xmin` comme rowversion concurrency token (commentaire/annotation, pas de colonne réelle).

- [ ] **Step 4: Commit**

```bash
git add src/Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs src/Taxi.Infrastructure/Persistence/Migrations/
git commit -m "feat(infra): colonne offered_driver_ids + verrou optimiste xmin sur rides"
```

---

### Task 3: RideDispatcher — offre par vague (Application)

**Files:**
- Modify: `src/Taxi.Application/Dispatch/RideDispatcher.cs`
- Test: `tests/Taxi.Application.Tests/Dispatch/RideDispatcherWaveTests.cs` (create)

**Interfaces:**
- Consumes: `Ride.OfferWave` (Task 1) ; `IDriverLocator.FindNearestAsync` → `IReadOnlyList<NearbyDriver>` (existant) ; `NearbyDriver(int DriverId, string UserId, double DistanceMeters, double Latitude, double Longitude, string VehicleType)`.
- Produces: comportement « vague de `min(3, candidats libres)` » ; émet un `RideOfferedAsync` par chauffeur de la vague.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `tests/Taxi.Application.Tests/Dispatch/RideDispatcherWaveTests.cs` :

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taxi.Application.Abstractions;
using Taxi.Application.Dispatch;
using Taxi.Application.Realtime;
using Taxi.Application.Rides;
using Taxi.Domain.Rides;
using Xunit;

namespace Taxi.Application.Tests.Dispatch;

public class RideDispatcherWaveTests
{
    private static Ride PendingRideWithGps()
        => Ride.Request("client-1", "A", "B", "Z1", "Z2", 11.58, 43.14, 11.60, 43.15, 1000m);

    private static NearbyDriver Driver(int id)
        => new(id, $"user-{id}", 100 * id, 11.58, 43.14, "Taxi");

    private static (Mock<IDriverLocator>, Mock<IRepository<Ride>>, Mock<IRealtimeNotifier>) Mocks(
        Ride ride, IReadOnlyList<NearbyDriver> candidates)
    {
        var locator = new Mock<IDriverLocator>();
        locator.Setup(l => l.FindNearestAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        var rides = new Mock<IRepository<Ride>>();
        rides.Setup(r => r.FirstOrDefaultAsync(It.IsAny<RideByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ride);

        var notifier = new Mock<IRealtimeNotifier>();
        return (locator, rides, notifier);
    }

    [Fact]
    public async Task Offre_a_min_3_candidats()
    {
        var ride = PendingRideWithGps();
        var (locator, rides, notifier) = Mocks(ride, [Driver(1), Driver(2), Driver(3), Driver(4), Driver(5)]);
        var sut = new RideDispatcher(locator.Object, rides.Object, notifier.Object, NullLogger<RideDispatcher>.Instance);

        await sut.DispatchAsync(1, CancellationToken.None);

        ride.OfferedDriverIds.Should().BeEquivalentTo([1, 2, 3]);
        notifier.Verify(n => n.RideOfferedAsync(It.IsAny<string>(), 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Offre_a_tous_si_moins_de_3_candidats()
    {
        var ride = PendingRideWithGps();
        var (locator, rides, notifier) = Mocks(ride, [Driver(1), Driver(2)]);
        var sut = new RideDispatcher(locator.Object, rides.Object, notifier.Object, NullLogger<RideDispatcher>.Instance);

        await sut.DispatchAsync(1, CancellationToken.None);

        ride.OfferedDriverIds.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task Exclut_les_chauffeurs_deja_essayes()
    {
        var ride = PendingRideWithGps();
        ride.MarkDriverTried(1);
        ride.MarkDriverTried(2);
        var (locator, rides, notifier) = Mocks(ride, [Driver(1), Driver(2), Driver(3), Driver(4)]);
        var sut = new RideDispatcher(locator.Object, rides.Object, notifier.Object, NullLogger<RideDispatcher>.Instance);

        await sut.DispatchAsync(1, CancellationToken.None);

        ride.OfferedDriverIds.Should().BeEquivalentTo([3, 4]);
    }

    [Fact]
    public async Task Aucun_candidat_libre_notifie_les_admins()
    {
        var ride = PendingRideWithGps();
        ride.MarkDriverTried(1);
        var (locator, rides, notifier) = Mocks(ride, [Driver(1)]);
        var sut = new RideDispatcher(locator.Object, rides.Object, notifier.Object, NullLogger<RideDispatcher>.Instance);

        await sut.DispatchAsync(1, CancellationToken.None);

        ride.Status.Should().Be(RideStatus.Pending);
        notifier.Verify(n => n.NewPendingRideAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test tests/Taxi.Application.Tests --filter "FullyQualifiedName~RideDispatcherWaveTests"`
Expected: échec de compilation (`ride.Offer` n'existe plus → le dispatcher actuel ne compile pas).

- [ ] **Step 3: Adapter le dispatcher**

Dans `src/Taxi.Application/Dispatch/RideDispatcher.cs` :

Remplacer les constantes (lignes 21-23) :
```csharp
    private static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(30);
    private const double RadiusMeters = 5000;
    private const int MaxCandidates = 20;
```
par :
```csharp
    private static readonly TimeSpan OfferTtl = TimeSpan.FromSeconds(15);
    private const double RadiusMeters = 5000;
    private const int MaxCandidates = 20;
    private const int WaveSize = 3;
```

Remplacer le bloc lignes 41-57 (de `var candidates = ...` à `LogOfferMade(...);`) par :
```csharp
        var candidates = await locator.FindNearestAsync(
            ride.PickupLatitude.Value, ride.PickupLongitude.Value, RadiusMeters, MaxCandidates, cancellationToken);

        var wave = candidates
            .Where(c => !ride.TriedDriverIds.Contains(c.DriverId))
            .Take(WaveSize)
            .ToList();

        if (wave.Count == 0)
        {
            LogNoCandidate(logger, ride.Id);
            await notifier.NewPendingRideAsync(ride.Id, cancellationToken);
            return;
        }

        var expiresAt = DateTime.UtcNow + OfferTtl;
        ride.OfferWave(wave.Select(c => c.DriverId), expiresAt);
        await rides.UpdateAsync(ride, cancellationToken);

        foreach (var candidate in wave)
            await notifier.RideOfferedAsync(candidate.UserId, ride.Id, expiresAt, cancellationToken);

        LogWaveOffered(logger, ride.Id, wave.Count, expiresAt);
```

Remplacer la déclaration du log `LogOfferMade` (lignes 62-63) par :
```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Course {RideId} offerte à une vague de {Count} chauffeur(s) (expire à {ExpiresAt:o})")]
    private static partial void LogWaveOffered(ILogger logger, int rideId, int count, DateTime expiresAt);
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test tests/Taxi.Application.Tests --filter "FullyQualifiedName~RideDispatcherWaveTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Taxi.Application/Dispatch/RideDispatcher.cs tests/Taxi.Application.Tests/Dispatch/RideDispatcherWaveTests.cs
git commit -m "feat(dispatch): offre par vague de min(3, candidats) au lieu d'un seul chauffeur"
```

---

### Task 4: Révocation d'offre — abstraction + SignalR (Application + Web.Api)

**Files:**
- Modify: `src/Taxi.Application/Realtime/IRealtimeNotifier.cs`
- Modify: `src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs`

**Interfaces:**
- Produces: `Task IRealtimeNotifier.RideOfferRevokedAsync(string driverUserId, int rideId, string reason, CancellationToken cancellationToken)` ; émet l'événement SignalR `rideOfferRevoked` avec `{ rideId, reason }` au groupe `DriverUser_{driverUserId}`.

- [ ] **Step 1: Ajouter la méthode à l'abstraction**

Dans `src/Taxi.Application/Realtime/IRealtimeNotifier.cs`, ajouter après la ligne 10 (`RideOfferedAsync`) :

```csharp

    /// <summary>
    /// Notifie un chauffeur que l'offre de course ne lui est plus proposée (course prise, vague expirée ou course annulée),
    /// afin que son écran retire la carte d'offre. La <paramref name="reason"/> vaut "taken", "expired" ou "cancelled".
    /// </summary>
    Task RideOfferRevokedAsync(string driverUserId, int rideId, string reason, CancellationToken cancellationToken);
```

- [ ] **Step 2: Implémenter dans SignalR**

Dans `src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs`, ajouter après la méthode `RideOfferedAsync` (après la ligne 52, avant l'accolade fermante de la classe) :

```csharp

    public async Task RideOfferRevokedAsync(string driverUserId, int rideId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group($"DriverUser_{driverUserId}")
                .SendAsync("rideOfferRevoked", new { rideId, reason }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notify (rideOfferRevoked) failed for ride {RideId}", rideId);
        }
    }
```

- [ ] **Step 3: Vérifier la compilation**

Run: `dotnet build src/Taxi.Web.Api`
Expected: succès (sous réserve que les tasks 1-3 soient faites ; OfferTimeoutService/DeclineHandler corrigés en tasks 5-6 sinon erreurs résiduelles attendues).

- [ ] **Step 4: Commit**

```bash
git add src/Taxi.Application/Realtime/IRealtimeNotifier.cs src/Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs
git commit -m "feat(realtime): événement rideOfferRevoked pour révoquer une offre côté chauffeur"
```

---

### Task 5: AcceptOffer + DeclineOffer — verrou 409 et révocation des perdants (Application)

**Files:**
- Modify: `src/Taxi.Application/Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs`
- Modify: `src/Taxi.Application/Dispatch/DeclineOffer/DeclineOfferCommandHandler.cs`

**Interfaces:**
- Consumes: `Ride.AcceptOffer`/`Ride.DeclineOffer`/`Ride.OfferedDriverIds` (Task 1) ; `IRealtimeNotifier.RideOfferRevokedAsync` (Task 4) ; `RideErrors.OfferTaken` (Task 1) ; `DriverByIdSpec`/`DriverByUserIdSpec` (voir Step 1) ; `IDriverLocator` n'est PAS utilisé ici.
- Produces: AcceptOffer renvoie `Conflict(OfferTaken)` (→ 409) en cas de race ; révoque l'offre aux autres chauffeurs de la vague.

> **Résolution UserId des perdants** : le handler a besoin du `UserId` SignalR de chaque chauffeur perdant. On le résout via le repository `Driver` avec une spec par lot d'Ids. Vérifier qu'une spec `DriversByIdsSpec` existe ; sinon la créer (Step 1).

- [ ] **Step 1: Créer la spec DriversByIdsSpec si absente**

Vérifier `src/Taxi.Application/Drivers/DriverSpecs.cs`. Si `DriversByIdsSpec` n'existe pas, l'ajouter :

```csharp
/// <summary>
/// Spécification : sélectionne les chauffeurs dont l'identifiant figure dans la liste fournie.
/// </summary>
internal sealed class DriversByIdsSpec : Specification<Driver>
{
    public DriversByIdsSpec(IEnumerable<int> ids) => Query.Where(d => ids.Contains(d.Id));
}
```
(Garder les `using Ardalis.Specification;` et `using Taxi.Domain.Drivers;` déjà présents en tête de fichier.)

- [ ] **Step 2: Adapter AcceptOfferCommandHandler (capture du verrou + révocation des perdants)**

Dans `src/Taxi.Application/Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs`, remplacer le corps de `Handle` (lignes 24-46) par :

```csharp
    public async Task<Result<RideDto>> Handle(AcceptOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        // Capture des perdants AVANT mutation (la vague est vidée par AcceptOffer).
        var losers = ride.OfferedDriverIds.Where(id => id != driver.Id).ToList();

        var accepted = ride.AcceptOffer(driver.Id);
        if (accepted.IsFailure)
            return Result.Failure<RideDto>(accepted.Error);

        try
        {
            await rides.UpdateAsync(ride, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un autre chauffeur de la vague a gagné la course entre la lecture et l'écriture.
            return Result.Failure<RideDto>(RideErrors.OfferTaken);
        }

        driver.SetAvailability(false);
        await drivers.UpdateAsync(driver, cancellationToken);
        LogOfferAccepted(logger, ride.Id, driver.Id);

        await RevokeLosersAsync(losers, ride.Id, "taken", cancellationToken);
        await notifier.RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, ride.Status.ToString(), cancellationToken);
        return RideDto.From(ride);
    }

    /// <summary>
    /// Révoque l'offre auprès des chauffeurs perdants de la vague en résolvant leur identifiant utilisateur SignalR.
    /// </summary>
    private async Task RevokeLosersAsync(IReadOnlyCollection<int> loserDriverIds, int rideId, string reason, CancellationToken cancellationToken)
    {
        if (loserDriverIds.Count == 0)
            return;

        var losers = await drivers.ListAsync(new DriversByIdsSpec(loserDriverIds), cancellationToken);
        foreach (var loser in losers)
            await notifier.RideOfferRevokedAsync(loser.UserId, rideId, reason, cancellationToken);
    }
```

Ajouter les `using` manquants en tête de fichier (après les `using` existants) :
```csharp
using Microsoft.EntityFrameworkCore;
```

> Note : `DriversByIdsSpec` est dans le namespace `Taxi.Application.Drivers`, déjà importé (ligne 3 `using Taxi.Application.Drivers;`). `Driver.UserId` est public (utilisé ailleurs). `ride.OfferedDriverIds` est `IReadOnlyCollection<int>`.

- [ ] **Step 3: Adapter DeclineOfferCommandHandler**

Dans `src/Taxi.Application/Dispatch/DeclineOffer/DeclineOfferCommandHandler.cs`, remplacer le corps de `Handle` (lignes 21-40) par :

```csharp
    public async Task<Result<RideDto>> Handle(DeclineOfferCommand command, CancellationToken cancellationToken)
    {
        var driver = await drivers.FirstOrDefaultAsync(new DriverByUserIdSpec(command.DriverUserId), cancellationToken);
        if (driver is null)
            return Result.Failure<RideDto>(RideErrors.NoDriverProfile);

        var ride = await rides.FirstOrDefaultAsync(new RideByIdSpec(command.RideId), cancellationToken);
        if (ride is null)
            return Result.Failure<RideDto>(RideErrors.NotFound);

        ride.MarkDriverTried(driver.Id);
        var declined = ride.DeclineOffer(driver.Id);
        if (declined.IsFailure)
            return Result.Failure<RideDto>(declined.Error);

        await rides.UpdateAsync(ride, cancellationToken);

        // Si la vague est vidée, la course est repassée en Pending → on relance vers la vague suivante.
        if (ride.Status == RideStatus.Pending)
            await dispatcher.DispatchAsync(ride.Id, cancellationToken);

        return RideDto.From(ride);
    }
```

> Note : on retire l'ancienne garde `ride.OfferedDriverId != driver.Id` (ligne 31) car `DeclineOffer` valide déjà l'appartenance à la vague et renvoie `OfferMismatch`/`NotOffered`. `RideStatus` est déjà importé via `using Taxi.Domain.Rides;` (ligne 5).

- [ ] **Step 4: Vérifier la compilation**

Run: `dotnet build src/Taxi.Application`
Expected: succès.

- [ ] **Step 5: Lancer toute la suite de tests Application**

Run: `dotnet test tests/Taxi.Application.Tests`
Expected: PASS (tests existants + RideWaveTests + RideDispatcherWaveTests).

- [ ] **Step 6: Commit**

```bash
git add src/Taxi.Application/Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs src/Taxi.Application/Dispatch/DeclineOffer/DeclineOfferCommandHandler.cs src/Taxi.Application/Drivers/DriverSpecs.cs
git commit -m "feat(dispatch): verrou 409 sur acceptation concurrente + révocation des perdants de la vague"
```

---

### Task 6: OfferTimeoutService — révocation à l'expiration (Infrastructure)

**Files:**
- Modify: `src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs`

**Interfaces:**
- Consumes: `Ride.OfferedDriverIds`/`Ride.ReturnToPending`/`Ride.MarkDriverTried` (Task 1) ; `IRealtimeNotifier.RideOfferRevokedAsync` (Task 4) ; `DriversByIdsSpec` (Task 5) ; `ExpiredOffersSpec` (existant).
- Produces: à l'expiration d'une vague, marque chaque chauffeur comme essayé, leur révoque l'offre (`reason="expired"`), repasse en Pending et relance le dispatch.

- [ ] **Step 1: Adapter le service**

Dans `src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs`, remplacer le bloc lignes 24-36 (de `using var scope = ...` à la fin du `foreach`) par :

```csharp
                using var scope = scopeFactory.CreateScope();
                var rides = scope.ServiceProvider.GetRequiredService<IRepository<Ride>>();
                var drivers = scope.ServiceProvider.GetRequiredService<IRepository<Driver>>();
                var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRideDispatcher>();

                var expired = await rides.ListAsync(new ExpiredOffersSpec(DateTime.UtcNow), stoppingToken);
                foreach (var ride in expired)
                {
                    var waveDriverIds = ride.OfferedDriverIds.ToList();
                    foreach (var driverId in waveDriverIds)
                        ride.MarkDriverTried(driverId);

                    ride.ReturnToPending();
                    await rides.UpdateAsync(ride, stoppingToken);

                    if (waveDriverIds.Count > 0)
                    {
                        var waveDrivers = await drivers.ListAsync(new DriversByIdsSpec(waveDriverIds), stoppingToken);
                        foreach (var driver in waveDrivers)
                            await notifier.RideOfferRevokedAsync(driver.UserId, ride.Id, "expired", stoppingToken);
                    }

                    await dispatcher.DispatchAsync(ride.Id, stoppingToken);
                }
```

Ajouter les `using` manquants en tête de fichier (après les `using` existants, lignes 1-7) :
```csharp
using Taxi.Application.Drivers;
using Taxi.Application.Realtime;
using Taxi.Domain.Drivers;
```

> Note : `DriversByIdsSpec` (namespace `Taxi.Application.Drivers`) est `internal` ; le projet Infrastructure référence Application, et l'`InternalsVisibleTo` n'est pas requis car le service est dans un assembly différent — si la spec `internal` n'est pas visible, la passer en `public` dans `DriverSpecs.cs` (cohérent avec `ExpiredOffersSpec` qui est déjà `public`). **Vérifier la visibilité : si erreur d'accessibilité, rendre `DriversByIdsSpec` `public`.**

- [ ] **Step 2: Vérifier la compilation de la solution complète**

Run: `dotnet build Taxi.slnx`
Expected: succès complet.

- [ ] **Step 3: Générer la migration EF (la Task 2 Step 3 différée)**

Run:
```bash
dotnet ef migrations add WaveDispatch --project src/Taxi.Infrastructure --startup-project src/Taxi.Web.Api --output-dir Persistence/Migrations
```
Expected: migration générée (drop `offered_driver_id`, add `offered_driver_ids`). Relire le fichier généré pour confirmer.

- [ ] **Step 4: Lancer toute la suite de tests**

Run: `dotnet test Taxi.slnx`
Expected: PASS (tous les tests, y compris Architecture.Tests).

- [ ] **Step 5: Commit**

```bash
git add src/Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs src/Taxi.Infrastructure/Persistence/Migrations/ src/Taxi.Application/Drivers/DriverSpecs.cs
git commit -m "feat(infra): révocation de la vague à l'expiration + migration WaveDispatch"
```

---

### Task 7: Vérification du verrou « premier-gagne » (intégration manuelle)

**Files:** aucun changement de code — vérification.

> Le verrou `xmin` ne fonctionne qu'avec une vraie base PostgreSQL (pas en InMemory). Ce test est donc une vérification manuelle sur l'environnement Aspire, pas un test unitaire automatique.

- [ ] **Step 1: Démarrer l'application**

Run: `dotnet run --project Taxi.AppHost`
Attendre que PostgreSQL+PostGIS et l'API soient up (dashboard Aspire http://localhost:15888).

- [ ] **Step 2: Scénario de race condition**

Via Scalar (http://localhost:5000/scalar) ou un script :
1. Créer une course avec GPS (un client) → elle passe en `Offered` à une vague.
2. Avec deux comptes chauffeurs de la même vague, appeler `POST /api/rides/{id}/accept-offer` quasi simultanément (deux requêtes en parallèle).

Expected:
- Exactement **un** chauffeur reçoit `200 OK` + le `RideDto` en `Accepted`.
- L'autre reçoit **`409 Conflict`** avec le code `Ride.OfferTaken` (ou `Ride.NotOffered` si l'écriture du gagnant est déjà committée avant la lecture du perdant — les deux sont des 409 acceptables).
- Le chauffeur perdant reçoit l'événement SignalR `rideOfferRevoked` avec `reason="taken"`.

- [ ] **Step 3: Documenter le résultat**

Noter dans le commit ou la PR le comportement observé (qui a gagné, code HTTP du perdant, réception de `rideOfferRevoked`). Pas de commit de code à cette étape.

---

### Task 8: Révocation à l'annulation client pendant l'offre (Application)

**Files:**
- Modify: `src/Taxi.Application/Rides/Cancel/CancelRideCommandHandler.cs`

**Interfaces:**
- Consumes: `Ride.OfferedDriverIds`/`Ride.Status` (Task 1) ; `IRealtimeNotifier.RideOfferRevokedAsync` (Task 4) ; `DriversByIdsSpec` (Task 5) ; `DriverByIdSpec` (existant, déjà utilisé ligne 50 du handler).
- Produces: quand une course en `Offered` est annulée par le client, chaque chauffeur de la vague reçoit `rideOfferRevoked` avec `reason="cancelled"`.

> Contexte : `CancelByClient()` autorise l'annulation depuis `Offered`. Dans ce cas la vague est vidée par le passage à `Cancelled`, donc il faut capturer `OfferedDriverIds` AVANT l'appel à `CancelByClient()`.

- [ ] **Step 1: Capturer la vague avant annulation et révoquer après**

Dans `src/Taxi.Application/Rides/Cancel/CancelRideCommandHandler.cs`, remplacer le bloc lignes 27-47 (de `Result outcome;` jusqu'à `await rides.UpdateAsync(ride, cancellationToken);` inclus) par :

```csharp
        // Capture de la vague d'offre AVANT mutation : si la course est annulée pendant qu'elle est offerte,
        // ces chauffeurs doivent voir leur offre révoquée.
        var offeredWave = ride.Status == RideStatus.Offered
            ? ride.OfferedDriverIds.ToList()
            : [];

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

        if (offeredWave.Count > 0)
        {
            var waveDrivers = await drivers.ListAsync(new DriversByIdsSpec(offeredWave), cancellationToken);
            foreach (var waveDriver in waveDrivers)
                await notifier.RideOfferRevokedAsync(waveDriver.UserId, ride.Id, "cancelled", cancellationToken);
        }
```

> Note : `RideStatus` et `DriversByIdsSpec` sont accessibles (`using Taxi.Domain.Rides;` ligne 5, `using Taxi.Application.Drivers;` ligne 2 — déjà présents). La liste vide `[]` est typée `List<int>` par inférence.

- [ ] **Step 2: Vérifier la compilation**

Run: `dotnet build src/Taxi.Application`
Expected: succès.

- [ ] **Step 3: Lancer la suite de tests**

Run: `dotnet test tests/Taxi.Application.Tests`
Expected: PASS (aucun test existant cassé).

- [ ] **Step 4: Commit**

```bash
git add src/Taxi.Application/Rides/Cancel/CancelRideCommandHandler.cs
git commit -m "feat(rides): révocation de la vague quand le client annule pendant une offre"
```

---

## Notes de vérification finale (self-review)

**Couverture spec :**
- §3 va-et-vient (immédiat vs job) → Tasks 5 (decline) + 6 (timeout). ✓
- §4 modèle d'état → Task 1. ✓
- §5 algorithme de vague `min(3, candidats)` → Task 3. ✓
- §6 verrou xmin + 409 → Tasks 2 (config) + 5 (catch). ✓
- §7 `rideOfferRevoked` (taken/expired/cancelled) → Tasks 4 (event) + 5 (taken) + 6 (expired) + 8 (cancelled). ✓
- §8 tests → Tasks 1, 3 (unitaires) + 7 (intégration manuelle). ✓
- §9 fichiers touchés → tous couverts. ✓

**Frontend :** l'écoute de `rideOfferRevoked` côté `TaxiDjibouti.Frontend` (retrait de la carte + toast) est hors de ce plan backend. Ticket séparé côté frontend.
