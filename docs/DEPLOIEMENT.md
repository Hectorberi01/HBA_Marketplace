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

# Déploiement et vérification — local, dev, pré-production, production

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


> **Ce document décrit les quatre étages dans l'ordre où on les traverse.**
> Chaque étage a une question à laquelle il répond, et une seule. Sauter un
> étage, c'est reporter sa question à l'étage suivant — où elle coûte dix fois
> plus cher à instruire.

| Étage | Où | Question à laquelle il répond | Prouvé aujourd'hui |
|---|---|---|---|
| **Local** | poste, `docker compose` | est-ce que ça compile, démarre et se parle ? | oui |
| **Dev** | cluster k3d jetable | les manifests Kubernetes sont-ils justes ? | partiellement |
| **Pré-prod (staging)** | VPS OVH, k3s | est-ce que ça tient sous charge, avec TLS et sauvegardes ? | **non** |
| **Prod** | VPS OVH, k3s | promotion d'une image déjà validée, rien d'autre | **non** |

**Les deux dernières colonnes disent la vérité, et il faut la lire.** Le code
Terraform et Ansible de `infra/` n'a **jamais été appliqué** : pas d'identifiants
OVH, donc pas de `terraform plan`, pas de `ansible-playbook`. Sa syntaxe et son
câblage sont vérifiés à chaque `check-all.sh` (le contrôle `infra`) ; son
comportement ne l'est pas. Les étages 3 et 4 sont une **procédure proposée**, à
relire avant le premier passage — pas un mode d'emploi éprouvé.

---

## Étage 0 — le contrôle qui se paie en secondes

À lancer avant tout, à chaque étage, y compris avant un déploiement en
production :

```bash
./scripts/check-all.sh
```

Neuf contrôles bloquants. Chacun est né d'une panne réelle et attrape une classe
d'erreurs que le compilateur ne voit pas : une dépendance injectée que personne
ne fournit, un `using` manquant sur un namespace frère, une table configurée sans
migration, une clé d'environnement qui ne lie rien, un manifeste dont l'overlay
défait ce que la base garantissait, un workflow au YAML invalide, un module
Terraform mal câblé.

**Trente secondes ici, contre une demi-heure de `dev-up.sh` et, plus loin, une
reconstruction de cluster.** C'est le meilleur rapport du dépôt.

---

## Étage 1 — LOCAL

**Question : est-ce que ça compile, démarre, et est-ce que les services se
parlent ?**

### 1.1 Compiler et tester

```bash
make restore
make build
make test
```

### 1.2 Démarrer la pile

```bash
./scripts/dev-up.sh            # construit les 14 images, une par une, puis démarre
./scripts/dev-up.sh --fresh    # + supprime les volumes : bases reconstruites à neuf
```

**Ne pas remplacer par `docker compose up --build`.** Les quatorze images se
construiraient en parallèle, chacune lançant un SDK .NET ; sur une machine à 8 Go
alloués à Docker, le noyau en tue une au hasard et BuildKit rend
`ResourceExhausted: cannot allocate memory` — sur un service différent à chaque
essai, sans désigner aucun fichier. On cherche alors un problème de code là où il
n'y en a pas. `dev-up.sh` construit séquentiellement : le pic mémoire vaut celui
d'**une** image.

**`--fresh` n'est pas optionnel après un ajout de base.**
`postgres/init/001-create-databases.sql` n'est joué qu'au **premier** démarrage du
volume — `docker-entrypoint-initdb.d` est ignoré si les données existent déjà.
Sans `-v`, une base ajoutée au script n'apparaîtra jamais, et le service qui la
cherche échouera sur une erreur de connexion qu'on imputera au réseau.

### 1.3 Ce qu'on vérifie ici, et rien de plus

```bash
make ps                          # 14 services + infra en « healthy »
make logs S=identity-service     # un démarrage propre, pas d'exception au boot
curl -fsS localhost:8080/health  # la passerelle répond
./scripts/seed-accounts.sh && ./scripts/seed-stores.sh && ./scripts/seed-catalog-categories.sh
```

Puis un parcours de bout en bout à la main : créer un compte, poser une boutique,
un produit, une commande, un paiement, une livraison. C'est le seul étage où
c'est rapide.

**En local, les migrations s'appliquent AU DÉMARRAGE.**
`Database:MigrateOnStartup` n'est vrai qu'en `Development`. C'est ce qui rend cet
étage jetable — et c'est aussi ce qui masque un défaut de migration : un schéma
construit par accumulation de démarrages n'est pas un schéma construit à froid.
D'où le contrôle `check-migrations.py`, et d'où l'étage suivant.

### 1.4 Ce que l'étage local ne peut pas dire

- rien sur les sondes, les limites de ressources, les NetworkPolicies ;
- rien sur le comportement à plusieurs replicas — un verrou pris en mémoire au
  lieu de Redis passe ici sans bruit ;
- rien sur TLS, ni sur l'Ingress.

---

## Étage 2 — DEV

**Question : les manifests Kubernetes sont-ils justes ?**

**Terraform ne provisionne que staging et production.** L'environnement `dev`
n'a pas d'infrastructure dédiée : il est prévu pour un cluster **jetable local**
(k3d ou kind). C'est délibéré — un cluster dev partagé devient un second
environnement de production que personne n'entretient — mais cela veut dire que
la commande ci-dessous crée le cluster en même temps que le déploiement.

### 2.1 Vérifier les manifests SANS cluster

```bash
make k8s-check      # construit les trois overlays et vérifie le cahier
```

**Ce contrôle construit vraiment les overlays.** Lire `k8s/base/` ne prouve
rien : c'est l'overlay qui décide, et un patch peut défaire en silence ce que la
base garantissait. La vérification porte sur le **résultat** — non-root, les trois
sondes, requests/limits, pas de `latest` en production, deny-by-default présent,
aucun secret en clair, unicité des hôtes entre overlays.

### 2.2 Un cluster jetable

```bash
k3d cluster create hba-dev --agents 1 -p "8080:80@loadbalancer"
```

### 2.3 Les opérateurs, AVANT les charges

```bash
# Ingress — les manifests déclarent `ingressClassName: nginx`
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/cloud/deploy.yaml

# Kafka
kubectl create namespace hba-dev
kubectl apply -f https://strimzi.io/install/latest?namespace=hba-dev -n hba-dev
```

**Sans le CRD de Strimzi, `kubectl apply -k` échoue sur
`no matches for kind "Kafka"`.** Le message ne dit pas qu'il manque un
opérateur — il ressemble à une faute de frappe dans un manifeste.

# ═══════════════════════════════════════════════════════════════════════════════
**CLOUDNATIVEPG N'EST PLUS INSTALLÉ, ET CET ÉTAGE N'A PLUS DE BASE À LUI.**

Les manifestes Postgres ont été retirés de `k8s/` : la base vit sur un VPS
séparé (§3.4). L'opérateur n'a donc plus rien à piloter — l'installer créerait un
contrôleur sans ressource à gérer.

