# Wave Dispatch — Conception (premier-arrivé-gagne par vagues parallèles)

**Date :** 2026-06-21
**Statut :** Conception validée — prêt pour le plan d'implémentation
**Auteur :** Samatar (avec assistance IA)

---

## 1. Problème à résoudre

**Douleur #1 identifiée :** l'attente client est trop longue avant qu'un chauffeur accepte une course.

**Cause racine (dans le code actuel) :** le `RideDispatcher` offre la course à **un seul chauffeur à la fois**, avec un TTL de 30 secondes par offre. Si 3 chauffeurs ignorent l'offre successivement, le client a déjà attendu ~90 secondes avant qu'on sollicite le 4e chauffeur. En heure de pointe, une course peut traîner plusieurs minutes.

**Objectif :** réduire drastiquement le délai d'acceptation en offrant la course à **plusieurs chauffeurs en parallèle** (« vague »), le premier à accepter gagnant la course.

**Contexte produit :** vrai produit à lancer à Djibouti (parc de chauffeurs limité, volume modéré — dizaines de courses simultanées en pointe, pas des dizaines de milliers). La robustesse et l'expérience client priment sur la sophistication algorithmique. Pas de sur-ingénierie.

---

## 2. Décisions de conception (validées)

| Décision | Choix retenu | Raison |
|----------|--------------|--------|
| Stratégie de matching | **Wave dispatch** (vagues parallèles) | Tue l'attente longue sans inonder tout le parc |
| Règle de conflit | **Premier arrivé gagne** | Simple, transparent, juste pour le client (course prise au plus vite) |
| Taille de vague | **`min(3, candidats disponibles)`** | Adaptatif : ne jamais offrir à plus de chauffeurs qu'il n'y en a |
| Verrou de concurrence | **Optimiste (`xmin` PostgreSQL)** | Suffisant pour le volume ; pas de Redis ni de lock distribué |
| Mémoire du va-et-vient | **`TriedDriverIds` sur le `Ride` (en base)** | Dispatcher sans état ; robuste au redémarrage |
| Ordre des vagues | **Par proximité** (existant) | Le scoring rating/historique est reporté au palier 2 |

---

## 3. Architecture du va-et-vient

Le `RideDispatcher` reste **sans état** : à chaque appel, il recalcule la prochaine vague à partir de `TriedDriverIds`. Deux déclencheurs alimentent le cycle :

1. **Déclenchement immédiat (synchrone)** — quand un événement survient :
   - Client demande une course → `RequestRideCommandHandler` → `DispatchAsync`
   - Un chauffeur décline → `DeclineOfferCommandHandler` → `DispatchAsync`
