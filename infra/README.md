# Infrastructure

Ce qui fait tourner les services, et ce qui permet de savoir qu'ils tournent.

| Dossier | Étage | État |
|---|---|---|
| `docker/` | local | éprouvé |
| `observability/` | local et cluster | partiel |
| `terraform/` | VMs, réseau, DNS, stockage objet chez OVH | **jamais appliqué** |
| `ansible/` | k3s sur ces VMs | **jamais exécuté** |
| `../k8s/` | les charges (Kustomize) | overlays construits et vérifiés |

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
cd /Users/hector/Documents/HBAEpress_Projets/marketPlace/HBA
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

### Pile Infrastructure Complète

La pile située dans `infra/docker` inclut l'infrastructure, les services,
la gateway et l'observabilité.

Créer le fichier d'environnement :

```bash
cp infra/docker/.env.example infra/docker/.env
```

Renseigner au minimum dans `infra/docker/.env` :

```text
POSTGRES_PASSWORD=
REDIS_PASSWORD=
MINIO_ROOT_PASSWORD=
JWT_SIGNING_KEY=
INTERNAL_API_KEY=
API_DOMAIN=
GRAFANA_DOMAIN=
GRAFANA_ADMIN_PASSWORD=
```

Démarrer :

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose --env-file infra/docker/.env -f infra/docker/compose.yml up --build
```

En arrière-plan :

```bash
COMPOSE_PARALLEL_LIMIT=2 docker compose --env-file infra/docker/.env -f infra/docker/compose.yml up --build -d
```

Voir l'état :

```bash
docker compose --env-file infra/docker/.env -f infra/docker/compose.yml ps
```

Arrêter :

```bash
docker compose --env-file infra/docker/.env -f infra/docker/compose.yml down
```

Supprimer aussi les volumes :

```bash
docker compose --env-file infra/docker/.env -f infra/docker/compose.yml down -v
```

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
