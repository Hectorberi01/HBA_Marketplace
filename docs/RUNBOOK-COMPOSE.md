# Runbook — déploiement Docker Compose de production

| | |
|---|---|
| VPS applicatif | **79.137.35.129** |
| Base PostgreSQL | **10.20.0.2**, second VPS, par le tunnel |
| Domaine | **api.hba-express.com** |
| Registre | **ghcr.io/hectorberi01** |

**18 services applicatifs** plus Redis, Kafka, MinIO, rembg et la passerelle.

`docker-compose.prod.yml` est **engendré** par `scripts/generer-compose-prod.py`
depuis `docker-compose.dev.yml`. Ne pas l'éditer à la main : la prochaine
génération écraserait la correction. Corriger le service dans le compose de
développement, puis relancer le script.

---

## Ce qui n'est pas déployé, et ce que ça coûte

**notification-service** — aucun adaptateur `ISmsSender` de production n'existe,
et le SMS est le canal OTP par défaut. Conséquence directe : **aucun courriel ni
SMS ne part**. Pas de vérification d'adresse, pas de mot de passe oublié, pas de
code de connexion. Le compte administrateur amorcé est le seul moyen d'entrer, et
son mot de passe ne se récupère pas.

**return-refund-service** — deux adaptateurs gRPC restent des bouchons : la
marchandise retournée n'est jamais remise en stock, et aucune course d'enlèvement
n'est créée alors qu'un numéro est rendu au client. Le troisième — la vérification
des preuves photo — a été écrit le 29 août.

**payment-service — écarté, et c'est plus grave que « il n'encaisse rien ».**
Cette ligne disait qu'il était déployé sans encaisser. C'était faux : il **refuse
de démarrer**. `PaymentsModuleInstaller` lève dans deux cas, tous deux réunis
aujourd'hui — aucune clé PSP pour encaisser, et pas de clé FedaPay LIVE avec
`EnablePayouts=true` pour verser.

Les deux gardes sont délibérées et justes. Une passerelle simulée marquerait les
commandes « payées » sans qu'un centime ne bouge, et clôturerait un retrait
vendeur en « payé » avec un solde débité — sans moyen de distinguer après coup un
versement simulé d'un versement réel perdu.

Conséquence : aucun encaissement, aucun versement, et les appels gRPC
d'order-service vers le paiement échouent. Le jour où une clé existe, le retirer
de `BLOQUES` dans `scripts/generer-compose-prod.py` suffit.

---

## 0. Trois chemins, et il faut choisir le sien

**Depuis le poste — `./scripts/deployer.sh prod`.** Le client Docker parle au
démon du VPS par un contexte SSH. Le compose, le fichier d'environnement et les
sources sont lus **sur le poste** ; seuls les appels d'API et le contexte de
build partent. La destination SSH est l'alias `ovh-server` de `~/.ssh/config` —
c'est lui qui porte le port 8022, l'utilisateur et la clé.

**Depuis le VPS — les commandes `docker compose` de ce runbook.** Tout est
local à la machine, dépôt cloné compris.

**Par Coolify — le chemin retenu pour la production.** Coolify construit et
démarre depuis un dépôt Git qu'il clone sur le VPS ; `deployer.sh` ne fait plus
que garder la barrière (contrôles, compilation, 1054 tests), pousser le commit,
et le lui signaler. Voir la section 0 bis.

Les deux fonctionnent. Ce qui ne fonctionne pas, c'est de mélanger : un fichier
d'environnement posé sur le VPS n'est **jamais lu** par `deployer.sh`, parce que
`--env-file` est traité par le client compose, ici.


## 0 bis. Coolify

Coolify tourne sur le VPS de production et y pilote Docker. **Deux outils qui
commandent le même démon se marchent dessus** : depuis ce basculement,
`deployer.sh` ne parle plus au démon du tout — il pousse, et Coolify décide.

### La source : un dépôt nu sur le VPS, pas GitHub

Coolify doit cloner un dépôt pour construire les dix-neuf images. Le dépôt vit
donc sur la machine elle-même, alimenté par `git push` depuis le poste :

```bash
# créé automatiquement au premier ./scripts/deployer.sh prod
ssh ovh-server 'mkdir -p depots && git init --bare --initial-branch=main depots/hba.git'
```

### La ressource, dans Coolify 4.3

