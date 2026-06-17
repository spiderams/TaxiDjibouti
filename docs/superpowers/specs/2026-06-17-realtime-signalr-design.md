# Module Temps réel (SignalR) — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Legacy de référence :** `C:\prjRecherche\TaxiDjibouti\TaxiDjibouti.Api\Hubs\RideHub.cs`

## 1. Objectif

Porter le **suivi temps réel** du legacy (dernière feature backend non migrée) et l'enrichir :
1. **Position du chauffeur en direct** — le chauffeur diffuse sa position pendant une course ; poussée au client, à la course et aux admins.
2. **Push des changements de statut de course** — chaque transition (demande/acceptation/arrivée/départ/fin/annulation) est poussée en temps réel (au-delà du legacy).
3. **Persistance de la dernière position** du chauffeur (au-delà du legacy ; prépare le futur Dispatch).

## 2. Décisions validées

- **In-process SignalR** (`AddSignalR`), **pas** d'Azure SignalR (YAGNI, Phase 2+ pour le scale).
- **Le hub ne touche PAS au `DbContext`** (contrairement au legacy). Deux sens séparés :
  - **Entrant** (chauffeur → serveur → clients) : géré dans le hub, qui délègue la validation/persistance à un **command handler** Application puis diffuse.
  - **Sortant** (commande REST → clients) : via l'abstraction **`IRealtimeNotifier`** (Application), implémentée en Web.Api avec `IHubContext<RideHub>`.
- **Persistance** : dernière position stockée sur l'entité `Driver` (pas de table dédiée) → 1 migration.
- **Auth WebSocket** : JWT lu en **query string** (`access_token`) via l'event JwtBearer `OnMessageReceived`.
- **`ClientId` est un `string` (GUID)** chez nous (int dans le legacy) → les groupes `Client_{clientId}` utilisent le string.

## 3. Le Hub — `Taxi.Web.Api/Realtime/RideHub.cs`

- `[Authorize]`, mappé sur `/hubs/ride`.
- **Groupes** (portés du legacy) avec contrôle d'accès :
  - `JoinDriversGroup()` → groupe `Drivers` (tout chauffeur connecté).
  - `JoinAdminsGroup()` → groupe `Admins` (refusé si le rôle ≠ Admin).
  - `JoinClientGroup(string clientId)` → groupe `Client_{clientId}` (refusé si non-Admin et `clientId` ≠ user courant).
  - `JoinRideGroup(int rideId)` → groupe `Ride_{rideId}` (refusé si l'utilisateur n'a pas accès à la course : admin = ok ; client = sa course ; chauffeur = course assignée).
- L'accès à la course pour `JoinRideGroup` utilise `IRepository<Ride>` (charger la course + vérifier `ClientId`/`DriverId`→`UserId`), **pas** le `DbContext`.
- **`SendDriverLocation(DriverLocationDto location)`** : le hub
  1. récupère l'`userId` du chauffeur depuis `Context.User` ;
  2. appelle `ICommandHandler<UpdateDriverLocationCommand, DriverLocationBroadcast>` (validation + persistance) ;
  3. si échec (`Result` en erreur) → `throw new HubException(error.Description)` ;
  4. si succès → diffuse `driverLocationUpdated` (le `DriverLocationBroadcast`) aux groupes `Client_{broadcast.ClientId}`, `Ride_{broadcast.RideId}`, `Admins`.

**DTO entrant** (`Taxi.Web.Api/Realtime/DriverLocationDto.cs`) :
```csharp
public sealed record DriverLocationDto(int RideId, double Latitude, double Longitude, double? Heading, double? Speed);
```

L'`userId` du chauffeur est résolu côté serveur via `Context.User` (jamais transmis par le client) — sécurité.

## 4. Position entrante — command handler (TDD)

`Taxi.Application/Realtime/UpdateDriverLocation/` :

```csharp
public sealed record UpdateDriverLocationCommand(
    string DriverUserId, int RideId, double Latitude, double Longitude, double? Heading, double? Speed)
    : ICommand<DriverLocationBroadcast>;

public sealed record DriverLocationBroadcast(
    int RideId, string ClientId, int DriverId, double Latitude, double Longitude,
    double? Heading, double? Speed, DateTime SentAt);
```

