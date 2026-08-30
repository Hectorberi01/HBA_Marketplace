#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# DÉPLOIEMENT DEPUIS LE POSTE, SANS PASSER PAR GITHUB.
#
#     ./scripts/deployer.sh dev
#     ./scripts/deployer.sh staging
#     ./scripts/deployer.sh prod
#
# ═══════════════════════════════════════════════════════════════════════════════
# POURQUOI LES IMAGES SE CONSTRUISENT SUR LE VPS, ET NON SUR LE POSTE.
#
# Le poste est un Mac Apple Silicon : arm64. Les VPS sont amd64. Une image
# construite localement et transférée par `docker save` DÉMARRE quand même —
# Docker la refuse rarement — puis meurt sur « exec format error », ou pire,
# tourne sous émulation à un dixième de la vitesse sans que rien ne le dise.
#
# `docker buildx --platform linux/amd64` compilerait juste, mais émule le SDK
# .NET : compter des heures pour vingt services.
#
# Ce script emploie donc un CONTEXTE DOCKER SUR SSH. Le contexte de build — les
# sources — est envoyé au démon du VPS, qui construit nativement. Le premier
# passage est long ; les suivants réemploient son cache de couches.
#
# CE QUE CE SCRIPT NE FAIT PAS :
#   - il ne pousse aucune image dans un registre, et n'en tire aucune ;
#   - il ne crée ni le fichier d'environnement, ni les bases, ni le proxy TLS —
#     voir docs/RUNBOOK-COMPOSE.md ;
#   - il n'applique les migrations que si on le lui demande (`--migrer`) ;
#   - il ne sait pas revenir en arrière. Le retour arrière, c'est redéployer le
#     commit précédent.
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

# ── Les cibles ───────────────────────────────────────────────────────────────
#
# Le VPS de la base (10.20.0.2) n'apparaît PAS ici, et c'est délibéré : il n'est
# joignable que par le tunnel, et rien de ce script n'a à s'y connecter.
VPS_STAGING="193.168.145.162"
VPS_PROD="79.137.35.129"

# ═════════════════════════════════════════════════════════════════════════════
# LA DESTINATION EST UN ALIAS SSH, PAS UN COUPLE UTILISATEUR@ADRESSE.
#
# CE QUI ÉTAIT CASSÉ : ce script posait `root@<ip>` sur le port 22. Le VPS de
# production écoute le 8022, avec l'utilisateur `ubuntu` et une clé dédiée.
# `ssh root@79.137.35.129 true` rend « Connection refused », et le transport SSH
# de Docker, lui, ne rend RIEN — juste un contexte injoignable.
#
# Recopier port, utilisateur et clé ici les mettrait à deux endroits, et
# `~/.ssh/config` resterait la source de vérité pour `ssh` mais pas pour ce
# script. Pire : le nom donné à `ssh` est ce qui sélectionne le bloc `Host`.
# `ssh ubuntu@79.137.35.129 -p 8022` NE lit PAS `IdentityFile` du bloc
# `Host ovh-server` — la correspondance se fait sur le nom écrit, pas sur
# l'adresse résolue. L'alias est donc le seul moyen d'hériter de la clé.
#
# CE QUE CELA NE COUVRE PAS : l'alias vit dans `~/.ssh/config`, hors du dépôt.
# Un autre poste n'a pas le même. D'où les deux variables d'environnement, et
# la vérification plus bas que l'alias résout bien vers l'adresse attendue.
# ═════════════════════════════════════════════════════════════════════════════
DESTINATION_STAGING="${HBA_SSH_STAGING:-hba-staging}"
DESTINATION_PROD="${HBA_SSH_PROD:-ovh-server}"

