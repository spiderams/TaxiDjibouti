# Module Dispatch — Phase 1 (moteur de matching PostGIS) — Design

- **Version :** 1.0
- **Date :** 2026-06-17
- **Statut :** Design validé — à transformer en plan d'implémentation
- **Projet :** `C:\prjRecherche\Taxi` (refonte .NET 10, monolithe modulaire)
- **Au-delà du legacy** : le legacy n'avait aucun dispatch (acceptation manuelle via `/api/rides/pending`).

## 1. Objectif

Poser le **moteur de proximité géospatial** : à partir d'un point de prise en charge, retourner les chauffeurs
**disponibles** les plus proches, triés par distance. C'est la fondation réutilisable du futur flux d'auto-assignation
(Phase 2). Cette phase n'altère **pas** la machine à états des courses.

## 2. Décisions validées

- **Objectif global = auto-assignation du chauffeur le plus proche**, livré en 2 phases. Cette spec = **Phase 1** (moteur de matching). Phase 2 = flux d'offre/timeout/réattribution (spec séparée).
- **PostGIS via colonne `geography(Point, 4326)` + index GiST + NetTopologySuite** (choix validé ; conforme au doc d'archi). Pas de calcul Haversine maison.
- **`IDriverLocator`** : abstraction en Application (la requête spatiale a besoin du `DbContext` + EF NTS), implémentée en Infrastructure — **même pattern que `IUserDirectory`**, la couche Application reste sans dépendance EF.
- **Filtre de fraîcheur** : on ne considère que les chauffeurs dont la position date de moins de N minutes (défaut **10 min**, configurable).
- **Endpoint de vérification/diagnostic** en rôle Admin ; la Phase 2 appellera `IDriverLocator` directement (pas l'endpoint).

## 3. Infrastructure PostGIS

- **AppHost** (`Taxi.AppHost/AppHost.cs`) : remplacer l'image Postgres par une image **PostGIS** (`postgis/postgis`, tag aligné sur la version majeure Postgres courante). ⚠️ Le **volume de données Taxi devra être recréé** (dev only ; isolé des autres projets de l'utilisateur — vérifier le nom du volume avant suppression).
- **Extension** : `modelBuilder.HasPostgresExtension("postgis")` dans `AppDbContext.OnModelCreating` → la migration émet `CREATE EXTENSION IF NOT EXISTS postgis`.
- **NetTopologySuite** : configurer Npgsql + EF pour NTS. Deux côtés à câbler :
  - la **source de données Npgsql** doit activer le plugin NTS (mapping des types geography) ;
  - **EF** : `UseNetTopologySuite()` sur les options Npgsql.
  Avec Aspire `AddNpgsqlDbContext`, le point d'intégration exact (data source vs `configureDbContextOptions`) sera **résolu et pinné dans le plan** (c'est le risque technique principal de la phase). Packages : `NetTopologySuite` + `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`.

## 4. Entité `Driver` — position géospatiale

Remplacer les doubles `LastLatitude`/`LastLongitude` (ajoutés en temps réel Phase 1) par un point géographique :

```csharp
public Point? LastLocation { get; private set; }       // geography(Point, 4326)
public DateTime? LastLocationAt { get; private set; }   // conservé

[NotMapped] public double? LastLatitude => LastLocation?.Y;
[NotMapped] public double? LastLongitude => LastLocation?.X;

public void UpdateLocation(double latitude, double longitude)
{
    LastLocation = new Point(longitude, latitude) { SRID = 4326 };
    LastLocationAt = DateTime.UtcNow;
}
```

- `Point` = `NetTopologySuite.Geometries.Point` → **NTS devient dépendance de Domain (+ Application)**. Acceptable (lib standard de modélisation géo).
- Le **broadcast temps réel** utilise les coordonnées du message entrant (`command.Latitude/Longitude`), pas l'entité → inchangé. Le **test Phase 1 temps réel** asserte `driver.LastLatitude/LastLongitude` → fonctionne via les propriétés dérivées.
- **Config EF** (`DriverConfiguration`) : `LastLocation` en `geography (Point)`, **index GiST** : `builder.HasIndex(d => d.LastLocation).HasMethod("gist")`.
- **Migration `AddDriverGeolocation`** : `CREATE EXTENSION postgis` (via `HasPostgresExtension`), drop colonnes `last_latitude`/`last_longitude`, add `last_location geography(Point,4326)` + index GiST.

## 5. Moteur de matching — `IDriverLocator`

**Abstraction** (`Taxi.Application/Dispatch/IDriverLocator.cs`) :
```csharp
public interface IDriverLocator
{
    Task<IReadOnlyList<NearbyDriver>> FindNearestAsync(
        double latitude, double longitude, double radiusMeters, int max, CancellationToken cancellationToken);
}

public sealed record NearbyDriver(
    int DriverId, string UserId, double DistanceMeters,
    double Latitude, double Longitude, string VehicleType);
```