**CONSÉQUENCE POUR CET ÉTAGE, ET ELLE N'EST PAS RÉSOLUE ICI.** L'overlay `dev`
ne crée plus aucun Postgres. Trois façons de s'en sortir, aucune n'étant décidée :

  • **La voie courte : ne pas utiliser cet overlay.** L'environnement de
    développement réel est `docker-compose.dev.yml`, qui porte son propre
    Postgres et ses quatorze bases. L'étage 2 sert alors à éprouver les
    MANIFESTES, pas à faire tourner l'application — et dans ce cas il suffit de
    vérifier que les pods démarrent et échouent proprement sur la base absente.

  • **Un Postgres jetable dans le cluster k3d**, posé à la main, hors Kustomize.
    Suffisant pour un cluster de validation qu'on détruit chaque soir.

  • **Le même VPS qu'en staging, avec des bases préfixées `hba_dev_`.** Le §2
    exige des bases distinctes par environnement — préfixer les respecte à la
    lettre. Mais deux environnements sur une machine partagent son sort : une
    saturation de disque en dev arrête staging.

À trancher avant de se servir de cet étage. En attendant, il ne fait pas ce que
la suite de cette section décrit.
# ═══════════════════════════════════════════════════════════════════════════════

**cert-manager n'est pas nécessaire en dev**, et l'Ingress y servira donc un
certificat auto-signé. Attendu. En staging et en production, il l'est.

### 2.4 Le secret — l'étape qu'on découvre en échouant

`k8s/base/common/secret.yaml` est **vide par construction** : il déclare les noms
de clés, jamais les valeurs (§12). En dev, on le remplit à la main :

**UNE CHAÎNE PAR SERVICE, ET NON PLUS UNE SEULE.** Cette commande n'en posait
qu'une, `CONNECTIONSTRINGS__DEFAULT`, montée à l'identique sur tous les pods par
`envFrom` : les quatorze bases créées par le Job `postgres-databases` restaient
vides et les treize services migraient dans la même. Rien ne l'aurait signalé —
chaque `DbContext` porte son propre schéma, donc les migrations s'empilent sans
conflit visible. Chaque service reprend désormais SA clé (voir le patch de
`k8s/base/services/<nom>/kustomization.yaml`).

**Deux clés manquaient aussi.** `SECURITY__SECRETPROTECTION__KEY` — sans elle les
codes de vérification repartent en clair sur Kafka, et `AesGcmSecretProtector`
refuse de démarrer hors développement. Elle doit être **identique** pour identity
et notification, ce que ce Secret partagé garantit par construction.

```bash
PG='Host=10.0.0.1;Port=5432;Database='

kubectl create secret generic hba-platform -n hba-dev \
  --from-literal=CONNECTIONSTRINGS__IDENTITY="${PG}hba_identity" \
  --from-literal=CONNECTIONSTRINGS__USER="${PG}hba_user" \
  --from-literal=CONNECTIONSTRINGS__MEDIA="${PG}hba_media" \
  --from-literal=CONNECTIONSTRINGS__NOTIFICATION="${PG}hba_communication" \
  --from-literal=CONNECTIONSTRINGS__PAYMENT="${PG}hba_financial" \
  --from-literal=CONNECTIONSTRINGS__PROMOTION="${PG}hba_promotion" \
  --from-literal=CONNECTIONSTRINGS__REVIEW="${PG}hba_engagement" \
  --from-literal=CONNECTIONSTRINGS__CATALOG="${PG}hba_catalog" \
  --from-literal=CONNECTIONSTRINGS__CART="${PG}hba_commerce" \
  --from-literal=CONNECTIONSTRINGS__INVENTORY="${PG}hba_inventory" \
  --from-literal=CONNECTIONSTRINGS__ORDER="${PG}hba_order" \
  --from-literal=CONNECTIONSTRINGS__SELLER="${PG}hba_merchant" \
  --from-literal=CONNECTIONSTRINGS__RETURNREFUND="${PG}hba_commerce" \
  --from-literal=CONNECTIONSTRINGS__DEFAULT="${PG}hba_identity" \
  --from-literal=REDIS__CONNECTIONSTRING='redis:6379' \
  --from-literal=AUTHENTICATION__SIGNINGKEY="$(openssl rand -base64 48)" \
  --from-literal=INTERNAL__APIKEY="$(openssl rand -hex 32)" \
  --from-literal=SECURITY__SECRETPROTECTION__KEY="$(openssl rand -base64 32)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

**L'hôte dépend de la voie retenue ci-dessus** (§2.3) : `10.0.0.1` pour la base
externe, un Service local pour un Postgres jetable. Ce qui ne change pas, c'est
qu'il faut une chaîne PAR SERVICE : une seule les ferait tous migrer dans la même
base, sans qu'aucune erreur ne le dise.

### 2.5 Déployer et vérifier

```bash
kubectl apply -k k8s/overlays/dev
kubectl -n hba-dev rollout status deploy --timeout=5m
kubectl -n hba-dev get pods
```

Ce qu'on regarde, dans cet ordre :

1. **Tous les pods `Running`** — un `CrashLoopBackOff` ici est presque toujours
   une clé de configuration manquante, pas un bug applicatif.
2. **Les migrations à froid.** `ASPNETCORE_ENVIRONMENT=Development` les applique
   au démarrage sur une base neuve : c'est **le** test que l'étage local ne fait
   pas. Un `column does not exist` ici veut dire qu'une migration a été écrite en
   regardant une base existante.
3. **Les NetworkPolicies mordent-elles vraiment ?**

```bash
kubectl -n hba-dev run sonde --rm -it --image=busybox --restart=Never -- \
  timeout 5 nc -zv redis 6379
```

**La sonde visait `postgres-rw`, qui n'existe plus** — la base est hors cluster.
Redis reste un datastore du namespace et porte la même étiquette : le test garde
son sens. Pour éprouver la règle d'egress vers la base externe, viser plutôt
`10.0.0.1 5432` depuis un pod SANS l'étiquette `part-of: hba-express` — cette
connexion doit expirer.

**Cette connexion DOIT échouer** depuis un pod sans étiquette autorisée. Si
elle s'ouvre, `k8s/base/policies/` ne sert à rien — les objets existent,
`kubectl get netpol` les affiche, et aucun paquet n'est filtré. C'est la panne la
plus silencieuse de tout `k8s/`. Sur k3s, le contrôleur de kube-router les
applique par défaut ; k3d en hérite. Sur un cluster dont le CNI ne les gère pas,
il faut Calico ou Cilium.

4. **Les topics Kafka existent** — ils sont générés depuis les attributs
   `[HbaEvent]` du code, pas écrits à la main :

```bash
kubectl -n hba-dev get kafkatopics
```

---

## Étage 3 — PRÉ-PRODUCTION (staging)

**Question : est-ce que ça tient avec TLS, à plusieurs nœuds, sous charge, et
est-ce qu'on sait restaurer ?**

**Tout cet étage est non éprouvé.** Compter une première fois longue, et la
faire à deux.

### 3.1 Provisionner les machines

```bash
export OVH_APPLICATION_KEY=... OVH_APPLICATION_SECRET=... OVH_CONSUMER_KEY=...
export OS_AUTH_URL=https://auth.cloud.ovh.net/v3 OS_USERNAME=... OS_PASSWORD=... OS_TENANT_ID=...
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=...      # clés S3 OVH, pour l'état

