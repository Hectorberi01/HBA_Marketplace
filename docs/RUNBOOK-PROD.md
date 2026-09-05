> **CE DOCUMENT DECRIT UN CHEMIN RETIRE DU DEPOT (3 septembre 2026).**
>
> Il parle de Kubernetes, de k3s, de calques kustomize et de `kubectl`. Ces
> dossiers — `k8s/`, `infra/ansible/`, `infra/terraform/` — n'existent plus, et
> les workflows qui les employaient non plus.
>
> **La production tourne sur Docker Compose + Traefik.** Le document a jour est
> [`RUNBOOK-COMPOSE.md`](RUNBOOK-COMPOSE.md), et le deploiement passe par le
> workflow « Deployer la production (Compose) ».
>
> Celui-ci est conserve pour son historique : il explique POURQUOI certains
> choix ont ete faits, et plusieurs de ces raisons valent encore. Ne pas
> l'appliquer tel quel.

# Runbook — premier déploiement en production

> **Depuis le 2 septembre 2026, une partie des commandes de ce document
> n'existe plus.** L'outillage de déploiement local a été supprimé : `ansible/`,
> `scripts/deployer.sh`, `scripts/deployer-ansible.sh`,
> `scripts/deployer-service-prod.sh`, `scripts/migrer-prod.sh`,
> `scripts/publier-images.sh`, et neuf scripts Python.
>
> Le déploiement passe désormais par la CI :
> **`.github/workflows/deploy-compose.yml`** pour la production Compose +
> Traefik, **`.github/workflows/deploy-branches.yml`** pour Kubernetes.
> Les explications de ce runbook restent valables — ce sont les commandes qui
> ont déménagé, pas les raisons.


| | |
|---|---|
| VPS applicatif | **79.137.35.129** |
| Base PostgreSQL | **10.20.0.2**, second VPS, joignable par le tunnel |
| Namespace | **hba-prod** |
| Domaine | **api.hba-express.com** |
| Registre | **ghcr.io/hectorberi01** |

Ce runbook déploie **neuf services** : les sept du lot `common` moins
notification-service, plus les trois du lot `delivery`.

`identity-service`, `user-service`, `media-service`, `payment-service`,
`promotion-service`, `review-service`, `delivery-service`, `driver-service`,
`delivery-pricing-service`.

Ce qu'il ne déploie pas, et pourquoi, est à la fin — pas en note de bas de page.

---

## Comment le déploiement se passe — les trois endroits

C'est le point qui prête le plus à confusion, parce que rien ne se passe au même
endroit.

**1. GitHub construit les images.** À chaque poussée sur `main`, `ci.yml`
compile, publie sur `ghcr.io/hectorberi01/<service>` avec le SHA du commit comme
tag, et signe. Tu n'as rien à faire.

**2. Tu promeus, depuis GitHub.** `cd.yml` se lance à la main — onglet Actions,
« Run workflow » — avec un SHA et un environnement. Il vérifie les signatures,
pose le tag dans `k8s/overlays/prod` **et** dans `k8s/overlays/migrations-prod`,
puis commite la promotion.

**3. Tu appliques, depuis ton Mac.** `cd.yml` s'arrête volontairement avant
`kubectl apply` : l'y faire obligerait à confier un kubeconfig de production à
GitHub Actions. C'est écrit en tête du fichier comme une décision en attente.
Après la promotion : `git pull`, puis les commandes ci-dessous.

Les étapes 1 à 4 de ce runbook ne se font **qu'une fois**. Les étapes 5 à 8 se
refont à chaque déploiement.

---

## Ce qui est déjà fait

- Les quatorze bases et leurs quatorze rôles existent sur 10.20.0.2. Le
  cloisonnement a été éprouvé en vrai : `hba_identity` est refusé sur `hba_user`.
- Le namespace `hba-prod` existe.
- `api.hba-express.com. A 79.137.35.129` est posé, TTL ~1 h.

---

## 1. Les opérateurs, avant toute charge

