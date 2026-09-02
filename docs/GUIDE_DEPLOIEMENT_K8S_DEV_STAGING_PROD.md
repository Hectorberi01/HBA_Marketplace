# Guide de deploiement Kubernetes - dev, staging, production

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


Ce document explique comment deployer HBAExpress avec Kubernetes, Ansible et CI/CD, en partant du principe que le lecteur ne connait pas encore ces outils.

Il est volontairement pratique : chaque section explique d'abord les mots importants, puis donne les commandes a executer, puis indique comment verifier que l'etape a vraiment fonctionne.

## 1. Ce que l'on deploie

HBAExpress est une plateforme composee de plusieurs microservices .NET 9 :

- services communs : identity, user, media, notification, payment, promotion, review ;
- services marketplace : catalog, cart, inventory, order, seller, return-refund ;
- services food ;
- services delivery ;
- BFF et API gateway.

Dans Kubernetes, chaque service applicatif devient en general :

- une image Docker publiee dans GHCR ;
- un `Deployment`, qui lance un ou plusieurs pods ;
- un `Service`, qui donne une adresse reseau stable aux pods ;
- des variables de configuration via `ConfigMap` et `Secret` ;
- des probes de sante pour permettre a Kubernetes de redemarrer ou retirer un pod du trafic.

Etat important du depot au moment de redaction :

- `infra/ansible` prepare un cluster k3s vide sur des VMs. Il ne deploie pas encore l'application.
- `k8s/overlays/dev`, `k8s/overlays/staging` et `k8s/overlays/prod` existent.
- `k8s/base/services/kustomization.yaml` active actuellement les services communs. Les services marketplace sont deja presents mais commentes. Les manifests Kubernetes des services food, delivery et BFF doivent etre ajoutes avant un deploiement complet des 31 services.
- Postgres est considere externe au cluster. `k8s/base/data` contient Redis et MinIO, pas CloudNativePG.
- Kafka passe par Strimzi : l'operateur Strimzi doit etre installe avant d'appliquer les manifests HBA.

## 2. Vocabulaire minimal

### Kubernetes

Kubernetes, souvent abrege `K8s`, est l'orchestrateur qui lance les conteneurs, les redemarre s'ils tombent, les expose sur le reseau, et applique l'etat desire decrit par des fichiers YAML.

Les objets essentiels :

- `Cluster` : ensemble de machines qui executent Kubernetes.
- `Node` : une machine du cluster. Avec k3s, on parle souvent d'un serveur et d'agents.
- `Namespace` : espace logique pour separer les environnements. Ici : `hba-dev`, `hba-staging`, `hba-prod`.
- `Pod` : plus petite unite lancee par Kubernetes. Un pod contient un ou plusieurs conteneurs.
- `Deployment` : objet qui dit combien de pods d'un service doivent tourner et avec quelle image.
- `Service` : adresse interne stable pour joindre des pods. Exemple : `identity-service:8080`.
- `Ingress` : entree HTTP/HTTPS depuis Internet vers les services internes.
- `ConfigMap` : configuration non secrete.
- `Secret` : configuration sensible, par exemple mots de passe, cles JWT, chaines de connexion.
- `Probe` : test de sante appele par Kubernetes. HBA utilise `/health/live` et `/health/ready`.
- `HPA` : Horizontal Pod Autoscaler, ajuste le nombre de pods selon la charge.
- `NetworkPolicy` : regles reseau entre pods. Attention : elles ne sont appliquees que si le CNI du cluster les supporte.

### Kustomize

Kustomize compose les manifests Kubernetes.

Dans ce depot :

- `k8s/base` contient les manifests communs.
- `k8s/overlays/dev` adapte la base pour dev.
- `k8s/overlays/staging` adapte la base pour staging.
- `k8s/overlays/prod` adapte la base pour production.

La commande importante :

```bash
kustomize build k8s/overlays/staging
```

Elle affiche le YAML final qui sera applique au cluster.

### Ansible

Ansible automatise la configuration des VMs.

Dans ce depot, Ansible sert a :

- durcir les VMs ;
- installer k3s ;
- attacher les agents au serveur ;
- verifier que les nodes sont prets.

Ansible ne remplace pas Kubernetes. Il prepare les machines. Ensuite, Kubernetes recoit les manifests HBA.

### CI/CD

CI signifie Continuous Integration. Elle verifie le code a chaque changement : compilation, tests, construction d'images.

CD signifie Continuous Delivery ou Deployment. Ici, le workflow CD promeut des images deja construites vers staging ou prod en modifiant les tags dans les overlays Kubernetes.

