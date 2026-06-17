# Module Courses — Phase 2 (Notation & Signalement) — Design

- **Version :** 1.0
- **Date :** 2026-06-16
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Legacy de référence :** `C:\prjRecherche\TaxiDjibouti` (`Rating`, `Report`, `RideService.RateRideAsync`/`ReportRideAsync`)

## 1. Objectif

Compléter le module Courses : permettre à un client de **noter** une course terminée et de **signaler** une course.
La notation **recalcule la note moyenne du chauffeur** (referme la boucle laissée ouverte dans le module Drivers).

## 2. Décisions validées

- **Notation encadrée** : le Client note **sa propre course**, **uniquement si `Completed`** et qu'elle a un chauffeur, score **1-5**, **une seule note par course** (re-noter = **upsert** : mise à jour). La note **recalcule `Driver.AverageRating`**.
- **Signalement** : le Client signale **sa propre course**, **à tout statut** ; **plusieurs signalements autorisés** par course.
- Ajout d'une méthode **`Driver.UpdateAverageRating(double)`** au module Drivers.
- `ClientId`/`rideId` viennent **du JWT + route**, jamais du body. Rôle requis : `Client`.

## 3. Entités (Domain/Rides)

**`Rating : Entity`** (base : int Id, CreatedAt), setters privés :
- `RideId` (int), `ClientId` (string), `DriverId` (int), `Score` (int), `Comment` (string?).
- `static Rating Create(int rideId, string clientId, int driverId, int score, string? comment)`.
- `void UpdateScore(int score, string? comment)` (pour l'upsert).

**`Report : Entity`** :
- `RideId` (int), `ClientId` (string), `DriverId` (int?), `Reason` (string), `Description` (string?).
- `static Report Create(int rideId, string clientId, int? driverId, string reason, string? description)`.

## 4. Opérations (CQRS) — rôle `Client`

| Endpoint | Entrée | Règles |
|----------|--------|--------|
| `POST /api/rides/{id}/rate` | `{ score, comment }` | course m'appartient (sinon `RideErrors.NotAssignedDriver`→403 — réutilisé pour « pas le propriétaire ») ; statut `Completed` (sinon `Rating.RideNotCompleted`→409) ; `DriverId` non null ; **upsert** (`RatingByRideSpec`) ; recalcule la moyenne chauffeur ; renvoie `RatingDto` |
| `POST /api/rides/{id}/report` | `{ reason, description }` | course m'appartient (sinon 403) ; tout statut ; crée un `Report` avec `ride.DriverId` ; renvoie `ReportDto` |

`RatingErrors`/`ReportErrors` (static, dans Domain/Rides) :
- `RatingErrors.RideNotCompleted` = `Error.Conflict("Rating.RideNotCompleted", "On ne peut noter qu'une course terminée.")`.
- `RatingErrors.NoDriver` = `Error.Conflict("Rating.NoDriver", "Aucun chauffeur associé à cette course.")`.
- Pour « pas le propriétaire » on réutilise `RideErrors.NotAssignedDriver` (Forbidden→403). *(Cosmétique : nom orienté chauffeur ; acceptable, déjà noté en Phase 1.)*

## 5. Logique des handlers

**`RateRideCommandHandler`** (inject `IRepository<Ride>`, `IRepository<Rating>`, `IRepository<Driver>`) :
1. Charger la course (`RideByIdSpec`) ; null → `RideErrors.NotFound`.
2. `ride.ClientId != command.ClientId` → `RideErrors.NotAssignedDriver` (403).
3. `ride.Status != Completed` → `RatingErrors.RideNotCompleted`.
4. `ride.DriverId is null` → `RatingErrors.NoDriver`.
5. Upsert : chercher `RatingByRideSpec(rideId)` ; si existe → `UpdateScore` + `UpdateAsync` ; sinon `Rating.Create(...)` + `AddAsync`.
6. Recalcul moyenne : `RatingsByDriverSpec(ride.DriverId.Value)` → moyenne des `Score` → charger le `Driver` (`DriverById` ou via repo) → `driver.UpdateAverageRating(avg)` + `UpdateAsync`.
7. Renvoyer `RatingDto`.

**`ReportRideCommandHandler`** (inject `IRepository<Ride>`, `IRepository<Report>`) :
1. Charger la course ; null → `NotFound`.
2. `ride.ClientId != command.ClientId` → `NotAssignedDriver` (403).
3. `Report.Create(ride.Id, command.ClientId, ride.DriverId, command.Reason, command.Description)` + `AddAsync`.
4. Renvoyer `ReportDto`.

> Pour charger le `Driver` à l'étape 6 : `IRepository<Driver>` + une spec par Id. On peut réutiliser une `EntityByIdSpec` ou ajouter `DriverByIdSpec(int)`. **Décision : ajouter `DriverByIdSpec(int id)`** dans `Taxi.Application.Drivers` (petite spec, cohérente).

## 6. Validation

- `RateRideCommandValidator` : `Score` `InclusiveBetween(1,5)`.
- `ReportRideCommandValidator` : `Reason` `NotEmpty`.
- Exécutée par le décorateur de validation existant.

## 7. Accès données, DTO, infra

- `IRepository<Rating>`, `IRepository<Report>` (génériques, déjà disponibles).
- Specs : `RatingByRideSpec(int rideId)` (unicité/upsert), `RatingsByDriverSpec(int driverId)` (moyenne), `DriverByIdSpec(int id)`.
- `RatingDto` (Id, RideId, ClientId, DriverId, Score, Comment, CreatedAt), `ReportDto` (Id, RideId, ClientId, DriverId, Reason, Description, CreatedAt).
- `RatingConfiguration` (table `ratings`, **index unique sur `ride_id`**, Comment maxlen), `ReportConfiguration` (table `reports`, Reason maxlen) + `DbSet<Rating>`/`DbSet<Report>`.
- Migration EF `AddRatingsAndReports`.

## 8. Modification du module Drivers

Ajout à l'entité `Driver` (Domain/Drivers) : `public void UpdateAverageRating(double average) => AverageRating = average;`. Petit ajout, ne casse rien (le champ existait déjà). Test unitaire de la méthode.

## 9. Stratégie de test (TDD)

- **Entités** : `Rating.Create`/`UpdateScore`, `Report.Create`, `Driver.UpdateAverageRating`.
- **`RateRideCommandHandler`** : pas propriétaire → 403 ; pas terminée → 409 ; upsert (création puis mise à jour) ; recalcul de la moyenne (le `Driver` reçoit la moyenne attendue, vérifié via `UpdateAsync` + valeur).
- **`ReportRideCommandHandler`** : pas propriétaire → 403 ; création OK.
- **Vérification manuelle (Scalar)** : compléter une course (parcours Phase 1) → la noter (200) → re-noter (200, mise à jour) → vérifier `GET /api/drivers/me` du chauffeur montre `averageRating` mis à jour → signaler une course (200) → noter une course non terminée → 409 → noter la course d'un autre → 403.

## 10. Hors périmètre

Modération/consultation admin des signalements (→ Administration), affichage des notes côté frontend, notation du client par le chauffeur (bidirectionnel).

## 11. Risques & points d'attention

- **Recalcul de moyenne** : recalculé à chaque note (charge toutes les notes du chauffeur). Volume faible en MVP → acceptable ; optimisation (moyenne incrémentale) non nécessaire (YAGNI).
- **Concurrence** : deux notes simultanées sur la même course → l'index unique sur `ride_id` protège ; l'upsert lit-puis-écrit (fenêtre de course possible mais bénigne en MVP).
- **Réutilisation de `RideErrors.NotAssignedDriver`** pour « pas le propriétaire » : 403 correct, nom cosmétiquement orienté chauffeur (déjà acté en Phase 1).
