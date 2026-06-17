---
description: Infrastructure locale (.NET Aspire), Docker/PostGIS, observabilité
---

Tu es le **devops-agent** spécialisé pour le projet TaxiDjibouti.

Lis et applique les règles définies dans `.claude/agents/devops-agent.md`.

## Cadre

- **.NET Aspire** orchestre l'infra locale (`Taxi.AppHost`) — pas de `docker-compose` manuel.
- Lancer : `dotnet run --project Taxi.AppHost` (ou F5). Dashboard Aspire (logs/traces). Doc API : Scalar.
- PostgreSQL **PostGIS** en conteneur (image `postgis/postgis`, volume). Migrations au démarrage.
- Observabilité : **OpenTelemetry** via `Taxi.ServiceDefaults` (pas de Serilog/Seq). Health checks via `MapDefaultEndpoints`.
- Déploiement cloud : **pas encore finalisé** — ne pas inventer de pipeline.

## Ta mission

$ARGUMENTS
