# Runbook — premier déploiement staging

VPS applicatif **193.168.145.162** (k3s) · VPS base **51.255.40.214** (PostgreSQL)
· domaine common seul, 7 services + la passerelle.

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
l'étape 2. **`H='Host=193.168.145.162;Port=5432'` pour le staging**, et non le
`10.0.0.1` du §3.7, qui décrit la production.

Pourquoi pas `localhost` : ces chaînes sont lues par des pods, pas par l'hôte.
Dans un conteneur, `localhost` désigne le pod lui-même.

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

## 7. Les images

`docs/DEPLOIEMENT.md` §3.6. Les manifestes tirent
`ghcr.io/hectorberi01/<service>:main`. Le workflow doit avoir tourné sur `main`
pour les huit images, sinon les pods restent en `ImagePullBackOff`.

## 8. Déployer

```bash
kustomize version                  # v5 minimum — v4 ignore `includeTemplates`
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
kubectl -n hba-staging logs deploy/identity-service | head -50
```

---

## Ce que ce déploiement ne donne pas

- **Aucune supervision.** `OPENTELEMETRY__ENDPOINT` est vide : aucun collecteur
  n'est déployé, et pointer un nom qui ne résout pas remplirait les journaux
  d'échecs de connexion. Le diagnostic repose sur `kubectl logs` et les sondes.
  Tenable à un nœud, plus en production.
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