`docs/DEPLOIEMENT.md` §3.2 et §3.3 : **ingress-nginx**, **cert-manager**,
**Strimzi**. k3s est installé avec Traefik désactivé — les manifestes demandent
`ingressClassName: nginx`.

CloudNativePG n'est pas dans la liste : la base est hors cluster.

```bash
# Ingress — les manifestes déclarent `ingressClassName: nginx`.
#
# CETTE URL POINTE `main`, DONC UNE CIBLE MOUVANTE. Elle vient telle quelle de
# `docs/DEPLOIEMENT.md` §2.3. Réinstaller dans six mois ne redonnera pas le même
# contrôleur, et rien ne le dira. Relever la version installée et l'épingler :
#   https://github.com/kubernetes/ingress-nginx/releases
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/cloud/deploy.yaml
kubectl -n ingress-nginx rollout status deploy --timeout=5m

# cert-manager — version ÉPINGLÉE, relevée le 29 août 2026 sur
# https://cert-manager.io/docs/installation/kubectl/
# Avant de monter de version, lire https://cert-manager.io/docs/installation/upgrade/ :
# les sauts de version mineure changent parfois les CRD.
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.21.1/cert-manager.yaml
kubectl -n cert-manager rollout status deploy --timeout=5m

# Kafka
kubectl apply -f "https://strimzi.io/install/latest?namespace=hba-prod" -n hba-prod
```

**Sans le CRD de Strimzi, `kubectl apply -k` échoue sur
`no matches for kind "Kafka"`.** Le message ne dit pas qu'il manque un opérateur —
il ressemble à une faute de frappe dans un manifeste.

Puis les émetteurs de certificats. Le manifeste est dans le dépôt depuis le
29 août ; avant, trois fichiers l'exigeaient et aucun ne le fournissait.

```bash
kubectl apply -f k8s/cluster/clusterissuer.yaml
kubectl get clusterissuer                    # letsencrypt et letsencrypt-staging
```

Ce fichier n'est dans **aucun** overlay, à dessein : un ClusterIssuer est à portée
cluster, et l'appliquer par `apply -k` le recréerait à chaque déploiement
d'environnement.

**Le premier certificat se demande à l'émetteur de recette.** Let's Encrypt limite
les échecs de validation à cinq par heure : un DNS pas encore propagé ou un port 80
fermé consomme le quota en essayant, et l'on attend une heure sans savoir pourquoi.
Basculer l'annotation de l'Ingress sur `letsencrypt-staging`, vérifier que le
certificat passe `READY=True`, puis remettre `letsencrypt` et supprimer le Secret
TLS pour forcer une nouvelle émission.

**Vérifier que le DNS résout avant de lancer le premier challenge :**

```bash
dig +short api.hba-express.com          # doit rendre 79.137.35.129
```

## 2. Les cinq Secrets

Aucun n'est dans Git, aucun n'est posé par `apply -k` (§12). **Un `apply`
remplace la carte `data` entière** : chaque commande doit porter toutes les clés
de son Secret, pas seulement celles qui changent.

```bash
cd ~/Documents/HBA
umask 077
```

### 2.1 `ghcr` — le tirage des images

Les images sont sur un dépôt privé. Sans ce Secret, `kubelet` tire en anonyme et
les neuf Deployments restent en `ImagePullBackOff`.

```bash
kubectl -n hba-prod create secret docker-registry ghcr \
  --docker-server=ghcr.io \
  --docker-username=hectorberi01 \
  --docker-password=<jeton GitHub avec read:packages> \
  --dry-run=client -o yaml | kubectl apply -f -
```

Le nom **`ghcr`** n'est pas libre : `k8s/base/services/_service/serviceaccount.yaml`
le désigne, et les neuf comptes de service en héritent par `namePrefix`.

`docs/DEPLOIEMENT.md` §721 dit de patcher le compte `default`. **Ne le fais
pas** : `deployment.yaml` pose `serviceAccountName: service`, renommé par
`namePrefix` en `identity-service`, `user-service`, etc. Aucun pod n'emploie
`default` — la commande réussirait sans rien changer, et le message d'erreur
parlerait d'authentification, pas de compte de service.

