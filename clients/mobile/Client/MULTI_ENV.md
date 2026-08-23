# Environnements staging & prod — application cliente

## La décision

**Une seule fiche par plateforme est publiée.** Staging et production partagent le
même identifiant applicatif et ne se distinguent que par l'URL du backend, injectée
au build.

| Plateforme | Identifiant publié | Fiche |
| --- | --- | --- |
| Google Play | `com.hbaexpress.client` | `HbaExpress` |
| App Store | `fr.hbamarket.app` | `HbaExpress` (id `6786785756`) |

| Environnement | `API_BASE_URL` |
| --- | --- |
| `staging` | `https://m.marketplace-staging.hba-marketplace.fr` |
| `prod` | `https://m.hba-express.org` |

La recette est diffusée par les **pistes de test** de chaque store — TestFlight
côté Apple, pistes interne/fermée/ouverte côté Google — et non par une seconde
application.

### Pourquoi ce choix

Le projet visait initialement **deux fiches distinctes**, avec des identifiants
suffixés `.staging`. Trois raisons ont fait abandonner ce modèle :

1. **La règle des 12 testeurs s'applique par application.** Un compte développeur
   Google personnel créé après le 13 novembre 2023 doit mener un test fermé de
   14 jours avec 12 testeurs avant d'accéder à la production. Le faire sur une
   fiche « staging » ne débloque rien pour la fiche de production : deux semaines
   perdues.
2. **Une seconde fiche exige une présence store complète** — captures d'écran,
   descriptions, classification du contenu, politique de confidentialité — pour
   une application purement interne.
3. **Une seule empreinte SHA-256** à déclarer pour les liens universels, au lieu
   de deux jeux de configuration à maintenir en parallèle.

### Ce que ce choix coûte

Rien à l'écran ne distingue plus un binaire de recette d'un binaire de production.

Ce n'est pas une hypothèse : l'en-tête de `lib/src/core/config/app_config.dart`
documente l'incident déjà survenu — une archive envoyée sur TestFlight pointait sur
la recette, sans que personne le remarque. Le garde-fou ajouté alors fait échouer
un build de release **sans** URL ; il ne protège pas contre une **mauvaise** URL.

Le risque est plus aigu côté Apple : la soumission à l'examen **sélectionne un build
déjà présent sur TestFlight**. Deux entrées identiques dans la liste, que seul leur
numéro distingue.

Les scripts de livraison compensent par trois dispositifs, décrits dans
[`scripts/README.md`](scripts/README.md) : environnement dans le nom du fichier,
manifeste écrit à côté de chaque artefact, avertissement à chaque build `staging`.

---

## Le flavor `staging` reste utile — en local

Les flavors n'ont pas été retirés du projet. Ils servent au **développement**, pour
installer les deux environnements côte à côte sur un même appareil :

| Flavor | iOS | Android | Usage |
| --- | --- | --- | --- |
| `staging` | `fr.hbamarket.app.staging` | `com.hbaexpress.client.staging` | développement local uniquement |
| `prod` | `fr.hbamarket.app` | `com.hbaexpress.client` | **livré aux stores** |

```bash
flutter run --flavor staging --dart-define=API_BASE_URL=https://m.marketplace-staging.hba-marketplace.fr
```

**Un flavor est obligatoire, même en développement.** Sans lui, Gradle cherche
une variante `debug` qui n'existe pas, et l'erreur remontée est illisible.

Les scripts de livraison, eux, passent **toujours** `--flavor prod` : c'est
l'identifiant publié.

---

## Livrer

```bash
./scripts/build-appbundle.sh staging     # AAB, identifiant prod, backend recette
./scripts/build-appbundle.sh prod        # AAB, identifiant prod, backend production
./scripts/build-ipa.sh staging
./scripts/build-ipa.sh prod
```

Le détail des options, des prérequis et des étapes de téléversement est dans
[`scripts/README.md`](scripts/README.md).

---

## Configuration en place (référence)

Cette partie décrit ce qui est **déjà câblé**. Elle n'est à relire qu'en cas de
reprise du projet sur une nouvelle machine, ou si l'on décidait un jour de
republier une fiche staging distincte.

### Android

- `android/app/build.gradle.kts` : flavors `staging` (`applicationIdSuffix
  ".staging"`, nom « HbaExpress Staging ») et `prod` (nom « HbaExpress »).
- `AndroidManifest.xml` : `android:label="@string/app_name"`, fourni par le flavor.
- `android/key.properties` : keystore de release. **Jamais dans le dépôt.**

### Firebase

Les deux identifiants existent comme apps distinctes du **même projet** Firebase —
le push FCM est lié au bundle ID.

- `android/app/google-services.json` contient les deux `package_name`.
- `ios/config/staging/GoogleService-Info.plist` et `ios/config/prod/…` : une build
  phase Xcode copie le bon selon la configuration.

### iOS — Xcode

Configurations dupliquées (`Debug-staging`, `Release-staging`, `Profile-staging`, et
les trois `-prod` correspondantes), schemes partagés `staging` et `prod`, bundle ID
et nom d'affichage réglés par configuration.

Build phase « Firebase plist par flavor », placée **après** *Copy Bundle Resources* :

```bash
case "${CONFIGURATION}" in
  *staging*) ENV="staging" ;;
  *)         ENV="prod" ;;
esac
SRC="${PROJECT_DIR}/config/${ENV}/GoogleService-Info.plist"
DEST="${BUILT_PRODUCTS_DIR}/${PRODUCT_NAME}.app/GoogleService-Info.plist"
if [ ! -f "${SRC}" ]; then echo "error: ${SRC} introuvable"; exit 1; fi
cp "${SRC}" "${DEST}"
```

Capacités **Push Notifications** et **Associated Domains** actives sur les deux
App IDs du portail Apple Developer.

### Liens universels

`Runner.entitlements` déclare les deux domaines (`m.hba-express.org` et
`m.marketplace-staging.hba-marketplace.fr`) : un binaire unique doit ouvrir les
liens des deux environnements.

Côté backend, `src/Bff/Marketplace.Bff.Mobile/appsettings.json`, section `DeepLink`,
déclare les identifiants Apple et les packages Android autorisés.

> **`DeepLink:AndroidSha256` est encore vide.** Après le premier téléversement, la
> signature d'application Play resigne l'AAB avec la clé de Google : relevez
> l'empreinte dans Play Console → *Intégrité de l'application* → *Certificat de la
> clé de signature* et renseignez-la. Sans cela, les liens `/p/` et `/s/` ouvriront
> le navigateur au lieu de l'application.