**Choisir « Private Git Repository (with Deploy Key) », PAS la carte « Docker
Compose ».**

Les deux mènent à un compose, et c'est le piège. La carte « Docker Compose » est
une *Docker source* : elle déploie « **without a Git repository** », donc sans
sources — elle ne peut que tirer des images déjà construites. Nos dix-neuf
services ont un `build:` ; il faut la *Git source*, qui clone puis construit.

La carte retenue annonce « Set the URL and branch **manually**, no automatic
deploys ». Les deux moitiés comptent : l'URL est libre — un dépôt nu sur la
machine passe — et **rien ne se déploie tout seul**. C'est `deployer.sh` qui
déclenche, après les tests.

| Champ | Valeur |
|---|---|
| Repository URL | `ssh://ubuntu@79.137.35.129:8022/home/ubuntu/depots/hba.git` |
| Branch | `main` |
| Build Pack | `Docker Compose` |
| Docker Compose Location | `/docker-compose.prod.yml` |

Le schéma `ssh://` est obligatoire dès qu'il y a un port : la forme courte
`ubuntu@hôte:chemin` ne sait pas porter le 8022.

La clé publique du couple engendré par Coolify (**Keys & Tokens → Private
Keys**) doit être ajoutée à `~/.ssh/authorized_keys` de `ubuntu` sur le VPS.
Coolify se connecte à sa propre machine : son conteneur n'hérite pas des clés
de l'hôte.

### Les vingt-trois variables

Elles se saisissent dans l'onglet **Environment Variables** de la ressource, et
non dans un fichier. La liste exacte se lit sans rien afficher de secret :

```bash
python3 scripts/verifier-env-compose.py docker-compose.prod.yml
```

#### Ne jamais coller le bloc de la section 1 dans Coolify

Ce bloc est écrit pour un fichier `.env` lu par un shell. Coolify n'est ni l'un
ni l'autre : il attend **une variable par ligne, un nom et une valeur
littérale**. Trois choses s'y perdent, et la troisième a déjà coûté un
déploiement.

  • **Les commentaires.** Coolify recopie la valeur telle quelle dans
    `build-time.env`. Un `# Le compte administrateur — le seul moyen d'entrer.`
    devient une ligne que l'analyseur lit comme un nom de variable, et le
    déploiement échoue sur `unexpected character "\" in variable name`.

  • **Les valeurs sur plusieurs lignes.** Une seule variable qui porterait le
    fichier entier produit le même effet, en pire : quarante-neuf lignes là où
    on en attend vingt-trois, et l'erreur désigne un numéro de ligne qui ne
    correspond à rien de ce qu'on croit avoir saisi.

  • **`$(openssl rand …)` n'est PAS évalué.** Dans un shell, c'est une commande.
    Dans Coolify, c'est une chaîne de dix-neuf caractères — et ce serait
    littéralement le mot de passe administrateur. Engendrer les valeurs sur le
    poste, puis coller le RÉSULTAT.

Le contrôle qui reste possible ici : la liste des noms, à comparer à ce que
l'interface affiche.

`AUTHENTICATION__SIGNINGKEY` et `JWT__SIGNINGKEY` doivent porter la **même**
valeur. `SECURITY__SECRETPROTECTION__KEY` ne se régénère pas.

**Le piège du dollar vaut ici aussi.** Une valeur contenant `$` est lue comme
une référence de variable et devient une chaîne vide — le service part avec un
mot de passe tronqué et échoue à la connexion sur une erreur qui parle
d'authentification. Doubler : `$` devient `$$`.

### Le domaine et le port

**Le compose ne publie plus aucun port.** La passerelle publiait `8080:8080`,
pour qu'un proxy TLS posé à la main puisse l'interroger. Sous Coolify ce proxy
existe déjà, sur la même machine, et il tient ce port : le démarrage échouait
sur `Bind for 0.0.0.0:8080 failed: port is already allocated`.

Le publier serait de toute façon une faute — cela exposerait l'API **en clair**
sur Internet, à côté du HTTPS servi par le proxy. Celui-ci joint la passerelle
par le réseau Docker.

`gateway` porte donc `expose: ["8080"]` : rien n'est publié sur l'hôte, mais le
port reste **déclaré**, ce qui permet au proxy de savoir où router.

