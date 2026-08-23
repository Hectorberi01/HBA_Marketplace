# Déploiement et vérification — local, dev, pré-production, production

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
câblage sont vérifiés à chaque `check-all.sh` (`scripts/check-infra.py`) ; son
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

# Bases de données et Kafka
kubectl apply --server-side -f https://raw.githubusercontent.com/cloudnative-pg/cloudnative-pg/release-1.24/releases/cnpg-1.24.1.yaml
kubectl create namespace hba-dev
kubectl apply -f https://strimzi.io/install/latest?namespace=hba-dev -n hba-dev
```

**Sans les CRD des opérateurs, `kubectl apply -k` échoue sur
`no matches for kind "Cluster"`.** Le message ne dit pas qu'il manque un
opérateur — il ressemble à une faute de frappe dans un manifeste.

**cert-manager n'est pas nécessaire en dev**, et l'Ingress y servira donc un
certificat auto-signé. Attendu. En staging et en production, il l'est.

### 2.4 Le secret — l'étape qu'on découvre en échouant

`k8s/base/common/secret.yaml` est **vide par construction** : il déclare les noms
de clés, jamais les valeurs (§12). En dev, on le remplit à la main :

```bash
kubectl create secret generic hba-platform -n hba-dev \
  --from-literal=CONNECTIONSTRINGS__DEFAULT='Host=postgres-rw;Database=hba;Username=hba;Password=...' \
  --from-literal=REDIS__CONNECTIONSTRING='redis:6379' \
  --from-literal=AUTHENTICATION__SIGNINGKEY="$(openssl rand -base64 48)" \
  --from-literal=INTERNAL__APIKEY="$(openssl rand -hex 32)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

**Le mécanisme d'injection pour staging et production n'est PAS tranché** —
External Secrets/Vault, ou `kubectl create secret` hors GitOps. Voir l'encadré du
fichier. Tant que rien n'est décidé, un déploiement échoue au démarrage sur une
clé de signature vide : le bon échec, bruyant et immédiat.

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
  timeout 5 nc -zv postgres-rw 5432
```

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
dig +short api.staging.<votre-domaine>
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

### 3.4 Déployer

```bash
kubectl apply -k k8s/overlays/staging
kubectl -n hba-staging rollout status deploy --timeout=10m
kubectl -n hba-staging get certificate     # READY=True, sinon l'ACME est bloqué
```

**Ici, la migration est une étape de release, pas un effet de bord du
démarrage** (§15). `MigrateOnStartup` est faux hors `Development`.

### 3.5 Ce qu'on vient chercher à cet étage, et nulle part ailleurs

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
KUBECONFIG=./kubeconfig-production.yaml kubectl apply -k k8s/overlays/prod
KUBECONFIG=./kubeconfig-production.yaml kubectl -n hba-prod rollout status deploy --timeout=15m
```

Un déploiement à moitié automatisé qu'on croit complet serait pire que celui-ci,
qui dit ce qu'il ne fait pas.

### 4.3 Rollback

```bash
kubectl -n hba-prod rollout undo deploy/<service>     # immédiat, un service
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

# 3. pré-prod
cd infra/terraform/environments/staging && terraform apply
cd infra/ansible && ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
# opérateurs + cert-manager, secret, puis :
kubectl apply -k k8s/overlays/staging

# 4. prod
# GitHub Actions → CD → environnement: prod, sha: <celui validé en staging>
```