### 2.2 `hba-platform` — les chaînes de connexion et les clés partagées

Vingt et une clés : seize chaînes de connexion, `DEFAULT` vide, Redis, la clé de
signature, la clé d'API interne, la clé de protection des données.

```bash
export REDIS__CONNECTIONSTRING='redis:6379'
export AUTHENTICATION__SIGNINGKEY="$(openssl rand -base64 48)"
export INTERNAL__APIKEY="$(openssl rand -hex 32)"
export SECURITY__SECRETPROTECTION__KEY="$(openssl rand -base64 32)"   # base64, PAS -hex : voir ci-dessous

# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
kubectl -n hba-prod apply -f ~/secrets-hba-prod/secret-hba-platform.yaml
./scripts/check-secrets-cluster.sh prod
```

**`openssl rand -base64 32`, jamais `-hex 32`.** Cette ligne portait `-hex 32`,
qui rend 64 caractères hexadécimaux. L'hexadécimal étant un sous-ensemble de
l'alphabet base64 et 64 un multiple de 4, le décodage **réussit** et donne 48
octets là où AES-256 en exige 32. Le symptôme n'était pas un refus au démarrage :
les services partaient, `/login` fonctionnait, et `POST /api/v1/auth/register`
rendait un 500 muet — la seule route qui construit le protecteur.
`scripts/verifier-env-compose.sh` refuse maintenant ce format, et le service
refuse de démarrer plutôt que de servir à moitié.

Le script n'affiche **aucune valeur** : sa sortie ne donne que des noms de clés et
des longueurs. Il refuse un fichier source qui ne serait pas en 0600, refuse un
mot de passe contenant `;` ou un guillemet — qui couperait la chaîne de connexion
en deux — et dérive toujours l'utilisateur du nom de la base.

Il reprend les valeurs déjà posées dans le cluster quand elles existent : le
relancer ne fait pas tourner les clés par accident. Une variable d'environnement
l'emporte sur le cluster — c'est ainsi qu'on fait une rotation volontaire, et le
script le dit en toutes lettres quand ça arrive.

Puis supprime les deux fichiers en clair :

```bash
rm -f ~/secrets-hba-prod/secret-hba-platform.yaml ./motsdepasse-*.txt
unset REDIS__CONNECTIONSTRING AUTHENTICATION__SIGNINGKEY INTERNAL__APIKEY SECURITY__SECRETPROTECTION__KEY
```

### 2.3 `hba-identites-internes` — les identités gRPC et l'administrateur

`kubectl` refuse `--from-env-file` avec `--from-literal`, d'où le fichier
intermédiaire.

```bash
./scripts/generer-identites-internes.sh

export ADMIN__PASSWORD="$(openssl rand -base64 24)"
printf '%s' "$ADMIN__PASSWORD" | pbcopy      # dans le gestionnaire, MAINTENANT

FICHIER="$HOME/secrets-hba-prod/identites-plus-admin.env"
cat "$HOME/.hba-identites/identites.env" > "$FICHIER"
printf 'ADMIN__PASSWORD=%s\n' "$ADMIN__PASSWORD" >> "$FICHIER"

kubectl create secret generic hba-identites-internes -n hba-prod \
  --from-env-file="$FICHIER" \
  --dry-run=client -o yaml | kubectl apply -f -

rm -f "$FICHIER"
unset ADMIN__PASSWORD
```

Le compte `hector.adjakpa@hbatechettrade.com` est **le seul moyen d'entrer** :
notification-service n'est pas déployé, donc « mot de passe oublié » n'envoie
rien. Perdre cette valeur, c'est un `UPDATE` en base.

L'amorçage est idempotent et **ne réinitialise jamais** un mot de passe existant.
Changer `ADMIN__PASSWORD` après le premier démarrage ne change rien au compte —
le changement se fait depuis l'application.

Le script d'identités engendre plus de clés que le Secret n'en déclare : sa liste
d'hôtes couvre food et route. Ce n'est pas une erreur, les clés en trop ne sont
lues par personne.

