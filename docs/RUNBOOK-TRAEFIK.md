# RUNBOOK — Docker Compose et Traefik sur le VPS

> Cible : `79.137.35.129`, utilisateur `ubuntu`, SSH `ovh-server` (port 8022).
> Base de données : VPS séparé, `10.20.0.2:5432`, par le tunnel WireGuard.
> Domaine : `api.hba-express.com`. Registre : `ghcr.io/hectorberi01`.

---

## Le chemin normal est maintenant la CI

Les sections 3 à 6 de ce runbook — porter le compose, porter le fichier
d'environnement, se connecter au registre, tirer, démarrer, vérifier — sont
exécutées par **`.github/workflows/deploy-compose.yml`**, déclenché à la main
depuis l'onglet Actions avec le tag des images à déployer.

**Ce runbook reste la référence** pour deux choses : comprendre ce que le
workflow fait, et déployer quand la CI est indisponible. Les commandes n'ont pas
changé ; c'est leur exécution qui a déménagé.

Ce que le workflow exige, et qui doit exister dans l'environnement GitHub
`production` avant le premier déclenchement :

| Nom | Nature | Contenu |
|---|---|---|
| `VPS_SSH_KEY` | secret | clé privée OpenSSH sans phrase de passe |
| `VPS_KNOWN_HOSTS` | secret | sortie de `ssh-keyscan -p 8022 79.137.35.129` |
| `PROD_ENV_FILE` | secret | le fichier d'environnement entier, 46 variables |
| `GHCR_TOKEN` | secret | jeton GitHub, portée `read:packages` |
| `VPS_HOST` | variable | `79.137.35.129` |
| `VPS_USER` | variable | `ubuntu` |
| `VPS_PORT` | variable | `8022` |
| `HBA_DOMAINE` | variable | `api.hba-express.com` |

**`VPS_KNOWN_HOSTS` n'est pas un confort.** Le workflow envoie au VPS un fichier
qui porte quatorze mots de passe de base, les clés de signature des jetons et la
clé FedaPay de production. Sans empreinte, `StrictHostKeyChecking=no` les
enverrait à qui répond à cette adresse. Le job refuse de démarrer sans elle.

**Poser une règle de révision obligatoire sur l'environnement `production`**
(Settings → Environments → production → Required reviewers) ajoute une
approbation humaine devant `docker compose up -d`. Sans elle, quiconque peut
lancer le workflow met en ligne.

---

## 0. Ce que ce runbook fait, et ce qu'il ne fait pas

**Il fait** : mettre k3s en pause, puis lancer les vingt-cinq conteneurs du
compose de production derrière Traefik, avec TLS émis par Let's Encrypt.

**Il ne fait pas** :

- il ne supprime PAS le cluster k3s. Les Secrets, les volumes et les manifestes
  restent en place ; `systemctl start k3s` rétablit tout. La bascule est
  réversible dans les deux sens, et c'est délibéré ;
- il ne migre PAS les bases. Elles l'ont été le 1er septembre par les Jobs
  Kubernetes, et le schéma est le même : les conteneurs Compose lisent les
  mêmes quatorze bases sur `10.20.0.2` ;
- il ne déploie ni `notification-service` (adaptateur SMS absent) ni
  `return-refund-service` (deux adaptateurs gRPC simulés). Le générateur les
  écarte, et les raisons sont dans `k8s/base/services/kustomization.yaml`.

### Pourquoi les deux ne peuvent pas coexister

k3s tient 80 et 443 par le `hostPort` d'ingress-nginx. Traefik demande les
mêmes. Le second à démarrer échoue sur « address already in use » — ou pire,
selon l'ordre, c'est le premier qui perd la main. Il faut donc arrêter l'un
avant de lancer l'autre.

---

## 1. Mettre k3s en pause

```bash
ssh ovh-server 'sudo systemctl stop k3s && sudo systemctl disable k3s'
ssh ovh-server 'sudo ss -tulpn | grep -E ":(80|443)\b" || echo "80 et 443 libres"'
```

**`disable` autant que `stop`.** Sans lui, k3s repart au prochain redémarrage du
VPS et reprend les deux ports — la panne surviendrait des semaines plus tard,
après un reboot, et personne ne ferait le lien.