Point important : le workflow `.github/workflows/cd.yml` ne fait pas de `kubectl apply`. Il prepare la promotion dans Git. Le deploiement reel se fait ensuite par GitOps, ou manuellement avec `kubectl apply -k`.

## 3. Environnements

### Dev

Objectif : valider les manifests Kubernetes rapidement.

Caracteristiques :

- namespace : `hba-dev` ;
- configuration : `ASPNETCORE_ENVIRONMENT=Development` ;
- images : tags `main` ;
- faibles ressources ;
- replicas reduits ;
- domaine placeholder : `api.dev.hba-express.example`.

En dev, les migrations peuvent etre appliquees au demarrage selon la configuration applicative. C'est utile pour un environnement jetable, mais ce n'est pas le modele a utiliser en production.

### Staging

Objectif : reproduire la production avant mise en ligne.

Caracteristiques :

- namespace : `hba-staging` ;
- donnees anonymisees ou de test ;
- images : actuellement `ghcr.io/hectorberi01/<service>:main` ;
- ingress actuel : `backendapi.marketplace-staging.hba-marketplace.fr` ;
- DNS attendu vers l'IP publique du cluster ou du load balancer ;
- ressources plus proches de la production, mais encore limitees.

Staging doit servir a tester :

- TLS ;
- ingress ;
- migrations ;
- Kafka ;
- appels gRPC interservices ;
- readiness/liveness ;
- rollback ;
- sauvegardes et restauration.

### Production

Objectif : servir les utilisateurs reels avec des versions controlees.

Caracteristiques :

- namespace : `hba-prod` ;
- images : tags immuables par SHA, pas `main`, pas `latest` ;
- domaine de production a renseigner dans l'overlay ;
- secrets de production separes ;
- sauvegardes obligatoires ;
- deploiement avec approbation manuelle ;
- rollback documente et teste.

## 4. Prerequis poste de travail

Installer ou verifier :

```bash
docker version
kubectl version --client
kustomize version
ansible --version
terraform version
python3 --version
```

Installer PyYAML pour les scripts de verification Kubernetes :

```bash
python3 -m pip install --user pyyaml
```

Outils recommandes :

- `gh`, pour lancer ou lire les workflows GitHub depuis le terminal ;
- `cosign`, si la verification de signature d'image est active dans la CD ;
- `psql`, pour tester Postgres ;
- `jq`, pour lire les sorties JSON de Terraform.

## 5. Verification du depot avant tout deploiement

Depuis la racine du depot :

```bash
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
dotnet run --project tools/HBA.Controls -- k8s
kustomize build k8s/overlays/dev >/tmp/hba-dev.yaml
kustomize build k8s/overlays/staging >/tmp/hba-staging.yaml
kustomize build k8s/overlays/prod >/tmp/hba-prod.yaml
```

Si une commande echoue, ne pas deployer. Corriger d'abord.

Ce que ces commandes protegent :

- YAML invalide ;
- service sans probes ;
- service sans limites CPU/memoire ;
- image `latest` en production ;
- secret commite en clair ;
- overlay casse ;
- topics Kafka incoherents.

## 6. Images Docker

Kubernetes ne compile pas le code. Il tire des images deja construites.

Le cycle normal :

1. Le code est pousse sur GitHub.
2. La CI compile et teste.
3. La CI construit les images Docker.
4. Les images sont poussees dans GHCR.
5. Les overlays Kubernetes referencent ces images.
6. Kubernetes tire les images et lance les pods.

Convention du depot :

```text
ghcr.io/hectorberi01/<service>:main
ghcr.io/hectorberi01/<service>:<sha-git>
```

Pour staging, le depot utilise actuellement `main`.

Pour production, utiliser un SHA immuable :

```text
ghcr.io/hectorberi01/identity-service:9f3c...
```

Ne jamais utiliser `latest` en production. On ne peut pas savoir exactement quel code tourne derriere un tag mouvant.

## 7. Preparation des VMs avec Terraform

Terraform cree ou reference l'infrastructure cloud. Dans ce depot, les dossiers sont :

```text
infra/terraform/environments/staging
infra/terraform/environments/production
```

Procedure type staging :

```bash
cd infra/terraform/environments/staging
cp terraform.tfvars.example terraform.tfvars
```

Modifier `terraform.tfvars` avec les valeurs OVH reelles.

Puis :

```bash
terraform init
terraform plan
terraform apply
terraform output -json
```

Pour production, faire la meme chose dans :

```bash
cd infra/terraform/environments/production
```

Regle importante : ne jamais reutiliser les memes bases, secrets ou buckets entre staging et production.