### 2.4 `minio` — le stockage objet

```bash
kubectl -n hba-prod create secret generic minio \
  --from-literal=root-user="hba-minio" \
  --from-literal=root-password="$(openssl rand -base64 24)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

Le mot de passe est **aussi** celui de media-service :
`MEDIA__STORAGE__SECRETACCESSKEY` lit la même clé. Le changer sans redémarrer
media-service lui laisse un identifiant périmé, et l'échec se lit
« SignatureDoesNotMatch » — un message qui désigne la signature, pas la rotation.

### 2.5 `hba-notifications`

Inutile pour l'instant : notification-service n'est pas dans le lot. Son contenu
— compte de service Firebase et clé Resend — est décrit dans
`k8s/base/common/secret-notifications.yaml`.

## 3. Les données

Redis, MinIO et Kafka viennent de `k8s/base/data` et `k8s/base/kafka`, et sont
posés par l'overlay à l'étape 7. **Il n'y a donc rien à faire ici** — les buckets
MinIO se créent après, à l'étape 7 bis, quand le pod existe.

## 4. Vérifier que la base est joignable depuis le cluster

Postgres doit **écouter** sur 10.20.0.2 et `pg_hba.conf` doit accepter les
connexions venant du cluster. Le runbook staging détaille ces deux réglages, ils
valent à l'identique ici.

Une NetworkPolicy qui refuse produit un **délai d'attente, pas une erreur** — et
le diagnostic part vers le pare-feu. L'overlay de production autorise la sortie
vers `10.20.0.2/32` sur le port 5432, et rien d'autre hors du namespace.

---

## 5. Promouvoir les images

Sur GitHub : Actions → `cd.yml` → Run workflow → SHA + environnement `prod`.

Le workflow refuse une image non signée, pose le tag dans les deux overlays, et
commite. Puis, sur ton poste :

```bash
git pull
./scripts/preflight-k8s.sh prod
```

Pour le faire depuis le poste plutôt que par le workflow :

```bash
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
```

Le §13 exige une image immuable : `latest`, `main` et le placeholder sont
refusés, parce qu'un simple redémarrage de pod tirerait sinon une version que
personne n'a choisie. Le script ne touche que les services déployés — les autres
gardent `REMPLACE-PAR-LA-PROMOTION`, qui est précisément ce qui dit « pas encore
promu ».

**Ne pas employer `kustomize edit set image` à la main.** Sa syntaxe est
`<nom dans le manifeste>=<image réelle>:<tag>`, elle s'inverse sans prévenir, et
une entrée inversée ne désigne aucun conteneur : le build réussit et les pods
tirent l'image d'origine.

## 6. Les migrations, avant les services

**Vérifier le rendu avant d'appliquer.** Le Job tourne sous le compte de service
de son service, et ce nom doit être réécrit par `namePrefix` :

```bash
kustomize build k8s/overlays/migrations-prod | grep -E "serviceAccountName|image:"
```

Chaque `serviceAccountName` doit valoir `<service>` — `user-service`,
`identity-service` — et jamais `service` tout court. Un `service` littéral veut
dire que le ServiceAccount n'est pas dans la kustomization : le Job sera créé,
ne lancera **aucun pod**, et `kubectl logs` répondra « no pods found ». La cause
n'est visible que dans `kubectl describe job`.

```bash
./scripts/preflight-k8s.sh prod --cluster
kubectl apply -k k8s/overlays/migrations-prod
kubectl -n hba-prod get pods -l app.kubernetes.io/component=migration
```

**Ne pas attendre sur `kubectl wait --for=condition=complete` seul.** Un Job qui
épuise son `backoffLimit` passe en `condition=Failed`, et ce `wait` n'a pas de
sortie anticipée : il attend l'expiration complète du délai au lieu de rendre la
main sur l'échec. Quinze minutes passées à attendre une panne déjà survenue.

Surveiller plutôt en voyant ce qui se passe :

```bash
kubectl -n hba-prod get pods -l app.kubernetes.io/component=migration -w
```

Ou, pour un script qui doit rendre un code de sortie, attendre les deux issues et
prendre la première :

```bash
kubectl -n hba-prod wait --for=condition=complete job \
  -l app.kubernetes.io/component=migration --timeout=15m &
