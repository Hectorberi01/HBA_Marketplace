# Runbook — premier déploiement en production

VPS applicatif **79.137.35.129**, cluster **hba-prod**, base **10.20.0.2** par le
tunnel. Domaine **api.hba-express.com**.

Ce runbook déploie **six services**, pas sept ni treize. Ce qu'il ne déploie pas
et pourquoi est dit à la fin, pas en note de bas de page.

---

## Ce qui est déjà fait

- Les quatorze bases et leurs quatorze rôles existent sur 10.20.0.2, cloisonnés
  et éprouvés en vrai — `hba_identity` est refusé sur `hba_user`.
- Le namespace `hba-prod` existe.
- L'enregistrement `api.hba-express.com. A 79.137.35.129` est posé.

## Ce qui reste, dans l'ordre

### 1. Les quatre Secrets

Aucun n'est dans Git, aucun n'est posé par `apply -k` (§12). **Un `apply`
remplace la carte `data` entière** : chaque commande doit porter toutes les clés
de son Secret, pas seulement celles qui changent.

```bash
cd ~/Documents/HBA
umask 077
```

**`hba-platform`** — dix-huit clés : treize chaînes de connexion, `DEFAULT` vide,
Redis, la clé de signature, la clé d'API interne, la clé de protection des
données.

```bash
export REDIS__CONNECTIONSTRING='redis:6379'
export AUTHENTICATION__SIGNINGKEY="$(openssl rand -base64 48)"
export INTERNAL__APIKEY="$(openssl rand -hex 32)"
export SECURITY__SECRETPROTECTION__KEY="$(openssl rand -hex 32)"

python3 scripts/db/secret-depuis-motsdepasse.py ./motsdepasse-<horodatage>.txt
kubectl -n hba-prod apply -f ~/secrets-hba-prod/secret-hba-platform.yaml
```

Le script n'affiche aucune valeur : sa sortie ne donne que des noms de clés et
des longueurs. Il refuse un fichier source qui ne serait pas en 0600.

**`hba-identites-internes`** — les clés gRPC, plus le mot de passe
administrateur. `kubectl` refuse `--from-env-file` avec `--from-literal`, d'où le
fichier intermédiaire.

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
```

Ce mot de passe est **le seul moyen d'entrer** : notification-service n'est pas
déployé, donc « mot de passe oublié » n'envoie rien. Le perdre, c'est un `UPDATE`
en base.

**`minio`** — les identifiants du stockage objet, que media-service lit aussi.

```bash
kubectl -n hba-prod create secret generic minio \
  --from-literal=root-user="hba-minio" \
  --from-literal=root-password="$(openssl rand -base64 24)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

**`hba-notifications`** — inutile pour l'instant : notification-service n'est pas
dans le lot.

### 2. Le secret de tirage GHCR

Sans lui, les six Deployments restent en `ImagePullBackOff` — le registre est
privé.

### 3. Les images

Le workflow doit avoir publié les six images sur `ghcr.io/hectorberi01/`. Puis le
tag de promotion, **aux deux endroits** :

La forme de `kustomize edit set image` est `<nom dans les manifestes>=<nouveau
nom>:<tag>`. Le membre de GAUCHE est le nom que portent les manifestes —
`hba/identity-service` — et non l'image du registre. L'inverser pose un `newTag`
absurde et un `newName` vide, sans que kustomize s'en plaigne.

Les six services, dans les deux overlays, en une fois :

```bash
TAG=<le tag publié par le workflow>

for OVERLAY in prod migrations-prod; do
  ( cd "k8s/overlays/$OVERLAY" || exit 1
    for S in identity user media payment promotion review; do
      kustomize edit set image \
        "hba/$S-service=ghcr.io/hectorberi01/$S-service:$TAG"
    done )
done

python3 scripts/check-k8s.py     # refuse que les deux overlays divergent
```

Ou, en un geste — même effet, et il refuse en plus un tag mutable :

```bash
python3 scripts/poser-tag-prod.py <tag>
```

Le §13 exige une image immuable : `latest`, `main` et le placeholder sont
refusés, parce qu'un simple redémarrage de pod tirerait alors une version que
personne n'a choisie. Le script ne touche QUE les six services déployés — les
huit autres gardent `REMPLACE-PAR-LA-PROMOTION`, qui est précisément ce qui dit
« pas encore promu ». Il relance `check-k8s.py` en terminant.

`REMPLACE-PAR-LA-PROMOTION` n'est pas un tag valide : le tirage échoue tout de
suite, ce qui est le bon échec. `scripts/check-k8s.py` refuse que les deux
overlays divergent — migrer avec une image plus ancienne que celle qui sert
applique un schéma en retard sur le code, et l'erreur remonte à la première
requête qui touche la colonne absente.