**Conséquence : sans domaine attribué à `gateway`, l'API n'est joignable de
nulle part.** Le compose seul ne suffit plus. Dans Coolify, section Domains de
la ressource, le domaine du service `gateway` doit désigner le port du
conteneur — la documentation donne la forme `http://exemple.com:3000` pour un
service qui n'écoute pas sur 80.

### Ce que Coolify NE fait pas

**Les migrations.** `deployer.sh --migrer` refuse désormais plutôt que de faire
semblant. À lancer service par service une fois la pile debout.

**Les buckets MinIO** — section 5, inchangée. **Les sujets Kafka** — le script
passe par `docker compose`, que Coolify pilote maintenant.

**Les identités gRPC par service** restent le premier trou à boucher : une seule
`INTERNAL__PRIVATEKEY` pour tous, là où il en faut une par service.

**`container_name: hba-<service>`** est conservé par le compose. Coolify le
respecte, mais un nom fixe interdit qu'une nouvelle version démarre à côté de
l'ancienne : chaque déploiement passe par un arrêt. Acceptable pour un MVP,
à revoir le jour où l'interruption coûte.

---

## 1. Le fichier d'environnement, hors du dépôt

Chaque secret du compose est une référence `${VAR:?...}` : Compose **refuse de
démarrer** si la variable manque, plutôt que de lancer un service avec une chaîne
vide. Les valeurs vivent dans un fichier que Git ne voit jamais.

```bash
umask 077
mkdir -p ~/secrets-hba-prod
$EDITOR ~/secrets-hba-prod/prod.env
chmod 600 ~/secrets-hba-prod/prod.env
```

Sur le POSTE si l'on déploie avec `deployer.sh`, sur le VPS si l'on lance
compose là-bas. `deployer.sh` refuse de partir si le fichier manque ou s'il est
lisible par d'autres que son propriétaire ; `HBA_ENV_FILE=<chemin>` le déplace.

Il lui faut :

```bash
# HBA_TAG n'est PAS à poser ici : `deployer.sh` le dérive du commit déployé.

# La base. HBA_PGHOST vaut 10.20.0.2 par défaut, inutile de le poser.
HBA_IDENTITY_PASSWORD=...
HBA_USER_PASSWORD=...
HBA_MEDIA_PASSWORD=...
HBA_FINANCIAL_PASSWORD=...
HBA_PROMOTION_PASSWORD=...
HBA_ENGAGEMENT_PASSWORD=...
HBA_CATALOG_PASSWORD=...
HBA_COMMERCE_PASSWORD=...
HBA_INVENTORY_PASSWORD=...
HBA_ORDER_PASSWORD=...
HBA_MERCHANT_PASSWORD=...
HBA_DELIVERY_PASSWORD=...
HBA_FOOD_PASSWORD=...

# Les clés partagées.
AUTHENTICATION__SIGNINGKEY=$(openssl rand -base64 48)
JWT__SIGNINGKEY=<la même valeur que ci-dessus>
INTERNAL__APIKEY=$(openssl rand -hex 32)
SECURITY__SECRETPROTECTION__KEY=$(openssl rand -hex 32)

# Le compte administrateur — le seul moyen d'entrer.
ADMIN__PASSWORD=$(openssl rand -base64 24)

# MinIO, et les mêmes valeurs pour media-service.
MINIO_ROOT_USER=hba-minio
MINIO_ROOT_PASSWORD=$(openssl rand -base64 24)
MEDIA__STORAGE__ACCESSKEYID=<même valeur que MINIO_ROOT_USER>
MEDIA__STORAGE__SECRETACCESSKEY=<même valeur que MINIO_ROOT_PASSWORD>
```

Les treize mots de passe de base sont ceux de
`./motsdepasse-<horodatage>.txt`, produit par `scripts/db/creer-bases.sh`.

`SECURITY__SECRETPROTECTION__KEY` **ne se régénère pas** : ce qu'elle a chiffré ne
se déchiffre pas avec la suivante. Une fois posée, elle est définitive.

**Les identités gRPC ne sont pas encore dans ce fichier.** `INTERNAL__PRIVATEKEY`
diffère par service — c'est ce que `scripts/generer-identites-internes.sh`
produit. Sous Compose, il faut une variable par service, et le compose engendré
n'en porte qu'une. C'est une lacune connue, notée à la fin de ce runbook.

## 2. Déployer depuis le poste