SUCCES=$!
kubectl -n hba-prod wait --for=condition=failed job \
  -l app.kubernetes.io/component=migration --timeout=15m && kill ${SUCCES} 2>/dev/null
wait ${SUCCES}
```

Le `get pods` intercalé n'est pas décoratif : c'est lui qui distingue « les Jobs
tournent » de « les Jobs n'ont rien lancé ». Zéro pod à cet instant est une
panne, pas un délai.

Neuf Jobs, engendrés depuis le câblage réel de chaque service par
`scripts/generer-jobs-migration.py` — pas recopiés, dérivés : un Job écrit à la
main diverge au premier changement du service.

Ils tournent avec `DATABASE__MIGRATEONLY=true` : les migrations s'appliquent,
**aucun port ne s'ouvre**, et le conteneur sort avec le code 0. Sans ce réglage
le conteneur démarrerait un serveur web, le Job resterait `Running`, et le `wait`
expirerait sur une migration pourtant réussie.

`delivery-pricing-service` fait partie de ce lot : son `Program.cs` appelle
`MigrateHbaDatabaseAsync` et sort proprement en mode `DATABASE__MIGRATEONLY`.

Si un Job échoue :

```bash
kubectl -n hba-prod logs job/<service>-migration
```

Un Job terminé ne se relance pas. Pour rejouer :

```bash
kubectl -n hba-prod delete job -l app.kubernetes.io/component=migration
```

## 7. Déployer

```bash
kustomize version                 # v5 minimum — v4 ignore `includeTemplates`
kubectl apply -k k8s/overlays/prod
kubectl -n hba-prod rollout status deploy --timeout=10m
```

`apply -k` ne touche à aucun Secret : les cinq sont hors des `resources`.

## 7 bis. Les buckets MinIO

Maintenant seulement : le pod `minio-0` n'existait pas avant l'étape 7.

```bash
kubectl -n hba-prod rollout status statefulset/minio --timeout=5m
kubectl -n hba-prod apply -f k8s/base/data/minio/job-buckets.yaml
kubectl -n hba-prod wait --for=condition=complete job/minio-buckets --timeout=5m
kubectl -n hba-prod logs job/minio-buckets
```

**media-service démarre très bien sans les buckets** — `IsConfigured` ne vérifie
que l'endpoint et les identifiants, pas l'existence des seaux. Le pod est vert,
les sondes passent, et l'échec n'arrive qu'au premier envoi de fichier, en
`NoSuchBucket`. C'est pour ça que cette étape ne peut pas être oubliée : rien ne
la réclamera.

Le Job n'est pas dans les `resources` du kustomization : un Job est immuable, et
le rejouer à chaque `apply -k` échouerait sur des champs non modifiables. Il est
rejouable à la main — `--ignore-existing` sur chaque bucket — après un
`kubectl -n hba-prod delete job minio-buckets`.

## 8. Vérifier

```bash
kubectl -n hba-prod get pods
kubectl -n hba-prod get certificate         # READY=True
kubectl -n hba-prod logs deploy/identity-service | grep -i "amorçage administrateur"
```

La dernière commande doit dire :

```
Amorçage administrateur : « hector.adjakpa@hbatechettrade.com » CRÉÉ (actif, rôle Admin).
```

Sur une base neuve, `existe déjà — inchangé` voudrait dire que le compte a été
créé avec un autre mot de passe.

Le certificat ne peut être émis qu'une fois le DNS propagé **et** le contrôleur
d'Ingress joignable en 80 depuis l'extérieur.

### Quand un pod ne démarre pas

| Symptôme | Cause la plus probable |
|---|---|
| `ImagePullBackOff` | Secret `ghcr` absent, ou tag non promu |
| `CreateContainerConfigError` | une clé de `secretKeyRef` manque dans le Secret |
| `CrashLoopBackOff` sur media | buckets non créés, ou Secret `minio` absent |
| Démarrage puis délai d'attente sur la base | `pg_hba.conf`, ou la règle d'egress |
| `Unauthenticated` sur tous les appels d'un service | mauvaise clé `INTERNAL_KEY_*` |

Les trois premières se lisent dans `kubectl describe pod`. La quatrième ne
produit **pas** d'erreur, seulement un délai.

---

## Ce que ce déploiement ne donne pas

**notification-service n'est pas déployé.** `NotificationsModuleInstaller` lève
en production dans les deux branches du canal SMS : configuré, parce qu'aucun
adaptateur `ISmsSender` n'existe dans ce dépôt ; non configuré, parce que le SMS
est le canal OTP par défaut et qu'un code qui n'atteint personne est un échec
totalement silencieux. Conséquence directe : **aucun courriel ni SMS ne part**.
Pas de vérification d'adresse, pas de mot de passe oublié, pas de code de
connexion. La plateforme fonctionne pour qui a déjà un compte et son mot de
passe — c'est-à-dire l'administrateur amorcé, et personne d'autre.

**payment-service démarre mais n'encaisse rien.** En production, une passerelle
non configurée n'est pas simulée : elle n'est pas enregistrée du tout. Le service
répond, aucun paiement n'aboutit.

**`route-service` n'est pas déployé.** L'audit du 27 août n'a trouvé aucun
appelant dans le dépôt.

**Le lot `food` n'est pas là.** `food-cart-service` refuse de démarrer en
production : il n'est branché sur aucun service de promotion, donc toute remise
serait ignorée et tout code promo refusé, en silence. Le garde-fou est délibéré,
et sa levée est chiffrée par son propre commentaire à une demi-journée — brancher
`PromotionPricingModuleApi` comme `cart-service` le fait déjà.
`food-order-service` et `restaurant-service` n'ont pas ce blocage, mais n'ont pas
non plus de manifeste.

**Le lot marketplace n'est pas là.** Catalogue, panier, stock, commandes,
vendeurs, retours : six lignes commentées dans
`k8s/base/services/kustomization.yaml`. Leurs bases, leurs rôles et leurs clés
d'identité existent déjà.

**MinIO vit sur le même VPS que les pods**, sur un disque unique, sans
sauvegarde — et il porte les pièces KYB et les preuves de livraison. Un stockage
objet externe reste le bon choix pour des pièces de conformité. Les identifiants
employés sont ceux du compte root de MinIO, faute de compte de service limité.

**Supervision minimale, sauvegarde de base absente.** Le collecteur OTLP interne
est déployé et les services pointent vers `otel-collector:4317`. Il manque encore
la pile Helm Prometheus/Grafana/Loki/Tempo pour les dashboards, alertes et traces
consultables. La base n'a toujours ni pgBackRest ni réplique.

**Le cluster n'est pas reconstructible depuis Git seul.** Cinq Secrets sont créés
à la main. Perdre le namespace, c'est perdre les valeurs — sauf si elles sont
ailleurs qu'un fichier sur le poste.

**La console Kubernetes n'est pas installée.** `k8s/outils/` n'est référencé par
aucun overlay, à dessein. Si tu l'installes, `--values k8s/outils/headlamp-valeurs.yaml`
n'est pas optionnel : la charte amont donne `cluster-admin` par défaut, ce qui
laisserait la console lire les quatorze mots de passe de base.

**Rien de ce dépôt n'a été compilé ni exécuté au moment d'écrire ces lignes.** Le
mode `MigrateOnly`, la sortie après migration insérée dans dix-huit `Program.cs`,
le câblage MinIO, les trois manifestes de `delivery` : tout est vérifié
statiquement — `check-braces.py` sur 1993 fichiers, YAML valide, `check-k8s.py`
au vert — et rien n'est vérifié en marche. Le premier `dotnet build` est la
véritable première épreuve.