cd infra/terraform/environments/staging
cp terraform.tfvars.example terraform.tfvars     # puis renseigner
terraform init
terraform plan -out=plan.tfplan                  # à LIRE, entièrement
terraform apply plan.tfplan
```

**Au tout premier passage, l'œuf et la poule.** Le bucket d'état est créé par
ce code, et le backend veut y écrire. La séquence d'amorçage — commenter le
backend, appliquer `-target=module.object_storage`, décommenter,
`init -migrate-state`, **puis supprimer le `terraform.tfstate` local** — est
détaillée en tête de `environments/staging/main.tf`. L'étape oubliée est
toujours la dernière : l'état local reste, quelqu'un applique sans backend, et
les deux états divergent en silence.

### 3.2 Installer k3s

```bash
terraform output -json noeuds        # ip_public / ip_prive
cd ../../../ansible
cp inventory/staging.yml.example inventory/staging.yml   # puis reporter les IP
ssh-keyscan -H <ip1> <ip2> >> ~/.ssh/known_hosts
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
```

**`--check` ment sur un cluster neuf.** Les tâches agents dépendent du jeton
que l'installation du serveur produit ; en simulation, ce jeton n'existe pas.
Un `--check` rouge sur des machines vierges n'est pas une alerte. Il ne devient
informatif qu'ensuite, pour dire ce qui a dérivé.

Le playbook rapatrie `kubeconfig-staging.yaml`. **C'est un accès administrateur
complet, sans expiration** — à traiter comme un mot de passe, et `.gitignore`
l'exclut.

### 3.3 DNS et TLS, dans cet ordre

Terraform a posé l'enregistrement A. Vérifier qu'il **résout** avant d'installer
cert-manager :

```bash
./scripts/check-dns-ingress.sh staging
```

**Un challenge ACME lancé sur un DNS qui ne résout pas encore échoue, et
cert-manager réessaie avec un recul croissant — jusqu'à une heure d'attente pour
un enregistrement posé deux minutes trop tard.** Attendre la propagation coûte
cinq minutes ; ne pas l'attendre en coûte soixante.

Puis les opérateurs (§2.3) **plus** cert-manager et un ClusterIssuer nommé
`letsencrypt` — c'est le nom que l'annotation de l'Ingress attend. Sans lui,
l'Ingress est créé, le Secret TLS ne l'est jamais, et le contrôleur sert un
certificat auto-signé : le navigateur avertit, l'application mobile échoue sans
message lisible.

**Remplacer les domaines `.example` des trois overlays.** `.example` est
réservé (RFC 2606) et ne résoudra jamais — c'est délibéré, un placeholder qui
résout est un placeholder qu'on oublie de remplacer. `check-k8s.py` les liste à
chaque exécution.

### 3.4 La base de données — un VPS séparé, joint par le lien privé

**La base ne tourne PAS dans le cluster.** Postgres est installé à la main sur un
second VPS. Les manifestes CloudNativePG ont été retirés de `k8s/` — les garder
aurait créé un second Postgres, démarré, vide, et que personne n'utilise : un
cluster CNPG sain sans aucune connexion ressemble à un cluster au repos.

**CE QUE CE RETRAIT EMPORTE.** La création des quatorze bases se fait désormais à
la main (ci-dessous), et **il n'y a plus aucune sauvegarde** — voir §3.10.

#### 3.4.1 Relever l'état de la machine avant d'y toucher

Les chemins de configuration diffèrent selon la distribution, et les supposer est
le meilleur moyen d'éditer un fichier que Postgres ne lit pas.

```bash
ssh root@51.255.40.214
psql -V
sudo -u postgres psql -tAc 'SHOW config_file'      # postgresql.conf réel
sudo -u postgres psql -tAc 'SHOW hba_file'         # pg_hba.conf réel
sudo -u postgres psql -tAc 'SHOW listen_addresses'
ss -lntp | grep 5432                               # sur quelles interfaces, aujourd'hui
```

**Si `ss` montre `0.0.0.0:5432`, la base écoute déjà sur Internet.** À vérifier
avant tout le reste : une installation par défaut avec un mot de passe faible et
5432 ouvert se trouve en quelques heures. Le tunnel ci-dessous ferme cela.

#### 3.4.2 Le lien privé — PRODUCTION UNIQUEMENT, et il n'existe pas encore

> **LE STAGING N'EST PLUS CONCERNÉ PAR CETTE SECTION.** Décision du 27 août : sa
> base tourne sur le VPS applicatif lui-même (193.168.145.162), sans tunnel. La
> procédure d'accès y est dans `docs/RUNBOOK-STAGING.md`, étape 0 —
> `listen_addresses`, `pg_hba` et la règle nftables que pose Ansible.
>
> Ce qui suit décrit la topologie de PRODUCTION : deux VPS, la base sur
> 51.255.40.214, joints par WireGuard. Les overlays `dev` et `prod` gardent le
> `10.0.0.1/32` dans leur NetworkPolicy ; seul `staging` le remplace par l'IP de
> son nœud.

**CETTE SECTION A DIT DEUX CHOSES FAUSSES, SUCCESSIVEMENT.** Elle a d'abord décrit
un tunnel en `10.88.0.x` qui n'a jamais existé. Corrigée, elle a ensuite affirmé
qu'un tunnel en `10.0.0.x` était « en place » et qu'il suffisait de le vérifier.
Il ne l'est pas. Une procédure qui décrit une autre machine que la vôtre est pire
qu'une page vide : on la suit, elle échoue, et on cherche l'erreur dans ce qu'on
vient de taper.

Les deux machines :

| | IP publique | IP tunnel | rôle WireGuard |
|---|---|---|---|
| VPS applicatif (k3s) | 193.168.145.162 | `10.0.0.2` | **initiateur** |
| VPS base (PostgreSQL) | 51.255.40.214 | `10.0.0.1` | **répondeur** |

**`10.0.0.1` EST LA BASE — CONFIRMÉ, PAS DÉDUIT.** Le plan a d'abord été lu comme
une hypothèse (l'adresse avait été donnée en désignant la base) ; elle a ensuite
été confirmée explicitement. L'applicatif porte `.2`.

Le `psql -h 10.0.0.1` de la fin de cette section reste néanmoins la PREMIÈRE
chose à lancer. Non plus pour trancher, mais parce qu'un tunnel monté à l'envers,
un `listen_addresses` oublié ou un `pg_hba` trop étroit se manifestent tous là —
avant qu'une seule base, un seul secret ou un seul pod n'existe.

**LE PLAN DU TUNNEL RECOUVRE CELUI DE TERRAFORM. AUJOURD'HUI SANS CONSÉQUENCE.**

`infra/terraform/modules/network` déclare le réseau privé OVH en `10.0.0.0/16`,
et `inventory/staging.yml.example` y place ses nœuds en `10.0.0.11` / `10.0.0.12`.
Le tunnel occupe `10.0.0.0/24` — un sous-réseau de ce `/16`.

Rien n'entre en conflit tant que ce réseau OVH n'existe pas : la topologie réelle
est deux VPS joints par le seul tunnel, et l'inventaire employé
(`inventory/staging.yml`) ne porte que des adresses publiques.

Le jour où ce réseau est provisionné, deux choses se produisent, et aucune ne
lève d'erreur. La route `/16` de l'interface privée et l'`AllowedIps` en `/32` du
tunnel coexistent — le `/32` gagne, la base reste joignable. Mais un hôte OVH qui
recevrait `10.0.0.1` deviendrait inatteignable depuis le VPS applicatif, et
`pg_hba.conf` autorisant `10.0.0.0/24` couvrirait alors des machines du réseau
OVH en plus du tunnel. **Choisir un préfixe hors du `/16` — `10.100.0.0/24` par
exemple — avant de provisionner, pas après.**

**TROIS ENDROITS PORTENT CETTE ADRESSE, ET CHANGENT ENSEMBLE** le jour où le plan
bouge : `k8s/base/policies/network-policies.yaml` (le `/32` d'egress), les chaînes
de connexion du Secret (§3.7), et `listen_addresses` / `pg_hba.conf` (§3.4.3).
**En oublier un donne un délai d'attente, pas un refus** — et le diagnostic part
vers le pare-feu, où il n'y a rien à trouver.

**QUI INITIE N'EST PAS UN DÉTAIL DE STYLE.** nftables, sur le VPS applicatif,
jette tout ce qui entre sauf 22, 80, 443 et l'interface du tunnel — mais il
accepte `ct state established,related`. Un tunnel que le VPS applicatif OUVRE
fonctionne donc sans ouvrir le moindre port de plus. L'inverse — le VPS base
initiant — exigerait d'ouvrir UDP 51820 en entrée sur la machine exposée à
Internet. On choisit le sens qui n'ouvre rien.

##### Sur les deux machines

```bash
sudo apt-get update && sudo apt-get install -y wireguard
umask 077
wg genkey | sudo tee /etc/wireguard/prive.key | wg pubkey | sudo tee /etc/wireguard/public.key
```

Relever les deux clés PUBLIQUES : chacune va dans la configuration de l'autre.
La clé privée ne quitte jamais sa machine.

##### VPS base — 51.255.40.214, le répondeur

`/etc/wireguard/wg0.conf` :

```ini
[Interface]
Address = 10.0.0.1/24
ListenPort = 51820
PostUp = wg set %i private-key /etc/wireguard/prive.key

