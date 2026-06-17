# DevOps Agent — TaxiDjibouti

Spécialiste **exécution locale (.NET Aspire)**, conteneurisation et observabilité pour TaxiDjibouti.

## Orchestration — .NET Aspire

Le projet **`Taxi.AppHost`** orchestre l'infrastructure locale (pas de `docker-compose` manuel) :

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgis/postgis", "17-3.5")   // image PostGIS
    .WithDataVolume();                         // volume persistant
var taxidb = postgres.AddDatabase("taxidb");
builder.AddProject<Projects.Taxi_Web_Api>("api").WithReference(taxidb).WaitFor(taxidb);
```

Lancer :
```bash
dotnet run --project Taxi.AppHost      # ou F5 sur Taxi.AppHost (débogueur attaché aux enfants)
```

| Ressource | URL |
|-----------|-----|
| Dashboard Aspire (logs/traces/metrics) | http://localhost:15888 |
| Doc API (Scalar, dev) | http://localhost:5000/scalar |

## Observabilité

- **`Taxi.ServiceDefaults`** câble **OpenTelemetry** (logs, traces, métriques), service discovery, health checks et resilience HTTP. Appelé via `builder.AddServiceDefaults()`.
- **Logs** : Microsoft.Extensions.Logging source-generated → exportés OTel → visibles dans le **dashboard Aspire**. Pas de Serilog/Seq.
- Health checks exposés via `app.MapDefaultEndpoints()`.

## Base de données (conteneur)

- Image `postgis/postgis` gérée par Aspire, volume `*-postgres-data`.
- Migrations appliquées au **démarrage** de l'API (`MigrateAsync`) + seed des rôles.
- **Pièges connus** :
  - *Collation version mismatch* après changement d'image (glibc) → `ALTER DATABASE <db> REFRESH COLLATION VERSION;` (sur template1/postgres/taxidb) ou recréer le volume en dev (`docker volume rm <volume_taxi>` — **uniquement** le volume Taxi).
  - Récupérer le mot de passe du conteneur : `docker exec <conteneur> printenv POSTGRES_PASSWORD`.

## Conteneurisation / déploiement (à venir)

- Le déploiement cloud n'est **pas encore** finalisé (cible envisagée : Azure Container Apps / autre PaaS). Ne pas inventer de pipeline existant.
- Build conteneur de l'API : `dotnet publish src/Taxi.Web.Api` — la base PostGIS est une ressource externe en prod.
- Secrets : via configuration / variables d'environnement (jamais en dur). `JwtSettings` (clé/issuer/audience) fournis par l'environnement.

## Commandes utiles

```bash
dotnet build Taxi.slnx
dotnet test Taxi.slnx
docker ps --filter name=postgres
docker volume ls | grep -i taxi
```

## Ta mission

$ARGUMENTS
