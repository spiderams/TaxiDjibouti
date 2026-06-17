# TaxiDjibouti.Frontend

Frontend MVP React + Vite de l'application Taxi Djibouti.

## Stack retenue pour le MVP

- React + Vite
- TypeScript
- Tailwind CSS
- Axios pour les appels HTTP
- React Router pour séparer les espaces
- SignalR client prêt pour le temps réel
- Leaflet / OpenStreetMap prévu pour la carte

## Espaces livrés

### 1. Espace Client

Le client peut :

- se connecter ou s'inscrire ;
- demander un taxi ;
- choisir départ et destination ;
- voir le prix estimé ;
- voir le statut de ses courses.

Plus tard : carte Leaflet/OpenStreetMap, position chauffeur en temps réel, paiement D-Money.

### 2. Espace Chauffeur

Le chauffeur peut :

- se connecter ou s'inscrire ;
- créer son profil chauffeur ;
- se mettre disponible ;
- voir les courses en attente ;
- accepter une course ;
- changer le statut : `arrivé`, `course commencée`, `course terminée`.

### 3. Espace Admin

L'admin peut :

- voir les utilisateurs ;
- voir les chauffeurs ;
- voir les courses ;
- voir les statistiques ;
- créer ou mettre à jour un profil chauffeur pour un utilisateur `Driver`.

## Ordre de développement conseillé

1. Login
2. Dashboard Admin
3. Page Chauffeur
4. Page Client

## Prérequis

- Node.js 20 ou plus récent
- npm
- API TaxiDjibouti lancée avec .NET/Aspire ou via son projet API

## Installer les dépendances

Depuis la racine du dépôt :

```bash
npm install --prefix TaxiDjibouti.Frontend
```

## Tester le frontend seul

1. Démarrer l'API backend.
2. Lancer Vite en indiquant l'URL HTTPS de l'API :

```bash
VITE_API_PROXY_TARGET=https://localhost:5001 npm run dev --prefix TaxiDjibouti.Frontend
```

Si votre API utilise un autre port, remplacez `https://localhost:5001` par l'URL affichée par Swagger ou Aspire.

3. Ouvrir l'URL Vite affichée dans le terminal, généralement :

```text
http://localhost:5173
```

## Tester avec Aspire

Depuis la racine du dépôt :

```bash
dotnet run --project TaxiDjibouti.AppHost --launch-profile https
```

Puis ouvrir le Dashboard Aspire et cliquer sur la ressource `frontend`.

## Scénario fonctionnel rapide

1. Créer ou connecter un compte `Client`.
2. Demander une course avec les zones `Centre-ville` vers `Balbala`.
3. Créer ou connecter un compte `Driver`.
4. Créer le profil chauffeur, puis cliquer sur `Me mettre disponible`.
5. Accepter la course depuis la liste des courses disponibles.
6. Avancer le statut : `Arrivé`, `Commencer`, puis `Terminer`.
7. Créer ou connecter un compte `Admin` pour consulter les statistiques et toutes les courses.

## Build de production

```bash
npm run build --prefix TaxiDjibouti.Frontend
```

La sortie de build est générée dans `TaxiDjibouti.Frontend/dist`.