[Peer]
# VPS applicatif
PublicKey = <clé publique du VPS applicatif>
AllowedIps = 10.0.0.2/32
```

Pas d'`Endpoint` côté répondeur : il apprend l'adresse du pair au premier
handshake. C'est ce qui rend l'initiateur libre de changer d'IP.

Le pare-feu de cette machine — celui que vous gérez à la main, ce playbook n'y
touche pas — doit accepter **UDP 51820** depuis 193.168.145.162, et **rien**
sur 5432 depuis l'extérieur du tunnel.

##### VPS applicatif — 193.168.145.162, l'initiateur

`/etc/wireguard/wg0.conf` :

```ini
[Interface]
Address = 10.0.0.2/24
PostUp = wg set %i private-key /etc/wireguard/prive.key

[Peer]
# VPS base
PublicKey = <clé publique du VPS base>
Endpoint = 51.255.40.214:51820
AllowedIps = 10.0.0.1/32
PersistentKeepalive = 25
```

**`AllowedIps` EN `/32`, PAS `10.0.0.0/24`.** WireGuard s'en sert comme table de
routage : un `/24` enverrait dans le tunnel tout le sous-réseau, y compris des
adresses que personne ne sert. Le symptôme serait un délai d'attente, jamais un
refus.

**`PersistentKeepalive = 25` N'EST PAS FACULTATIF ICI.** Sans lui, un tunnel
inactif se referme silencieusement derrière un NAT ou un pare-feu à état : la
première requête après une accalmie expire, les suivantes passent. Un pod
Kubernetes redémarre alors en boucle sur une sonde qui échoue une fois sur dix —
la panne intermittente qui ne se reproduit jamais quand on la cherche.

Démarrer, des deux côtés :

```bash
sudo systemctl enable --now wg-quick@wg0
```

##### Vérifier — depuis le VPS applicatif, et dans cet ordre

```bash
wg show                     # « latest handshake » de quelques secondes
ping -c3 10.0.0.1
ss -lntp | grep 5432        # sur le VPS BASE : 10.0.0.1:5432, JAMAIS 0.0.0.0
psql -h 10.0.0.1 -p 5432 -U hector -d postgres -c 'SELECT 1'
```

**`0.0.0.0:5432` signifie que la base écoute sur Internet.** Une installation par
défaut avec un mot de passe faible s'y trouve en quelques heures. C'est la seule
ligne de cette section qui puisse coûter la base entière.

**CE QUE CE TUNNEL NE FAIT PAS.** Il ne chiffre que ce qui passe par `10.0.0.x`.
Il ne survit pas à un redémarrage si `wg-quick@wg0` n'est pas `enable` — d'où le
`--now` ci-dessus, qui fait les deux. Et il n'entre PAS dans l'inventaire
Ansible : `interface_privee: wg0` dit à nftables d'accepter cette interface, rien
de plus. Le tunnel se monte à la main, avant Ansible.

#### 3.4.3 Postgres n'écoute que sur le tunnel

Dans le `postgresql.conf` relevé en 3.4.1 :

```ini
listen_addresses = 'localhost,10.0.0.1'
password_encryption = scram-sha-256
```

**`localhost` reste, et ce n'est pas de la négligence.** Le retirer coupe l'accès
local par TCP — dont `psql -h localhost`, les scripts de maintenance et la sonde
de bien des outils. Le socket Unix continue de fonctionner, ce qui masque le
problème jusqu'au premier outil qui passe par TCP.

Dans le `pg_hba.conf` relevé en 3.4.1, **avant** les lignes par défaut :

```
# base                utilisateur        source          méthode
host  all             all                10.0.0.0/24    scram-sha-256
```

**`scram-sha-256` et non `md5`.** Une installation ancienne peut encore proposer
md5 ; il est cassé et Postgres 14+ ne l'utilise plus par défaut. Si les rôles ont
été créés avant le changement de `password_encryption`, **leur mot de passe reste
stocké en md5** et la connexion échouera en scram : il faut le réattribuer une
fois (`\password <role>`), ce qui le réencode.

```bash
systemctl reload postgresql
sudo -u postgres psql -tAc 'SHOW listen_addresses'
ss -lntp | grep 5432            # 10.0.0.1:5432 et 127.0.0.1:5432, JAMAIS 0.0.0.0
```

#### 3.4.4 Les quatorze bases et leurs rôles

```bash
# sur le VPS de base
sudo -u postgres /chemin/vers/HBA/scripts/db/creer-bases.sh --simulation   # à blanc
sudo -u postgres /chemin/vers/HBA/scripts/db/creer-bases.sh
```

**TROIS FICHIERS, ET LE CHOIX DÉPEND DE L'OUTIL — PAS DU GOÛT.**

| Fichier | Client | Nombre d'exécutions |
|---|---|---|
| `creer-bases.sh` | shell sur le VPS | 1 |
| `creer-bases.sql` | **psql uniquement** | 1 |
| `creer-bases-pgadmin.sql` | pgAdmin, DBeaver, tout client SQL | 3 + 14 lignes |

**`creer-bases.sql` NE PASSE PAS DANS pgAdmin.** Il emploie `\set`, `\gexec` et
`\echo` : ce sont des commandes du CLIENT psql, pas du langage SQL. pgAdmin
transmet le texte tel quel au serveur, qui répond `syntax error at or near "\"`
dès la première. Ce n'est pas une faute de frappe dans le fichier — c'est le
mauvais client.