rouge()  { printf '\033[31m%s\033[0m\n' "$*" >&2; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
titre()  { printf '\n\033[1m═══ %s ═══\033[0m\n' "$*"; }
info()   { printf '    %s\n' "$*"; }

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/deployer.sh <dev|staging|prod> [options]

  --sans-tests     saute la compilation et les tests. À N'EMPLOYER QUE si on
                   vient de les lancer : c'est la seule barrière avant le VPS.
  --sans-build     ne reconstruit pas les images ; redéploie celles du VPS.
  --migrer         applique les migrations après le démarrage.
  --sujets         crée les sujets Kafka après le démarrage.
  --oui            ne demande aucune confirmation (pour un usage scripté).

Le fichier d'environnement est lu SUR CE POSTE, en
~/secrets-hba-<cible>/<cible>.env — compose le lit côté client, pas sur le VPS.
Le remplacer par HBA_ENV_FILE=<chemin>. Voir docs/RUNBOOK-COMPOSE.md.

La destination SSH est un alias de ~/.ssh/config : « ovh-server » pour la
production, « hba-staging » pour le staging. La remplacer par HBA_SSH_PROD ou
HBA_SSH_STAGING. C'est l'alias qui porte le port et la clé.
FIN
  exit 2
}

CIBLE="${1:-}"
[ -n "$CIBLE" ] || usage
shift || true

SANS_TESTS=0; SANS_BUILD=0; MIGRER=0; SUJETS=0; SANS_DEMANDER=0
for arg in "$@"; do
  case "$arg" in
    --sans-tests) SANS_TESTS=1 ;;
    --sans-build) SANS_BUILD=1 ;;
    --migrer)     MIGRER=1 ;;
    --sujets)     SUJETS=1 ;;
    --oui)        SANS_DEMANDER=1 ;;
    *) rouge "option inconnue : $arg"; usage ;;
  esac
done

case "$CIBLE" in
  dev)
    COMPOSE="docker-compose.dev.yml"; HOTE=""; DESTINATION=""; ENV_FILE="" ;;
  staging)
    COMPOSE="docker-compose.prod.yml"; HOTE="$VPS_STAGING"
    DESTINATION="$DESTINATION_STAGING"
    ENV_FILE="${HBA_ENV_FILE:-$HOME/secrets-hba-staging/staging.env}" ;;
  prod)
    COMPOSE="docker-compose.prod.yml"; HOTE="$VPS_PROD"
    DESTINATION="$DESTINATION_PROD"
    ENV_FILE="${HBA_ENV_FILE:-$HOME/secrets-hba-prod/prod.env}" ;;
  *) rouge "cible inconnue : $CIBLE"; usage ;;
esac

# ═════════════════════════════════════════════════════════════════════════════
# 1. L'ÉTAT DU DÉPÔT — CE QUI PART DOIT ÊTRE CE QUI EST ÉCRIT.
#
# Déployer un arbre de travail modifié produit une version que personne ne peut
# retrouver : le tag dit un commit, l'image en contient un autre. Le jour d'un
# retour arrière, on redéploie le commit — et le défaut reste.
# ═════════════════════════════════════════════════════════════════════════════
titre "État du dépôt"
SHA="$(git rev-parse --short HEAD)"
SALE=""
if ! git diff --quiet || ! git diff --cached --quiet; then
  SALE="-sale"
fi
TAG="${SHA}${SALE}"
info "commit  : ${SHA}"
info "tag     : ${TAG}"

if [ -n "$SALE" ]; then
  if [ "$CIBLE" = "prod" ]; then
    rouge "REFUS : l'arbre de travail est modifié, et la cible est la production."
    rouge "  Le tag « ${TAG} » ne désignerait aucun commit retrouvable."
    rouge "  Commiter d'abord, ou déployer en staging."
    exit 1
  fi
  info "arbre modifié — toléré hors production, le tag porte « -sale »"
fi

