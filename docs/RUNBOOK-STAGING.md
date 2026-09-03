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

# Runbook — premier déploiement staging

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


VPS applicatif **193.168.145.162** (k3s) · VPS base **51.255.40.214** (PostgreSQL)
· lots common + delivery, 10 services + la passerelle.

Ce fichier est l'ordre d'exécution. Le détail et les raisons sont dans
`docs/DEPLOIEMENT.md` — chaque étape renvoie à sa section.

---

## Les valeurs posées

**Domaine** — `backendapi.marketplace-staging.hba-marketplace.fr`, posé dans
l'overlay staging (règle Ingress et hôte TLS). **L'enregistrement A doit exister
avant l'étape 8** :

```
backendapi.marketplace-staging.hba-marketplace.fr.  A  193.168.145.162
```

Sans lui, cert-manager boucle sur un challenge HTTP-01 qu'il ne peut pas valider,
le Certificate reste `READY=False`, et le client voit une **erreur TLS** — pas une
erreur DNS. Le diagnostic part alors vers cert-manager au lieu de la zone.

**Base de données** — **sur le VPS applicatif lui-même**, décision du 27 août.
PostgreSQL installé par apt, hors du cluster ; les pods le joignent à
`193.168.145.162:5432`. Le tunnel WireGuard et le VPS 51.255.40.214 ne sont plus
sur le chemin du staging — **ils restent la topologie de la production**, et
`docs/DEPLOIEMENT.md` §3.4.2 les décrit à ce titre.

Trois endroits portent l'adresse du nœud et changent **ensemble** si le VPS
change d'IP : `ip_privee` dans `infra/ansible/inventory/staging.yml` (qui alimente
aussi la règle nftables), le patch NetworkPolicy de `k8s/overlays/staging`, et les
chaînes de connexion du Secret. En oublier un donne un **délai d'attente**, pas un
refus — le CNI et nftables jettent, ils ne rejettent pas.

---

## 0. Installer PostgreSQL sur le VPS

**AUCUN DOCUMENT DE CE DÉPÔT NE DISAIT DE L'INSTALLER**, et cela n'a jamais posé
problème : sur l'ancienne topologie, la base tournait déjà sur 51.255.40.214,
posée à la main avant que la documentation n'existe. Le VPS de staging, lui, part
nu.

**LE COMPTE DE CE VPS EST `root`**, pas `ubuntu` : c'est l'image du fournisseur
qui le décide, et c'est ce que porte `infra/ansible/inventory/staging.yml`. Toutes
les commandes de ce runbook qui touchent la machine sont donc écrites **sans
`sudo`**. Sur une image minimale, `sudo` peut d'ailleurs être absent : la
commande échouerait alors sur « command not found », ce qui se lit comme un
problème de droits alors que c'est un binaire manquant.

```bash
ssh root@193.168.145.162

apt-get update
apt-get install -y postgresql
psql --version                      # relever la majeure : elle décide du chemin des configs
```

Les fichiers à modifier à l'étape suivante vivent dans
`/etc/postgresql/<majeure>/main/`. Sur Debian et Ubuntu, le paquet démarre le
service et crée un cluster `main` tout seul ; `pg_lsclusters` le confirme.

**L'utilisateur `postgres` est le seul à pouvoir se connecter au départ**, par
authentification `peer` sur la socket locale. C'est lui qui lancera le script de
l'étape 2 :

```bash
su - postgres -c "psql -c 'SELECT current_user, version()'"
```

## 1. Rendre PostgreSQL joignable depuis les pods

Trois réglages, et **aucun des trois n'échoue bruyamment** s'il manque : le
symptôme est toujours le même, une connexion qui expire.

**a. Postgres écoute sur l'adresse du nœud.** Dans `postgresql.conf` :

```
listen_addresses = 'localhost,193.168.145.162'
```

**Pas `'*'`, et pas non plus l'adresse du pont CNI.** `'*'` fait apparaître
`0.0.0.0:5432` — la base n'est alors protégée que par le pare-feu, et une seule
erreur de règle l'expose à Internet. L'adresse du pont (`10.42.0.1`) semble plus
sûre, mais elle n'existe qu'une fois k3s démarré : au redémarrage de la machine,
PostgreSQL perdrait la course et **refuserait de démarrer** sur une adresse
absente. L'IP publique du nœud, elle, existe dès le boot.