**ET pgAdmin NE PEUT PAS CRÉER LES QUATORZE BASES EN UN ENVOI.** `CREATE DATABASE`
refuse d'être dans une transaction, or pgAdmin envoie tout l'éditeur en UNE
requête, et une requête à plusieurs instructions EST une transaction implicite :

```
ERROR: CREATE DATABASE cannot run inside a transaction block
```

D'où la partie 2 de la version pgAdmin, à jouer ligne par ligne — sélectionner la
ligne, F5. Quatorze fois. `psql` fait le tout en une commande, sur le VPS où
PostgreSQL est déjà installé : c'est la voie courte.

Un rôle par base : un service compromis ne lit pas les tables d'un autre. C'est ce
que ne donnait pas le compte `hba` unique du développement.

**Le script ne crée AUCUN schéma ni AUCUNE table** — ce sont les migrations, à
l'étape de release (§3.8). Une base créée ici est vide, et c'est normal.

**IL EST REJOUABLE, ET LES MOTS DE PASSE EXISTANTS NE BOUGENT PAS.** Un rôle déjà
présent garde le sien : rejouer le script pour ajouter une base ne périme pas le
Secret Kubernetes en place. Régénérer se demande explicitement, avec `--rotation`,
et impose alors de reconstruire le Secret.

**Ce qu'il vérifie avant de rendre la main** : chaque rôle se connecte à sa base,
**et** un rôle est refusé sur celle d'un autre. La seconde question compte autant
que la première — PostgreSQL accorde `CONNECT` à `PUBLIC` sur toute base nouvelle,
et sans le `REVOKE` les quatorze rôles atteindraient les quatorze bases. Créer un
rôle par service sans révoquer PUBLIC donne l'apparence du cloisonnement et aucune
de ses propriétés.

Il refuse de démarrer si `password_encryption` ne vaut pas `scram-sha-256` : un
rôle créé sous `md5` garde un mot de passe md5 même après bascule du paramètre, et
`pg_hba.conf` en scram le refusera — l'erreur, « password authentication failed »,
ressemble à un mot de passe faux, on le regénère, et ça échoue encore.

**Les mots de passe sortent dans un fichier en 0600**, jamais sur la sortie
standard. Les recopier dans le gestionnaire, construire le Secret (§3.7), **puis
supprimer le fichier** — le script le rappelle en terminant.

**`cart-service` et `return-refund-service` partagent `hba_commerce`**, donc le
rôle `hba_commerce` : c'est la seule paire dans ce cas, et c'est l'état réel du
code — deux schémas dans une base.

**`hba_delivery` et `hba_food` sont créées maintenant** bien que leurs services
soient au lot suivant : une base vide ne coûte rien, et l'oubli se paierait à un
moment où l'on ne pense plus à cette page.

##### Pourquoi un script, alors que deux mécanismes existaient

Les deux échouent, et aucun ne le dit :

- `infra/postgres/init/001-create-databases.sql` **ne vaut que pour la pile de
  développement**, et il y était lui-même inopérant : le compose montait le
  dossier PARENT sur `/docker-entrypoint-initdb.d`, et le point d'entrée de
  l'image postgres ignore les sous-dossiers. Il lui manquait en outre
  `hba_promotion` — treize bases pour quatorze. Les deux défauts sont corrigés
  (le fichier a quitté `infra/docker/`, retiré du dépôt), et `check-infra.py`
  compare désormais les bases injectées aux bases créées. **Cela ne change rien
  à la production** : ce script n'y tourne pas, l'image postgres n'y est pas
  employée — Postgres y est installé à la main.
- **En développement, ça marche quand même**, et c'est ce qui masque tout :
  `Database.Migrate()` d'EF Core crée la base absente avant d'appliquer les
  migrations. Le défaut ci-dessus n'a donc jamais eu de symptôme.

En production, `MigrateOnStartup` vaut faux (§15) : plus personne ne crée rien, et
les treize services échouent au démarrage sur « database does not exist ». C'est
le seul environnement où le défaut se voit, et le plus mauvais endroit pour le
découvrir.

#### 3.4.5 Vérifier depuis le VPS applicatif, pas depuis la base

```bash
# depuis le VPS APPLICATIF
psql "host=10.0.0.1 port=5432 dbname=hba_identity user=hba_identity password=<mdp>" -c 'SELECT 1'
```

**Tester depuis la machine de base ne prouve rien** : `localhost` emprunte une
autre ligne de `pg_hba.conf` et une autre interface. La seule question qui compte
est « le VPS applicatif atteint-il la base par le tunnel », et elle ne se pose que
de là.

#### 3.4.6 La règle d'egress Kubernetes, sans laquelle rien ne passe

`k8s/base/policies/` n'autorise la sortie que vers les pods du namespace. La base
étant hors cluster, une exception `ipBlock: 10.0.0.1/32` sur le port 5432 a été
ajoutée. **Si l'IP du tunnel change, elle est à changer là aussi** — sinon les
paquets sont jetés, pas rejetés : la connexion EXPIRE au lieu d'être refusée, et
le diagnostic part vers le pare-feu du VPS, où tout sera correct.

### 3.5 Le périmètre de ce premier déploiement — `common` seul

**Sept services**, plus la passerelle : identity, user, media, notification,
payment, promotion, review. `k8s/base/services/kustomization.yaml` ne liste
qu'eux ; les six de `marketplace` y sont écrits en commentaire, dossiers déjà
prêts — les décommenter est le seul geste du lot suivant.

