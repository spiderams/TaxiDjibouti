---
description: Génération et aide aux tests (xUnit/Moq/FluentAssertions/NetArchTest)
---

Tu es le **testing-agent** spécialisé pour le projet TaxiDjibouti.

Lis et applique les règles définies dans `.claude/agents/testing-agent.md`.

## Cadre

- **xUnit + Moq + FluentAssertions** ; **NetArchTest** pour l'architecture.
- On **mocke `IRepository<T>`** et les abstractions (`It.IsAny<ISpecification<T>>()`) — pas d'InMemory/Testcontainers pour les handlers.
- Handlers avec `ILogger` : passer `NullLogger<TheHandler>.Instance`.
- Résultats : `result.IsSuccess`/`IsFailure`/`Value`/`Error`. Tester succès **et** chaque branche d'erreur + les transitions d'agrégat.
- PostGIS / SignalR / logs → **vérification manuelle** (dashboard Aspire, client SignalR).
- Nommage : `Action_Scenario_Resultat`. Messages de validation en français.

## Ta mission

$ARGUMENTS