**Implémentation** (`Taxi.Infrastructure/Dispatch/DriverLocator.cs`, injecte `AppDbContext`) :
1. construit le point de prise en charge `new Point(longitude, latitude) { SRID = 4326 }` ;
2. requête sur `Drivers` filtrant :
   - `IsAvailable == true`,
   - `LastLocation != null`,
   - **fraîcheur** : `LastLocationAt >= now - freshnessWindow` (défaut 10 min),
   - `LastLocation.IsWithinDistance(pickup, radiusMeters)` → traduit en **`ST_DWithin`** (utilise l'index GiST) ;
3. trie par `LastLocation.Distance(pickup)` → **`ST_Distance`** (en **mètres** car `geography`) ;
4. `Take(max)` ;
5. projette en `NearbyDriver` (distance = `.Distance(pickup)`).

La fenêtre de fraîcheur est une constante interne à l'implémentation (configurable plus tard ; YAGNI sur l'exposition).

## 6. Query CQRS + endpoint

**Query** (`Taxi.Application/Dispatch/FindNearestDrivers/`) :
```csharp
public sealed record FindNearestDriversQuery(double Lat, double Lon, double RadiusMeters, int Max)
    : IQuery<IReadOnlyList<NearbyDriver>>;
```
Handler : appelle `IDriverLocator.FindNearestAsync(...)` et renvoie la liste.

**Endpoint** : `GET /api/dispatch/nearest-drivers?lat={lat}&lon={lon}&radius={radiusMeters}&max={max}`
- rôle **Admin** (`RequireRole(RoleNames.Admin)`), tag `Dispatch` (ajout `Tags.Dispatch`).
- defaults : `radius = 5000` (m), `max = 10` si non fournis.
- convention OpenAPI (`WithName`/`WithSummary`).

## 7. Architecture (placement)

```
Taxi.AppHost/AppHost.cs                                          — modifié (image PostGIS)
Taxi.Domain/Drivers/Driver.cs                                    — modifié (Point LastLocation + dérivées)
Taxi.Infrastructure/Persistence/AppDbContext.cs                  — modifié (HasPostgresExtension postgis)
Taxi.Infrastructure/Persistence/Configurations/DriverConfiguration.cs — modifié (geography + GiST)
Taxi.Infrastructure/Persistence/Migrations/*AddDriverGeolocation*      — généré
Taxi.Infrastructure/<wiring NTS>                                 — modifié (data source + UseNetTopologySuite)
Taxi.Application/Dispatch/IDriverLocator.cs, NearbyDriver        — créés
Taxi.Application/Dispatch/FindNearestDrivers/{Query,Handler}.cs  — créés
Taxi.Infrastructure/Dispatch/DriverLocator.cs (+ DI)            — créé
Taxi.Web.Api/Modules/Dispatch/DispatchEndpoints.cs + Tags.Dispatch — créés/modifié
Directory.Packages.props                                         — modifié (packages NTS)
```

## 8. Stratégie de test

- **`FindNearestDriversQueryHandler`** : mock `IDriverLocator` → vérifie le passage des paramètres et le renvoi de la liste (test unitaire léger).
- **Correction spatiale (PostGIS/NTS)** : non mockable utilement → **vérification manuelle** contre une vraie base PostGIS (comme `IUserDirectory`/`UserDirectory`).
- **Vérif manuelle** :
  1. Créer 2-3 chauffeurs, les rendre disponibles, leur envoyer des positions connues (via le hub temps réel `SendDriverLocation`, ou en base) — p.ex. un proche du centre-ville de Djibouti, un plus loin, un indisponible.
  2. `GET /api/dispatch/nearest-drivers?lat=11.588&lon=43.145&radius=5000&max=10` (token Admin) → la liste renvoie les chauffeurs **disponibles** triés par `DistanceMeters` croissante, exclut l'indisponible et ceux hors rayon, et exclut une position **périmée** (> 10 min).
  3. Vérifier que `DistanceMeters` est cohérent (ordre de grandeur en mètres).

## 9. Risques & points d'attention

- **Câblage NTS + Aspire** : `AddNpgsqlDbContext` construit la source de données en interne ; activer le plugin NTS sur cette source ET sur EF est le point délicat. À résoudre/pinner dans le plan, avec build de validation.
- **Recréation du volume** : changer d'image (postgis) peut nécessiter de recréer le volume Taxi. Vérifier le nom exact du volume et n'effacer que celui-ci (les autres projets de l'utilisateur partagent la même instance Docker).
- **`geography` vs `geometry`** : on utilise **`geography`** (SRID 4326) → `ST_Distance`/`ST_DWithin` renvoient/raisonnent en **mètres** directement, pas de conversion degrés→mètres. Important pour des distances correctes à Djibouti.
- **Perte des colonnes lat/lon** : la migration supprime `last_latitude`/`last_longitude` (remplacées par `last_location`). Sans conséquence (données de dev ; volume probablement recréé).
- **NTS dans le Domain** : `Driver` référence `NetTopologySuite.Geometries.Point`. Acceptable, mais c'est une dépendance externe dans le Domain — assumée pour le géospatial.

## 10. Hors périmètre (Phase 2)

Auto-assignation, état d'offre (`OfferedDriverId`/`OfferExpiresAt`), timeout + réattribution au suivant, endpoint de refus, background service de réattribution, notifications SignalR d'offre, cas « aucun chauffeur trouvé ».