### 4. Les données, avant les services

Redis, MinIO et Kafka viennent de `k8s/base/data` et `k8s/base/kafka`, posés par
l'overlay. Les opérateurs Kafka doivent être installés **avant** les charges.

Puis, à la main, ce que rien n'automatise :

```bash
# les deux buckets — MinIO ne les crée pas tout seul
kubectl -n hba-prod exec -it minio-0 -- mc mb local/hba-public local/hba-private
```

### 5. Les migrations

```bash
kubectl apply -k k8s/overlays/migrations-prod
kubectl -n hba-prod wait --for=condition=complete job -l app.kubernetes.io/component=migration --timeout=15m
```

Six Jobs, un par service, engendrés depuis le câblage réel de chaque service par
`scripts/generer-jobs-migration.py`. Ils tournent avec `DATABASE__MIGRATEONLY=true` :
les migrations s'appliquent, **aucun port ne s'ouvre**, et le conteneur sort avec
le code 0. Sans ce réglage le conteneur démarrerait un serveur web, le Job
resterait `Running`, et `wait` expirerait sur une migration pourtant réussie.

Si un Job échoue :

```bash
kubectl -n hba-prod logs job/<service>-migration
```

Un Job terminé ne se relance pas. Pour rejouer :

```bash
kubectl -n hba-prod delete job -l app.kubernetes.io/component=migration
```

### 6. Déployer

```bash
kustomize version                 # v5 minimum — v4 ignore `includeTemplates`
kubectl apply -k k8s/overlays/prod
kubectl -n hba-prod rollout status deploy --timeout=10m
```

### 7. Vérifier

```bash
kubectl -n hba-prod get pods
kubectl -n hba-prod get certificate           # READY=True, sinon l'ACME est bloqué
kubectl -n hba-prod logs deploy/identity-service | grep -i "amorçage administrateur"
```

La dernière ligne doit dire `« hector.adjakpa@hbatechettrade.com » CRÉÉ (actif,
rôle Admin)`. Sur une base neuve, `existe déjà — inchangé` voudrait dire que le
compte a été créé avec un autre mot de passe.

Le certificat ne peut être émis qu'une fois le DNS propagé **et** le contrôleur
d'Ingress joignable en 80 depuis l'extérieur. Le TTL de l'enregistrement est de
près d'une heure.

---

## Ce que ce déploiement ne donne pas

**notification-service n'est pas déployé.** `NotificationsModuleInstaller` lève en
production dans les deux branches du canal SMS : configuré, parce qu'aucun
adaptateur `ISmsSender` n'existe dans ce dépôt ; non configuré, parce que le SMS
est le canal OTP par défaut. Conséquence directe : **aucun courriel ni SMS ne
part**. Pas de vérification d'adresse, pas de mot de passe oublié, pas de code de
connexion. La plateforme fonctionne pour qui a déjà un compte et son mot de
passe — c'est-à-dire l'administrateur amorcé, et personne d'autre.

**payment-service démarre mais n'encaisse rien.** En production, une passerelle
non configurée n'est pas simulée : elle n'est pas enregistrée du tout. Le service
répond, aucun paiement n'aboutit.

**Le lot marketplace n'est pas là.** Catalogue, panier, stock, commandes,
vendeurs, retours : six lignes commentées dans
`k8s/base/services/kustomization.yaml`. Leurs bases, leurs rôles et leurs clés
d'identité existent déjà.

**MinIO vit sur le même VPS que les pods**, sur un disque unique, sans
sauvegarde — et il porte les pièces KYB et les preuves de livraison. Un stockage
objet externe reste le bon choix pour des pièces de conformité.

**Aucune supervision, aucune sauvegarde de base.** `OPENTELEMETRY__ENDPOINT` est
vide, aucun collecteur n'est déployé, et la base n'a ni pgBackRest ni réplique.
Le diagnostic tient dans `kubectl logs` et les sondes.

**Le cluster n'est pas reconstructible depuis Git seul.** Quatre Secrets sont
créés à la main. Perdre le namespace, c'est perdre les valeurs — sauf si elles
sont ailleurs qu'un fichier sur le poste.

**Rien de ce dépôt n'a été compilé ni exécuté au moment d'écrire ces lignes.** Le
mode `MigrateOnly`, la sortie après migration insérée dans dix-huit `Program.cs`,
le câblage MinIO : tout est vérifié statiquement, rien n'est vérifié en marche.
Le premier `dotnet build` est la véritable première épreuve.