## 8. Installation du cluster avec Ansible

Ansible part de VMs existantes et installe k3s.

### 8.1 Creer l'inventaire

Depuis la racine du depot :

```bash
cd infra/ansible
cp inventory/staging.yml.example inventory/staging.yml
```

Modifier `inventory/staging.yml` :

- IP publique du serveur k3s ;
- IP publique des agents ;
- utilisateur SSH ;
- chemin de cle SSH ;
- variable d'environnement si presente.

Ajouter les hosts dans `known_hosts` :

```bash
ssh-keyscan -H <ip-serveur> <ip-agent-1> <ip-agent-2> >> ~/.ssh/known_hosts
```

### 8.2 Lancer un dry-run

```bash
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml --check
```

Le mode `--check` simule. Sur un cluster neuf, il peut signaler des limites parce que certains fichiers n'existent pas encore. Lire les erreurs, ne pas les ignorer.

### 8.3 Installer vraiment

```bash
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml
```

Le playbook fait trois choses dans l'ordre :

1. durcissement OS sur toutes les machines ;
2. installation du serveur k3s ;
3. installation des agents k3s ;
4. verification que tous les nodes sont `Ready`.

### 8.4 Acceder a l'API Kubernetes

Le port `6443` ne doit pas etre ouvert publiquement. Utiliser un tunnel SSH :

```bash
ssh -N -L 6443:127.0.0.1:6443 <utilisateur>@<ip-serveur>
```

Dans un autre terminal :

```bash
export KUBECONFIG=infra/ansible/kubeconfig-staging.yaml
kubectl get nodes
```

Resultat attendu :

```text
NAME        STATUS   ROLES
serveur-1   Ready    control-plane
agent-1     Ready    <none>
```

## 9. Installer les briques Kubernetes communes

Le cluster k3s est vide apres Ansible. Installer ensuite les briques suivantes.

### 9.1 CNI avec NetworkPolicy

Les `NetworkPolicy` ne filtrent le trafic que si le CNI les applique.

Verifier le CNI :

```bash
kubectl -n kube-system get pods
```

Si le cluster utilise le flannel par defaut de k3s, les NetworkPolicies peuvent ne pas etre appliquees. Pour staging et production, choisir un CNI compatible NetworkPolicy, par exemple Cilium ou Calico, puis valider par un test reseau.

### 9.2 Ingress nginx

Les manifests HBA declarent `ingressClassName: nginx`.

Installer ingress-nginx selon le mode retenu pour le cluster. Exemple generique :

```bash
kubectl get ingressclass
```

Verification attendue :

```text
nginx
```

### 9.3 cert-manager

cert-manager cree et renouvelle les certificats TLS.

Verifier :

```bash
kubectl get pods -n cert-manager
kubectl get clusterissuer
```

Pour staging et production, creer au minimum :

- un `ClusterIssuer` Let's Encrypt staging pour les tests ;
- un `ClusterIssuer` Let's Encrypt production pour le domaine final.

### 9.4 Strimzi pour Kafka

Kafka est decrit par des Custom Resources. Sans Strimzi, Kubernetes ne sait pas quoi faire des objets Kafka.

Installer Strimzi dans le namespace cible avant HBA :

```bash
kubectl create namespace hba-staging
kubectl apply -f 'https://strimzi.io/install/latest?namespace=hba-staging' -n hba-staging
kubectl -n hba-staging get pods
```

Puis seulement :

```bash
kubectl apply -k k8s/overlays/staging
```

### 9.5 Redis et MinIO

Redis et MinIO sont dans `k8s/base/data`.

Apres deploiement :

```bash
kubectl -n hba-staging get statefulset
kubectl -n hba-staging get pvc
```

Verifier qu'ils ont des volumes persistants et que les pods sont `Ready`.

## 10. Secrets obligatoires

Les secrets ne doivent pas etre commites dans Git.

Le depot garde `k8s/base/common/secret.yaml` comme contrat de configuration, mais ce fichier n'est pas applique par Kustomize. Il sert a savoir quelles cles doivent exister.

### 10.1 Secret hba-platform

Creer un secret par namespace :

```bash
kubectl create namespace hba-staging
```

Exemple de structure :