2. **Job de fond (`OfferTimeoutService`)** — filet de sécurité, boucle toutes les 5 s :
   - détecte les vagues **expirées** (silence total : personne n'a répondu)
   - les remet en `Pending` et relance `DispatchAsync`

```
Vague 1 offerte (N chauffeurs, TTL 15s)
        │
        ├──► un chauffeur DÉCLINE   → DeclineHandler relance immédiatement (vague suivante)
        │
        └──► PERSONNE ne répond     → le JOB détecte l'expiration après 15s et relance
```

**Point clé :** le job ne « fait pas tourner les vagues ». Il ne rattrape que les timeouts. Le va-et-vient normal est porté par les déclenchements immédiats. La mémoire des vagues passées est portée par `TriedDriverIds` (en base), pas par le job ni le dispatcher.

---

## 4. Modèle d'état du `Ride`

### Avant (offre solo)
```
OfferedDriverId  (int?)        ← un seul chauffeur ciblé
OfferExpiresAt   (DateTime?)   ← expiration de cette offre
TriedDriverIds   (int[])       ← chauffeurs déjà sollicités
```

### Après (offre multi-chauffeurs / vague)
```
OfferedDriverIds (int[])       ← les N chauffeurs de la VAGUE en cours
OfferExpiresAt   (DateTime?)   ← expiration commune de la vague
TriedDriverIds   (int[])       ← inchangé : tous ceux déjà sollicités (toutes vagues)
```

### Méthodes de transition (Domain)
- **`OfferWave(driverIds, expiresAt)`** : `Pending → Offered`, enregistre la vague, ajoute les ids à `TriedDriverIds`.
- **`AcceptOffer(driverId)`** : réussit uniquement si `driverId ∈ OfferedDriverIds` **ET** `Status == Offered` **ET** non expiré. Passe à `Accepted`. Le passage de statut sert de verrou « premier gagne » (voir §6).
- **`DeclineOffer(driverId)`** : retire `driverId` de la vague courante. Si la vague devient vide → `ReturnToPending()`.

Le reste de la machine à états (`Accepted → DriverArrived → InProgress → Completed`, annulations) **ne change pas**.

---

## 5. Algorithme du `RideDispatcher`

```
DispatchAsync(rideId):
    ride = charger le ride
    si ride.Status != Pending : STOP   (déjà pris ou annulé)

    si pas de GPS pickup : notifier les admins (flux manuel)   ← inchangé

    # 1. Chercher les chauffeurs proches NON encore essayés
    candidats = locator.FindNearest(pickup, rayon=5km, max=20)
    libres    = candidats.filtrer(c => c.DriverId NOT IN ride.TriedDriverIds)

    si libres est vide :
        notifier les admins (tous essayés / aucun dispo)   ← garde-fou
        STOP

    # 2. Prendre les N prochains = UNE VAGUE
    tailleVague = min(3, libres.count)
    vague = libres.prendre(tailleVague)         # triés par distance

    # 3. Offrir à toute la vague d'un coup
    expiration = maintenant + TTL                # 15s
    ride.OfferWave(vague.driverIds, expiration)  # → Offered, ajoute à TriedDriverIds
    sauvegarder ride

    # 4. Notifier chaque chauffeur de la vague (SignalR)
    pour chaque chauffeur de vague :
        notifier.RideOffered(chauffeur.UserId, rideId, expiration)
```

**Constantes :** `TailleVagueMax = 3`, `OfferTtl = 15s`, `RadiusMeters = 5000`, `MaxCandidates = 20`.
La taille de vague étant `min(3, candidats)`, on n'offre jamais à plus de chauffeurs qu'il n'y en a de disponibles.

---

## 6. Verrou « premier arrivé gagne » (optimistic concurrency)

**Scénario à risque (heure de pointe) :** A, B, C reçoivent la même offre et cliquent « accepter » dans la même seconde. Trois transactions veulent passer le même `Ride` en `Accepted`.

**Solution :** optimistic concurrency natif EF Core + PostgreSQL via le token de version `xmin` (aucune colonne supplémentaire, pas de changement de schéma pour le token lui-même).

```
A accepte :
  1. Lire le ride            → Status=Offered, xmin=42
  2. ride.AcceptOffer(A)     → validations OK → Status=Accepted
  3. SaveChanges → UPDATE ... WHERE id=7 AND xmin=42 → 1 ligne ✓ → A GAGNE (xmin→43)

B accepte (200 ms après, avait lu xmin=42) :
  1. Lire le ride            → xmin=42
  2. ride.AcceptOffer(B)     → validations OK en mémoire
  3. SaveChanges → UPDATE ... WHERE id=7 AND xmin=42 → 0 ligne (xmin vaut 43 !)
                 → DbUpdateConcurrencyException → catch → Error.Conflict → HTTP 409
```

C'est la base de données, dans une instruction atomique unique, qui tranche. Deux UPDATE ne peuvent pas matcher la même version.

**Handler :**
```
Handle(AcceptOfferCommand):
    ride = charger le ride
    résultat = ride.AcceptOffer(driver.Id)        # validation domaine
    si résultat échoue : return résultat.Error     # (hors vague, expiré…)
    try:
        driver.SetAvailability(false)
        await SaveChanges()                         # ← le verrou agit ICI
    catch DbUpdateConcurrencyException:
        return Error.Conflict("Cette course vient d'être prise.")   # → HTTP 409
    notifier.RideStatusChanged(...)
    return RideDto
```

**À mettre en place :**
1. `UseXminAsConcurrencyToken()` sur la configuration EF du `Ride`.
2. Catch `DbUpdateConcurrencyException` → `Error` de type `Conflict` (mappé en 409 par `ToHttpResult()`, déjà en place).

---

## 7. Temps réel (SignalR) — révocation d'offre

Avec les vagues, quand A gagne, B et C ont une offre affichée qui n'est plus valide. On la retire proprement plutôt que de laisser B/C cliquer et recevoir un 409 sec.

**Nouvel événement : `rideOfferRevoked`** (cible `DriverUser_{id}`), `payload = { rideId, reason }`.

| Situation | Reçoivent `rideOfferRevoked` | `reason` |
|-----------|------------------------------|----------|
| A accepte | les autres de la vague (B, C) | `"taken"` |
| La vague expire | tous ceux de la vague | `"expired"` |
| Le client annule pendant l'offre | toute la vague | `"cancelled"` |

On émet la révocation à chaque **sortie de l'état `Offered`**, vers tous les `OfferedDriverIds` sauf le gagnant.

**Abstraction `IRealtimeNotifier` :**
```
+ Task RideOfferRevokedAsync(string driverUserId, int rideId, string reason, CancellationToken ct)
```
Implémentée dans `SignalRRealtimeNotifier` (Web.Api).

**Frontend :** à la réception, retirer la carte d'offre + toast court (« Course déjà prise »).

---

## 8. Plan de tests

**Domaine `Ride` (unitaires purs) :**
- `OfferWave` : `Pending → Offered`, remplit `OfferedDriverIds` + `TriedDriverIds`
- `AcceptOffer` réussit pour un chauffeur de la vague
- `AcceptOffer` échoue pour un chauffeur hors vague
- `AcceptOffer` échoue si offre expirée
- `AcceptOffer` échoue si `Status != Offered` (garantit le « premier gagne »)
- `DeclineOffer` retire de la vague ; vague vide → `ReturnToPending`

**`RideDispatcher` (Moq) :**
- offre à `min(3, candidats)` chauffeurs
- exclut les `TriedDriverIds`
- candidats épuisés → notifie les admins
- émet `rideOffered` à chaque chauffeur de la vague

**Intégration — race condition :**
- 3 `AcceptOfferCommand` en parallèle (`Task.WhenAll`) sur le même ride → exactement 1 succès, 2 `Conflict`

**NetArchTest :** vérifier que les règles de dépendances entre couches ne sont pas cassées.

---

## 9. Fichiers touchés

| Fichier | Changement |
|---------|-----------|
| `Taxi.Domain/Rides/Ride.cs` | `OfferedDriverId` → `OfferedDriverIds` ; `OfferWave`, `AcceptOffer`, `DeclineOffer` |
| `Taxi.Infrastructure/Persistence/Configurations/RideConfiguration.cs` | colonne `int[]` + `UseXminAsConcurrencyToken()` |
| `Taxi.Application/Dispatch/RideDispatcher.cs` | logique de vague (`min(3, candidats)`) |
| `Taxi.Application/Dispatch/AcceptOffer/AcceptOfferCommandHandler.cs` | catch `DbUpdateConcurrencyException` → `Conflict` |
| `Taxi.Application/Dispatch/DeclineOffer/DeclineOfferCommandHandler.cs` | retrait de la vague, relance si vide |
| `Taxi.Application/Realtime/IRealtimeNotifier.cs` | + `RideOfferRevokedAsync` |
| `Taxi.Web.Api/Realtime/SignalRRealtimeNotifier.cs` | implémente la révocation |
| `Taxi.Infrastructure/Dispatch/OfferTimeoutService.cs` | révoque la vague à l'expiration (quasi inchangé) |
| **migration EF** | nouvelle colonne `offered_driver_ids` |

---

## 10. Hors périmètre (YAGNI)

- ❌ Scoring rating/historique → **palier 2**, quand des données réelles seront disponibles
- ❌ Redis / file de messages → le volume ne le justifie pas
- ❌ ETA / API maps → axe séparé
- ❌ Surge pricing → axe séparé

---

## 11. Paliers futurs (notés, non planifiés)

- **Palier 2 — Scoring multi-critères :** trier l'ordre des vagues par `score(distance, rating, taux d'acceptation, équité)` une fois le rating réellement alimenté et l'historique suffisant.
- **Palier 3 — Vague élargissante :** vague 1 = 3, vague 2 = 5, vague 3 = tous, si une course traîne.
