# Infrastructure

Ce qui fait tourner les services, et ce qui permet de savoir qu'ils tournent.

| Dossier | Rôle | Qui le lit | État |
|---|---|---|---|
| `ansible/` | prépare le VPS et pose k3s | `ansible-playbook`, à la main | syntaxe vérifiée, **jamais exécuté** |
| `postgres/init/` | crée les 14 bases au **premier** démarrage du volume | `docker-compose.dev.yml` | vivant, développement seulement |
| `rembg/` | image du détourage d'images | `docker-compose.dev.yml` | vivant |
| `terraform/` | VMs, réseau, DNS, stockage objet chez OVH | personne | **jamais appliqué** |
| `observability/` | configs Prometheus, Grafana, Loki, OTel, Tempo | **personne** | orphelin — voir ci-dessous |
| `../k8s/` | les charges (Kustomize) | `kubectl apply -k` | overlays construits et vérifiés |

**`docker/` a été retiré** le 2026-08-26 vers `_to_delete/2026-08-26-pile-compose-serveur/` :
aucun lecteur, treize anciens noms de service, contexte de construction hors du
dépôt. Ses deux rescapés sont `postgres/init/` et
[`../docs/COMPILATION-IMAGES.md`](../docs/COMPILATION-IMAGES.md).

**`observability/` n'a plus aucun lecteur, et c'est ce retrait qui l'a orphelin.**
Son seul consommateur était `docker/compose.monitoring.yml`, qui montait
`prometheus.yml`, les provisions Grafana, `loki.yml` et `otel-collector.yml`.
Côté cluster, `k8s/base/observability/` ne contient qu'un README : la supervision
y est prévue en brique Helm tierce (kube-prometheus-stack), qui apporte ses
propres configurations et ne lira pas celles-ci. **Ces fichiers décrivent donc
une pile que rien ne démarre.** Deux d'entre eux n'ont d'ailleurs jamais été lus,
même avant le retrait : `tempo/tempo.yml`, que le compose ne montait pas, et
`grafana/dashboards/`, qui ne contient qu'un README — le montage pointait un
dossier sans tableau de bord.

Ne pas les supprimer pour autant : `otel-collector.yml` et `prometheus.yml`
portent le travail de câblage qu'il faudra refaire le jour où la supervision est
déployée. Mais ils ne doivent pas être lus comme l'état du système.

**`terraform/` et `ansible/` n'ont jamais tourné** — pas d'identifiants OVH,
donc ni `terraform plan` ni `ansible-playbook`. Leur syntaxe et leur câblage sont
vérifiés à chaque `./scripts/check-all.sh` (`scripts/check-infra.py`, ou
`make infra`) ; leur comportement ne l'est pas. À relire avant le premier
passage, pas à appliquer de confiance.

**La procédure complète des quatre étages — local, dev, pré-production,
production — est dans [`../docs/DEPLOIEMENT.md`](../docs/DEPLOIEMENT.md).** Ce
fichier-ci ne couvre que le démarrage local.

## Démarrage Local

Toutes les commandes ci-dessous se lancent depuis la racine du projet :

```bash
cd <racine du dépôt>          # le chemin en dur d'avant ne correspondait plus à rien
```

### Prérequis

- Docker Desktop démarré.
- SDK .NET installé pour compiler localement.
- Ports locaux libres : `5432`, `6379`, `8082`, `8090`, `9000`, `9001`, `9092`.

### Vérifier Le Code Avant Lancement

```bash
dotnet restore HBA.sln
dotnet build HBA.sln --no-restore -v:minimal /nr:false /m:1
```

Pour vérifier uniquement les contrats gRPC :

```bash
dotnet build shared/contracts/HBA.Catalog.Contracts.Grpc/HBA.Catalog.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Inventory.Contracts.Grpc/HBA.Inventory.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Commerce.Contracts.Grpc/HBA.Commerce.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Order.Contracts.Grpc/HBA.Order.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Food.Contracts.Grpc/HBA.Food.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Deliveries.Contracts.Grpc/HBA.Deliveries.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Financial.Contracts.Grpc/HBA.Financial.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Engagement.Contracts.Grpc/HBA.Engagement.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Communication.Contracts.Grpc/HBA.Communication.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
dotnet build shared/contracts/HBA.Merchants.Contracts.Grpc/HBA.Merchants.Contracts.Grpc.csproj --no-restore -v:minimal /nr:false /m:1
```

### Lancer La Pile De Développement

La pile de développement locale est déclarée dans `docker-compose.dev.yml`.
Elle démarre Postgres, Redis, Kafka, MinIO et les treize services applicatifs.

Construire les images :

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose -f docker-compose.dev.yml build
```

La limite de parallélisme évite les échecs `code 137` / `cannot allocate memory`
pendant les `dotnet publish`, surtout avec Docker Desktop.

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose -f docker-compose.dev.yml up --build
```