**b. `pg_hba.conf` accepte les pods, et eux seuls :**

```
host  all  all  10.42.0.0/16       scram-sha-256
host  all  all  193.168.145.162/32 scram-sha-256
```

Deux lignes, parce qu'un paquet partant d'un pod vers l'IP de son propre nœud
arrive avec l'adresse du pod ou celle du nœud selon les règles de masquage que
k3s installe. N'en mettre qu'une marche sur une version et pas sur la suivante.

**c. nftables ouvre 5432 pour ces mêmes sources** — c'est Ansible qui le fait,
à l'étape 3, parce que `infra/ansible/inventory/staging.yml` porte
`postgres_sur_hote: true`. Rien à taper ici ; c'est dit pour que la règle ne
paraisse pas venir de nulle part quand tu la verras dans `nft list ruleset`.

**Vérifier, dans cet ordre :**

```bash
ss -lntp | grep 5432        # 193.168.145.162:5432 et 127.0.0.1:5432, JAMAIS 0.0.0.0
nft list ruleset | grep 5432
```

Le test depuis un pod ne peut venir qu'après l'étape 3 — le cluster n'existe pas
encore, et `kubectl` n'a rien à joindre. Il exige aussi le tunnel SSH et
`KUBECONFIG` (§3.10) : sans eux, `kubectl` retombe sur `localhost:8080`, une
valeur par défaut historique qui n'a jamais désigné ce cluster.

```bash
ssh -N -L 6443:127.0.0.1:6443 root@193.168.145.162 &
export KUBECONFIG=infra/ansible/kubeconfig-staging.yaml
```


```bash
kubectl -n hba-staging run pg-test --rm -it --restart=Never \
  --image=postgres:16-alpine -- psql "host=193.168.145.162 port=5432 \
  dbname=hba_identity user=hba_identity password=<mdp>" -c 'SELECT 1'
```

**`0.0.0.0:5432` signifie que la base écoute sur Internet.** nftables ne
l'expose pas — 5432 n'est pas dans la liste publique — mais une base qui ne
dépend que du pare-feu se retrouve exposée à la première erreur de règle.

## 2. Les bases et les rôles

`docs/DEPLOIEMENT.md` §3.4.4 — le script tourne **sur le VPS applicatif**, en
local, puisque la base y est. `PGHOST` vaut `localhost`, pas une adresse de
tunnel :

```bash
su - postgres -c "$PWD/scripts/db/creer-bases.sh --simulation"   # lire ce qui va être fait
su - postgres -c "$PWD/scripts/db/creer-bases.sh"                # 14 bases, 14 rôles
```

**Sous l'identité `postgres`, et pas autrement.** Le script se connecte sans `-U` : il
emprunte donc l'identité du compte Unix qui le lance, par authentification
`peer`. Lancé sous `root`, il échoue sur « impossible de se connecter » — un message qui
parle de `PGHOST`/`PGUSER` et envoie chercher du côté du réseau, alors que la
base est sur la même machine. `su - postgres` change de répertoire de travail :
d'où le chemin absolu du script. Il vérifie ensuite que
ce compte a `createdb` ET `createrole` : `postgres` les a, les autres non.

Le script n'engendre les mots de passe qu'à la CRÉATION, ou sur `--rotation`. Un
rejeu ne casse pas la production existante. Conservez sa sortie : ces mots de
passe entrent à l'étape 5 et n'existent nulle part ailleurs.

## 3. Préparer le VPS et poser k3s

L'inventaire est écrit : `infra/ansible/inventory/staging.yml` (ignoré par Git).

```bash
cd infra/ansible
ansible-galaxy collection install -r requirements.yml   # ansible.posix — sans ça, le rôle `commun` échoue
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml --check
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
```

**`interface_privee: wg0` et `interface_flannel: ""`** — deux variables là où il
n'y en avait qu'une, parce que les deux emplois voulaient l'inverse : nftables
doit accepter le tunnel, et k3s ne doit surtout pas en dépendre. L'encadré en tête
de l'inventaire le détaille.

## 4. Les opérateurs, AVANT les charges

`docs/DEPLOIEMENT.md` §3.2 et §3.3 : ingress-nginx, cert-manager, Strimzi.
k3s est installé avec Traefik désactivé — les manifestes demandent
`ingressClassName: nginx`.