# ═════════════════════════════════════════════════════════════════════════════
# 2. LES CONTRÔLES ET LES TESTS — LA SEULE BARRIÈRE AVANT LE VPS.
# ═════════════════════════════════════════════════════════════════════════════
if [ "$SANS_TESTS" = "0" ]; then
  # ═══════════════════════════════════════════════════════════════════════════
  # L'ENVIRONNEMENT DES OUTILS SE POSE TOUT SEUL.
  #
  # `check-all.sh` disait « PyYAML absent, lancer preparer-outils.sh », puis
  # continuait — et deux contrôles sur vingt-deux sortaient en échec, ce qui
  # arrêtait le déploiement. L'utilisateur voyait donc trois cents lignes de
  # contrôles verts se terminer par un refus dont la cause tenait en une
  # commande qu'il fallait avoir lue au milieu.
  #
  # Demander un geste préalable à chaque fois, c'est le rendre facultatif — et
  # celui-ci ne l'est pas : sans PyYAML, le contrôle des manifestes et celui du
  # compose ne peuvent PAS se dégrader, ils lisent de la structure.
  #
  # Il est donc posé ici, une fois, en l'annonçant. Le dossier est local au
  # dépôt et ignoré par Git : rien de global n'est touché.
  # ═══════════════════════════════════════════════════════════════════════════
  if [ ! -x "$ROOT_DIR/.venv/bin/python3" ]; then
    titre "Outils de contrôle"
    info "aucun environnement Python — il est posé maintenant"
    if ! ./scripts/preparer-outils.sh; then
      rouge "La préparation des outils a échoué (voir ci-dessus)."
      rouge "  Sans elle, le contrôle des manifestes et celui du compose ne"
      rouge "  peuvent pas s'exécuter, et le déploiement s'arrêterait de toute"
      rouge "  façon. Corriger l'installation Python, puis relancer."
      exit 1
    fi
  fi

  titre "Contrôles du dépôt"
  ./scripts/check-all.sh

  titre "Compilation"
  dotnet build HBA.sln --configuration Release /m:1

  titre "Tests"
  dotnet test HBA.sln --configuration Release --no-build \
    --logger "console;verbosity=minimal"
  vert "tests au vert"
else
  rouge "TESTS SAUTÉS (--sans-tests). Rien n'a vérifié ce qui part."
fi

# ── Dev : tout est local, on s'arrête là ─────────────────────────────────────
if [ "$CIBLE" = "dev" ]; then
  titre "Démarrage local"
  if [ "$SANS_BUILD" = "0" ]; then
    docker compose -f "$COMPOSE" build
  fi
  docker compose -f "$COMPOSE" up -d
  if [ "$SUJETS" = "1" ]; then
    titre "Sujets Kafka"
    HBA_COMPOSE_FILE="$ROOT_DIR/$COMPOSE" ./scripts/kafka-topics.sh
  fi
  vert "dev démarré — docker compose -f $COMPOSE ps"
  exit 0
fi

# ═════════════════════════════════════════════════════════════════════════════
# 3. LE CONTEXTE DOCKER SUR SSH.
#
# `docker context` fait pointer le client local vers le démon DU VPS. Toutes les
# commandes qui suivent s'exécutent donc là-bas, avec les sources envoyées par
# SSH comme contexte de build. Aucune image ne transite en tant qu'image.
# ═════════════════════════════════════════════════════════════════════════════
CONTEXTE="hba-${CIBLE}"
titre "Contexte Docker « ${CONTEXTE} » → ${DESTINATION}"

# ═════════════════════════════════════════════════════════════════════════════
# ON RÉSOUT L'ALIAS AVANT DE S'EN SERVIR, ET ON VÉRIFIE OÙ IL MÈNE.
#
# `ssh -G` rend la configuration EFFECTIVE pour une destination, sans ouvrir de
# connexion : l'adresse, l'utilisateur, le port, tels que `ssh` les emploiera.
#
# Le contrôle qui compte est la dernière ligne : que l'alias mène bien à
# l'adresse que ce script associe à la cible. Sans lui, un `Host ovh-server`
# réécrit un jour vers une autre machine déploierait la production ailleurs,
# et rien ne le dirait. Un alias absent tombe dans le même filet : `ssh -G`
# rend alors le nom lui-même comme hôte, qui ne ressemble à aucune adresse.
# ═════════════════════════════════════════════════════════════════════════════
CONFIG_SSH="$(ssh -G "$DESTINATION" 2>/dev/null || true)"
HOTE_RESOLU="$(printf '%s\n' "$CONFIG_SSH" | awk '/^hostname /{print $2; exit}')"
USER_RESOLU="$(printf '%s\n' "$CONFIG_SSH" | awk '/^user /{print $2; exit}')"
PORT_RESOLU="$(printf '%s\n' "$CONFIG_SSH" | awk '/^port /{print $2; exit}')"

