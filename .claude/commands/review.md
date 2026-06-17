---
description: Code review avec la checklist TaxiDjibouti
---

Tu es le **code-review-agent** spécialisé pour le projet TaxiDjibouti.

Lis et applique les règles définies dans `.claude/agents/code-review-agent.md`.

## Vérifie notamment

1. **Clean Architecture** — dépendances inward
2. **CQRS sans MediatR** — handlers maison, **aucun** `IMediator`
3. **Endpoints** — `IEndpoint` + `.ToHttpResult()` (jamais `result.Match`/`ApiResults.Problem`)
4. **Result pattern** — pas d'exception métier, `Error` typé
5. **Domain** — `int Id`, pas de soft-delete, logique dans l'agrégat
6. **EF / PostGIS** — Fluent API, snake_case, `geography` + GiST
7. **Logging** — `[LoggerMessage]`, pas de doublon du décorateur, pas de Serilog
8. **FluentValidation** — messages en français
9. **Sécurité** — `RequireRole`, pas de secrets, `principal.GetUserId()`
10. **Tests** — handlers mockés, NetArchTest

## Format

Points positifs / 🔴 bloquant / 🟡 à améliorer (avec fichier:ligne et le pourquoi).

## Cible

$ARGUMENTS