**POURQUOI DÉCOUPER ENCORE.** Sept Deployments qui démarrent contre une base neuve,
c'est sept endroits où un défaut de configuration peut se voir. Treize d'un coup
double le bruit sans rien apprendre de plus, et le premier déploiement sert à
apprendre.

**CE QUI RÉPOND 502, ET CE N'EST PAS UNE PANNE.** La passerelle déclare les
adresses des seize services absents en `[Required, Url]` : elle démarre
normalement — la validation regarde la *forme* de l'adresse, pas son existence —
et chaque route vers eux répond 502. Sont hors périmètre de ce lot : catalogue,
panier, commandes, vendeurs, stock, retours, toute la livraison et toute la
restauration.

Concrètement, ce qui doit répondre après ce déploiement :

| Ce qui marche | Chemins |
|---|---|
| authentification | `/api/auth`, `/api/identity`, `/api/v1/auth` |
| comptes et géo | `/api/users`, `/api/geo` |
| médias | `/api/media` |
| notifications | `/api/notifications` |
| paiements et portefeuille | `/api/payments`, `/api/wallet`, `/api/financial/*` |
| promotions | `/api/v1/promotions` |
| avis, recommandations, envies | `/api/reviews`, `/api/recommendations`, `/api/wishlist` |

**Le Secret porte quand même les treize chaînes.** Les six de `marketplace` ne
serviront qu'au lot suivant, mais les déclarer maintenant évite de reconstruire le
Secret — et une clé inutilisée ne coûte rien, là où un Secret repris à la main
sous pression coûte une faute de frappe.

### 3.6 Le registre — ghcr.io, et le cluster doit pouvoir tirer

Les trois overlays portent `ghcr.io/hector-adjakpa/hba/<service>`. Sur un dépôt
privé, le tirage demande un identifiant : sans lui, les quatorze pods restent en
`ImagePullBackOff` avec « denied », qui ressemble à une image absente.

```bash
kubectl create secret docker-registry ghcr -n hba-staging \
  --docker-server=ghcr.io \
  --docker-username=<utilisateur-github> \
  --docker-password=<token-classique-avec-read:packages>

kubectl -n hba-staging patch serviceaccount default \
  -p '{"imagePullSecrets":[{"name":"ghcr"}]}'
```

**Le patch porte sur le ServiceAccount, pas sur les Deployments.** Chaque service
a le sien (`identity-`, `catalog-`…) : les patcher un par un ferait quatorze
commandes à retenir. Le gabarit ne les rattache pas encore à un `imagePullSecret`
commun — c'est le raccourci assumé de ce premier déploiement, et il est à
reprendre dans `_service/serviceaccount.yaml` avant la production.

### 3.7 Le secret — décision prise, et ce qu'elle coûte

**`kubectl create secret`, hors GitOps.** External Secrets/Vault reste la cible du
§25 ; pour un premier staging, la brique de coffre supplémentaire coûterait plus
qu'elle n'apporte.

**CE QUE CE CHOIX ABANDONNE, ET IL FAUT L'ÉCRIRE.** Le cluster cesse d'être
reconstructible depuis Git seul. Perdre le namespace, c'est perdre les valeurs :
il n'en existe aucune copie versionnée. Conserver ces valeurs ailleurs — un
gestionnaire de mots de passe, pas un fichier sur le poste — fait partie de la
manœuvre, sinon la reconstruction du cluster demandera de tout regénérer, et une
clé de protection des secrets regénérée rend **illisibles** les jetons déjà posés
dans l'outbox.

**À retrancher avant la production**, pas après.

**UN RÔLE ET UN MOT DE PASSE PAR SERVICE.** La base externe déclare quatorze
rôles (§3.4.4) ; chaque chaîne porte le sien. Reprendre un compte unique annulerait
l'isolation qu'on vient de poser côté Postgres, sans qu'aucune erreur ne le dise —
la connexion fonctionnerait très bien.

```bash
# staging
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
kubectl -n hba-staging apply -f ~/secrets-hba-staging/secret-hba-platform.yaml
./scripts/check-secrets-cluster.sh staging

# production
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
kubectl -n hba-prod apply -f ~/secrets-hba-prod/secret-hba-platform.yaml
./scripts/check-secrets-cluster.sh prod
```

**`CART` et `RETURNREFUND` portent la MÊME valeur** — même base, même rôle. Ce
n'est pas une faute de copie : les deux services écrivent dans `hba_commerce`,
schémas distincts, exactement comme en développement.

#### 3.7.1 Le second secret — les identités gRPC, sans lesquelles rien ne se parle

**CE QUI MANQUAIT, ET COMMENT ÇA SE SERAIT MANIFESTÉ.** `Internal:IdentitesNonSignees`
n'est pas posé hors développement, donc faux. Chaque hôte doit alors présenter une
attestation signée par SA clé privée, et vérifier celles des autres avec le
registre des clés publiques. Aucune des deux valeurs n'existait dans `k8s/`.

Le résultat n'aurait pas été un pod en échec. Les huit pods passent `Ready`, les
sondes sont vertes, `kubectl get pods` ne montre rien — et **chaque appel
inter-services** échoue en `FailedPrecondition: Internal identity not configured.`
Un cluster qui a l'air sain et qui ne fonctionne pas.

```bash
# 1. engendrer, HORS du dépôt
scripts/generer-identites-internes.sh ~/secrets-hba-staging

# 2. créer le Secret — les noms de clés sont exactement ceux du fichier
kubectl create secret generic hba-identites-internes -n hba-staging \
  --from-env-file=$HOME/secrets-hba-staging/identites.env \
  --dry-run=client -o yaml | kubectl apply -f -
```

**AUCUNE TRANSCRIPTION À LA MAIN**, et c'est le but : `--from-env-file` reprend le
fichier tel quel. Recopier vingt-quatre lignes de base64 de plusieurs centaines de
caractères produit tôt ou tard une ligne tronquée, et le symptôme —
`Unauthenticated` limité à un seul appelant — ne désigne pas le fichier.

**CE SECRET N'EST PAS MONTÉ PAR `envFrom`.** `hba-platform` l'est, et toutes ses
clés deviennent des variables dans tous les pods. Y placer les clés privées
donnerait à chaque conteneur l'identité des treize autres. Ici chaque Deployment
reprend la sienne par un `secretKeyRef` nommé — voir
`k8s/base/common/secret-identites.yaml`.

**LES DEUX FICHIERS DE SECRET SONT HORS DU BUILD KUSTOMIZE.** Ils restent dans le
dépôt comme contrat — la liste des clés attendues — mais
`k8s/base/common/kustomization.yaml` ne les liste plus en ressources. **Tant
qu'ils y étaient, `kubectl apply -k` de l'étape 3.9 réécrivait les deux Secrets
avec les valeurs vides du dépôt**, quelques minutes après une création qui avait
pourtant réussi. Rien ne l'aurait dit : `apply` annonce
`secret/hba-platform configured`, ce qui est exact.

