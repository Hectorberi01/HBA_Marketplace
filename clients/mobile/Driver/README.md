# HBA Driver

Application livreur de la marketplace HBA (Bénin, XOF, Mobile Money FedaPay).

## Avant le premier lancement

Ce dossier ne contient que le code Dart : `android/`, `ios/` et les fichiers de
plateforme n'y sont pas. Ils se génèrent avec la commande officielle, qui
préserve `lib/` et `pubspec.yaml` :

```bash
cd HBA/clients/mobile/Driver
flutter create --platforms=android,ios --org fr.hbamarketplace --project-name hba_driver .
flutter pub get
flutter run
```

> `--org fr.hbamarketplace` donne le bundle `fr.hbamarketplace.driver`, aligné
> sur `fr.hbamarketplace.seller` du Seller-portal.

## État

Squelette et cinq écrans : Connexion, Inscription (7 étapes), Vérification des
documents, Dashboard hors ligne, Dashboard disponible.

**Toutes les données sont simulées.** Aucun appel réseau, aucune persistance.
Voir `lib/src/core/mock/driver_mock_data.dart`.

## Dette connue

Le thème (`lib/src/core/theme/`) est une **copie** de celui du Seller-portal.
Trois applications finiront par porter la même charte ; le jour où une couleur
change, il faudra la corriger à trois endroits. À extraire dans un paquet
`hba_design` — les points concernés sont marqués `DUPLIQUÉ` dans le code.