`UpdateDriverLocationCommandHandler` (injecte `IRepository<Driver>`, `IRepository<Ride>`) :
1. charge le `Driver` par `UserId` (spec existante `DriverByUserIdSpec`/équivalent) → sinon `Result.Failure(RealtimeErrors.DriverNotFound)` ;
2. charge la `Ride` par id → sinon `RideErrors.NotFound` ;
3. si `ride.DriverId != driver.Id` → `RealtimeErrors.RideNotAssigned` ;
4. si `ride.Status` ∈ {Completed, Cancelled} → `RealtimeErrors.RideNotActive` ;
5. `driver.UpdateLocation(latitude, longitude)` + `await drivers.UpdateAsync(driver, ct)` ;
6. renvoie `new DriverLocationBroadcast(ride.Id, ride.ClientId, driver.Id, lat, lon, heading, speed, DateTime.UtcNow)`.

`RealtimeErrors` (`Taxi.Domain` ou `Taxi.Application.Realtime`) : `DriverNotFound` (NotFound), `RideNotAssigned` (Forbidden), `RideNotActive` (Validation/Conflict).

> Le handler est l'endroit testable. Le hub n'est qu'un transport.

## 5. Persistance — entité `Driver` + migration

- Ajout sur `Driver` (`Taxi.Domain/Drivers/Driver.cs`) : `double? LastLatitude`, `double? LastLongitude`, `DateTime? LastLocationAt` (setters privés) + méthode :
```csharp
public void UpdateLocation(double latitude, double longitude)
{
    LastLatitude = latitude;
    LastLongitude = longitude;
    LastLocationAt = DateTime.UtcNow;
}
```
- Config EF (`DriverConfiguration`) : colonnes nullable (rien d'obligatoire).
- Migration **`AddDriverLocation`** (3 colonnes nullable → pas de rupture des données existantes).

## 6. Push des statuts — `IRealtimeNotifier` + impl Web.Api

**Abstraction** (`Taxi.Application/Realtime/IRealtimeNotifier.cs`, **sans** dépendance SignalR) :
```csharp
public interface IRealtimeNotifier
{
    Task RideStatusChangedAsync(int rideId, string clientId, int? driverId, string status, CancellationToken cancellationToken);
    Task NewPendingRideAsync(int rideId, CancellationToken cancellationToken);
}
```

**Implémentation** `Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs` (injecte `IHubContext<RideHub>`) :
- `RideStatusChangedAsync` → pousse l'event `rideStatusChanged` (payload `{ rideId, status, driverId }`) aux groupes `Client_{clientId}`, `Ride_{rideId}`, `Admins`.
- `NewPendingRideAsync` → pousse `newPendingRide` (payload `{ rideId }`) au groupe `Drivers`.

**Câblage des handlers Courses existants** (appel **après** persistance réussie, dans le `Handle` qui retourne `Result` succès) :
| Handler | Appel |
|---------|-------|
| `RequestRideCommandHandler` | `NewPendingRideAsync(ride.Id)` |
| `AcceptRideCommandHandler` | `RideStatusChangedAsync(ride.Id, ride.ClientId, ride.DriverId, "Accepted")` |
| transitions (Arrived/Start/Complete) | `RideStatusChangedAsync(...)` avec le statut courant |
| `CancelRideCommandHandler` | `RideStatusChangedAsync(...)` avec `"Cancelled"` |

Ces handlers reçoivent `IRealtimeNotifier` en dépendance supplémentaire (injection constructeur).

## 7. Auth WebSocket (JWT query string)

Dans `AddIdentityInfrastructure` (`Taxi.Infrastructure/Identity/DependencyInjection.cs`), sur `AddJwtBearer(options => …)`, ajouter :
```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/ride"))
            context.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

## 8. Câblage `Program.cs`

- Services : `builder.Services.AddSignalR();` + `builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();`
- Pipeline : `app.MapHub<RideHub>("/hubs/ride");` (après `MapEndpoints()`).

## 9. Architecture (placement des fichiers)

```
Taxi.Domain/Drivers/Driver.cs                                 — modifié (Last* + UpdateLocation)
Taxi.Application/Realtime/IRealtimeNotifier.cs                — créé
Taxi.Application/Realtime/UpdateDriverLocation/{Command,Broadcast,Handler}.cs — créés
Taxi.Application/Realtime/RealtimeErrors.cs                   — créé
Taxi.Application/Rides/.../*CommandHandler.cs                 — modifiés (appel IRealtimeNotifier)
Taxi.Infrastructure/Persistence/Configurations/DriverConfiguration.cs — modifié (3 colonnes)
Taxi.Infrastructure/Persistence/Migrations/*AddDriverLocation*           — généré
Taxi.Infrastructure/Identity/DependencyInjection.cs          — modifié (OnMessageReceived)
Taxi.Web.Api/Realtime/RideHub.cs                             — créé
Taxi.Web.Api/Realtime/DriverLocationDto.cs                   — créé
Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs             — créé
Taxi.Web.Api/Program.cs                                      — modifié (AddSignalR, MapHub, DI notifier)
```

## 10. Découpage en 2 phases (pour le plan)

- **Phase 1** — Position : entité `Driver`+migration, `UpdateDriverLocationCommand`/handler (TDD), `RideHub` (groupes + `SendDriverLocation`), JWT query string, câblage `AddSignalR`/`MapHub`. Vérif manuelle (client SignalR : position diffusée + persistée).
- **Phase 2** — Push statuts : `IRealtimeNotifier` + `SignalRRealtimeNotifier`, modif des handlers Courses, group `Drivers`/`newPendingRide`. Vérif manuelle (transition REST → `rideStatusChanged`).

## 11. Stratégie de test

- **`UpdateDriverLocationCommandHandler` (TDD)** : `IRepository<Driver>`/`<Ride>` mockés → cas succès (persistance via `UpdateAsync` vérifiée + payload correct), chauffeur introuvable, course non assignée, course terminée/annulée.
- **Handlers Courses** : mock `IRealtimeNotifier` → vérifier l'appel sur les transitions (sans casser les tests existants : ajouter le mock au setup).
- **Hub + `SignalRRealtimeNotifier` + JWT query string** : **vérification manuelle** (testable via un client SignalR JS/.NET).
- **Vérif manuelle** :
  1. Se connecter au hub avec `?access_token=<JWT chauffeur>` ; `JoinRideGroup(rideId)` ; `SendDriverLocation` → le client (connecté en `Client_{id}`) reçoit `driverLocationUpdated`, et `Driver.LastLatitude/Longitude/LastLocationAt` est mis à jour en base.
  2. Sécurité : un client qui tente `JoinClientGroup` d'un autre id → pas ajouté (pas de broadcast reçu) ; `SendDriverLocation` sur une course non assignée → `HubException`.
  3. Déclencher une transition via REST (`POST /api/rides/{id}/accept`) → les abonnés reçoivent `rideStatusChanged`. `POST /api/rides/request` → le groupe `Drivers` reçoit `newPendingRide`.

## 12. Hors périmètre

- **CORS navigateur** (réglé à la phase frontend — un client SignalR .NET/CLI suffit pour la vérif).
- **Azure SignalR** (Phase 2+ scale).
- **Dispatch / matching proximité** (module séparé ; la position persistée le prépare).
- Historique/trace des positions (on ne garde que la dernière).

## 13. Risques & points d'attention

- **Ordre d'appel du notifier** : appeler `IRealtimeNotifier` **après** la persistance réussie du handler (sinon on notifie un état non commité). Ne pas faire échouer la commande si la notification échoue (best-effort ; envisager un try/catch loggé — à préciser au plan).
- **Scope DI** : `IHubContext<RideHub>` est singleton-friendly ; `IRealtimeNotifier` peut être scoped. Le hub résout le command handler en scoped (SignalR crée un scope par invocation de méthode hub).
- **`Driver.UpdateLocation` à chaque ping** : écriture DB fréquente. Acceptable au volume MVP ; optimisable plus tard (throttling, ou écriture en cache). À noter, pas à optimiser maintenant (YAGNI).
- **Sécurité du `userId`** : toujours pris de `Context.User`, jamais du payload client.
