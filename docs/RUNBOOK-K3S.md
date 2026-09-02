# RUNBOOK — k3s sur le VPS de production

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


> Cible : `79.137.35.129` (8 vCPU, 24 Go, 200 Go), utilisateur `ubuntu`,
> SSH `ovh-server` (port 8022, clé dédiée).
> Base de données : VPS séparé, joignable en `10.20.0.2:5432` par WireGuard.
> Domaine : `api.hba-express.com` → `79.137.35.129`.
> Registre : `ghcr.io/hectorberi01`.

---

## 0. Ce que ce runbook fait, et ce qu'il ne fait pas

**Il fait** : poser un cluster Kubernetes mono-nœud sur le VPS de production, y
installer les trois briques que les manifestes supposent déjà présentes
(ingress-nginx, cert-manager, Strimzi), y créer les six Secrets qui ne sont PAS
dans Git, migrer les bases, puis déployer les vingt workloads — dix-neuf
services et la passerelle.

**Il n'est pas la voie nominale.** En régime établi, c'est la CD qui déploie sur
un merge vers `develop` (§14). Ce runbook sert à monter le cluster la première
fois, et à dépanner ensuite.

**Il ne fait pas** :

- il ne remplace pas Coolify tout de suite. Coolify reste installé et continue
  de servir son tableau de bord ; c'est seulement la RESSOURCE HBA et son proxy
  qui s'arrêtent, pour libérer 80 et 443. Le retour arrière consiste à
  redémarrer cette ressource, ce qui reste possible tant que k3s ne tient pas
  les deux ports ;