```bash
PG='Host=<host-postgres>;Port=5432;Username=<user>;Password=<password>;Database='

kubectl create secret generic hba-platform -n hba-staging \
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
  --from-literal=CONNECTIONSTRINGS__RETURNREFUND="${PG}hba_return_refund" \
  --from-literal=CONNECTIONSTRINGS__DEFAULT="${PG}hba_identity" \
  --from-literal=REDIS__CONNECTIONSTRING='redis:6379' \
  --from-literal=AUTHENTICATION__SIGNINGKEY="$(openssl rand -base64 48)" \
  --from-literal=INTERNAL__APIKEY="$(openssl rand -hex 32)" \
  --from-literal=SECURITY__SECRETPROTECTION__KEY="$(openssl rand -base64 32)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

Adapter les noms de bases et les utilisateurs selon la strategie retenue.

Pour production, utiliser des utilisateurs Postgres separes avec droits minimaux. Ne pas reutiliser les mots de passe de staging.

### 10.2 Secret des identites gRPC internes

Les appels synchrones interservices passent par gRPC. Les services signent leurs appels avec une identite interne.

Generer les cles hors du depot :

```bash
scripts/generer-identites-internes.sh /chemin/hors/depot/hba-identites-staging
```

Creer le secret Kubernetes :

```bash
kubectl create secret generic hba-identites-internes -n hba-staging \
  --from-env-file=/chemin/hors/depot/hba-identites-staging/identites.env \
  --dry-run=client -o yaml | kubectl apply -f -
```

Regle de rotation : redemarrer tous les services ensemble apres rotation des cles. Une rotation partielle provoque des erreurs `Unauthenticated`, car certains services connaissent l'ancien registre et d'autres le nouveau.

### 10.3 Secret de tirage GHCR

Si les images GHCR sont privees :

```bash
kubectl create secret docker-registry ghcr-pull-secret -n hba-staging \
  --docker-server=ghcr.io \
  --docker-username=<github-user> \
  --docker-password=<github-token> \
  --docker-email=<email>
```

Verifier que les Deployments utilisent bien ce secret, ou ajouter `imagePullSecrets` dans les manifests.

## 11. Bases de donnees et migrations

Postgres est externe au cluster. Avant Kubernetes :

1. creer une base par service ;
2. creer un utilisateur par service si possible ;
3. donner les droits minimaux ;
4. verifier la connexion depuis le cluster ;
5. appliquer les migrations.

Exemple de test depuis le cluster :

```bash
kubectl -n hba-staging run psql-test --rm -it --image=postgres:16 --restart=Never -- \
  psql 'Host=<host-postgres>;Port=5432;Username=<user>;Password=<password>;Database=hba_identity' -c 'select 1;'