En arrière-plan :

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose -f docker-compose.dev.yml up --build -d
```

Voir l'état :

```bash
docker compose -f docker-compose.dev.yml ps
```

Lire les journaux :

```bash
docker compose -f docker-compose.dev.yml logs -f
```

Lire les journaux d'un service :

```bash
docker compose -f docker-compose.dev.yml logs -f catalog-service
docker compose -f docker-compose.dev.yml logs -f order-service
docker compose -f docker-compose.dev.yml logs -f communication-service
```

Arrêter sans supprimer les volumes :

```bash
docker compose -f docker-compose.dev.yml down
```

Arrêter et supprimer les volumes de données locaux :

```bash
docker compose -f docker-compose.dev.yml down -v
```

### Ports Internes

Dans la pile de développement :

- HTTP REST par service : `8080`.
- gRPC interne entre services : `9090`.
- Kafka interne entre conteneurs : `kafka:29092`.
- Kafka depuis l'hôte : `localhost:9092`.
- Kafka UI : `http://localhost:8090`.
- Redis : `redis:6379`.
- Redis UI : `http://localhost:8082`.
- Postgres : `postgres:5432`.
- MinIO API : `http://localhost:9000`.
- MinIO Console : `http://localhost:9001`.

Les communications synchrones entre services doivent pointer vers le port gRPC :

```text
SERVICES__CATALOG=http://catalog-service:9090
SERVICES__ORDER=http://order-service:9090
SERVICES__MEDIA=http://media-service:9090
```

Les communications asynchrones passent par Kafka. Chaque service publie sur un topic unique :

```text
service.identity.v1
service.user.v1
service.merchant.v1
service.catalog.v1
service.inventory.v1
service.commerce.v1
service.order.v1
service.food.v1
service.delivery.v1
service.financial.v1
service.engagement.v1
service.communication.v1
service.media.v1
```

### Démarrer la pile locale

CETTE SECTION DECRIVAIT UNE PILE QUI N'EXISTE PLUS.

Elle enseignait `docker compose --env-file infra/docker/.env -f
infra/docker/compose.yml up --build`. Ce dossier a ete retire vers
`_to_delete/2026-08-26-pile-compose-serveur/` : plus rien ne le lisait, ni le
`Makefile`, ni les workflows, ni un script. Il portait les treize ANCIENS noms
de service (`commerce`, `engagement`, `financial`, `merchant`,
`communication`), et son `context: ../../..` sortait du depot — meme lancee,
cette pile ne construisait rien. Un clone neuf qui suivait ce README obtenait
donc une pile morte, et rien ne le lui disait.

La seule pile lancee est `docker-compose.dev.yml`, a la racine, via le
`Makefile` :

```bash
make up      # demarre les 32 conteneurs en arriere-plan
make ps      # etat
make logs S=identity-service
make down    # arrete
```

Pour tout supprimer, volumes compris — c'est ce qu'il faut faire quand les
bases applicatives manquent, le script d'initialisation ne repassant jamais sur
un volume deja peuple :

```bash
docker compose -f docker-compose.dev.yml down -v
```

Aucun fichier d'environnement n'est requis. `docker-compose.dev.yml` porte ses
valeurs en clair — ce sont des secrets de developpement, sans valeur hors de la
machine — et ne lit qu'une seule variable de l'hote, `HOST_LAN_IP`, pour que le
portail admin joigne la passerelle depuis un autre appareil du reseau local.

CE QUE CETTE PILE NE CONTIENT PAS :

- **aucune supervision** — ni Prometheus, ni Grafana, ni Loki, ni collecteur
  OTLP. C'est pourquoi la passerelle pose `OPENTELEMETRY__ENDPOINT: ""` : une
  adresse pointant sur un collecteur absent produirait une erreur de connexion
  toutes les quelques secondes. La supervision vit desormais dans `k8s/`.
- **aucune passerelle TLS** — la passerelle publie 8080 en clair. Traefik et
  `api.hba-express.com` etaient dans la pile retiree.

Pour construire les images, et pour comprendre pourquoi elles se compilent en
`linux/amd64` meme sur un Mac Apple Silicon, voir
[`docs/COMPILATION-IMAGES.md`](../docs/COMPILATION-IMAGES.md).

### Diagnostic Rapide

Créer les topics Kafka de développement :

```bash
docker compose -f docker-compose.dev.yml exec kafka bash -lc 'for topic in service.identity.v1 service.user.v1 service.merchant.v1 service.catalog.v1 service.inventory.v1 service.commerce.v1 service.order.v1 service.food.v1 service.delivery.v1 service.financial.v1 service.engagement.v1 service.communication.v1 service.media.v1; do kafka-topics --bootstrap-server localhost:9092 --create --if-not-exists --topic "$topic" --partitions 3 --replication-factor 1; done'
```

Lister les topics Kafka :

```bash
docker compose -f docker-compose.dev.yml exec kafka kafka-topics --bootstrap-server localhost:9092 --list
```

Tester une sonde de service depuis le conteneur :

```bash
docker compose -f docker-compose.dev.yml exec catalog-service wget -qO- http://localhost:8080/health/live
```

Reconstruire un seul service :

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose -f docker-compose.dev.yml build catalog-service
docker compose -f docker-compose.dev.yml up -d catalog-service
```
