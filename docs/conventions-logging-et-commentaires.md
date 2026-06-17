# Conventions — Logs & commentaires

## Logs
- **Ne jamais logguer le cycle de vie d'un handler** (début/succès/échec) : le `LoggingDecorator`
  (commande ET requête) le fait automatiquement pour tous les handlers, via `RequestLog` (source-generated).
- **Logguer un événement MÉTIER** uniquement quand il apporte une info que le décorateur ignore :
  décision, branche importante, sécurité, effet de bord. Exemples : réutilisation de token détectée,
  chauffeur rendu indisponible, offre faite à un chauffeur, moyenne recalculée.
- **Pattern** (performant, source-generated) :
  ```csharp
  internal sealed partial class MonHandler(..., ILogger<MonHandler> logger) : ICommandHandler<...>
  {
      // ... après l'action métier ...
      LogQuelqueChose(logger, id);

      [LoggerMessage(Level = LogLevel.Information, Message = "Quelque chose est arrivé pour {Id}")]
      private static partial void LogQuelqueChose(ILogger logger, int id);
  }
  ```
- **Niveaux** : `Information` = événement normal notable ; `Warning` = anormal récupérable (échec attendu,
  sécurité) ; `Error` = exception/incident. Mettre un `if (logger.IsEnabled(LogLevel.X))` UNIQUEMENT
  quand le message implique une construction coûteuse (`string.Join`, concat).
- **Sink** : OpenTelemetry d'Aspire (dashboard Aspire). Pas de Serilog.

## Commentaires XML
- Tout type public + interface porte un `/// <summary>` **multi-ligne** en français expliquant son INTENTION
  (à quoi il sert, pourquoi il existe), pas sa syntaxe.
- Méthodes publiques non triviales : `<summary>` + `<param>`/`<returns>` si utile.
- Ne pas commenter le trivial (getters auto, ctors triviaux) ni le code généré (migrations).
