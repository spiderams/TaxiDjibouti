# Module Courses — Phase 1 (Cycle de course) — Design

- **Version :** 1.0
- **Date :** 2026-06-16
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Legacy de référence :** `C:\prjRecherche\TaxiDjibouti` (`Ride`, `RideService`, `RideEndpoints`)

## 1. Objectif

Migrer le **cœur métier** : le cycle de vie d'une course. Un client demande une course (prix estimé),
un chauffeur disponible l'accepte, puis la fait progresser (arrivé → en cours → terminée), avec annulation
encadrée. L'aggregate `Ride` applique lui-même la machine à états.

## 2. Découpage du module Courses

- **Phase 1 (CE spec)** : entité `Ride` + machine à états + demande/acceptation/transitions/annulation.
- **Phase 2 (ultérieure)** : notation (`Rating`) et signalement (`Report`).

## 3. Décisions validées

- **Aggregate `Ride` riche** : applique les transitions, **retourne `Result`** (pas d'exceptions de contrôle). Statut en **enum `RideStatus`**.
- `ClientId` en **string** (FK `ApplicationUser`), `DriverId` en **int?** (FK entité `Driver` du module Drivers, null tant que non accepté).
- **Coordonnées stockées** sur la course (le frontend les envoie ; utiles pour la carte/temps réel).
- **Commandes séparées par transition** (accept/arrived/start/complete/cancel), pas de `ChangeStatus` générique.
- `accept` exige un **chauffeur disponible**.
- **Annulation encadrée (règle A)** : Client annule en `Pending`/`Accepted`/`DriverArrived` ; Driver annule en `Accepted`/`DriverArrived` ; jamais après `InProgress`.
- `ClientId`/`driverUserId` viennent **du JWT**, jamais du body.
- Prix : **réutilise la logique `EstimatePrice`** (pas de duplication).

## 4. Aggregate `Ride` (Domain/Rides)

`Ride : Entity` (base : `int Id`, `CreatedAt`), setters privés.

| Propriété | Type |
|-----------|------|
| `ClientId` | string |
| `DriverId` | int? |
| `PickupAddress`, `DestinationAddress` | string |
| `PickupZone`, `DestinationZone` | string |
| `PickupLatitude`, `PickupLongitude`, `DestinationLatitude`, `DestinationLongitude` | double? |
| `EstimatedPrice` | decimal |
| `Status` | `RideStatus` |
| `AcceptedAt`, `CompletedAt` | DateTime? |

**Enum** : `RideStatus { Pending, Accepted, DriverArrived, InProgress, Completed, Cancelled }`.

**Machine à états** (chaque méthode renvoie `Result` ; garde sur le statut courant) :
- `static Ride Request(string clientId, addresses/zones/coords, decimal estimatedPrice)` → `Pending`.
- `Result Accept(int driverId)` — si `Pending` → `Accepted` (+ `DriverId`, `AcceptedAt`), sinon `RideErrors.NotPending`.
- `Result MarkArrived()` — si `Accepted` → `DriverArrived`, sinon `RideErrors.InvalidTransition`.
- `Result Start()` — si `DriverArrived` → `InProgress`, sinon `RideErrors.InvalidTransition`.
- `Result Complete()` — si `InProgress` → `Completed` (+ `CompletedAt`), sinon `RideErrors.InvalidTransition`.
- `Result CancelByClient()` — si `Pending`/`Accepted`/`DriverArrived` → `Cancelled`, sinon `RideErrors.CannotCancel`.
- `Result CancelByDriver()` — si `Accepted`/`DriverArrived` → `Cancelled`, sinon `RideErrors.CannotCancel`.

```
Pending ─accept→ Accepted ─arrived→ DriverArrived ─start→ InProgress ─complete→ Completed
              └──────── cancel (client: Pending/Accepted/DriverArrived ; driver: Accepted/DriverArrived) ────────┘
```

`RideErrors` (static) : `NotPending`, `InvalidTransition`, `CannotCancel`, `NotFound`, `DriverNotAvailable`, `NotAssignedDriver`, `NoDriverProfile`.

## 5. Réutilisation du prix

`RequestRide` calcule `EstimatedPrice` en **réutilisant la logique `EstimatePrice`** du module Tarification.
Implémentation : extraire une petite brique partagée **`IPriceEstimator`** (Application) — `Task<decimal> EstimateAsync(fromZone, toZone, ct)` — utilisée à la fois par `EstimatePriceQueryHandler` (refactor léger) et par `RequestRideHandler`. (Évite d'appeler un handler depuis un handler et garde la logique en un seul endroit.)

## 6. Opérations (CQRS) + autorisations

| Endpoint | Rôle | Logique |
|----------|------|---------|
| `POST /api/rides/request` | Client | `Ride.Request(...)` en `Pending`, prix via `IPriceEstimator` |
| `GET /api/rides/my-rides` | Client / Driver | Client → `RidesByClientSpec(userId)` ; Driver → `RidesByDriverSpec(driverId)` |
| `GET /api/rides/pending` | Driver | `PendingRidesSpec` |
| `POST /api/rides/{id}/accept` | Driver | résout le `Driver` du chauffeur (doit être **disponible**) → `ride.Accept(driver.Id)` |
| `POST /api/rides/{id}/arrived` | Driver assigné | vérifie `ride.DriverId == driver.Id` → `ride.MarkArrived()` |
| `POST /api/rides/{id}/start` | Driver assigné | → `ride.Start()` |
| `POST /api/rides/{id}/complete` | Driver assigné | → `ride.Complete()` |
| `POST /api/rides/{id}/cancel` | Client / Driver | Client (propriétaire) → `CancelByClient()` ; Driver (assigné) → `CancelByDriver()` |

`userId` extrait du JWT (`GetUserId()`). Pour les opérations chauffeur, on charge le `Driver` du user via `IRepository<Driver>` + `DriverByUserIdSpec` (réutilise le module Drivers) ; absent → `RideErrors.NoDriverProfile`.

## 7. Accès données, DTO, infra

- `IRepository<Ride>` générique + specs : `RideByIdSpec`, `RidesByClientSpec`, `RidesByDriverSpec`, `PendingRidesSpec`.
- `RideDto` : tous les champs de la course + `Status` (string) + IDs. **Enrichissement des noms client/chauffeur différé.**
- `RideConfiguration` (table `rides`, snake_case ; `Status` stocké en string via conversion enum→string ; `EstimatedPrice` numeric) + `DbSet<Ride>`.
- Migration EF `AddRides` (incrémentale).

## 8. Gestion d'erreurs (mapping HTTP)

- `RideErrors.NotFound` → 404.
- `NotPending` / `InvalidTransition` / `CannotCancel` / `DriverNotAvailable` → **409 Conflict** (état incohérent).
- `NotAssignedDriver` → **403** (ou Unauthorized) — un chauffeur non assigné tente une transition.
- `NoDriverProfile` → 404 / 400.
Via le `ResultExtensions` existant (étendre si besoin : `Conflict`→409 existe déjà ; pour 403 on peut réutiliser `Unauthorized`→401 ou ajouter `ErrorType.Forbidden`→403 — **décision : ajouter `ErrorType.Forbidden`→403** au SharedKernel, propre pour « authentifié mais pas le bon acteur »).

## 9. Stratégie de test

- **Aggregate `Ride` (TDD)** : chaque transition valide ; chaque transition invalide rejetée (Accept hors Pending, Start hors DriverArrived, Complete hors InProgress, Cancel hors fenêtre). C'est le cœur testable sans I/O.
- **Handlers (TDD)** : request (prix calculé via `IPriceEstimator` mocké), accept (dispo / indispo / pas de profil), transitions (mauvais chauffeur → Forbidden), cancel (règles A), avec `IRepository<Ride>`/`IRepository<Driver>` mockés.
- **Vérification manuelle (Scalar)** : parcours complet request → accept → arrived → start → complete ; + cas d'annulation et transitions invalides (409/403).

## 10. Hors périmètre

Notation/Signalement (Phase 2), enrichissement noms, matching proximité PostGIS (Dispatch), diffusion temps réel position (SignalR), paiement, frontend.

## 11. Risques & points d'attention

- **Dépendance au module Drivers** : `accept`/transitions chargent le `Driver` du chauffeur. Si un user `Driver` n'a pas créé son profil → `NoDriverProfile` (404). À documenter pour la vérif manuelle (créer le profil chauffeur d'abord).
- **Refactor léger de `EstimatePrice`** vers `IPriceEstimator` : ne pas casser la verticale Tarification existante (ses tests doivent rester verts).
- **`ErrorType.Forbidden`→403** : petite extension du SharedKernel (comme `Unauthorized`→401 en Phase 1 Identité).
- **Enum→string en base** : configurer la conversion EF pour lisibilité (`HasConversion<string>()`).
