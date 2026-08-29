# HBA Driver App

Application mobile Flutter pour les livreurs HBAExpress.

Elle couvre les deux familles de courses du domaine Delivery :

- `FOOD-*` : livraison de repas issue du domaine Food ;
- `SHIP-*` : livraison de colis issue du domaine Marketplace.

Le livreur ne doit pas avoir a connaitre le domaine d'origine. L'application affiche une course, une adresse de retrait, une adresse de livraison, une remuneration et les actions attendues.

## Objectif fonctionnel

L'application livreur doit permettre de :

- se connecter avec un compte livreur ;
- finaliser l'activation du profil livreur ;
- passer disponible ou hors ligne ;
- recevoir des propositions de course ;
- accepter ou refuser une course ;
- suivre une livraison en cours ;
- ouvrir la navigation GPS ;
- confirmer la recuperation ;
- confirmer la livraison ;
- fournir une preuve de livraison selon le cas : photo, signature, code client ;
- consulter les gains ;
- demander un retrait ;
- consulter l'historique des courses ;
- recevoir les notifications utiles.

## Socle actuel

Ce dossier contient maintenant un projet Flutter initialise avec :

- Android ;
- iOS ;
- theme Material 3 ;
- ecran onboarding ;
- ecran connexion ;
- tableau de bord livreur ;
- liste de propositions de course ;
- course active ;
- detail de course ;
- preuve de livraison ;
- retrait Mobile Money ;
- documents livreur ;
- vehicule ;
- parametres ;
- notifications ;
- onglets courses, solde et profil ;
- donnees mockees pour demarrer sans backend.

Les fichiers principaux :

```text
lib/main.dart
lib/src/app/driver_app.dart
lib/src/app/app_theme.dart
lib/src/core/models/delivery_task.dart
lib/src/core/mock/driver_mock_data.dart
lib/src/features/auth/login_screen.dart
lib/src/features/onboarding/onboarding_screen.dart
lib/src/features/home/driver_home_screen.dart
lib/src/features/home/dashboard_screen.dart
lib/src/features/deliveries/deliveries_screen.dart
lib/src/features/deliveries/delivery_detail_screen.dart
lib/src/features/deliveries/proof_delivery_screen.dart
lib/src/features/wallet/wallet_screen.dart
lib/src/features/wallet/withdraw_screen.dart
lib/src/features/profile/profile_screen.dart
lib/src/features/profile/documents_screen.dart
lib/src/features/profile/vehicle_screen.dart
lib/src/features/profile/settings_screen.dart
lib/src/features/notifications/notifications_screen.dart
```

## Lancer l'application

Depuis la racine du depot :

```bash
cd clients/driver-app
flutter pub get
flutter analyze
flutter test
flutter run
```

Lister les simulateurs ou appareils disponibles :

```bash
flutter devices
```

Lancer sur un appareil precis :

```bash
flutter run -d <device-id>
```

## Architecture cible

Structure recommandee pour la suite :

```text
lib/
  main.dart
  src/
    app/
      driver_app.dart
      app_theme.dart
      app_router.dart
    core/
      config/
      errors/
      http/
      grpc/
      location/
      notifications/
      storage/
      tracking/
    features/
      auth/
      onboarding/
      availability/
      deliveries/
      proof_of_delivery/
      wallet/
      profile/
```

Regles :

- `app/` contient le bootstrap, le theme et la navigation.
- `core/` contient les briques partagees : configuration, client API, stockage local, geolocalisation, erreurs.
- `features/` contient les ecrans et la logique par domaine fonctionnel.
- Les mocks restent dans `core/mock` jusqu'au branchement API.
- Les appels reseau ne doivent pas etre eparpilles dans les widgets.

## Backend a brancher

Services backend concernes :

- `delivery-service` : cycle de vie d'une livraison ;
- `dispatch-service` : propositions et affectations de course ;
- `driver-service` : profil, disponibilite, documents, vehicule ;
- `tracking-service` : position temps reel ;
- `route-service` : itineraire et estimation ;
- `proof-of-delivery-service` : preuve de livraison ;
- `delivery-pricing-service` : remuneration et frais.

Communication attendue cote mobile :

- HTTP/REST via Gateway ou Driver BFF pour les actions utilisateur ;
- WebSocket, SignalR ou stream dedie pour les propositions et le tracking temps reel ;
- notifications push pour les nouvelles courses et changements critiques ;
- gRPC reserve aux communications backend interservices, pas directement expose a l'app mobile.

## Variables d'environnement a prevoir

Pour la suite, prevoir une configuration par environnement :

```text
HBA_API_BASE_URL
HBA_ENVIRONMENT
HBA_MAPS_API_KEY
HBA_PUSH_SENDER_ID
```

En Flutter, ces valeurs peuvent etre passees avec `--dart-define` :

```bash
flutter run \
  --dart-define=HBA_ENVIRONMENT=dev \
  --dart-define=HBA_API_BASE_URL=http://localhost:8080
```

## Permissions mobiles a prevoir

Android et iOS devront demander :

- position pendant l'utilisation ;
- position en arriere-plan si le tracking reste actif pendant une course ;
- notifications push ;
- camera pour photo de preuve ;
- galerie si import de document ;
- stockage securise pour tokens.

Ne pas activer les permissions avant d'avoir l'ecran et le flux qui les justifient.

## Roadmap d'implementation

1. Ajouter configuration d'environnement avec `--dart-define`.
2. Ajouter client API authentifie.
3. Ajouter stockage securise du token.
4. Brancher connexion livreur.
5. Brancher disponibilite livreur.
6. Brancher propositions de course.
7. Ajouter acceptation/refus.
8. Ajouter detail de course.
9. Ajouter tracking GPS.
10. Ajouter preuve de livraison.
11. Ajouter notifications push.
12. Ajouter solde et retrait.
13. Ajouter tests widget et tests de logique par feature.

## Tests

Commandes minimales avant commit :

```bash
flutter analyze
flutter test
```

Le test actuel verifie le parcours de demarrage :

1. onboarding ;
2. connexion ;
3. dashboard livreur.
