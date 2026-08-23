# Livraison de l'application cliente HbaExpress

Deux scripts produisent les binaires signés, prêts à téléverser sur les stores.
Ils **construisent seulement** : aucun envoi automatique, aucun secret de store
sur la machine. Le téléversement reste un geste délibéré.

| Script | Plateforme | Sortie |
| --- | --- | --- |
| `build-appbundle.sh` | Android | `dist/HbaExpress-android-<env>-<version>+<build>.aab` |
| `build-ipa.sh` | iOS | `dist/HbaExpress-ios-<env>-<version>+<build>.ipa` |

Chaque artefact est accompagné d'un fichier `.txt` de même nom : le **manifeste**.
Il consigne l'environnement, l'URL du backend, le numéro de build et le commit.

---

## Le principe : une seule fiche par plateforme

Un seul identifiant applicatif est publié :

| Plateforme | Identifiant publié |
| --- | --- |
| Google Play | `com.hbaexpress.client` |
| App Store | `fr.hbamarket.app` |

Staging et production ne se distinguent **que par l'URL du backend**, injectée au
build. Les binaires sont sinon rigoureusement identiques.

| Environnement | `API_BASE_URL` |
| --- | --- |
| `staging` | `https://m.marketplace-staging.hba-marketplace.fr` |
| `prod` | `https://m.hba-express.org` |

Le flavor `staging` (identifiants suffixés `.staging`) existe toujours dans le
projet, mais il ne sert plus qu'au **développement local**, pour installer les deux
environnements côte à côte :

```bash
flutter run --flavor staging --dart-define=API_BASE_URL=https://m.marketplace-staging.hba-marketplace.fr
```

### Ce que ce choix coûte

Rien à l'écran ne dit plus à quel serveur l'application parle. Un binaire de
recette et un binaire de production se ressemblent trait pour trait.

Ce n'est pas une hypothèse : l'en-tête de `lib/src/core/config/app_config.dart`
documente l'incident qui a déjà eu lieu — une archive envoyée sur TestFlight
pointait sur la recette, sans que personne le remarque. Le garde-fou ajouté alors
empêche un build de release **sans** URL ; il ne protège pas contre une **mauvaise**
URL.

Trois dispositifs compensent, et il faut s'en servir :

1. **le nom du fichier** porte l'environnement ;
2. **le manifeste** l'écrit noir sur blanc, avec le commit ;
3. **le script avertit** à chaque build `staging` qu'il produit un binaire à
   l'identifiant de production.

---

## Commandes

```bash
# Android
./scripts/build-appbundle.sh staging
./scripts/build-appbundle.sh prod

# iOS
./scripts/build-ipa.sh staging
./scripts/build-ipa.sh prod
```

### Options

| Option | Effet |
| --- | --- |
| `--build-number N` | impose ce numéro au lieu de l'incrément automatique |
| `--no-bump` | réutilise le numéro courant de `pubspec.yaml` sans l'incrémenter |
| `--url URL` | remplace l'URL de l'API (stack locale, domaine de test) |
| `--yes` | n'attend aucune confirmation (intégration continue) |

```bash
# Stack locale sur émulateur Android
./scripts/build-appbundle.sh staging --url http://10.0.2.2:8080

# Reprendre un build raté sans consommer un numéro de plus
./scripts/build-ipa.sh staging --no-bump
```

---

## Le numéro de build

`pubspec.yaml` porte `version: <nom>+<build>`. Le nombre après `+` devient le
`versionCode` Android **et** le build number iOS.

Les scripts l'**incrémentent automatiquement** à chaque exécution et réécrivent
`pubspec.yaml`. Pensez à valider ce changement dans Git.

> Ce champ était absent du projet : Flutter retombait alors sur `1` pour toute
> compilation. Or les deux stores refusent un numéro déjà utilisé — et l'erreur
> n'apparaît qu'au téléversement, après plusieurs minutes de build. On n'aurait pu
> livrer qu'une seule fois.

**Les deux plateformes partagent ce compteur.** Livrer Android puis iOS produit
`+12` et `+13` : c'est sans conséquence, les stores n'exigent qu'une progression
stricte, pas une continuité.

**Premier envoi sur une fiche existante :** si TestFlight ou Play Console ont déjà
reçu des builds pour la même version, relevez le dernier numéro utilisé et forcez
un numéro supérieur avec `--build-number`.

---

## Prérequis

### Android

`android/key.properties` doit exister et désigner un keystore accessible :

```properties
storeFile=/chemin/absolu/vers/hbaexpress-client.jks
storePassword=…
keyAlias=hbaexpress
keyPassword=…
```

Le script **refuse de démarrer** si le fichier manque ou si le keystore est
introuvable. Sans lui, Gradle retombe sur la clé de débogage — dont le mot de
passe est littéralement `android` — et Play Console rejette le binaire.

> **Le keystore ne doit jamais entrer dans le dépôt, ni être perdu.** Google
> n'accepte que des mises à jour signées par la même clé. Un keystore perdu oblige
> à publier une nouvelle application, en abandonnant utilisateurs et avis.

### iOS

- macOS avec Xcode et CocoaPods ;
- signature configurée : `ios/Runner.xcworkspace` → Runner → *Signing & Capabilities* ;
- capacités **Push Notifications** et **Associated Domains** actives sur l'App ID.

Le script vérifie après coup que l'archive porte bien un profil de distribution
(`aps-environment = production`). Sans lui, l'application s'installe et fonctionne,
mais ne reçoit **jamais** aucune notification — une panne parfaitement silencieuse.

---

## Après le build

### Google Play

1. Play Console → **Test** → choisir la piste (interne, fermée, ouverte)
2. **Créer une release** → téléverser le `.aab`
3. Notes de version → **Envoyer pour examen**

> Les comptes développeur **personnels** créés après le 13 novembre 2023 doivent
> mener un test fermé avec **12 testeurs pendant 14 jours consécutifs** avant
> d'obtenir l'accès à la production. Cette exigence s'applique **par application**.
> Les comptes d'organisation en sont exemptés.

### App Store

1. **Transporter** (ou Xcode → Organizer) → téléverser le `.ipa`
2. App Store Connect → **TestFlight** → attendre le traitement (10 à 30 min)
3. Testeurs internes : accès immédiat. Groupes externes : Beta App Review, environ
   24 h, pour le premier build de chaque version seulement.

> **Sur Apple, la soumission à l'examen SÉLECTIONNE un build déjà présent sur
> TestFlight.** Si tous vos builds TestFlight pointent sur la recette, il faut en
> téléverser un supplémentaire avec l'URL de production. Deux entrées identiques
> dans la liste, distinguées par leur seul numéro : **consultez le manifeste avant
> de choisir.**

---

## Après le premier téléversement Android

En activant la signature d'application Play, Google **resigne l'AAB avec sa propre
clé** : l'empreinte SHA-256 change.

Relevez-la dans Play Console → *Intégrité de l'application* → *Certificat de la clé
de signature*, puis renseignez-la côté backend dans `DeepLink:AndroidSha256`
(`src/Bff/Marketplace.Bff.Mobile/appsettings.json`). Sans cela, les liens partagés
`/p/` et `/s/` ouvriront le navigateur au lieu de l'application.