**Ce que la pause conserve** : les volumes `local-path` (Kafka, Redis, MinIO),
les six Secrets, et tous les manifestes appliqués. Rien n'est détruit.

**Ce qu'elle coûte** : les données de Kafka et de MinIO côté Kubernetes ne sont
pas celles que Compose utilisera — le compose a ses propres volumes
(`kafka-data`, `minio-data`). Les buckets MinIO sont donc à recréer, et les
sujets Kafka repartent de zéro. Les bases PostgreSQL, elles, sont les mêmes :
elles vivent sur l'autre VPS.

Pour revenir à Kubernetes plus tard :

```bash
ssh ovh-server 'sudo docker compose -p hba -f /opt/hba/docker-compose.prod.yml down'
ssh ovh-server 'sudo systemctl enable --now k3s'
```

---

## 2. Le fichier d'environnement

Le compose exige **46 variables**. Deux sont nouvelles avec Traefik :

```
HBA_DOMAINE=api.hba-express.com
HBA_ACME_EMAIL=hector.adjakpa@hbatechettrade.com
HBA_TAG=<le tag des images publiées>
```

Le contrôle qui les liste toutes vit désormais **dans le workflow**, étape
« Écrire et contrôler le fichier d'environnement ». Il ne s'exécute donc plus
depuis le Mac : c'est la CI qui refuse de déployer un fichier incomplet.

Il vérifie quatre choses, et n'affiche jamais de valeur : les variables
absentes, les variables présentes mais vides, les `$` non échappés, et l'égalité
du couple de clés de signature.

**`AUTHENTICATION__SIGNINGKEY` et `JWT__SIGNINGKEY` doivent porter la MÊME
valeur** — le script le vérifie. Le socle valide les jetons avec la première,
identity-service les émet avec la seconde. Deux valeurs différentes ne lèvent
nulle part : tout appel authentifié revient en 401, sans cause nommée.

**Le `$` dans une valeur doit être doublé.** Compose interpole aussi le fichier
d'environnement : `mot$depasse` y est lu comme une référence de variable.
Écrire `mot$$depasse`. Le contrôle le signale.

---

## 3. Porter le compose sur le VPS

Seuls trois éléments sont nécessaires : le fichier compose, le fichier
d'environnement, et `infra/rembg` — le seul service encore construit sur place.

```bash
ssh ovh-server 'mkdir -p /opt/hba'
scp -P 8022 docker-compose.prod.yml ovh-server:/opt/hba/
scp -P 8022 -r infra/rembg ovh-server:/opt/hba/infra/
scp -P 8022 ~/secrets-hba-prod/prod.env ovh-server:/opt/hba/.env
ssh ovh-server 'chmod 600 /opt/hba/.env'
```

**Le fichier d'environnement est en 0600 sur le VPS aussi.** Il porte quatorze
mots de passe de base, les clés de signature et la clé FedaPay.

---

## 4. Se connecter au registre

Les images sont privées. Un jeton GitHub avec la portée `read:packages` suffit.

```bash
ssh -t ovh-server 'read -rs JETON && printf "%s" "$JETON" | sudo docker login ghcr.io -u hectorberi01 --password-stdin && unset JETON'
```

`ssh -t` alloue un terminal : sans lui, `read -rs` n'attend rien et le jeton
part vide.

---

## 5. Tirer, puis démarrer

```bash
ssh ovh-server 'cd /opt/hba && sudo docker compose -p hba pull'
ssh ovh-server 'cd /opt/hba && sudo docker compose -p hba build rembg'
ssh ovh-server 'cd /opt/hba && sudo docker compose -p hba up -d'
```

`pull` d'abord, **séparément** : c'est là que se voit une image absente du
registre ou un tag qui n'existe pas, et l'erreur nomme l'image. Mélangé au `up`,
le même échec arrive au milieu du démarrage de vingt-cinq conteneurs.

`rembg` se construit sur place — il n'est publié nulle part, et c'est une image
Python légère, pas un des vingt hôtes .NET.

---

## 6. Vérifier