info "alias         : ${DESTINATION}"
info "résout vers   : ${USER_RESOLU:-?}@${HOTE_RESOLU:-?}:${PORT_RESOLU:-?}"

if [ "$HOTE_RESOLU" != "$HOTE" ]; then
  rouge "REFUS : « ${DESTINATION} » ne mène pas à la machine attendue pour ${CIBLE}."
  rouge "  attendu : ${HOTE}"
  rouge "  résolu  : ${HOTE_RESOLU:-<rien>}"
  rouge ""
  rouge "  Si l'alias n'existe pas sur ce poste, l'ajouter à ~/.ssh/config :"
  rouge ""
  rouge "      Host ${DESTINATION}"
  rouge "          HostName ${HOTE}"
  rouge "          User <utilisateur>"
  rouge "          Port <port>"
  rouge "          IdentityFile ~/.ssh/<clé>"
  rouge "          IdentitiesOnly yes"
  rouge ""
  rouge "  Ou désigner un autre alias : HBA_SSH_$(printf '%s' "$CIBLE" | tr '[:lower:]' '[:upper:]')=<alias> ./scripts/deployer.sh ${CIBLE}"
  exit 1
fi

POINT_ATTENDU="ssh://${DESTINATION}"

if ! docker context inspect "$CONTEXTE" >/dev/null 2>&1; then
  docker context create "$CONTEXTE" \
    --docker "host=${POINT_ATTENDU}" \
    --description "HBA ${CIBLE}"
  info "contexte créé"
else
  # ═══════════════════════════════════════════════════════════════════════════
  # UN CONTEXTE EXISTANT N'EST PAS UN CONTEXTE CORRECT.
  #
  # `docker context create` ne s'exécute qu'à la première fois. Si l'adresse du
  # VPS change, ou si HBA_SSH_PROD est posé après coup, le contexte garde son
  # ancien point de terminaison — et ce script le réemploie en silence.
  #
  # Le cas qui coûte cher : déployer la PRODUCTION sur le staging parce que
  # « hba-prod » avait été créé un jour où HOTE valait autre chose. Aucune
  # commande n'échoue, `ps` montre des conteneurs qui tournent, et la mauvaise
  # machine sert le trafic.
  #
  # On compare donc, et on refuse. La correction est destructrice d'un objet
  # local seulement, d'où le fait qu'on la nomme au lieu de la faire : effacer
  # un contexte que quelqu'un a réglé à la main pour de bonnes raisons serait
  # pire que s'arrêter.
  # ═══════════════════════════════════════════════════════════════════════════
  POINT_ACTUEL="$(docker context inspect "$CONTEXTE" \
    --format '{{.Endpoints.docker.Host}}' 2>/dev/null || echo "")"

  if [ "$POINT_ACTUEL" != "$POINT_ATTENDU" ]; then
    rouge "REFUS : le contexte « ${CONTEXTE} » ne pointe pas où ce script croit."
    rouge "  attendu : ${POINT_ATTENDU}"
    rouge "  actuel  : ${POINT_ACTUEL:-<illisible>}"
    rouge "  Corriger : docker context rm ${CONTEXTE}   puis relancer."
    exit 1
  fi
  info "contexte déjà présent, et il pointe bien ${POINT_ACTUEL}"
fi

