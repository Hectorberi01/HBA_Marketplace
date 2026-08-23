# Scripts de build — HbaExpress PRO

Deux scripts pour générer les binaires de l'app vendeur, avec l'URL de l'API
injectée selon l'environnement.

| Script | Plateforme | Sortie |
| --- | --- | --- |
| `build-ipa.sh` | iOS | `dist/HbaExpressPro-<env>-<version>.ipa` |
| `build-appbundle.sh` | Android | `dist/HbaExpressPro-<env>-<version>.aab` |

Les deux s'utilisent **depuis la racine du projet** (`HbaExpressPro/`).

> **Deux apps sur les stores (staging + prod côte à côte) ?** Ces scripts passent
> désormais `--flavor` : staging a l'identifiant `fr.hbamarketplace.seller.staging`,
> prod `fr.hbamarketplace.seller`. La mise en place complète (Xcode, Firebase par
> flavor, fiches App Store / Play) est décrite dans [`../MULTI_ENV.md`](../MULTI_ENV.md).
> En dev, un flavor est obligatoire : `flutter run --flavor staging --dart-define=API_BASE_URL=…`.

## Pourquoi ces scripts ?

L'URL du BFF vendeur est injectée au build via `--dart-define=API_BASE_URL`.
**Sans ce flag, l'app part sur l'URL par défaut (staging)** — un binaire de release
doit donc toujours la fixer explicitement. Ces scripts s'en chargent selon
l'environnement choisi :

| Environnement | API_BASE_URL |
| --- | --- |
| `staging` | `https://seller.marketplace-staging.hba-marketplace.fr` |
| `prod` | `https://seller.hba-express.org` |

## Commandes

```bash
# iOS
./scripts/build-ipa.sh staging          # IPA staging
./scripts/build-ipa.sh prod             # IPA prod
./scripts/build-ipa.sh all              # les deux

# Android
./scripts/build-appbundle.sh staging    # AAB staging
./scripts/build-appbundle.sh prod       # AAB prod
./scripts/build-appbundle.sh all        # les deux
```

### Numéro de build (2ᵉ argument)

L'App Store et le Play Store **refusent deux fois le même numéro de build**.
Passe-le en 2ᵉ argument pour chaque envoi sur les stores :

```bash
./scripts/build-ipa.sh prod 42
./scripts/build-appbundle.sh prod 42
```

À défaut, c'est le `+N` de `version:` dans `pubspec.yaml` qui est utilisé.

### Surcharger l'URL

Pour pointer temporairement ailleurs (test local, autre domaine) :

```bash
API_BASE_URL_OVERRIDE=http://10.0.2.2:8081 ./scripts/build-appbundle.sh staging
```

## Prérequis

### iOS (`build-ipa.sh`)
- Xcode installé.
- Signature configurée : ouvrir `ios/Runner.xcworkspace` → cible **Runner** →
  *Signing & Capabilities* → cocher **Automatically manage signing** + choisir le **Team**.
- Sans signature valide, `flutter build ipa` échoue à l'export ; le script te renvoie
  vers `Runner.xcworkspace`. Détails : [`RELEASE_IOS.md`](../RELEASE_IOS.md).

### Android (`build-appbundle.sh`)
- Fichier **`android/key.properties`** présent (keystore de release) :
  ```
  storeFile=/chemin/absolu/vers/hbaexpress-seller.jks
  storePassword=…
  keyAlias=hbaexpress
  keyPassword=…
  ```
- Sans lui, l'AAB est signé avec la clé de **débogage** → **refusé par le Play Store**
  (le script t'avertit). Détails : [`CICD_MOBILE.md`](../CICD_MOBILE.md).

## Sortie

Les binaires sont copiés dans **`dist/`** (créé automatiquement, **gitignoré**),
nommés par environnement et version — les deux environnements coexistent :

```
dist/
  HbaExpressPro-staging-2.1.4.ipa
  HbaExpressPro-prod-2.1.4.ipa
  HbaExpressPro-staging-2.1.4.aab
  HbaExpressPro-prod-2.1.4.aab
```

## Et la CI/CD ?

Ces scripts sont pour les **builds locaux**. La publication automatisée sur les
stores (tag `v*` → TestFlight + Google Play, via GitHub Actions + Fastlane) est
décrite dans [`CICD_MOBILE.md`](../CICD_MOBILE.md).