**Aucun registre, aucun passage par GitHub.** `scripts/deployer.sh` fait tout
depuis le Mac :

```bash
./scripts/deployer.sh prod --migrer --sujets
```

Il enchaîne : contrôles du dépôt, `dotnet build`, `dotnet test`, puis
construction et démarrage **sur le VPS**, migrations, sujets Kafka.

**Les images se construisent sur le VPS, pas sur le poste.** Le Mac est arm64,
les VPS sont amd64 : une image construite localement puis transférée démarre
quand même — Docker la refuse rarement — puis meurt sur « exec format error »,
ou tourne sous émulation à un dixième de la vitesse sans que rien ne le dise.
`docker buildx --platform linux/amd64` compilerait juste, mais émule le SDK .NET :
des heures pour vingt services.

Le script emploie donc un **contexte Docker sur SSH** : les sources sont envoyées
au démon du VPS, qui construit nativement. Le premier passage est long, les
suivants réemploient son cache de couches.

Trois garde-fous avant que quoi que ce soit parte :

- **un arbre de travail modifié interdit la production.** Le tag porterait
  `<sha>-sale`, qui ne désigne aucun commit retrouvable — et le jour d'un retour
  arrière, on redéploierait le commit sans le défaut qui était réellement en
  ligne. Toléré en staging, le tag le dit ;
- **les tests passent avant le déploiement.** `--sans-tests` existe, et le script
  écrit en rouge que rien n'a vérifié ce qui part ;
- **le démon joint est nommé** avant toute action. Un contexte qui pointe le
  mauvais hôte déploierait la production sur le staging sans qu'aucune commande
  n'échoue.

La production demande une confirmation tapée.

### Ce que le script fait aussi

```bash
./scripts/deployer.sh dev --sujets          # tout en local
./scripts/deployer.sh staging --migrer      # 193.168.145.162
./scripts/deployer.sh prod --sans-build     # redéployer sans reconstruire
```

Compose s'arrête sur la première variable d'environnement manquante en la
nommant. C'est voulu : un service lancé avec une clé de signature vide rejette
tous les jetons, démarre normalement, et rend 401 partout sans qu'aucune erreur
ne l'explique.

## 3. Les migrations

`./scripts/deployer.sh prod --migrer` les enchaîne, un service à la fois. Ce qui
suit décrit le geste manuel, pour un service isolé.

Le compose ne les applique pas de lui-même. Les services ont `Database:MigrateOnStartup` à
faux hors Development — c'est délibéré, les migrations sont une étape qu'on
déclenche et qu'on regarde.

```bash
docker compose --env-file ~/secrets-hba-prod/prod.env \
  -f docker-compose.prod.yml run --rm \
  -e DATABASE__MIGRATEONLY=true identity-service
```

À répéter pour chaque service porteur d'un `DbContext`. `MigrateOnly` applique
les migrations puis **rend la main** : aucun port ne s'ouvre, le conteneur sort
avec le code 0. Sans ce réglage, le conteneur démarrerait un serveur web et
`run` ne rendrait jamais la main.

## 4. Les sujets Kafka

**Les vingt `KafkaTopic` de `k8s/` sont inertes ici.** Ce sont des ressources
Strimzi, et il n'y a pas de Strimzi sous Compose. Leurs réglages —
`partitions: 3`, `replicas: 3`, `min.insync.replicas: 2` — ne s'appliquent pas,
et deux d'entre eux ne le pourraient pas : **le Compose n'a qu'un seul
courtier**, un facteur de réplication de 3 serait refusé.

Sans intervention, l'image Confluent crée les sujets toute seule au premier
producteur, avec ses propres réglages. Ce n'est pas une panne, c'est un
brouillard : les consommateurs s'abonnent au démarrage, avant qu'aucun événement
n'existe, et `librdkafka` ne crée rien à l'abonnement — contrairement au client
Java. Chaque service, chaque sujet, en boucle :

```
Subscribed topic not available: service.identity.v1: Broker: Unknown topic or partition
```

Les vraies erreurs Kafka se noient dedans.

```bash
HBA_COMPOSE_FILE=docker-compose.prod.yml ./scripts/kafka-topics.sh
HBA_COMPOSE_FILE=docker-compose.prod.yml ./scripts/kafka-topics.sh --describe
```

