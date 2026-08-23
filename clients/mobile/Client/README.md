# Client_MP_Mobile

Application mobile **client** de la marketplace, en **Flutter**. Elle consomme le
**BFF Mobile** (`Marketplace.Bff.Mobile`, exposé par Traefik sur `mobile.localhost`).

## Stack

- **Flutter** (Material 3, thème marketplace vert)
- **Riverpod** 2 — gestion d'état (providers, `AsyncNotifier`)
- **Dio** — client HTTP (intercepteur Bearer + refresh automatique sur 401)
- **go_router** — navigation + garde d'authentification
- **flutter_secure_storage** — jetons en Keychain/Keystore

## Démarrage

Ce dépôt contient le code applicatif (`lib/`) et le `pubspec.yaml`, mais **pas**
les dossiers de plateforme (`android/`, `ios/`, `web/`). Générez-les une fois :

```bash
cd Client_MP_Mobile
flutter create .            # crée android/ ios/ … sans toucher à lib/ ni pubspec
flutter pub get
```

Puis lancez l'app en pointant sur votre BFF Mobile :

```bash
# iOS simulateur (localhost = la machine hôte)
flutter run --dart-define=API_BASE_URL=http://mobile.localhost

# Android émulateur (localhost de l'hôte = 10.0.2.2, Traefik sur le port 80)
flutter run --dart-define=API_BASE_URL=http://10.0.2.2
```

### Tester sur un téléphone physique (iPhone/Android réel)

Le téléphone n'a pas `mobile.localhost`. Il faut viser **l'IP LAN de la machine**
qui fait tourner Docker/Traefik, et le téléphone doit être sur le **même Wi‑Fi**.

1. Trouvez l'IP de votre Mac : Réglages → Wi‑Fi → Détails, ou en terminal :
   ```bash
   ipconfig getifaddr en0     # ex. 192.168.1.20
   ```
2. Traefik route désormais le préfixe `/mobile` quel que soit l'hôte (routeur
   `mobile-lan`), donc visez simplement l'IP sur le **port 80** :
   ```bash
   flutter run --dart-define=API_BASE_URL=http://192.168.1.20
   ```

> Le HTTP en clair est autorisé en dev (iOS `NSAllowsArbitraryLoads`, Android
> `usesCleartextTraffic`). **À retirer en production** : n'utilisez que du HTTPS.

> Adaptez `API_BASE_URL` à votre passerelle. Par défaut l'app vise
> `http://mobile.localhost` (voir `lib/src/core/config/app_config.dart`).

## Architecture (feature-first)

```
lib/
  main.dart                      Bootstrap + ProviderScope
  src/
    app.dart                     MaterialApp.router
    core/
      config/app_config.dart     baseUrl, timeouts, devise
      theme/app_theme.dart       Material 3 marketplace
      network/                   ApiClient (Dio + refresh), ApiException
      storage/token_storage.dart Jetons sécurisés
      router/app_router.dart     go_router + garde auth
      providers/                 Providers transverses (dio, storage…)
    navigation/main_shell.dart   Barre de navigation à 5 onglets
    features/
      auth/        login, register, AuthController
      home/        accueil (sections + catégories)
      catalog/     fiche produit, recherche, avis, offres
      cart/        panier (CartController)
      checkout/    paiement (mobile money / carte)
      orders/      liste + détail + annulation
      account/     profil, favoris, notifications
      messaging/   conversations + chat
    shared/        widgets (cartes produit, états async), formatters
```

Chaque feature suit le découpage **data** (modèles + API + providers) /
**presentation** (écrans). Le parsing JSON est **tolérant** : les contrats du BFF
peuvent évoluer sans casser l'app (valeurs par défaut, clés alternatives).

## Périmètre de cette v1

- **Auth** : connexion, inscription, refresh automatique, déconnexion.
- **Achat** : accueil, recherche, fiche produit (galerie, offres, avis),
  panier (quantités), checkout mobile money/carte, commandes (liste, détail,
  annulation).
- **Engagement** : favoris (wishlist), notifications.
- **Messagerie** : conversations + fil de discussion.

## Endpoints non encore branchés (côté BFF Mobile)

Certaines routes du BFF renvoient `NotImplemented` (OTP SMS, reset mot de passe,
coupon panier, adresses/devis de livraison). Les écrans correspondants ne sont
donc pas exposés. Ils pourront être activés dès que le backend les implémente.

## Notes

- Le paiement mobile money utilise le flux **RequestToPay** (validation sur le
  téléphone) ; la carte utilise un **HostedCheckout** (lien renvoyé par le PSP).
- Pas de WebView embarquée pour l'instant : pour la carte, l'URL de paiement est
  affichée. Brancher `webview_flutter` est l'étape suivante naturelle.
