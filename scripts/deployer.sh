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
UTILISATEUR_VPS="${HBA_VPS_USER:-root}"

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

Le fichier d'environnement est attendu en ~/secrets-hba-<cible>/<cible>.env
sur le VPS pour staging et prod. Voir docs/RUNBOOK-COMPOSE.md.
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
    COMPOSE="docker-compose.dev.yml"; HOTE=""; ENV_FILE="" ;;
  staging)
    COMPOSE="docker-compose.prod.yml"; HOTE="$VPS_STAGING"
    ENV_FILE="\$HOME/secrets-hba-staging/staging.env" ;;
  prod)
    COMPOSE="docker-compose.prod.yml"; HOTE="$VPS_PROD"
    ENV_FILE="\$HOME/secrets-hba-prod/prod.env" ;;
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
titre "Contexte Docker « ${CONTEXTE} » → ${UTILISATEUR_VPS}@${HOTE}"

if ! docker context inspect "$CONTEXTE" >/dev/null 2>&1; then
  docker context create "$CONTEXTE" \
    --docker "host=ssh://${UTILISATEUR_VPS}@${HOTE}" \
    --description "HBA ${CIBLE}"
  info "contexte créé"
else
  info "contexte déjà présent"
fi

# ON VÉRIFIE QU'ON PARLE BIEN À LA BONNE MACHINE.
#
# Un contexte qui pointe le mauvais hôte déploie la production sur le staging
# sans qu'aucune commande n'échoue. `docker info` nomme le démon joint.
NOM_DISTANT="$(docker --context "$CONTEXTE" info --format '{{.Name}}' 2>/dev/null || true)"
if [ -z "$NOM_DISTANT" ]; then
  rouge "Impossible de joindre le démon Docker de ${HOTE}."
  rouge "  Vérifier : ssh ${UTILISATEUR_VPS}@${HOTE} docker version"
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