**LA ROTATION SE FAIT SUR TOUS LES HÔTES ENSEMBLE.** Un hôte encore porteur de
l'ancien registre rejette les nouveaux appelants, et réciproquement : une rotation
partielle coupe les appels dans les deux sens.

### 3.8 Les migrations — l'étape que personne ne fournit

Le §15 veut la migration comme étape de release : `MigrateOnStartup` est faux
hors `Development`, et l'overlay staging n'y touche pas. **Il n'existe pourtant
aucun Job de migration dans `k8s/`.** Appliquer les manifestes sur des bases
neuves donnerait donc treize services démarrés et autant d'échecs sur des tables
absentes.

Pour l'amorçage, un basculement explicite et temporaire — qui ne touche pas Git :

```bash
kubectl -n hba-staging set env deploy --all DATABASE__MIGRATEONSTARTUP=true
kubectl -n hba-staging rollout status deploy --timeout=10m
kubectl -n hba-staging set env deploy --all DATABASE__MIGRATEONSTARTUP-
```

Un replica par service en staging : aucune course entre instances. La dernière
ligne retire la variable — l'oublier ferait migrer à chaque redémarrage de pod,
exactement ce que le §15 refuse.

**Ce n'est pas la solution, c'est l'amorçage.** Un Job de migration par service,
ordonné avant le déploiement, reste à écrire — décision à prendre avant la
production, où un pod qui migre au démarrage est un pod qui peut migrer deux fois.

### 3.9 Déployer

```bash
kubectl apply -k k8s/overlays/staging
kubectl -n hba-staging rollout status deploy --timeout=10m
kubectl -n hba-staging get certificate     # READY=True, sinon l'ACME est bloqué
```

**Ici, la migration est une étape de release, pas un effet de bord du
démarrage** (§15). `MigrateOnStartup` est faux hors `Development`.

### 3.10 Voir ce qui se passe dans le cluster

**D'ABORD LE TUNNEL, SINON RIEN NE RÉPOND.** `nftables` n'ouvre que 22, 80 et 443
sur l'interface publique : le port 6443 de l'API n'y est pas. Un `kubectl` lancé
sans tunnel reste suspendu jusqu'au délai d'attente, et rien dans son message ne
désigne le pare-feu.

```bash
ssh -N -L 6443:127.0.0.1:6443 root@193.168.145.162 &
export KUBECONFIG=infra/ansible/kubeconfig-staging.yaml
kubectl -n hba-staging get pods
```

Le kubeconfig rapatrié par Ansible pointe **déjà** `127.0.0.1` — rien à
réécrire. Le certificat le couvre : k3s inclut toujours cette adresse dans ses
SAN.

**SI `kubectl` DIT `localhost:8080`, C'EST QUE `KUBECONFIG` N'EST PAS POSÉ.**
Sans variable ni `~/.kube/config`, `kubectl` retombe sur une valeur historique —
`http://localhost:8080` — qui n'a jamais correspondu à ce cluster. Le message
« connection to the server localhost:8080 was refused » ne parle donc pas du
cluster : il dit qu'aucun cluster n'a été désigné.

**Le meilleur outil pendant un déploiement n'est pas une console web, c'est `k9s`.**
Il tourne sur VOTRE poste, contre le même kubeconfig, et ne pose rien dans le
cluster.

```bash
brew install k9s
k9s -n hba-staging
```

