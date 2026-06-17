# Module Dispatch — Phase 2 (auto-assignation) — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Dépend de :** Dispatch Phase 1 (`IDriverLocator`, position `geography` du chauffeur).

## 1. Objectif

Assigner automatiquement la course au chauffeur disponible **le plus proche**, via un flux d'**offre séquentielle**
avec **timeout** et **réattribution**. Le flux manuel existant (`/api/rides/pending` + `/api/rides/{id}/accept`)
reste en place comme **filet de sécurité**.

## 2. Décisions validées

- **Offre séquentielle** : un seul chauffeur à la fois (le plus proche non encore tenté), puis le suivant.
- **Timeout 30 s** (configurable) avant réattribution.
- **Sans preneur** (candidats épuisés ou pas de coordonnées) → la course **retourne en `Pending`** (flux manuel).
- **Disponibilité liée au cycle** (6a validé) : accepter une course rend le chauffeur indisponible ; terminer/annuler le rend disponible.
- **RequestRide délègue au dispatcher** (6b validé) : plus de `newPendingRide` inconditionnel ; le dispatcher décide (offre ciblée, ou retour Pending + `newPendingRide`).

## 3. Flux

```
RequestRide (coords présentes) → Dispatcher offre au plus proche → "Offered" (expire à +30s)
   ├─ accept-offer  → "Accepted" (DriverId fixé, chauffeur devient indisponible)
   ├─ decline-offer → driver marqué "tenté" → Dispatcher offre au suivant
   └─ timeout 30s   → (background) driver marqué "tenté" → Dispatcher offre au suivant
Aucun candidat / pas de coords → "Pending" (offre vidée) + newPendingRide (flux manuel /pending + /accept)
```

## 4. Machine à états

`RideStatus` (actuel : `Pending, Accepted, DriverArrived, InProgress, Completed, Cancelled`) gagne **`Offered`**.

`Ride` gagne :
- `int? OfferedDriverId`
- `DateTime? OfferExpiresAt`
- `List<int> TriedDriverIds` (chauffeurs déjà offerts/refusés/expirés sur cette course — collection primitive, mappée nativement par EF Core 10).

**Méthodes domaine** (retournent `Result`, sauf accesseurs) :
- `Offer(int driverId, DateTime expiresAt)` : exige `Pending` → `Offered`, fixe `OfferedDriverId`/`OfferExpiresAt`.
- `AcceptOffer(int driverId)` : exige `Offered` **et** `OfferedDriverId == driverId` **et** `OfferExpiresAt > now` → `Accepted`, `DriverId = driverId`, `AcceptedAt = now`, vide `OfferedDriverId`/`OfferExpiresAt`. Erreurs : `RideErrors.NotOffered` / `OfferMismatch` / `OfferExpired`.
- `ReturnToPending()` : exige `Offered` → `Pending`, vide `OfferedDriverId`/`OfferExpiresAt` (ne touche pas `TriedDriverIds`).
- `MarkDriverTried(int driverId)` : ajoute `driverId` à `TriedDriverIds` s'il n'y est pas.

Nouveaux `RideErrors` : `NotOffered` (Conflict), `OfferMismatch` (Forbidden), `OfferExpired` (Conflict).

Migration **`AddRideDispatch`** : colonnes `offered_driver_id` (int?), `offer_expires_at` (timestamptz?), `tried_driver_ids`.

## 5. `IRideDispatcher` (orchestrateur — couche Application)

N'orchestre que des abstractions (pas de DbContext) → vit en **Application** (`Taxi.Application.Dispatch`), implémentation `RideDispatcher` aussi en Application (injecte `IDriverLocator`, `IRepository<Ride>`, `IRealtimeNotifier`). Enregistré en DI.

```csharp
public interface IRideDispatcher
{
    Task DispatchAsync(int rideId, CancellationToken cancellationToken);
}
```

