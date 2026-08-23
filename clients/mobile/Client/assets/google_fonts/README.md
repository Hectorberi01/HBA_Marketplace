# Polices embarquées — Plus Jakarta Sans

Ce dossier doit contenir les fichiers `.ttf` de la police de l'application.

## Pourquoi

`google_fonts` télécharge normalement les polices depuis `fonts.gstatic.com` au
premier rendu de texte. Un plantage remonté par Crashlytics le 1er août 2026 —
`Failed to load font with url: https://fonts.gstatic.com/…` — a montré que ce
mécanisme échoue dès que le réseau flanche, et que le paquet lève alors une
exception comptabilisée en **plantage fatal**.

Sur les réseaux mobiles visés par l'application, ce n'est pas un cas limite.
S'ajoutent un premier affichage retardé par une requête HTTP, et l'envoi de
l'adresse IP de chaque utilisateur à Google au démarrage — que la politique de
confidentialité ne mentionne pas.

`main.dart` fixe donc `GoogleFonts.config.allowRuntimeFetching = false`.

## Fichiers attendus

Télécharger la famille depuis <https://fonts.google.com/specimen/Plus+Jakarta+Sans>
(bouton *Get font* → *Download all*), puis copier ici les instances **statiques**
du dossier `static/` de l'archive :

```
PlusJakartaSans-Regular.ttf      (400)
PlusJakartaSans-Medium.ttf       (500)
PlusJakartaSans-SemiBold.ttf     (600)
PlusJakartaSans-Bold.ttf         (700)
PlusJakartaSans-ExtraBold.ttf    (800)
```

Ces cinq graisses couvrent l'ensemble des usages du thème (`app_theme.dart` :
`w600`, `w700`, `w800`, plus le `textTheme` Material qui emploie `w400` et `w500`).

**Ne pas renommer les fichiers.** `google_fonts` associe les graisses par le
nom du fichier. Une variante mal nommée n'est pas trouvée, et le texte retombe
sur la police système — sans erreur, sans avertissement.

**Ne pas prendre la version variable** (`PlusJakartaSans[wght].ttf`) : elle est
ignorée par le paquet, qui attend des instances statiques.

## Vérifier

Après avoir copié les fichiers :

```bash
flutter pub get
flutter run --flavor staging --dart-define=API_BASE_URL=https://m.marketplace-staging.hba-marketplace.fr
```

Coupe le réseau de l'appareil et relance l'application : le texte doit s'afficher
dans la même police qu'avec le réseau. S'il change d'aspect, un fichier manque ou
porte un nom incorrect.