```bash
ssh ovh-server 'cd /opt/hba && sudo docker compose -p hba ps'
ssh ovh-server 'sudo docker ps --format "{{.Names}}\t{{.Status}}" | grep -v Up || echo "tout est Up"'
ssh ovh-server 'sudo docker logs hba-traefik --tail=30'

curl -sI http://api.hba-express.com/          # doit rendre 301 vers https
curl -sI https://api.hba-express.com/health/ready
```

**Le certificat prend une à deux minutes.** Traefik demande à Let's Encrypt par
défi HTTP-01 : le DNS doit déjà pointer `79.137.35.129` et le port 80 répondre
depuis Internet. En cas d'échec, `docker logs hba-traefik` le dit franchement.

**Let's Encrypt limite à cinq échecs par heure et par domaine.** Le volume
`traefik-acme` conserve les certificats entre les redémarrages ; sans lui,
chaque `up` en redemanderait un et le quota tomberait en une matinée.

---

## 7. Ce que Traefik expose, et ce qu'il n'expose pas

**Un seul service porte `traefik.enable=true` : la passerelle.** C'est
`--providers.docker.exposedByDefault=false` qui rend cela vrai. Sans ce réglage,
Traefik publierait les vingt-cinq conteneurs — Kafka, MinIO, Redis et les vingt
hôtes — sous des noms engendrés, sans authentification, et sans qu'aucune erreur
ne le signale. C'est la ligne la plus importante du bloc.

**Aucun tableau de bord** (`--api=false`) : celui de Traefik expose la
configuration complète du routage.

**Les consoles restent sur la boucle locale** : `kafka-ui` en `127.0.0.1:8090`,
MinIO en `127.0.0.1:9001`. On y accède par un tunnel SSH, jamais par le domaine :

```bash
ssh -L 8090:127.0.0.1:8090 -L 9001:127.0.0.1:9001 ovh-server
```

### Ce que ce montage ne couvre pas

**Le socket Docker est monté en lecture seule, et ce n'est pas anodin.** Même en
lecture, l'API Docker rend les variables d'environnement de TOUS les conteneurs
— donc les mots de passe de base, la clé FedaPay et les clés de signature. Un
Traefik compromis les lit. C'est le compromis assumé de la découverte par
étiquettes ; l'alternative, un fichier de configuration statique, coûte la
découverte automatique.

**Aucune limite de débit ni de taille de requête.** nginx-ingress posait
`proxy-body-size: 20m` pour les pièces KYB ; Traefik n'a pas de limite par
défaut — donc rien à régler pour les envois légitimes, mais rien ne protège non
plus d'un envoi massif.

---

## 8. Après le démarrage

Les buckets MinIO ne se créent pas tout seuls :

```bash
ssh ovh-server 'sudo docker exec hba-minio mc alias set local http://127.0.0.1:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" 2>/dev/null || true'
```

En pratique, le plus simple est la console sur `127.0.0.1:9001` par le tunnel de
l'étape 7 : créer `hba-public` et `hba-private`, une fois.

Les sujets Kafka se créent à la volée (`auto.create.topics.enable`), mais avec
**une** partition et la réplication par défaut. Sur un courtier unique c'est
sans conséquence ; le jour où il y en aura plusieurs, il faudra les provisionner.

---

## 9. Ce qui reste ouvert

- **Trois services ne tournent pas** : `notification-service` (adaptateur SMS),
  `return-refund-service` (deux adaptateurs gRPC simulés), et
  `delivery-pricing-service` si son correctif de constructeur EF n'a pas encore
  été republié.
- **La barrière de tests de la CI reste rouge** : vingt-trois échecs sur
  `HBA.Merchants.IntegrationTests`, qui passent en local. Le logger console
  ajouté à `ci.yml` fera apparaître l'exception au prochain run.
- **Les secrets ont circulé** pendant l'installation — historique de shell,
  terminal, conversation. À faire tourner une fois la production stable.
- **k3s est en pause, pas supprimé.** Le choix entre les deux n'est pas tranché ;
  tant qu'il ne l'est pas, garder les deux chemins documentés coûte peu et
  évite d'avoir à en reconstruire un dans l'urgence.