```

Pour staging et production, ne pas compter sur des migrations automatiques au demarrage des pods. Le bon modele est :

1. sauvegarder la base ;
2. appliquer les migrations comme etape de release ;
3. deployer les nouveaux pods ;
4. verifier les endpoints de sante ;
5. conserver un plan de rollback compatible avec le schema.

Tant qu'un Job Kubernetes de migration n'existe pas dans le depot, cette etape doit etre executee explicitement par l'equipe avant `kubectl apply -k`.

## 12. Deploiement dev

Dev sert a verifier Kubernetes sans risquer staging.

### 12.1 Creer ou selectionner le cluster

Avec un cluster local k3d :

```bash
k3d cluster create hba-dev --agents 1 -p "8080:80@loadbalancer"
kubectl config use-context k3d-hba-dev
```

Avec un cluster k3s distant, utiliser le `KUBECONFIG` fourni par Ansible.

### 12.2 Installer les dependances minimales

```bash
kubectl create namespace hba-dev
kubectl apply -f 'https://strimzi.io/install/latest?namespace=hba-dev' -n hba-dev
```

Installer aussi ingress-nginx si le cluster ne l'a pas deja.

### 12.3 Creer les secrets dev

```bash
kubectl create namespace hba-dev
```

Creer `hba-platform` et `hba-identites-internes` comme indique en section 10, avec des valeurs dev.

### 12.4 Verifier le rendu

```bash
dotnet run --project tools/HBA.Controls -- k8s
kustomize build k8s/overlays/dev >/tmp/hba-dev.yaml
```

### 12.5 Appliquer

```bash
kubectl apply -k k8s/overlays/dev
```

### 12.6 Suivre le deploiement

```bash
kubectl -n hba-dev get pods
kubectl -n hba-dev get deploy
kubectl -n hba-dev rollout status deploy/identity-service --timeout=5m
```

Pour tous les deployements :

```bash
kubectl -n hba-dev rollout status deploy --timeout=10m
```

### 12.7 Tester la gateway

Sans DNS local, utiliser un port-forward :

```bash
kubectl -n hba-dev port-forward svc/gateway-service 8080:8080
```

Dans un autre terminal :

```bash
curl -fsS http://localhost:8080/health/ready
curl -fsS http://localhost:8080/health/live
```

## 13. Deploiement staging

Staging doit etre traite comme une repetition generale de la production.

### 13.1 Pre-check staging

```bash
git status --short
dotnet run --project tools/HBA.Controls -- k8s
kustomize build k8s/overlays/staging >/tmp/hba-staging.yaml
```

Verifier dans le YAML genere :

```bash
rg -n 'backendapi.marketplace-staging.hba-marketplace.fr|image:|namespace:' /tmp/hba-staging.yaml
```

### 13.2 Verifier DNS

Le domaine staging doit pointer vers le cluster :

```bash
dig +short backendapi.marketplace-staging.hba-marketplace.fr
```

Le resultat doit etre l'IP publique attendue.

### 13.3 Verifier les images

Pour chaque service active dans Kustomize, l'image doit exister dans GHCR.

Exemple :

```bash
docker pull ghcr.io/hectorberi01/identity-service:main
```

### 13.4 Verifier les secrets

```bash
kubectl -n hba-staging get secret hba-platform
kubectl -n hba-staging get secret hba-identites-internes
```

Ne jamais afficher les valeurs en clair dans un terminal partage.

### 13.5 Appliquer les migrations

Avant de deployer les nouveaux pods :

```bash
# Exemple volontairement generique : utiliser la procedure EF/Core retenue par service.
# L'objectif est d'appliquer les migrations sur les bases staging avant rollout.
```

Ne pas passer a l'etape suivante si une migration echoue.

### 13.6 Deployer staging

```bash
kubectl apply -k k8s/overlays/staging
```

### 13.7 Verifier staging

```bash
kubectl -n hba-staging get pods
kubectl -n hba-staging get ingress
kubectl -n hba-staging get hpa
kubectl -n hba-staging get events --sort-by=.metadata.creationTimestamp
```

Verifier les rollouts :

```bash
kubectl -n hba-staging rollout status deploy/identity-service --timeout=5m
kubectl -n hba-staging rollout status deploy/gateway-service --timeout=5m
```

Smoke test HTTP :

```bash
curl -fsS https://backendapi.marketplace-staging.hba-marketplace.fr/health/ready
```

Si le certificat TLS n'est pas encore pret :

```bash
kubectl -n hba-staging describe certificate
kubectl -n hba-staging describe ingress
```

## 14. Deploiement production

Production ne doit pas utiliser directement `main`. Elle doit utiliser un SHA deja teste.

### 14.1 Conditions obligatoires avant prod

Ne pas deployer si une seule ligne est fausse :

- staging fonctionne avec le meme SHA ;
- les migrations ont ete testees sur staging ;
- un backup recent de production existe ;
- le rollback a ete teste ;
- les secrets production existent ;
- le domaine production pointe vers le bon cluster ou load balancer ;
- TLS production est pret ;
- les alertes minimum sont configurees ;
- aucune image `latest` n'apparait dans l'overlay prod ;
- aucune image `main` n'est utilisee en prod sauf decision explicite et temporaire.

### 14.2 Promouvoir avec GitHub Actions

Le workflow CD est manuel.

Depuis GitHub :

1. ouvrir l'onglet Actions ;
2. choisir le workflow CD ;
3. cliquer sur Run workflow ;
4. choisir `prod` ;
5. renseigner le SHA Git a promouvoir ;
6. lancer ;
7. attendre que le workflow modifie l'overlay prod.

Avec GitHub CLI, exemple :

```bash
gh workflow run cd.yml -f environnement=prod -f sha=<sha-git>
```

Le workflow prepare la promotion. Il ne fait pas le deploiement Kubernetes.

### 14.3 Relire la promotion

```bash
git pull
git diff HEAD~1 -- k8s/overlays/prod
kustomize build k8s/overlays/prod >/tmp/hba-prod.yaml
dotnet run --project tools/HBA.Controls -- k8s
```

Verifier :

```bash
rg -n 'latest|:main|REMPLACE-PAR-LA-PROMOTION' /tmp/hba-prod.yaml
```

La commande ne doit rien retourner pour les images applicatives de production.

### 14.4 Appliquer les migrations production

Avant le rollout :

1. mettre l'application en mode compatible si necessaire ;
2. faire un backup ;
3. appliquer les migrations ;
4. verifier les tables critiques ;
5. noter le point de rollback.

Ne jamais appliquer une migration destructive sans strategie de compatibilite avec l'ancienne version applicative.

### 14.5 Deployer production

Mode manuel :

```bash
kubectl apply -k k8s/overlays/prod
```

Mode GitOps :

1. Argo CD ou Flux observe le depot ;
2. la promotion Git modifie l'overlay prod ;
3. l'outil GitOps synchronise le cluster ;
4. l'equipe verifie le rollout.

### 14.6 Verifier production

```bash
kubectl -n hba-prod get pods
kubectl -n hba-prod get ingress
kubectl -n hba-prod get hpa
kubectl -n hba-prod get events --sort-by=.metadata.creationTimestamp
```

Rollout :

```bash
kubectl -n hba-prod rollout status deploy/gateway-service --timeout=10m
kubectl -n hba-prod rollout status deploy/identity-service --timeout=10m
```

Smoke test public :

```bash
curl -fsS https://<domaine-production>/health/ready
curl -fsS https://<domaine-production>/health/live
```

Tester ensuite un parcours fonctionnel court :

1. authentification ;
2. appel marketplace ;
3. appel food si active ;
4. appel delivery si active ;
5. evenement Kafka observe ;
6. logs sans erreurs repetitives.

## 15. Ajouter les services manquants au deploiement Kubernetes

Avant de parler d'un deploiement complet des 31 services, il faut que les manifests existent.

### 15.1 Activer marketplace

Dans `k8s/base/services/kustomization.yaml`, les services marketplace sont presents mais commentes.

Procedure :

1. decommenter un service ;
2. verifier son `Deployment`, `Service`, probes, ressources et secretKeyRef ;
3. construire l'overlay dev ;
4. deployer en dev ;
5. passer au service suivant.

Ne pas tout decommenter d'un coup si les secrets, images ou bases ne sont pas prets.

### 15.2 Ajouter food, delivery et BFF

Pour chaque service manquant :

1. creer `k8s/base/services/<service>/kustomization.yaml` en suivant le modele `_service` ;
2. definir le nom d'image GHCR ;
3. definir `INTERNAL__PRIVATEKEY` avec la bonne cle de `hba-identites-internes` ;
4. verifier le port HTTP `8080` et gRPC `9090` ;
5. ajouter le service dans `k8s/base/services/kustomization.yaml` ;
6. ajouter ou verifier les entrees `SERVICES__...` dans `k8s/base/common/configmap.yaml` ;
7. ajouter la chaine de connexion dans le secret `hba-platform` si le service a sa base ;
8. ajouter les topics Kafka necessaires ;
9. ajouter les routes gateway ou BFF si exposees ;
10. lancer `dotnet run --project tools/HBA.Controls -- k8s`.

## 16. Communication interservices

### Synchrone : gRPC

Utiliser gRPC quand le service appelant a besoin d'une reponse immediate.

Exemples :

- delivery-service demande un prix a delivery-pricing-service ;
- order-service verifie un vendeur ;
- gateway appelle un BFF ou un service interne.

En Kubernetes, les appels se font via les noms de services :

```text
http://delivery-pricing-service:9090
http://identity-service:9090
```

Verifier que chaque service expose le port gRPC et que `HOSTING__GRPCPORT=9090` est present dans la ConfigMap.

### Asynchrone : Kafka

Utiliser Kafka quand l'appelant ne doit pas attendre le traitement immediat.

Exemples :

- commande creee ;
- paiement confirme ;
- livraison affectee ;
- notification a envoyer ;
- review publiee.

Les topics doivent etre declares dans les overlays :

```bash
# commande supprimee le 2026-09-02 avec l'outillage local — le deploiement passe par la CI
kubectl -n hba-staging get kafkatopics
```

Verifier aussi les consumers :

```bash
kubectl -n hba-staging logs deploy/<service> --tail=100 | rg -i 'kafka|consumer|outbox|inbox|error'
```

## 17. Observabilite minimale

Avant production, avoir au minimum :

- logs centralises ;
- metriques Prometheus ;
- dashboards Grafana ;
- traces OpenTelemetry ;
- alertes sur pods non Ready ;
- alertes sur CrashLoopBackOff ;
- alertes sur erreurs HTTP 5xx ;
- alertes sur Kafka consumer lag ;
- alertes sur espace disque Postgres ;
- alertes sur expiration TLS.

Commandes Kubernetes utiles :

```bash
kubectl -n hba-prod top pods
kubectl -n hba-prod get events --sort-by=.metadata.creationTimestamp
kubectl -n hba-prod logs deploy/gateway-service --tail=200
```

Si `OPENTELEMETRY__ENDPOINT` est vide dans la ConfigMap, les traces ne partent pas vers un collector. Configurer le collector avant d'annoncer l'observabilite comme complete.

## 18. Sauvegardes

Production sans sauvegarde testee n'est pas prete.

Minimum :

- backup Postgres automatique ;
- retention definie ;
- restauration testee sur staging ;
- backup MinIO si les fichiers sont critiques ;
- export des secrets hors cluster dans un coffre securise ;
- procedure documentee pour restaurer Kafka si necessaire.

Test de restauration obligatoire :

1. prendre un backup staging ;
2. restaurer sur une base vide ;
3. lancer les services contre cette base ;
4. verifier un parcours metier ;
5. noter la duree reelle.

## 19. Rollback

Le rollback doit etre prepare avant le deploiement.

### Rollback applicatif

Avec Kubernetes :

```bash
kubectl -n hba-prod rollout history deploy/<service>
kubectl -n hba-prod rollout undo deploy/<service>
kubectl -n hba-prod rollout status deploy/<service> --timeout=10m
```

Rollback recommande en production :

1. remettre dans Git l'ancien SHA d'image ;
2. laisser GitOps ou `kubectl apply -k` appliquer l'etat ;
3. verifier le rollout ;
4. verifier les endpoints publics.

### Rollback base de donnees

Plus difficile que le rollback applicatif.

Regle : une migration production doit etre compatible avec l'ancienne et la nouvelle version applicative.

Eviter :

- supprimer une colonne utilisee par l'ancienne version ;
- renommer une colonne sans phase intermediaire ;
- changer un type de colonne sans backfill controle ;
- vider une table pendant un deploiement.

## 20. Commandes de diagnostic

Voir les pods :

```bash
kubectl -n hba-staging get pods -o wide
```

Comprendre pourquoi un pod ne demarre pas :

```bash
kubectl -n hba-staging describe pod <pod>
kubectl -n hba-staging logs <pod> --previous
kubectl -n hba-staging logs <pod>
```

Voir les evenements :

```bash
kubectl -n hba-staging get events --sort-by=.metadata.creationTimestamp
```

Verifier un service :

```bash
kubectl -n hba-staging get svc
kubectl -n hba-staging describe svc <service>
```

Verifier un ingress :

```bash
kubectl -n hba-staging get ingress
kubectl -n hba-staging describe ingress <ingress>
```

Entrer dans un pod :

```bash
kubectl -n hba-staging exec -it <pod> -- sh
```

Tester DNS interne :

```bash
kubectl -n hba-staging run dns-test --rm -it --image=busybox --restart=Never -- \
  nslookup identity-service