Le script dérive les sujets de la même source que les manifestes Kubernetes et
adapte le facteur de réplication au nombre de courtiers réellement présents. Il
annonce le fichier compose qu'il vise : le lancer sur le mauvais créerait les
sujets dans le Kafka du poste pendant qu'on croit préparer le VPS.

## 5. Les buckets MinIO

MinIO ne les crée pas tout seul, et `media-service` démarre très bien sans eux —
l'échec n'arrive qu'au premier envoi de fichier, en `NoSuchBucket`.

```bash
docker compose -f docker-compose.prod.yml exec minio \
  mc alias set local http://localhost:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"
docker compose -f docker-compose.prod.yml exec minio \
  mc mb --ignore-existing local/hba-public local/hba-private
```

## 6. Le proxy TLS

**Le compose n'en porte pas.** La passerelle publie `8080` en clair sur l'hôte ;
rien ne sert `https://api.hba-express.com`. Il faut un proxy devant — Caddy est
le plus court chemin, il obtient et renouvelle le certificat tout seul :

```
api.hba-express.com {
    reverse_proxy localhost:8080
}
```

Sans lui, l'API n'est joignable qu'en HTTP sur le port 8080, ce qu'aucune
application mobile n'acceptera.

## Les noms de conteneur

`hba-<service>` : `hba-identity-service`, `hba-gateway`, `hba-kafka`. Posés
explicitement par le générateur, sans préfixe de projet ni numéro d'exemplaire.

```bash
docker --context hba-prod logs -f hba-identity-service
docker --context hba-prod exec -it hba-minio sh
```

**Ce nom interdit la montée en charge.** `docker compose up --scale
order-service=3` échoue sur « can't set container_name and scale » : deux
conteneurs ne peuvent pas porter le même nom. C'est un arbitrage assumé pour un
MVP sur une machine — le jour où un service doit passer à plusieurs exemplaires,
il faudra lui retirer son `container_name`.

**Le nom ne porte pas l'environnement.** Staging et production ont des
conteneurs homonymes. Sur deux VPS distincts, c'est sans conséquence ; sur la
même machine, le second `up` échouerait sur un conflit de nom — et c'est le bon
échec, puisque le §2 interdit de les colocaliser.

**Ce nom ne sert qu'aux commandes d'exploitation.** Entre eux, les services se
joignent par le nom du SERVICE — `http://identity-service:8080`, tel qu'écrit
dans les `SERVICES__*` — que Compose pose en alias sur le réseau `hba-backend`.

## 7. Vérifier

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs identity-service | grep -i "amorçage administrateur"
```

La seconde commande doit dire
`« hector.adjakpa@hbatechettrade.com » CRÉÉ (actif, rôle Admin)`.

---

## Ce que ce déploiement ne donne pas

**Les identités gRPC ne sont pas câblées.** Chaque service doit recevoir SA clé
privée dans `INTERNAL__PRIVATEKEY`, plus le registre public commun. Le compose
engendré n'a qu'une variable pour tous. Tant que ce n'est pas corrigé, les appels
entre services répondront `Unauthenticated`. **C'est le premier trou à boucher.**

**Aucune supervision.** `OPENTELEMETRY__ENDPOINT` est vide. Le diagnostic tient
dans `docker compose logs`.

**Aucune sauvegarde.** Ni de la base — sur son VPS, sans pgBackRest ni réplique —
ni de MinIO, qui porte les pièces KYB sur le disque du VPS applicatif.

**Un seul courtier Kafka.** Facteur de réplication 1 : perdre le conteneur perd
les messages non consommés. Les manifestes Kubernetes demandaient trois répliques
et `min.insync.replicas: 2` ; c'est ce qu'on abandonne en passant à Compose sur
une machine.

**Aucune limite de ressources.** Un service qui s'emballe prend toute la mémoire
de la machine, et les vingt-deux autres tombent avec lui. Kubernetes donnait ça
gratuitement ; Compose demande un `deploy.resources.limits` par service.

**Pas de reprise ordonnée.** `restart: unless-stopped` relance un conteneur mort,
mais rien n'attend que Kafka soit prêt avant de relancer un consommateur.

**Rien de tout ceci n'a été exécuté.** Le compose est engendré et validé comme
YAML ; aucun conteneur n'a démarré. Et le dépôt n'a toujours pas compilé depuis
les corrections du 29 août.