# ON VÉRIFIE QU'ON PARLE BIEN À LA BONNE MACHINE.
#
# Un contexte qui pointe le mauvais hôte déploie la production sur le staging
# sans qu'aucune commande n'échoue. `docker info` nomme le démon joint.
NOM_DISTANT="$(docker --context "$CONTEXTE" info --format '{{.Name}}' 2>/dev/null || true)"
if [ -z "$NOM_DISTANT" ]; then
  rouge "Impossible de joindre le démon Docker de ${HOTE}."
  rouge ""
  # LE TRANSPORT SSH DE DOCKER EST NON INTERACTIF.
  #
  # Il n'a ni terminal ni moyen de poser une question. Tout ce qui ferait
  # s'arrêter `ssh` sur une invite — l'empreinte d'un hôte jamais joint, une
  # demande de mot de passe, une phrase de passe de clé — ne produit PAS de
  # message : la connexion meurt, et `docker info` rend une chaîne vide. D'où
  # cette liste, qui distingue les quatre pannes derrière le même silence.
  rouge "  La connexion SSH de Docker ne peut RIEN demander. Elle échoue sans"
  rouge "  un mot dès qu'une invite apparaît. Dans l'ordre :"
  rouge ""
  rouge "  1. ssh ${DESTINATION} true"
  rouge "     Si elle demande d'accepter une empreinte, ou un mot de passe, c'est là."
  rouge "     Une connexion manuelle une fois suffit à enregistrer l'empreinte ;"
  rouge "     le mot de passe, lui, demande une clé : ssh-copy-id ${DESTINATION}"
  rouge ""
  rouge "  2. ssh ${DESTINATION} docker version"
  rouge "     Docker peut simplement ne pas être installé sur le VPS."
  rouge ""
  rouge "  3. ssh ${DESTINATION} id"
  rouge "     L'utilisateur doit pouvoir parler au démon — root, ou membre du"
  rouge "     groupe docker."
  rouge ""
  rouge "  4. docker context inspect ${CONTEXTE} --format '{{.Endpoints.docker.Host}}'"
  rouge "     Pour lire l'adresse que le contexte emploie réellement."
  exit 1
fi
info "démon joint : ${NOM_DISTANT}"

ARCH_DISTANTE="$(docker --context "$CONTEXTE" info --format '{{.Architecture}}' 2>/dev/null || echo inconnue)"
info "architecture : ${ARCH_DISTANTE}"

# ═════════════════════════════════════════════════════════════════════════════
# 4. LA CONFIRMATION, POUR LA PRODUCTION SEULEMENT.
# ═════════════════════════════════════════════════════════════════════════════
if [ "$CIBLE" = "prod" ] && [ "$SANS_DEMANDER" = "0" ]; then
  titre "Confirmation"
  info "cible   : PRODUCTION — ${HOTE}"
  info "commit  : ${SHA}"
  printf '    Taper « oui » pour continuer : '
  read -r reponse
  if [ "$reponse" != "oui" ]; then
    rouge "annulé."
    exit 1
  fi
fi

# ═════════════════════════════════════════════════════════════════════════════
# 5. CONSTRUCTION ET DÉMARRAGE, SUR LE VPS.
#
# `HBA_TAG` est lu par `docker-compose.prod.yml` pour nommer les images. Comme
# rien n'est tiré d'un registre, ce tag ne sert qu'à NOMMER localement ce qui
# vient d'être construit — mais il le fait avec le SHA, donc `docker images` sur
# le VPS dit quel commit tourne.
# ═════════════════════════════════════════════════════════════════════════════
# ═════════════════════════════════════════════════════════════════════════════
# LE FICHIER D'ENVIRONNEMENT EST LU SUR LE POSTE, PAS SUR LE VPS.
#
# CE QUI ÉTAIT CASSÉ : ce script posait `ENV_FILE="\$HOME/secrets-hba-prod/..."`,
# avec le dollar échappé, en croyant désigner un chemin CHEZ LE VPS. Deux fautes
# dans une seule ligne.
#
# La première : `--env-file` et `-f` sont lus par le CLIENT compose, ici, sur le
# Mac. Seuls les appels d'API partent vers le démon distant. Un fichier posé sur
# le VPS n'aurait jamais été ouvert.
#
# La seconde : la chaîne littérale « $HOME/secrets-... » n'est développée par
# personne — ni ici, puisque le dollar est échappé, ni là-bas, puisque rien ne
# passe par un shell distant. Compose aurait cherché un dossier nommé « $HOME ».
#
# CE QUE CELA NE COUVRE PAS : le fichier reste en clair sur le poste. C'est le
# prix d'un déploiement depuis la machine du développeur, et c'est pourquoi on
# exige au moins qu'il ne soit lisible que par son propriétaire.
# ═════════════════════════════════════════════════════════════════════════════
if [ ! -f "$ENV_FILE" ]; then
  rouge "Fichier d'environnement introuvable : ${ENV_FILE}"
  rouge "  Il est lu ICI, sur ce poste — pas sur le VPS."
  rouge "  Voir docs/RUNBOOK-COMPOSE.md §1 pour la liste des variables attendues."
  rouge "  Autre emplacement : HBA_ENV_FILE=<chemin> ./scripts/deployer.sh ${CIBLE}"
  exit 1