`:pods` pour la liste, `l` pour les journaux d'un pod, `d` pour le décrire, `y`
pour son YAML. C'est là que se lisent les trois échecs de démarrage les plus
fréquents : `ImagePullBackOff` (registre ou identifiant), `CreateContainerConfigError`
(une clé absente du Secret), et une sonde de disponibilité qui ne passe jamais
(la base injoignable — voir la règle d'egress, §3.4.6).

**OpenLens ou Lens** font la même chose en fenêtré, si vous préférez le visuel. Même
principe : ils lisent le kubeconfig, ils ne déploient rien.

#### Une console DANS le cluster, si vous en voulez une

`k8s/outils/acces-lecture.yaml` pose le namespace, le compte de service et le droit
de **lecture** qu'une console demande :

```bash
kubectl apply -f k8s/outils/acces-lecture.yaml
kubectl -n hba-outils create token console --duration 8h    # à coller dans la console
```

La console elle-même (Headlamp, Kubernetes Dashboard) se prend chez son auteur, à
la version que vous choisissez — la recopier dans ce dépôt en ferait une copie
périmée à la première mise à jour amont.

**NE JAMAIS LUI POSER D'INGRESS.** C'est le vecteur qui a servi à compromettre des
clusters entiers : un tableau de bord joignable depuis Internet donne, selon ses
droits, la lecture des secrets ou l'exécution dans les pods. Qu'il demande un jeton
ne suffit pas — la surface est publique. L'accès passe par le kubeconfig :

```bash
kubectl -n hba-outils port-forward svc/<console> 8080:80
```

**LE DROIT POSÉ EST `view`, ET IL EXCLUT LES SECRETS.** La console montrera que
`hba-platform` existe, pas ses valeurs : les quatorze chaînes de connexion et la
clé de signature JWT n'ont aucune raison de traverser un navigateur. Pour agir
depuis la console il faut `edit` — à décider sciemment, pas par défaut.

**Le jeton dure huit heures**, le temps d'une session. Avant Kubernetes 1.24 un
compte de service portait un jeton SANS expiration : le lire une fois suffisait à
garder l'accès. Ne rallongez pas la durée « pour ne plus avoir à le refaire » —
c'est ainsi qu'un jeton de session devient un identifiant permanent oublié dans un
onglet.

### 3.11 Les sauvegardes — il n'y en a aucune, et c'est écrit ici

`ScheduledBackup` de CloudNativePG assurait le WAL/PITR vers OVH Object Storage,
avec la rétention de 30 jours du §18. **Sur ce Postgres installé à la main, plus
rien ne sauvegarde.**

C'est une décision, pas un oubli : en staging, sur des données anonymisées et
recréables, le coût d'un pgBackRest correctement configuré et ÉPROUVÉ dépasse ce
qu'il protège.

**CE QUI REND CE CHOIX INDÉFENDABLE, ET IL FAUT LE RECONNAÎTRE AU BON MOMENT :**
la première donnée réelle entrée dans cette base. Une commande de test passée par
quelqu'un avec un vrai numéro de téléphone suffit. À partir de là, « on verra plus
tard » devient une perte de données en attente.

Ce qu'il faudra poser à ce moment — et pas avant la veille :

- **pgBackRest vers un bucket OVH**, hors du VPS de base. Sauvegarder la machine
  sur elle-même ne sauvegarde rien : le jour où l'on en a besoin est précisément
  celui où le disque a disparu.
- **Une restauration réelle, chronomètre en main.** Le §18 le dit en une phrase :
  « une sauvegarde n'est valide qu'après un test de restauration ». Le §17 donne
  un RTO de 60 minutes, qui n'est vrai que si quelqu'un a déjà fait la manœuvre.

**Écrit ici plutôt que passé sous silence.** Une dette nommée se rembourse ; une
dette oubliée se découvre.

### 3.12 Ce qu'on vient chercher à cet étage, et nulle part ailleurs

1. **La charge.** Un seul replica par service au repos, HPA à 2 : un test de
   charge doit faire apparaître le second pod. C'est **le** moment où sortent les
   défauts qui ne cassent qu'à plusieurs — une affinité de session oubliée, un
   verrou en mémoire, un consommateur Kafka qui se croit seul sur sa partition.
   Découverts ici, ils coûtent une journée ; découverts en production, une nuit.

2. **La restauration, pas la sauvegarde.**

```bash
kubectl -n hba-staging get scheduledbackup
# puis : restaurer réellement dans un cluster neuf, et compter les lignes.
```

**Une sauvegarde jamais restaurée n'est pas une sauvegarde.** Le PITR CNPG
écrit hors cluster, dans le bucket OVH — c'est le seul endroit de
l'infrastructure où l'on paie délibérément une dépendance externe, précisément
parce que sauvegarder le cluster dans le cluster ne sauvegarde rien. Le §17 donne
un RTO de 60 minutes : il n'est vrai que si quelqu'un a déjà fait la manœuvre une
fois, chronomètre en main.

3. **La perte d'un nœud** (§24). L'éteindre, et regarder.

**Écart connu à ce test : le plan de contrôle n'est PAS redondé.** Un seul
serveur k3s, même en production à trois nœuds. Perdre ce nœud ne coupe pas les
pods déjà lancés, mais plus rien ne se replanifie et `kubectl` ne répond plus.
Fermer cet écart demande trois serveurs `--cluster-init` **et** un équilibreur
devant le port 6443. **Décision à prendre avant la mise en production**, pas
après.

---

## Étage 4 — PRODUCTION

**Question : aucune. On promeut une image déjà validée, et rien d'autre.**

### 4.1 Le principe

`.github/workflows/cd.yml` **ne construit aucune image**, et c'est tout son
intérêt.

**Reconstruire à la promotion produirait un binaire DIFFÉRENT de celui qui a
passé staging** — même code source, autre image. Le rollback ne voudrait alors
plus rien dire, puisqu'on ne saurait plus vers quoi revenir. La promotion écrit
un tag dans l'overlay Kustomize et le commite : c'est le dépôt qui devient la
source de vérité de ce qui tourne (§25).

```
Actions → CD → Run workflow → environnement: prod, sha: <40 caractères>
```

L'environnement GitHub `prod` porte l'approbation manuelle du §13.

### 4.2 Ce que le workflow fait, et où il s'arrête

Il vérifie que le SHA existe, vérifie la **signature cosign** des images avant de
promouvoir (§19), pose les tags, relance `check-k8s.py` sur le rendu, commite.

**Puis il s'arrête, avant `kubectl apply`. C'est une décision en attente.**
Deux façons de fermer la boucle :

- **Argo CD ou Flux** réconcilient le cluster avec Git — le commit suffit, et le
  cluster reste reconstructible depuis le dépôt ;
- **`kubectl apply` depuis le workflow** — plus direct, mais il faut confier un
  kubeconfig de production à GitHub Actions, et l'état du cluster cesse d'être
  déductible de Git.

Tant que rien n'est tranché, la réconciliation est **manuelle** :

```bash
./scripts/preflight-k8s.sh prod --cluster
KUBECONFIG=./kubeconfig-production.yaml kubectl apply -k k8s/overlays/prod
KUBECONFIG=./kubeconfig-production.yaml kubectl -n hba-prod rollout status deploy --timeout=15m
```

Le dépôt porte aussi un chemin automatisé GitHub Actions :

| Branche poussée | Cluster ciblé |
|---|---|
| `dev` | serveur de développement, namespace `hba-dev` |
| `staging` | serveur staging, namespace `hba-staging` |
| `develop` | serveur production, namespace `hba-prod` |

Chaque environnement GitHub (`dev`, `staging`, `prod`) doit porter le secret
`KUBECONFIG_B64`. Le workflow `.github/workflows/deploy-branches.yml` applique le
SHA du commit au rendu Kustomize dans le runner, lance `preflight-k8s.sh`, puis
déploie avec `kubectl apply -k`.

### 4.3 Rollback

```bash
./scripts/rollback-k8s.sh prod <service>
```

Puis, pour que Git redevienne vrai, **repromouvoir le SHA précédent** par le
workflow CD. **Un `rollout undo` non suivi d'une promotion laisse le cluster
et le dépôt en désaccord** — et le prochain `apply` réappliquera la version
fautive, sans que personne comprenne pourquoi elle est revenue.

### 4.4 Avant le premier passage en production — la liste des blocages

Aucun de ces points n'est un détail de confort. Chacun est un écart entre ce que
le cahier demande et ce que le dépôt fait aujourd'hui :

| Point | État | Conséquence si on passe outre |
|---|---|---|
| Injection des secrets (§12) | **non tranché** | démarrage impossible, ou secrets hors Git sans traçabilité |
| Réconciliation GitOps (§25) | **non tranché** | l'état du cluster n'est plus déductible du dépôt |
| Plan de contrôle redondé (§24) | **absent** | la perte d'un nœud fige la replanification |
| VPN / bastion / MFA (§19) | **absent** | SSH ouvert sur l'IP publique des nœuds |
| Domaines réels | `.example` | rien ne résout |
| Restauration testée (§17) | **jamais faite** | le RTO de 60 min est une hypothèse |
| Terraform / Ansible appliqués | **jamais** | tout l'étage 3 est théorique |

---

## Résumé opérationnel

```bash
# 0. toujours, partout
./scripts/check-all.sh

# 1. local
./scripts/dev-up.sh --fresh && make ps

# 2. dev
make k8s-check
k3d cluster create hba-dev --agents 1 -p "8080:80@loadbalancer"
# opérateurs, secret, puis :
kubectl apply -k k8s/overlays/dev

# 3. pré-prod  (common + marketplace seulement — delivery et food au lot suivant)
#    BASE EXTERNE sur 51.255.40.214, tunnel WireGuard, aucune sauvegarde (§3.10)
#    terraform apply → ansible-playbook → DNS résout → cert-manager + Strimzi
#    wg0 des deux côtés → 14 bases + 14 rôles → psql depuis le VPS APP
#    secret ghcr + secret hba-platform (13 chaînes, un rôle chacune) → apply -k
#    amorçage : DATABASE__MIGRATEONSTARTUP=true, puis le RETIRER
cd infra/terraform/environments/staging && terraform apply
cd infra/ansible && ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
# opérateurs + cert-manager, secret, puis :
kubectl apply -k k8s/overlays/staging

# 4. prod
# GitHub Actions → CD → environnement: prod, sha: <celui validé en staging>
```