CloudNativePG n'est plus dans la liste : la base est hors cluster.

## 5. Le secret de plateforme

`docs/DEPLOIEMENT.md` §3.7 — un rôle et un mot de passe par service, ceux de
l'étape 2. Le générateur pose automatiquement
`Host=193.168.145.162;Port=5432` pour le staging.

Pourquoi pas `localhost` : ces chaînes sont lues par des pods, pas par l'hôte.
Dans un conteneur, `localhost` désigne le pod lui-même.

```bash
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
kubectl -n hba-staging apply -f ~/secrets-hba-staging/secret-hba-platform.yaml
./scripts/check-secrets-cluster.sh staging
```

## 6. Le secret d'identités gRPC

`docs/DEPLOIEMENT.md` §3.7.1.

```bash
scripts/generer-identites-internes.sh ~/secrets-hba-staging
kubectl create secret generic hba-identites-internes -n hba-staging \
  --from-env-file=$HOME/secrets-hba-staging/identites.env \
  --dry-run=client -o yaml | kubectl apply -f -
```

**Sans ce secret, les pods ne démarrent pas** — `CreateContainerConfigError` sur
une clé absente. C'est le bon échec : immédiat et nommé. Il était impossible
avant aujourd'hui, parce que rien ne référençait ces clés : le cluster serait
parti sain et aurait échoué au premier appel inter-services.

## 6 bis. Les secrets de notification — sinon notification-service ne démarre pas

`notification-service` REFUSE de démarrer en production sans émetteur push, et le
calque staging n'écrase pas `ASPNETCORE_ENVIRONMENT: Production` du gabarit : ce
refus s'applique donc ici. Ce n'est pas un garde-fou de confort — l'offre de
course part au livreur par notification, et un `NullPushSender` en production est
un dispatch qui ne propose plus rien.

Le compte de service Google se pose en FICHIER, pas en variable : le JSON fait
plusieurs kilo-octets et porte une clé privée PEM. Le raisonnement est dans
`k8s/base/common/secret-notifications.yaml`.

```bash
# Le fichier vient de la console Firebase :
#   Paramètres du projet → Comptes de service → Générer une nouvelle clé privée.
# Le NOM DE LA CLÉ compte : le volume projette chaque clé du Secret en un fichier
# portant son nom, et le service attend /etc/hba/firebase/service-account.json.
kubectl create secret generic hba-notifications -n hba-staging \
  --from-file=service-account.json=$HOME/secrets-hba-staging/firebase.json \
  --dry-run=client -o yaml | kubectl apply -f -
```

**Vérifier que c'est le bon projet Firebase.** Un compte de service valide d'un
AUTRE projet fait démarrer le service et envoie des push que personne ne reçoit —
aucune erreur nulle part. Le `project_id` du JSON doit être celui que visent les
applications mobiles.

### L'e-mail — Resend

Le second refus. Sans canal e-mail, la vérification d'adresse et surtout la
RÉINITIALISATION DE MOT DE PASSE sont impossibles : un utilisateur qui oublie son
mot de passe est enfermé dehors définitivement, et l'exploitant ne l'apprend que
par les plaintes.

`EmailOptions.IsConfigured` exige **trois** valeurs. Une seule est un secret :

```bash
# La clé Resend rejoint le MÊME Secret que le compte de service Firebase.
# `--from-literal` s'ajoute à ce qui existe déjà si l'on recrée le secret d'un
# seul geste — sinon la seconde commande écraserait la première.
kubectl create secret generic hba-notifications -n hba-staging \
  --from-file=service-account.json=$HOME/secrets-hba-staging/firebase.json \
  --from-literal=NOTIFICATIONS__EMAIL__APIKEY='re_…' \
  --dry-run=client -o yaml | kubectl apply -f -
```

Les deux autres ne sont pas des secrets et vivent dans le ConfigMap
(`k8s/base/common/configmap.yaml`) :

| Clé | État | À faire |
|---|---|---|
| `NOTIFICATIONS__EMAIL__FROM` | `HBA Express <no-reply@hbaexpress.com>` | **Vérifier que ce domaine est authentifié chez Resend** (SPF + DKIM). |
| `NOTIFICATIONS__EMAIL__APPBASEURL` | **VIDE** | À renseigner par overlay avant de déployer. |