```

Tester TCP interne :

```bash
kubectl -n hba-staging run tcp-test --rm -it --image=busybox --restart=Never -- \
  nc -zv identity-service 8080
```

## 21. Erreurs frequentes

### ImagePullBackOff

Kubernetes ne peut pas tirer l'image.

Verifier :

```bash
kubectl -n hba-staging describe pod <pod>
docker pull ghcr.io/hectorberi01/<service>:<tag>
kubectl -n hba-staging get secret ghcr-pull-secret
```

Causes probables :

- image inexistante ;
- tag incorrect ;
- registry privee sans secret ;
- rate limit ;
- mauvaise architecture d'image.

### CrashLoopBackOff

Le conteneur demarre puis plante.

Verifier :

```bash
kubectl -n hba-staging logs <pod> --previous
```

Causes probables :

- secret manquant ;
- chaine de connexion invalide ;
- migration non appliquee ;
- service gRPC requis absent ;
- Kafka inaccessible.

### Secret not found

Le Deployment reference un secret absent.

Verifier :

```bash
kubectl -n hba-staging get secret
```

Creer `hba-platform` et `hba-identites-internes` avant `kubectl apply -k`.

### Readiness failed

Le pod tourne, mais Kubernetes ne l'envoie pas encore dans le trafic.

Verifier :

```bash
kubectl -n hba-staging describe pod <pod>
kubectl -n hba-staging logs <pod>
```

Causes probables :

- dependance externe indisponible ;
- endpoint `/health/ready` echoue ;
- port mal configure ;
- migration bloquee.

### Erreurs gRPC Unauthenticated

Verifier :

```bash
kubectl -n hba-staging get secret hba-identites-internes
kubectl -n hba-staging rollout restart deploy
```

Causes probables :

- `INTERNAL_PUBLIC_KEYS` absent ;
- cle privee du service absente ;
- rotation partielle des identites ;
- nom d'hote applicatif qui ne correspond pas au registre.

### Kafka no matches for kind

Strimzi n'est pas installe ou pas pret.

Verifier :

```bash
kubectl -n hba-staging get pods | rg -i strimzi
kubectl api-resources | rg -i kafka
```

### column does not exist ou column already exists

Probleme de migration ou d'etat de base.

Procedure :

1. identifier le service et le schema ;
2. regarder la table `__EFMigrationsHistory` du schema concerne ;
3. comparer avec les fichiers de migrations ;
4. ne pas modifier la base a la main sans noter l'operation ;
5. corriger la migration si elle n'est pas idempotente.

## 22. Checklist dev

Avant :

- Docker ou cluster local pret ;
- `kubectl` pointe le bon cluster ;
- Strimzi installe ;
- ingress-nginx installe si besoin ;
- secrets dev crees ;
- `dotnet run --project tools/HBA.Controls -- k8s` vert ;
- `kustomize build k8s/overlays/dev` vert.

Apres :

- pods `Running` ;
- deployments disponibles ;
- gateway repond a `/health/ready` ;
- logs sans exception de demarrage ;
- Kafka topics crees ;
- Redis et MinIO prets.

## 23. Checklist staging

Avant :

- CI verte ;
- images `main` disponibles ;
- DNS staging correct ;
- TLS staging configure ;
- secrets staging crees ;
- bases staging creees ;
- migrations appliquees ;
- Strimzi pret ;
- Redis et MinIO prets ;
- `dotnet run --project tools/HBA.Controls -- k8s` vert.

Apres :

- tous les rollouts termines ;
- gateway accessible en HTTPS ;
- smoke tests OK ;
- appels gRPC OK ;
- evenements Kafka consommes ;
- logs sans erreur repetitive ;
- rollback teste.

## 24. Checklist production

Avant :

- staging valide avec le SHA cible ;
- backup production recent ;
- restauration deja testee ;
- secrets production presents ;
- domaine production configure ;
- TLS production pret ;
- monitoring pret ;
- alertes actives ;
- aucun `latest`, aucun tag mouvant ;
- approbation humaine recueillie.

Pendant :

- appliquer migrations ;
- promouvoir l'overlay prod ;
- appliquer ou synchroniser GitOps ;
- surveiller rollouts ;
- surveiller logs et metriques.

Apres :

- smoke tests publics OK ;
- parcours metier court OK ;
- Kafka consomme ;
- pas de CrashLoopBackOff ;
- pas de 5xx anormaux ;
- decision de cloture ou rollback prise rapidement.

## 25. Evolution recommandee

Pour rendre le deploiement plus robuste :

1. ajouter les manifests Kubernetes manquants pour food, delivery et BFF ;
2. remplacer les secrets manuels par External Secrets ou Vault ;
3. ajouter un Job de migrations par service ;
4. installer Argo CD ou Flux pour le GitOps ;
5. rendre staging immuable comme prod, avec tags SHA ;
6. ajouter kube-prometheus-stack et OpenTelemetry Collector ;
7. ajouter des backups automatises et testes ;
8. documenter les SLO et alertes ;
9. tester les NetworkPolicies avec un CNI compatible ;
10. ajouter un runbook de rotation des secrets.

## 26. Ordre complet resume

Dev :

```bash
dotnet run --project tools/HBA.Controls -- k8s
kustomize build k8s/overlays/dev >/tmp/hba-dev.yaml
kubectl create namespace hba-dev
# creer hba-platform
# creer hba-identites-internes
# installer ingress-nginx et Strimzi
kubectl apply -k k8s/overlays/dev
kubectl -n hba-dev rollout status deploy --timeout=10m
curl -fsS http://localhost:8080/health/ready
```

Staging :

```bash
cd infra/terraform/environments/staging
terraform init
terraform plan
terraform apply

cd ../../../ansible
ansible-playbook -i inventory/staging.yml playbooks/cluster.yml

export KUBECONFIG=infra/ansible/kubeconfig-staging.yaml
# installer ingress-nginx, cert-manager, Strimzi
# creer secrets
# appliquer migrations

cd /Users/hector/Documents/HBA
dotnet run --project tools/HBA.Controls -- k8s
kubectl apply -k k8s/overlays/staging
kubectl -n hba-staging rollout status deploy --timeout=10m
curl -fsS https://backendapi.marketplace-staging.hba-marketplace.fr/health/ready
```

Production :

```bash
# choisir un SHA valide en staging
gh workflow run cd.yml -f environnement=prod -f sha=<sha-git>
git pull
dotnet run --project tools/HBA.Controls -- k8s
kustomize build k8s/overlays/prod >/tmp/hba-prod.yaml

# backup
# migrations
kubectl apply -k k8s/overlays/prod
kubectl -n hba-prod rollout status deploy --timeout=15m
curl -fsS https://<domaine-production>/health/ready
```