fi

# GNU d'abord : sur macOS `stat -c` échoue proprement et l'on bascule sur `-f`,
# alors que `stat -f` sous GNU signifie « système de fichiers » et rendrait un
# « ? » silencieux — le contrôle serait sauté sans que rien ne le dise.
MODE_ENV="$(stat -c '%a' "$ENV_FILE" 2>/dev/null || stat -f '%Lp' "$ENV_FILE" 2>/dev/null || echo "?")"
if [ "$MODE_ENV" != "600" ] && [ "$MODE_ENV" != "?" ]; then
  rouge "REFUS : ${ENV_FILE} est en mode ${MODE_ENV}."
  rouge "  Il porte tous les mots de passe de ${CIBLE}. Corriger :"
  rouge "      chmod 600 ${ENV_FILE}"
  exit 1
fi
info "environnement : ${ENV_FILE} (mode ${MODE_ENV}, lu sur ce poste)"

COMPOSE_DISTANT=(docker --context "$CONTEXTE" compose
                 --env-file "$ENV_FILE" -f "$COMPOSE" -p "hba-${CIBLE}")

if [ "$SANS_BUILD" = "0" ]; then
  titre "Construction sur ${HOTE} (le premier passage est long)"
  HBA_TAG="$TAG" "${COMPOSE_DISTANT[@]}" build
fi

titre "Démarrage"
HBA_TAG="$TAG" "${COMPOSE_DISTANT[@]}" up -d --remove-orphans

if [ "$MIGRER" = "1" ]; then
  titre "Migrations"
  # UN SERVICE À LA FOIS, ET SÉQUENTIELLEMENT.
  #
  # `MigrateOnly` applique les migrations puis rend la main. Les lancer en
  # parallèle ferait migrer plusieurs services contre la même base — le verrou
  # consultatif d'EF évite la corruption, pas l'attente ni le désordre.
  for service in identity-service user-service media-service payment-service \
                 promotion-service review-service catalog-service cart-service \
                 inventory-service order-service seller-service \
                 delivery-service driver-service \
                 food-cart-service food-order-service restaurant-service; do
    info "migration : ${service}"
    HBA_TAG="$TAG" "${COMPOSE_DISTANT[@]}" run --rm \
      -e DATABASE__MIGRATEONLY=true "$service" || {
        rouge "la migration de ${service} a échoué — arrêt."
        exit 1
      }
  done
  vert "migrations appliquées"
fi

if [ "$SUJETS" = "1" ]; then
  titre "Sujets Kafka"
  DOCKER_CONTEXT="$CONTEXTE" HBA_COMPOSE_FILE="$ROOT_DIR/$COMPOSE" \
    ./scripts/kafka-topics.sh
fi

titre "État"
"${COMPOSE_DISTANT[@]}" ps

vert "déployé sur ${CIBLE} (${HOTE}) — commit ${SHA}"
info "journaux : docker --context ${CONTEXTE} compose -p hba-${CIBLE} logs -f <service>"