- il ne déploie PAS `notification-service`. Il lui manque un adaptateur
  `ISmsSender` — c'est du CODE, pas de la configuration, et
  `NotificationsModuleInstaller` lève en production dans les deux cas : SMS
  configuré (aucun adaptateur) comme SMS absent (c'est le canal OTP par défaut).
  Aucune valeur posée ici ne le débloquera ;
- il ne donne aucune haute disponibilité. Un nœud, un courtier Kafka, un disque.
  Ce qui protège réellement les événements reste la table `outbox_messages`, en
  base, **sur un autre serveur**, que `OutboxRetryPolicy` rejoue ;
- il ne vérifie aucune VALEUR de secret. `check-secrets-cluster.sh` dit qu'une
  clé est présente et non vide ; que le mot de passe soit le bon, seul Postgres
  le dira.

### État de départ, à vérifier avant de commencer

```bash
ssh ovh-server 'ip -brief addr show wg0; timeout 5 bash -c "</dev/tcp/10.20.0.2/5432" && echo "5432 JOIGNABLE"'
ssh ovh-server 'ss -ltnp "sport = :80 or sport = :443"'
```

Le tunnel doit être monté AVANT k3s, et il doit le rester : les pods sortent
vers `10.20.0.2` par la table de routage de l'hôte, avec le NAT de flannel.

**Le plan d'adressage ne collisionne pas, et ce n'est pas un hasard.** flannel
prend `10.42.0.0/16` pour les pods et `10.43.0.0/16` pour les services ; le
tunnel est en `10.20.0.0/24`. Si un jour l'un des deux bouge, les pods
perdraient la base sans aucun message — une NetworkPolicy qui refuse et une
route absente donnent le même symptôme : un DÉLAI D'ATTENTE.

---

## 1. Libérer 80 et 443 — arrêter la ressource HBA de Coolify

Dans l'interface Coolify, sur la ressource HBA :

1. **Configuration → General → Proxy** : passer à **None**.
   Sans ça, le proxy de Coolify (Traefik) reprend les ports au prochain
   redémarrage du démon Docker, même ressource arrêtée.
2. **Configuration → Advanced → décocher l'auto-déploiement** (le webhook Git).
   Sinon, le prochain `git push` relance la pile et reprend les ports.
3. **Stop** sur la ressource.

Puis vérifier, depuis l'hôte :

```bash
ssh ovh-server 'docker ps --format "{{.Names}}\t{{.Ports}}" | grep -E ":80->|:443->" || echo "80 et 443 libres cote docker"'
ssh ovh-server 'sudo ss -ltnp "sport = :80 or sport = :443" || echo "80 et 443 libres cote hote"'
```

**Ne pas désinstaller Coolify.** Son tableau de bord (port 8000) reste le moyen
le plus rapide de relancer l'ancienne pile si l'installation de k3s tourne mal.
Il ne consomme rien de significatif une fois ses conteneurs applicatifs arrêtés.

**Ce que cette étape ne couvre pas.** Les conteneurs HBA arrêtés gardent leurs
volumes. Ils ne gênent pas k3s, mais ils occupent du disque — à ne nettoyer
qu'une fois la production Kubernetes confirmée, jamais avant.

---

## 2. Installer k3s

```bash
ssh ovh-server 'curl -sfL https://get.k3s.io | sh -s - \
  --disable traefik \
  --disable servicelb \
  --write-kubeconfig-mode 640 \
  --tls-san 79.137.35.129 \
  --tls-san api.hba-express.com'
```

**`--disable traefik`** : k3s installe Traefik par défaut. Nos manifestes
déclarent `ingressClassName: nginx` — avec Traefik seul, l'Ingress de la
passerelle serait créé, accepté par l'API, et **jamais servi par personne**.
Aucune erreur : juste un domaine qui ne répond pas.

**`--disable servicelb`** : klipper-lb donnerait une adresse au Service
`LoadBalancer` d'ingress-nginx, mais sur un nœud unique il ajoute un DaemonSet
de proxy qui renvoie vers la même machine — un saut de plus, et surtout un SNAT
qui fait perdre l'IP CLIENT RÉELLE. Les journaux d'accès et toute limitation par
IP verraient la même adresse pour tout le monde. On lui préfère un `hostPort`
sur ingress-nginx (étape 4), qui lie nginx directement aux ports du nœud.

**`--tls-san`** : sans lui, le certificat de l'API n'est valable que pour
`127.0.0.1` et l'IP interne — le kubeconfig ne fonctionnerait QUE depuis le
nœud, et la CI ne pourrait jamais s'y connecter. Les deux noms sont posés
maintenant parce qu'ajouter un SAN après coup demande de régénérer le
certificat de l'API.

**`--write-kubeconfig-mode 640`** : le défaut de k3s est `600` (root seul) ;
`644` (souvent recommandé) rend le fichier lisible par **tout utilisateur de la
machine**, et ce fichier est un accès administrateur complet et sans expiration.
`640` le laisse au groupe `root` uniquement, et on le lit par `sudo`.

Vérification :

```bash
ssh ovh-server 'sudo k3s kubectl get nodes -o wide'
ssh ovh-server 'sudo k3s kubectl get pods -A'
```

Le nœud doit passer `Ready` en moins d'une minute. `traefik` ne doit apparaître
nulle part.

---

## 3. Le kubeconfig — pour le poste et pour la CI

```bash
ssh ovh-server 'sudo cat /etc/rancher/k3s/k3s.yaml' \
  | sed 's#https://127.0.0.1:6443#https://79.137.35.129:6443#' \
  > ~/.kube/hba-prod.yaml
chmod 600 ~/.kube/hba-prod.yaml
KUBECONFIG=~/.kube/hba-prod.yaml kubectl get nodes
```

La réécriture de l'adresse est **obligatoire** : le fichier écrit par k3s
pointe `127.0.0.1`, ce qui ne veut rien dire depuis un autre poste. C'est
exactement le défaut constaté sur le contexte `orbstack`, dont le
`KUBECONFIG_B64` de la CI aurait hérité — la CI se serait connectée à
elle-même.

Pour le secret GitHub `KUBECONFIG_B64` :

```bash
base64 -i ~/.kube/hba-prod.yaml | tr -d '\n' | pbcopy   # macOS
```

**Ce fichier est un mot de passe d'administrateur du cluster, sans expiration.**
Il ne va dans aucun dépôt, aucun canal de discussion, aucun presse-papier
partagé. `.gitignore` couvre `*.yaml` sous `~/.kube` par construction — le
fichier vit hors du dépôt.

**Le port 6443 doit être joignable depuis GitHub Actions.** Si le pare-feu du
VPS le ferme, la CI échouera en délai d'attente sur `kubectl apply`, ce qui se
lit comme un cluster en panne. L'ouvrir au monde expose l'API : le compromis
raisonnable est de le laisser ouvert et de compter sur l'authentification par
certificat client, en sachant que c'est un compromis.

---

## 4. ingress-nginx

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash

sudo helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
sudo helm repo update

sudo helm --kubeconfig /etc/rancher/k3s/k3s.yaml \
  upgrade --install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace \
  --version 4.14.0 \
  --set controller.hostPort.enabled=true \
  --set controller.service.type=ClusterIP \
  --set controller.ingressClassResource.default=true \
  --set controller.resources.requests.cpu=100m \
  --set controller.resources.requests.memory=128Mi
```

**`--kubeconfig` PLUTOT QUE LA VARIABLE D'ENVIRONNEMENT.** `sudo -E helm …`
echoue sur cette machine : le sudoers refuse la preservation de
l'environnement (« preserving the entire environment is not supported, '-E' is
ignored »), `KUBECONFIG` ne traverse pas, et helm retombe sur son defaut
`localhost:8080` — d'ou une erreur « Kubernetes cluster unreachable » qui
designe le cluster alors que le probleme est le sudoers. `sudo KUBECONFIG=… helm`
fonctionne aussi ; l'option est plus lisible.

**LES `repo` SONT EN `sudo` EUX AUSSI, A DESSEIN.** helm range son cache dans le
`HOME` de l'utilisateur qui l'execute. Un `repo add` sous `ubuntu` suivi d'un
`upgrade` sous `root` rendrait « repo ingress-nginx not found ».

`controller.hostPort.enabled=true` + `service.type=ClusterIP` : nginx lie
directement 80 et 443 du nœud. C'est le pendant de `--disable servicelb` de
l'étape 2 — les deux réglages sont **appariés**, changer l'un sans l'autre
laisse l'Ingress sans écouteur (ClusterIP seul) ou avec deux prétendants aux
mêmes ports (hostPort + klipper).

La version est épinglée. **Noter ici celle réellement installée** le jour de
l'installation : un runbook qui dit « la dernière » décrit un système différent
à chaque lecture.

Vérification :

```bash
ssh ovh-server 'sudo k3s kubectl -n ingress-nginx get pods'
curl -I http://79.137.35.129/            # 404 de nginx = le contrôleur répond
```

Un **404 est le bon résultat** ici : aucun Ingress ne correspond encore. Une
connexion refusée voudrait dire que les ports ne sont pas pris — retour à
l'étape 1.

---

## 5. cert-manager et le ClusterIssuer

```bash
ssh ovh-server 'sudo k3s kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.21.1/cert-manager.yaml'
ssh ovh-server 'sudo k3s kubectl -n cert-manager rollout status deploy/cert-manager-webhook --timeout=180s'
```

Puis, depuis le poste :

```bash
KUBECONFIG=~/.kube/hba-prod.yaml kubectl apply -f k8s/cluster/clusterissuer.yaml
KUBECONFIG=~/.kube/hba-prod.yaml kubectl get clusterissuer
```

Le fichier pose DEUX émetteurs : `letsencrypt-staging` et `letsencrypt`.
L'Ingress de la passerelle annote `cert-manager.io/cluster-issuer: letsencrypt`,
donc le vrai. Le solveur HTTP-01 y déclare `ingressClassName: nginx` — c'est
pour cela que l'étape 4 précède celle-ci.

**Attendre que le webhook soit prêt avant d'appliquer le ClusterIssuer.**
Appliqué trop tôt, l'objet est refusé par un webhook injoignable, et le message
parle de connexion, pas d'ordre d'installation.

**Ce que cette étape ne couvre pas.** Aucun certificat n'est émis ici. Il le
sera au moment où l'Ingress existera (étape 12) — et seulement si le DNS
`api.hba-express.com` pointe déjà `79.137.35.129` ET que le port 80 est
joignable depuis l'extérieur, parce que HTTP-01 fait revenir Let's Encrypt sur
`/.well-known/acme-challenge/`. Tant que ce n'est pas le cas, l'Ingress répond
sans certificat valide.

---

## 6. Strimzi — version 0.51.0, et surtout pas « latest »

```bash
sudo kubectl create namespace hba-prod

sudo helm repo add strimzi https://strimzi.io/charts/
sudo helm repo update

sudo helm --kubeconfig /etc/rancher/k3s/k3s.yaml \
  upgrade --install strimzi strimzi/strimzi-kafka-operator \
  --namespace hba-prod --version 0.51.0

sudo kubectl -n hba-prod rollout status deploy/strimzi-cluster-operator --timeout=180s
sudo kubectl get crd kafkas.kafka.strimzi.io -o jsonpath='{.spec.versions[*].name}'; echo
```

**PASSER PAR LE CHART, PAS PAR `strimzi.io/install/<version>`.** Cette route
n'existe QUE pour `latest` : toute autre valeur rend un 404. Et `latest` est
precisement la version qu'on ne veut pas. Le chart, lui, epingle la version en
premier — c'est ce qui rend l'installation reproductible.

**Le dernier controle est celui qui compte** : `v1beta2` doit figurer dans les
versions servies par la CRD `Kafka`. Sinon l'`apply -k` echouera plus tard sur
« no matches for kind "Kafka" », et on ira chercher du cote des manifestes.

Sans `watchNamespaces`, l'operateur installe dans `hba-prod` ne surveille que
lui. Un operateur a portee cluster prendrait aussi la main sur un futur
`hba-staging` sur la meme machine.

**Strimzi 1.0.0 ne sert plus que l'API `v1`.** `v1beta2`, `v1beta1` et
`v1alpha1` y sont retirées. Tous nos manifestes Kafka — `cluster.yaml`,
`node-pool.yaml`, et les vingt `KafkaTopic` de chaque calque — sont en
`v1beta2`. Installer « latest » ferait échouer `kubectl apply -k` sur « no
matches for kind "Kafka" in version "kafka.strimzi.io/v1beta2" ».

0.51.0 est la dernière version du cycle 0.x, et elle sert encore `v1beta2`.
Passer en `v1` demande de convertir les trois familles de ressources ET les
CRD, dans cet ordre : c'est un chantier à part, pas une étape d'installation.
Le même avertissement est en tête de `k8s/base/kafka/cluster.yaml`.

L'installation est **cantonnée au namespace** `hba-prod` (`?namespace=` dans
l'URL) : l'opérateur ne surveille que lui. Un opérateur à portée cluster
prendrait aussi la main sur un futur `hba-staging` sur la même machine.

---

## 7. Le secret de tirage `ghcr`

Les images sont dans un registre **privé**. Le ServiceAccount de chaque service
porte `imagePullSecrets: [ghcr]` ; sans ce Secret, les dix-neuf pods restent en
`ImagePullBackOff` sur un « denied » qui se lit comme un problème de droits sur
le registre, et non comme un Secret absent.

Il faut un **jeton GitHub classique** avec la portée `read:packages` — un
`GITHUB_TOKEN` d'Actions ne convient pas ici, il n'existe que pendant un job.

```bash
export KUBECONFIG=~/.kube/hba-prod.yaml
read -rs GHCR_TOKEN            # saisie masquée : le jeton n'entre pas dans l'historique
kubectl -n hba-prod create secret docker-registry ghcr \
  --docker-server=ghcr.io \
  --docker-username=hectorberi01 \
  --docker-password="$GHCR_TOKEN" \
  --docker-email=hector.adjakpa@gmail.com
unset GHCR_TOKEN
```

`read -rs` plutôt que `--docker-password=xxx` écrit en clair : la seconde forme
laisse le jeton dans `~/.bash_history` et dans la table des processus, où tout
utilisateur de la machine peut le lire pendant l'exécution.

---

## 8. Les six Secrets qui ne sont pas dans Git

Ni `k8s/base/common/kustomization.yaml` ni celles de `k8s/base/data/` ne
référencent ces fichiers : ils y sont commentés, volontairement. Les fichiers du dépôt sont des **contrats** —
la liste des clés attendues, toutes vides. Les valeurs sont posées ici, par
`kubectl`, et ne traversent jamais Git.

### 8.1 `hba-platform` — chaînes de connexion, Redis, clés de signature

Ne pas recopier quatorze mots de passe à la main. Le script les lit et écrit le
Secret sans afficher aucune valeur :

### Le fichier de mots de passe se dérive du `.env` du compose

Les quatorze mots de passe existent déjà : `docker-compose.prod.yml` les lit
sous la forme `HBA_<ROLE>_PASSWORD`. Ne pas les recopier à la main — une recopie
se trompe, et surtout elle fait transiter chaque valeur par le presse-papier,
l'historique du shell et la sortie d'un terminal.

```bash
chmod 600 ~/hba-prod.env          # le script refuse tout autre mode
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
```

Il n'affiche que des noms de rôles et des longueurs. La liste des rôles attendus
est **dérivée** de la table `CLES` de `secret-depuis-motsdepasse.py`, elle-même
miroir de `secret.yaml` : un rôle absent du `.env` est nommé et le script refuse
d'écrire, plutôt que de produire un fichier à treize entrées qui échouerait une
étape plus loin.

`HBA_COMMUNICATION_PASSWORD` est celui qui manque le plus souvent : le compose
de production n'exécute pas notification-service, donc il ne le lit jamais — et
le Secret Kubernetes le déclare quand même, parce que `secret.yaml` porte les
vingt-et-une chaînes du catalogue, pas seulement celles du lot déployé.

Le fichier produit est en **deux colonnes**, `role motdepasse`, en **0600**.
Quatorze rôles :

```
hba_identity   <mot de passe>
hba_user       <mot de passe>
hba_media      <mot de passe>
hba_communication <mot de passe>
hba_financial  <mot de passe>
hba_promotion  <mot de passe>
hba_engagement <mot de passe>
hba_catalog    <mot de passe>
hba_commerce   <mot de passe>
hba_inventory  <mot de passe>
hba_order      <mot de passe>
hba_merchant   <mot de passe>
hba_delivery   <mot de passe>
hba_food       <mot de passe>
```

Vingt-et-une clés en sortent : `returnrefund` partage `hba_commerce` avec cart,
les quatre services de livraison partagent `hba_delivery`, et les trois services
food partagent `hba_food`. Une clé par service quand même — le jour où l'un
déménage vers sa propre base, on change une valeur ici et aucun manifeste.

```bash
export KUBECONFIG=~/.kube/hba-prod.yaml
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
  ~/hba-motsdepasse-prod.txt /tmp/secret-hba-platform.yaml

kubectl apply -f /tmp/secret-hba-platform.yaml
shred -u /tmp/secret-hba-platform.yaml 2>/dev/null || rm -f /tmp/secret-hba-platform.yaml
```

Le fichier de sortie porte **deux** objets : `hba-platform` et, quand le script
a engendré le mot de passe Redis, le Secret `redis` — voir 8.5. Un seul
`kubectl apply` les pose ensemble.

`KUBECONFIG` est exporté AVANT : le script lit le Secret déjà présent dans le
cluster pour reprendre les clés irremplaçables telles quelles. Sans cette
lecture, il engendrerait une nouvelle
`SECURITY__SECRETPROTECTION__KEY` — et une donnée chiffrée avec une clé perdue
ne se rechiffre pas, elle se perd.

Le fichier de sortie contient les secrets EN CLAIR. Il est en 0600 et hors du
dépôt, mais **il doit disparaître après le `apply`** — c'est la seule raison
pour laquelle la ligne de suppression est collée à celle d'application.

### Les quatre clés qui ne viennent pas de Postgres

`REDIS__CONNECTIONSTRING`, `AUTHENTICATION__SIGNINGKEY`, `INTERNAL__APIKEY` et
`SECURITY__SECRETPROTECTION__KEY` ne sortent d'aucun mot de passe de base. Le
script les résout dans cet ordre, et **annonce lequel** pour chacune :

1. la valeur déjà posée dans le cluster, reprise telle quelle — c'est ce qui
   rend le script rejouable sans rien casser ;
2. la variable d'environnement du même nom, si elle est posée ;
3. une valeur engendrée, annoncée `ENGENDREE MAINTENANT`.

**L'environnement passe AVANT le cluster, et c'est délibéré** : sans ça, poser
`export AUTHENTICATION__SIGNINGKEY=…` pour faire tourner la clé n'aurait rien
fait, le script aurait gardé l'ancienne en annonçant « REPRISE », et on aurait
cru la rotation faite.

Pour reprendre les valeurs **déjà en service dans le compose de production** —
ce qu'il faut faire ici, sinon les jetons émis avant la bascule deviennent
invalides :

```bash
read -rs AUTHENTICATION__SIGNINGKEY;      export AUTHENTICATION__SIGNINGKEY
read -rs INTERNAL__APIKEY;                export INTERNAL__APIKEY
read -rs SECURITY__SECRETPROTECTION__KEY; export SECURITY__SECRETPROTECTION__KEY

# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
  ~/hba-motsdepasse-prod.txt /tmp/secret-hba-platform.yaml

unset AUTHENTICATION__SIGNINGKEY INTERNAL__APIKEY SECURITY__SECRETPROTECTION__KEY
```

`read -rs` : la saisie est masquée et n'entre ni dans l'historique du shell ni
dans la table des processus.

REPRENDRE les valeurs existantes n'est pas une commodité. Un
`AUTHENTICATION__SIGNINGKEY` neuf invaliderait tous les jetons déjà émis, et une
`SECURITY__SECRETPROTECTION__KEY` neuve rendrait indéchiffrables les codes déjà
posés dans l'outbox — sans aucune erreur au démarrage.

**`AUTHENTICATION__SIGNINGKEY` et `JWT__SIGNINGKEY` doivent être identiques** —
c'est le contrôle que fait déjà `verifier-env-compose.py` côté compose. Deux
valeurs différentes ne cassent rien au démarrage : elles produisent des jetons
qu'aucun service ne sait valider.

**`SECURITY__SECRETPROTECTION__KEY` est partagée entre identity et
notification** : identity chiffre les codes de vérification avant l'outbox,
notification les déchiffre. Deux clés différentes ne lèvent nulle part — les
e-mails cessent simplement de partir.

### 8.2 `hba-identites-internes` — les 22 identités gRPC et le compte admin

C'est le Secret dont l'absence est la plus coûteuse à diagnostiquer. Sans la
clé privée d'un hôte, `InternalCallClientInterceptor` lève
`FailedPrecondition: Internal identity not configured.` **chez l'appelant** :
le service fautif n'apparaît dans aucun journal. Et le seul garde-fou au
démarrage refuse `IdentitesNonSignees` — **l'absence de clé passe en silence**.

```bash
scripts/generer-identites-internes.sh ~/hba-identites-prod
# écrit ~/hba-identites-prod/<hote>.key et identites.env
```

Puis construire le Secret à partir de `identites.env`, en y ajoutant
`ADMIN__PASSWORD` (le mot de passe du compte administrateur d'amorçage) :

```bash
kubectl -n hba-prod create secret generic hba-identites-internes \
  --from-env-file=$HOME/hba-identites-prod/identites.env
read -rs ADMIN_PWD
kubectl -n hba-prod patch secret hba-identites-internes --type merge \
  -p "{\"data\":{\"ADMIN__PASSWORD\":\"$(printf %s "$ADMIN_PWD" | base64)\"}}"
unset ADMIN_PWD
```

**`ADMIN__PASSWORD` est ici et surtout pas dans `hba-platform`** : ce dernier
est monté par `envFrom` dans les dix-neuf services, ce qui donnerait le mot de
passe administrateur à chaque conteneur de la plateforme.

**L'amorçage est idempotent et ne réinitialise jamais le mot de passe.** Le
changer ici après le premier démarrage ne change rien au compte existant.

Le nom de chaque clé vient du **projet**, pas du dossier :
`HBA.Identity.Api` → `INTERNAL_KEY_HBA_IDENTITY_API`. Se tromper de clé ne casse
rien au démarrage : le pod part, signe sous une identité qui n'est pas la
sienne, et tous ses appels reviennent en `Unauthenticated`.

### 8.3 `hba-paiements` — FedaPay

```bash
read -rs FEDAPAY_KEY
read -rs FEDAPAY_WEBHOOK
kubectl -n hba-prod create secret generic hba-paiements \
  --from-literal=PAYMENTS__FEDAPAY__APIKEY="$FEDAPAY_KEY" \
  --from-literal=PAYMENTS__FEDAPAY__WEBHOOKSECRET="$FEDAPAY_WEBHOOK"
unset FEDAPAY_KEY FEDAPAY_WEBHOOK
```

**Ce Secret est séparé de `hba-platform` pour une raison précise.** Une clé
FedaPay LIVE déplace de l'argent réel. `hba-platform` est monté par `envFrom`
dans TOUS les pods : y mettre la clé la donnerait à dix-neuf services dont un
seul en a besoin. Ici elle est lue par `secretKeyRef`, uniquement par
`payment-service`.

`PaymentsModuleInstaller` refuse de démarrer en production sans PSP réel, et
`KeyMatchesEnvironment` refuse si la clé et l'URL de base ne concordent pas —
une clé `sk_test_` avec l'URL de production est rejetée au démarrage. C'est le
bon échec.

### 8.4 `minio`

```bash
kubectl -n hba-prod create secret generic minio \
  --from-literal=root-user="$(openssl rand -hex 12)" \
  --from-literal=root-password="$(openssl rand -base64 32)"
```

Deux lecteurs, un seul Secret : le StatefulSet MinIO y lit
`MINIO_ROOT_USER`/`MINIO_ROOT_PASSWORD`, et `media-service` y lit ses
identifiants S3 sous les MÊMES clés. C'est ce qui garantit qu'ils ne divergent
pas.

### 8.5 `redis` — et la chaîne de connexion qui doit lui correspondre

Le StatefulSet Redis lit `REDIS_PASSWORD` dans un Secret nommé `redis`, clé
`password`. Les services, eux, ne lisent PAS ce Secret : ils lisent une chaîne
de connexion complète dans `hba-platform`. **Les deux doivent porter le même mot
de passe.** Deux valeurs différentes ne lèvent nulle part au démarrage : elles
produisent des `NOAUTH Authentication required` au premier accès, longtemps
après le déploiement.

**C'est le script de 8.1 qui les écrit, ensemble, depuis la même valeur.** Il
n'y a donc rien à taper ici au premier déploiement : le fichier de sortie porte
les deux objets, et un seul `kubectl apply` les pose. Le script annonce
`Secret redis : ECRIT dans le meme fichier`.

Quand la chaîne vient d'ailleurs — reprise du cluster, ou posée en variable
d'environnement — le script n'écrit PAS le second objet : il suppose un Secret
`redis` déjà accordé, le vérifie, et refuse en silence de le remplacer par un
mot de passe neuf qui casserait Redis. S'il signale `Secret redis : ABSENT`,
c'est cette forme manuelle qu'il faut, avec le mot de passe déjà contenu dans la
chaîne :

```bash
read -rs MDP    # le même que dans REDIS__CONNECTIONSTRING
kubectl -n hba-prod create secret generic redis --from-literal=password="$MDP"
unset MDP
```

Sans ce Secret, Redis reste en `CreateContainerConfigError`, son Service n'a
aucun endpoint, et ce sont les **dix-neuf services** qu'on voit échouer —
verrous distribués, idempotence, cache. La cause est à deux crans du symptôme.

### 8.6 Contrôle

```bash
./scripts/check-secrets-cluster.sh prod
```

Il vérifie les six Secrets (`hba-platform`, `hba-identites-internes`,
`hba-paiements`, `minio`, `redis`, `ghcr`), compare chacun au contrat du dépôt,
et signale toute clé absente ou vide. **Sa sortie ne contient que des noms de
clés** — jamais une valeur : elle finit dans des journaux de CI.

Il ne dit PAS qu'une valeur est la bonne.

---

## 9. Poser le tag des images — voie manuelle

> Les étapes 9 à 12 décrivent ce que la CD fait toute seule (§14). Les suivre à
> la main sert à comprendre l'ordre, à démarrer un cluster neuf avant que la CD
> ne soit branchée, et à dépanner. En régime nominal, on ne les tape pas.


Les deux calques portent `newTag: "REMPLACE-PAR-LA-PROMOTION"`. Rien ne démarre
tant qu'un tag réel n'est pas posé — et c'est voulu : le 13 interdit `latest` en
production.

En temps normal c'est `cd.yml` qui pose le tag dans les DEUX calques. À la
main :

```bash
SHA=<le sha d'images déjà publiées et validées>

for calque in k8s/overlays/prod k8s/overlays/migrations-prod; do
  (cd "$calque" \
     && sed -i.bak "s/REMPLACE-PAR-LA-PROMOTION/$SHA/g" kustomization.yaml \
     && rm -f kustomization.yaml.bak)
done

grep -c "$SHA" k8s/overlays/prod/kustomization.yaml            # doit rendre 21
grep -c "$SHA" k8s/overlays/migrations-prod/kustomization.yaml # doit rendre 18
```

**Les deux calques doivent porter le MÊME tag.** Une migration jouée depuis une
image plus ancienne que les services applique un schéma qui n'est pas celui
qu'ils attendent — et l'écart ne se voit qu'à la première requête qui touche la
colonne manquante. `check-k8s.py` vérifie leur accord.

---

## 10. Vérifier le rendu avant de toucher au cluster

```bash
kustomize build k8s/overlays/migrations-prod > /dev/null && echo "migrations : rendu ok"
kustomize build k8s/overlays/prod            > /dev/null && echo "prod       : rendu ok"
python3 scripts/check-k8s.py
python3 scripts/check-kafka-topics.py
```

`check-k8s.py` est SANS kustomize pour l'essentiel : c'est lui qui a trouvé les
cinq patches qui ne désignaient rien, les six noms d'images périmés, et les
quatre entrées manquantes du script de Secret. Un patch dont la cible n'existe
pas **ne fait pas échouer** `kustomize build` : il s'applique à rien, en
silence.

---

## 11. Migrer les bases — AVANT les services

```bash
export KUBECONFIG=~/.kube/hba-prod.yaml
kubectl apply -k k8s/overlays/migrations-prod
kubectl -n hba-prod wait --for=condition=complete job --all --timeout=15m
kubectl -n hba-prod get jobs
```

Dix-huit Jobs, un par service qui possède un `DbContext`. `route-service` n'en a
pas et n'en a donc aucun.

Chaque Job pose `DATABASE__MIGRATEONLY=true`, ce qui fait **sortir le processus
avant `app.Run()`** : pas de port à écouter, pas de Kafka ni de Redis à joindre.
Sans cette variable, le conteneur démarrerait un serveur web et le Job resterait
`Running` indéfiniment — `wait --for=condition=complete` expirerait sur une
migration pourtant réussie.

En cas d'échec :

```bash
kubectl -n hba-prod logs job/<service>-migration
```

Les Jobs disparaissent une heure après leur fin (`ttlSecondsAfterFinished`).
Lire les journaux **avant**.

**Un Job terminé ne se relance pas.** Rejouer une migration demande de le
supprimer d'abord :

```bash
kubectl -n hba-prod delete job -l app.kubernetes.io/component=migration
kubectl apply -k k8s/overlays/migrations-prod
```

**Ce que cette étape ne couvre pas.** Elle suppose que les bases et les rôles
existent déjà sur le VPS de base de données — c'est `scripts/db/creer-bases.sh`
qui les crée, et ce serveur **n'entre dans aucun inventaire Ansible** (nftables
y couperait 5432).

---

## 12. Déployer

```bash
kubectl apply -k k8s/overlays/prod
kubectl -n hba-prod rollout status deploy --timeout=10m
kubectl -n hba-prod get pods -o wide
```

Ce que le calque de production pose, et qui n'est pas évident :

- **20 workloads** : les 19 de `services/kustomization.yaml` plus la passerelle,
  qui vit dans `apps/gateway` et qu'on oublie naturellement. 8 d'entre eux sont
  à deux replicas (les critiques du 4) — **28 pods** ;
- `requests.cpu` à **100m** par pod. C'est une RÉSERVATION, pas une
  consommation : à 250m, les 28 pods réservaient 7000m des 8 vCPU (7800m avec Kafka, Redis, MinIO et le collecteur), et les
  derniers services seraient restés `Pending` sur `Insufficient cpu` — un
  message qui désigne le dernier arrivé, jamais le fautif ;
- **Kafka à un seul courtier**, avec `replication.factor: 1` et
  `min.insync.replicas: 1`. Sur un nœud unique, trois répliques vivent sur le
  même disque : la garantie serait écrite et jamais obtenue. Et à 3 répliques
  demandées sur 1 courtier disponible, Strimzi refuse la création des topics et
  les partitions restent sans leader — les services démarrent, et le premier
  événement publié reste en attente sans message qui nomme la cause ;
- la NetworkPolicy d'egress 5432 pointe **`10.20.0.2/32`** (le calque staging
  pointe `10.0.0.1/32`). Une NetworkPolicy qui refuse ne rend pas d'erreur :
  elle laisse le paquet partir et jamais revenir. Le symptôme est un DÉLAI
  D'ATTENTE, indiscernable d'un mot de passe faux ou d'un `pg_hba` mal réglé.

---

## 13. Après le déploiement

### 13.1 Les buckets MinIO

MinIO ne crée pas ses buckets tout seul, et le Job qui le fait **n'est pas dans
les `resources`** de sa kustomization : il est commenté, parce qu'un Job est
immuable et qu'il doit tourner APRÈS que MinIO réponde. `kubectl apply -k` ne
le pose donc pas. À appliquer à la main :

```bash
kubectl -n hba-prod rollout status statefulset/minio --timeout=180s
kubectl -n hba-prod apply -f k8s/base/data/minio/job-buckets.yaml
kubectl -n hba-prod wait --for=condition=complete job/minio-buckets --timeout=5m
```

`hba-public` et `hba-private` doivent exister avant le premier envoi de
media-service. Sans eux, l'envoi échoue au premier fichier, pas au démarrage.

### 13.2 Le certificat

```bash
kubectl -n hba-prod get certificate
kubectl -n hba-prod describe certificate gateway-tls | tail -30
curl -sI https://api.hba-express.com/health/ready
```

L'émission ne peut aboutir que si le DNS est propagé **et** que le port 80 est
joignable de l'extérieur (HTTP-01). En cas de blocage, basculer temporairement
l'annotation de l'Ingress sur `letsencrypt-staging` : les quotas y sont larges,
et on évite de brûler les cinq échecs par heure du vrai émetteur.

### 13.3 Les consoles — jamais par l'Ingress

```bash
kubectl -n hba-prod port-forward svc/minio 9001:9001      # console MinIO
```

Aucune console d'administration ne passe par l'Ingress : `port-forward`
uniquement, sur le poste, le temps de regarder. Le 1 pose que seuls l'Ingress
de la passerelle et les endpoints explicitement publics sortent.

### 13.4 Vérifications de bout en bout

```bash
kubectl -n hba-prod get pods --field-selector=status.phase!=Running
kubectl -n hba-prod get kafka,kafkanodepool,kafkatopic
kubectl -n hba-prod logs deploy/identity-service --tail=50
curl -s https://api.hba-express.com/api/health | head
```

Un appel qui traverse deux services (par exemple le panier vers le catalogue)
est le seul moyen de prouver que les identités gRPC sont bonnes. Un
`Unauthenticated` ici désigne une clé privée qui ne correspond pas au registre
public — étape 8.2.

---

## 14. La bascule réelle passe par la CD, pas par ces commandes

Les étapes 9 à 12 sont la voie MANUELLE. Elles servent à comprendre l'ordre et à
dépanner. **La mise en production nominale ne les exécute pas à la main** :

```
merge sur `develop`
   └─ CI : construit et SIGNE les 21 images sous le SHA du commit
       └─ Deploy Branches (déclenché par la réussite de la CI)
           ├─ pose le SHA sur k8s/overlays/prod ET migrations-prod
           ├─ vérifie les signatures cosign
           ├─ pré-vol : check-k8s, check-infra, DNS, SECRETS, dry-run serveur
           ├─ applique les Jobs de migration et attend leur fin
           └─ applique k8s/overlays/prod
```

`develop` **est** la branche de production ; `main` ne déploie rien. Le
raisonnement est en tête de `.github/workflows/deploy-branches.yml` — c'est
contre-intuitif, parce que `main` est la branche par défaut du dépôt, et une
poussée sur `main` verra sa CI passer au vert sans qu'aucun déploiement ne
suive, sans message.

### Ce que la CD exige d'avoir été fait AVANT le premier merge

| Étape | Sans elle |
|---|---|
| Cluster k3s en place (§2) | `kubectl` en délai d'attente ; se lit comme un cluster en panne |
| `KUBECONFIG_B64` dans l'environnement GitHub `prod` (§3) | échec explicite au premier pas — le seul échec propre de la liste |
| Les 6 Secrets créés (§8) | le pré-vol refuse, et il a raison |
| ingress-nginx, cert-manager, Strimzi (§4–6) | `apply -k` échoue sur des `kind` inconnus |
| Le namespace `hba-prod` existant (§6) | le pré-vol refuse |

**L'ordre n'est pas négociable** : le pré-vol appelle
`check-secrets-cluster.sh`, qui appelle `kubectl`. Un cluster absent et des
secrets absents donnent deux messages différents, mais tous deux au même
endroit — et on cherche alors du côté de la CI.

### L'environnement GitHub `prod`

Le job déclare `environment: prod`. C'est là que vit `KUBECONFIG_B64`, et c'est
là qu'on pose une **règle de révision obligatoire** si l'on veut qu'un humain
approuve chaque déploiement de production. Sans cette règle, un merge sur
`develop` part directement en production.

---

## 15. Publier depuis le poste, quand la CI bloque

La CI est le chemin normal. Quand sa barrière de tests bloque et qu'il faut
déployer malgré tout, `scripts/publier-images.sh` construit et pousse les images
depuis le poste :

```bash
cd ~/Documents/HBA
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
```

Il faut un jeton GitHub avec la portée **`write:packages`** (celui du tirage n'a
que `read:packages`). Le script le lit sur l'entrée standard, jamais en argument.

**`--platform linux/amd64` est la raison d'être de ce script.** Un Mac Apple
Silicon construit en arm64 par défaut. Le VPS est en amd64. Une image arm64 se
construit sans erreur, se pousse sans erreur, et le pod meurt au démarrage sur
« exec format error » — un message qui ne nomme jamais l'architecture. La
première construction est lente : l'étage SDK tourne sous émulation.

**La liste vient de deux inventaires confrontés** : `images-affectees --tous` pour
la correspondance service → Dockerfile, et les images de `k8s/overlays/prod` pour
ce que la production réclame. Une image réclamée sans Dockerfile arrête tout —
sinon un pod resterait en `ImagePullBackOff` sur un « denied » qui se lit comme
un problème de droits.

**Le tag est le SHA court du HEAD, et l'arbre doit être propre.** Un arbre
modifié produirait des images dont le tag nomme un commit qui ne contient pas ce
qu'elles portent. `--tag` force, en connaissance de cause ; `latest` est refusé.

Ensuite, poser le tag et dérouler les étapes 11 et 12 — le script rappelle les
commandes en fin d'exécution.

### Ce que ce chemin ne couvre pas

**Les images ne sont pas signées.** La CI signe en keyless via l'identité OIDC du
workflow, qu'un poste ne peut pas produire. Les images publiées ainsi seront donc
**refusées** par la vérification cosign de `cd.yml` et de `deploy-branches.yml` :
une fois la CI de nouveau verte, il faudra republier par elle pour que la chaîne
automatique reprenne la main.

**Rien ne les a validées** sinon ce que vous avez lancé vous-même. La barrière de
tests reste dans la CI, entière — ce script la contourne, il ne la retire pas, et
la distinction compte : le prochain qui poussera sur `develop` retrouvera la
porte fermée tant que le défaut n'est pas corrigé.

---

## 16. Remonter les services un par un

`scripts/deployer-service-prod.sh <service>` applique **les seules ressources
d'un service** — son Deployment, son Service, son HPA, son PDB, son
ServiceAccount — en filtrant le rendu du calque sur
`app.kubernetes.io/name`. La ConfigMap, les Secrets et les NetworkPolicies ne
portent pas cette étiquette et ne sont donc pas touchés.

```bash
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
```

Il **refuse** si le contexte `kubectl` ne vise pas `79.137.35.129`. Le contexte
`orbstack` du poste répond lui aussi : se tromper de cluster ne rend aucune
erreur, l'objet est appliqué — ailleurs. C'est arrivé deux fois pendant
l'installation.

### L'ordre, et pourquoi il est celui-là

**1. `identity-service`.** Tout le monde en dépend : c'est lui qui émet les
jetons que les dix-huit autres valident. Un défaut ici se lit partout ailleurs
en 401, et on cherche au mauvais endroit.

**2. `user-service`, `media-service`.** Les socles transverses. media porte les
pièces KYB et les images produit ; user, les profils. Ils n'appellent presque
personne.

**3. `catalog-service`, `inventory-service`.** La donnée produit. catalog
interroge inventory ; ni l'un ni l'autre n'a besoin du reste.

**4. `seller-service`, `promotion-service`, `review-service`.** Ils lisent
catalog et media, et sont lus par le panier.

**5. `cart-service`, `order-service`, `payment-service`.** Le chemin d'achat.
C'est le premier palier où un appel gRPC traverse trois services : si les
identités internes sont mauvaises, c'est ici que ça se voit.

**6. `delivery-service`, `driver-service`, `delivery-pricing-service`,
`route-service`.** La livraison, qui consomme les commandes.

**7. `food-cart-service`, `food-order-service`, `restaurant-service`.** Le lot
restauration, indépendant du reste.

**8. `api-gateway`, en dernier.** C'est la porte d'entrée : la remonter avant
les autres exposerait des services à moitié prêts, et les clients verraient des
502 sur des routes qui allaient fonctionner deux minutes plus tard.

### Ce que la sonde ne dit pas

Un rollout vert prouve que le processus démarre et se déclare prêt. Il ne prouve
pas qu'un appel métier aboutit. À chaque palier, éprouvez une route réelle — et
au palier 5, une route qui **traverse deux services** : c'est le seul moyen de
vérifier les identités gRPC. Un `Unauthenticated` là désigne une clé privée qui
ne correspond pas au registre public, et rien d'autre ne le révèle.

---

## 17. Ce qui reste ouvert après ce runbook

- `notification-service` : adaptateur `ISmsSender` absent. **Code, pas
  configuration.**
- `return-refund-service` : deux adaptateurs gRPC manquants, dont un nouveau
  RPC d'inventaire.
- `deploy-branches.yml` mappe `develop` → `hba-prod` et **ne se déclenche pas
  sur `main`**. À corriger avant de compter sur la CD.
- Rotation des secrets : les mots de passe de base et les clés posées ici ont
  transité par des canaux non sûrs. À faire une fois la production stable.
- Les 18 réponses 401 d'order-service en test d'intégration restent
  inexpliquées.
- **Coolify est toujours installé**, ressource arrêtée et proxy sur *None*. Il
  ne se désinstalle qu'après plusieurs jours de production Kubernetes stable :
  tant qu'il est là, le retour arrière tient en une minute. Le jour venu,
  vérifier d'abord qu'aucun volume Docker ne porte encore de donnée utile.
- Kafka : un seul courtier. La durabilité repose sur `outbox_messages`. Le jour
  où un second nœud rejoint le cluster, remettre ensemble le pool à 3, les cinq
  facteurs du `Kafka` dans `k8s/overlays/prod/kustomization.yaml`, et
  `REPLICAS`/`MIN_ISR` dans `scripts/k8s-kafka-topics.py` — puis régénérer.