`DispatchAsync` :
1. charge la course (`RideByIdSpec`) ; si absente, ou statut ∉ {`Pending`}, retourne (idempotent : on ne dispatch que les courses en attente d'offre).
2. si `PickupLatitude`/`PickupLongitude` null → laisse `Pending` (pas d'auto-dispatch) et retourne.
3. `IDriverLocator.FindNearestAsync(pickupLat, pickupLon, radius=5000, max=20)` → exclut les `TriedDriverIds` → 1er restant.
4. **si trouvé** : `ride.Offer(driver.DriverId, now + OfferTtl)`, `UpdateAsync`, puis `IRealtimeNotifier.RideOfferedAsync(driver.UserId, rideId, …)`.
5. **si aucun** : `ride.ReturnToPending()` (no-op si déjà Pending), `UpdateAsync`, puis `IRealtimeNotifier.NewPendingRideAsync(rideId)`.

`OfferTtl` = 30 s (constante interne, configurable plus tard). `now` = `DateTime.UtcNow`.

> Le dispatcher s'appuie sur `IsAvailable` (déjà filtré par `IDriverLocator`) ; la section 8 garantit qu'un chauffeur en course n'est pas `IsAvailable`.

## 6. Endpoints (rôle Driver)

- **`POST /api/rides/{id}/accept-offer`** : `userId` du JWT → charge le `Driver` → `AcceptOfferCommand(rideId, driverUserId)`. Handler : vérifie le profil, `ride.AcceptOffer(driver.Id)`, persiste, **rend le chauffeur indisponible** (`driver.SetAvailability(false)`), notifie `rideStatusChanged`.
- **`POST /api/rides/{id}/decline-offer`** : `DeclineOfferCommand(rideId, driverUserId)`. Handler : vérifie que l'offre concerne ce chauffeur, `ride.MarkDriverTried(driver.Id)` + `ride.ReturnToPending()`, persiste, puis `IRideDispatcher.DispatchAsync(rideId)` (offre au suivant).

L'`/accept` manuel existant (Pending → Accepted) est **conservé** ; il rend lui aussi le chauffeur indisponible (section 8).

## 7. Background service — timeout

`OfferTimeoutService : BackgroundService` dans **Infrastructure** (même emplacement/pattern que `RefreshTokenCleanupService`, enregistré en `AddHostedService`). Il résout `IRideDispatcher` (Application) dans un scope. Boucle toutes les **5 s** (dans un scope DI) :
- charge les courses `Offered` avec `OfferExpiresAt <= now` (spec dédiée `ExpiredOffersSpec`) ;
- pour chacune : `ride.MarkDriverTried(ride.OfferedDriverId.Value)` + `ride.ReturnToPending()`, persiste, puis `IRideDispatcher.DispatchAsync(rideId)`.

(Le service ré-offre au suivant ou retombe en Pending via le dispatcher.)

## 8. Disponibilité liée au cycle de course (6a)

Pour ne pas offrir à un chauffeur déjà en course, `IsAvailable` suit le cycle :
- **Accept** (manuel) et **AcceptOffer** : `driver.SetAvailability(false)` après l'acceptation.
- **Complete** et **Cancel** (par le chauffeur, ou client quand un chauffeur est assigné) : `driver.SetAvailability(true)` pour le chauffeur assigné.

Ces handlers chargent déjà le `Driver` (vérif d'appartenance) → ajout d'un `SetAvailability` + `UpdateAsync`. Pour `CancelRide` côté client : si `ride.DriverId` est renseigné, recharger ce driver et le remettre disponible.

## 9. Temps réel — offre ciblée

Le hub gagne :
- méthode `JoinMyDriverGroup()` → rejoint le groupe perso **`DriverUser_{userId}`** (le chauffeur s'abonne à ses offres).
- l'event **`rideOffered`** (payload : `{ rideId, expiresAt, pickupLat, pickupLon, estimatedPrice }`).

`IRealtimeNotifier` gagne **`RideOfferedAsync(string driverUserId, int rideId, DateTime expiresAt, CancellationToken)`** → `SignalRRealtimeNotifier` pousse `rideOffered` au groupe `DriverUser_{driverUserId}`.

## 10. Trigger initial (6b)

`RequestRideCommandHandler` : après `AddAsync(ride)`, **remplacer** l'appel `notifier.NewPendingRideAsync(...)` par `await dispatcher.DispatchAsync(ride.Id, ct)`. Le dispatcher gère la suite (offre ciblée, ou Pending + `newPendingRide` si pas de coords / personne). Le handler reçoit `IRideDispatcher` au lieu d'`IRealtimeNotifier`.

## 11. Architecture (placement)

```
Taxi.Domain/Rides/Ride.cs                          — modifié (Offered, champs offre, méthodes)
Taxi.Domain/Rides/RideStatus.cs                    — modifié (+ Offered)
Taxi.Domain/Rides/RideErrors.cs                    — modifié (NotOffered/OfferMismatch/OfferExpired)
Taxi.Application/Dispatch/IRideDispatcher.cs, RideDispatcher.cs  — créés
Taxi.Application/Dispatch/AcceptOffer/{Command,Handler}.cs       — créés
Taxi.Application/Dispatch/DeclineOffer/{Command,Handler}.cs      — créés
Taxi.Application/Rides/RideSpecs.cs                — modifié (+ ExpiredOffersSpec)
Taxi.Application/Realtime/IRealtimeNotifier.cs     — modifié (+ RideOfferedAsync)
Taxi.Application/Rides/Request/RequestRideCommandHandler.cs      — modifié (dispatcher)
Taxi.Application/Rides/Accept|Transitions|Cancel/*Handler.cs     — modifiés (SetAvailability)
Taxi.Infrastructure/Realtime/SignalRRealtimeNotifier.cs (Web.Api)— modifié (RideOfferedAsync)
Taxi.Infrastructure/.../OfferTimeoutService.cs     — créé (background, AddHostedService)
Taxi.Web.Api/Realtime/RideHub.cs                   — modifié (JoinMyDriverGroup)
Taxi.Web.Api/Modules/Rides/ (ou Dispatch/)         — endpoints accept-offer / decline-offer
Taxi.Infrastructure/Persistence/Migrations/*AddRideDispatch*     — généré
```
> Note placement : `SignalRRealtimeNotifier` et `RideHub` vivent en **Web.Api** (là où est `IHubContext<RideHub>`), pas en Infrastructure — corriger le tableau ci-dessus à l'implémentation. `OfferTimeoutService` peut vivre en Web.Api (il dépend du dispatcher Application, enregistré là où sont les autres hosted services / ou Infrastructure si on y déplace).

## 12. Stratégie de test

- **Domaine `Ride`** (TDD) : `Offer` (Pending→Offered), `AcceptOffer` (succès ; échec si non-Offered / mauvais chauffeur / expirée), `ReturnToPending`, `MarkDriverTried` (pas de doublon).
- **`RideDispatcher`** (TDD, mocks `IDriverLocator`/`IRepository<Ride>`/`IRealtimeNotifier`) : offre au plus proche non-tenté ; exclusion des `TriedDriverIds` ; retour Pending + `NewPendingRide` si aucun ; no-op si pas de coords.
- **AcceptOffer/DeclineOffer handlers** (TDD) : transitions + disponibilité + (decline) réappel du dispatcher.
- **Disponibilité** : les tests existants Accept/Complete/Cancel mis à jour (mock driver) vérifient `SetAvailability`.
- **Hub `JoinMyDriverGroup`, `OfferTimeoutService`, push `rideOffered`** : **vérification manuelle** (client Node) : demander une course avec coords → le chauffeur le plus proche reçoit `rideOffered` ; refuser → le suivant le reçoit ; ne pas répondre 30 s → réattribution ; aucun → retour Pending.

## 13. Risques & points d'attention

- **Concurrence** : offre séquentielle = un seul `OfferedDriverId` à la fois → pas de double acceptation. `AcceptOffer` vérifie statut+chauffeur+expiration ; une acceptation post-timeout échoue (`OfferExpired`) car le background l'aura repassée en Pending/réoffert.
- **Idempotence du dispatcher** : ne dispatch que les courses `Pending` ; un appel sur une course déjà `Offered`/`Accepted` est ignoré.
- **Background service + scope** : créer un scope DI par tick (services scoped : repos, dispatcher). Ne pas planter la boucle sur exception (try/catch + log).
- **Disponibilité au démarrage** : un chauffeur reste `IsAvailable=false` après une course jusqu'à Complete/Cancel ; s'il se déconnecte en cours, il faudra un moyen de se remettre dispo (déjà couvert par `set-availability` manuel).
- **`TriedDriverIds` qui grossit** : borné par le nombre de chauffeurs ; réinitialisé de fait quand la course est acceptée/terminée. Acceptable.
- **Pas de coords pickup** : beaucoup de courses legacy/manuelles n'ont pas de coords → elles restent en flux manuel (normal).

## 14. Hors périmètre

Pondération par note/ETA, zones de dispatch, réassignation après annulation chauffeur en cours de course (le client re-demande), notifications push (FCM), heatmap admin, limite du nombre de réattributions.