**`APPBASEURL` vide bloque le démarrage, et c'est délibéré.** C'est la base des
liens cliquables — pas l'adresse de l'API. Ce n'est donc PAS
`backendapi.marketplace-staging.hba-marketplace.fr` : c'est le site ou
l'application que l'utilisateur ouvre en cliquant sur « réinitialiser mon mot de
passe ». Inventer cette URL enverrait des e-mails portant un lien mort — pire
qu'un e-mail jamais parti, puisque l'utilisateur clique, tombe sur une erreur, et
conclut que son compte est cassé.

**Le domaine non vérifié est le piège discret.** Resend refuse alors l'envoi par
un 403, l'échec se produit DANS L'OUTBOX, et il est rejoué toutes les cinq
secondes sans jamais aboutir. Ni le service ni les sondes ne s'en plaignent :
seule la file grossit.

### Ce que ces deux étapes ne suffisent pas à débloquer

`notification-service` a un TROISIÈME refus, indépendant : le **SMS**. Et celui-là
ne se configure pas — **aucun adaptateur SMS de production n'existe dans le
dépôt**. Le fournisseur reste à choisir : c'est un contrat commercial, un compte
opérateur et un expéditeur à homologuer. Renseigner `Notifications:Sms` sans
adaptateur fait d'ailleurs échouer le démarrage exprès, pour ne pas laisser croire
que les codes partent.

Deux autres services du même lot refusent pour leurs propres raisons —
`media-service` sans stockage objet, `payment-service` sans passerelle réelle.
Voir « Ce que ce déploiement ne donne pas ».

## 7. Les images

`docs/DEPLOIEMENT.md` §3.6. Les manifestes tirent
`ghcr.io/hectorberi01/<service>:main`. Le workflow doit avoir tourné sur `main`
pour les huit images, sinon les pods restent en `ImagePullBackOff`.

## 8. Déployer

```bash
kustomize version                  # v5 minimum — v4 ignore `includeTemplates`
./scripts/preflight-k8s.sh staging --cluster
kubectl apply -k k8s/overlays/staging
kubectl -n hba-staging rollout status deploy --timeout=10m
```

**`kubectl apply -k` n'écrase plus les secrets.** Les deux fichiers de Secret sont
sortis du build : tant qu'ils y étaient, cette commande réécrivait avec des
valeurs vides ce que les étapes 4 et 5 venaient de poser, en annonçant
`secret/hba-platform configured`.

## 9. Les migrations

`docs/DEPLOIEMENT.md` §3.8 — bascule temporaire, hors Git. **Ce n'est pas la
solution, c'est l'amorçage** : un Job de migration par service reste à écrire
avant la production.

## 10. Vérifier

```bash
kubectl -n hba-staging get pods
kubectl -n hba-staging get certificate     # READY=True, sinon l'ACME est bloqué
kubectl -n hba-staging get deploy otel-collector
kubectl -n hba-staging port-forward svc/otel-collector 8889:8889
curl -fsS http://127.0.0.1:8889/metrics | head
kubectl -n hba-staging logs deploy/identity-service | head -50
```

---

## Ce que ce déploiement ne donne pas

- **Supervision minimale seulement.** Le collecteur OTLP interne est déployé et
  reçoit les métriques/traces des services. Prometheus, Grafana, Loki et Tempo
  restent à installer par Helm pour avoir dashboards, alertes et historique long.
- **Aucune sauvegarde.** Décision explicite, §3.11. La base vit sur un seul VPS,
  sans pgBackRest, sans réplique.
- **Aucune séparation entre l'applicatif et les données.** La base partage la
  machine, le disque et la mémoire des huit pods. Un service qui s'emballe et
  atteint sa limite mémoire pèse sur PostgreSQL, et l'inverse est vrai. C'est
  tenable en pré-production, et c'est précisément ce que la production évite en
  gardant la base sur 51.255.40.214.
- **Le cluster n'est pas reconstructible depuis Git seul.** Les deux Secrets sont
  créés à la main. Perdre le namespace, c'est perdre les valeurs — sauf si elles
  sont conservées ailleurs qu'un fichier sur le poste.
- **Marketplace n'est pas déployé.** Six lignes commentées dans
  `k8s/base/services/kustomization.yaml`. Leurs clés d'identité sont déjà
  déclarées dans le contrat de secret, pour que le lot suivant n'ait pas à
  découvrir l'oubli sur un cluster en marche.
