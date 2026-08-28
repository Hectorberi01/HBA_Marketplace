#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# Démarrage de la pile locale — CONSTRUCTION UN SERVICE À LA FOIS.
#
# POURQUOI CE SCRIPT EXISTE PLUTÔT QU'UN SIMPLE `docker compose up --build`.
#
# `up --build` construit toutes les images applicatives EN PARALLÈLE. Chacune lance un SDK
# .NET qui restaure des centaines de paquets puis compile une trentaine de
# projets. Sur une machine où Docker dispose de 4 à 8 Go, la somme dépasse la
# mémoire disponible : le noyau tue un processus au hasard et BuildKit rend
#
#     ResourceExhausted: cannot allocate memory
#
# L'erreur ne désigne aucun fichier, tombe sur un service différent à chaque
# essai, et fait chercher un problème de code là où il n'y en a pas.
#
# Construire séquentiellement rend le pic de mémoire égal à celui d'UNE image.
# C'est plus long en apparence — mais un build qui échoue à la douzième image
# puis qu'on relance coûte bien davantage.
#
# Usage :
#   ./scripts/dev-up.sh                    construit les services, puis démarre
#   ./scripts/dev-up.sh --core             services indispensables au parcours standard
#   ./scripts/dev-up.sh --marketplace      communs + marketplace + passerelle
#   ./scripts/dev-up.sh --marketplace-only marketplace seulement + passerelle
#   ./scripts/dev-up.sh --food             communs + marketplace + food + delivery minimal
#   ./scripts/dev-up.sh --food-only        food seulement + passerelle
#   ./scripts/dev-up.sh --delivery         communs + marketplace core + delivery + passerelle
#   ./scripts/dev-up.sh --delivery-only    delivery seulement + passerelle
#   ./scripts/dev-up.sh --common           services communs + passerelle
#   ./scripts/dev-up.sh --bff              BFF + passerelle
#   ./scripts/dev-up.sh --bff-only         BFF seulement + passerelle
#   ./scripts/dev-up.sh --all              les 31 images applicatives
#   ./scripts/dev-up.sh --core --list      affiche la liste sans construire
#   ./scripts/dev-up.sh gateway            ne construit QUE la passerelle
#   ./scripts/dev-up.sh restaurant-service order-service   deux services
#   ./scripts/dev-up.sh --fresh            supprime d'abord les volumes
#   ./scripts/dev-up.sh --no-cache         reconstruit sans cache Docker
#   ./scripts/dev-up.sh --build-only       s'arrête après la construction
#
# TOUCHER À `Directory.Packages.props` OU `Directory.Build.props` INVALIDE
#    LES IMAGES .NET.
#
# Les Dockerfiles les copient dans une couche précoce, avant la
# restauration NuGet. Changer une version de paquet — même pour un seul service —
# force donc une reconstruction complète. C'est le prix de la gestion centralisée
# des versions, et il se paie d'un coup.
# ═══════════════════════════════════════════════════════════════════════════

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.dev.yml"

cd "$ROOT_DIR"

FRESH=0
BUILD_ONLY=0
LIST_ONLY=0
NO_CACHE=0
PROFILE=all
ONLY=()

for arg in "$@"; do
  case "$arg" in
    --fresh)      FRESH=1 ;;
    --no-cache)   NO_CACHE=1 ;;
    --build-only) BUILD_ONLY=1 ;;
    --list)        LIST_ONLY=1 ;;
    --core)        PROFILE=core ;;
    --common)      PROFILE=common ;;
    --marketplace) PROFILE=marketplace ;;
    --marketplace-only) PROFILE=marketplace-only ;;
    --food)        PROFILE=food ;;
    --food-only)   PROFILE=food-only ;;
    --delivery)    PROFILE=delivery ;;
    --delivery-only) PROFILE=delivery-only ;;
    --bff)         PROFILE=bff ;;
    --bff-only)    PROFILE=bff-only ;;
    --all)         PROFILE=all ;;
    -*)           echo "Option inconnue : $arg" >&2; exit 2 ;;
    # TOUT ARGUMENT NON PRÉFIXÉ EST UN NOM DE SERVICE, ET C'EST LE POINT.
    #
    # Sans cela, la moindre retouche sur un seul service impose de reconstruire
    # toutes les images, l'une après l'autre. Séquentiel, cela dépasse vite la
    # demi-heure — et on finit par relancer `up --build` en parallèle, c'est-à-dire
    # à revenir au manque de mémoire que ce script existe pour éviter.
    *)            ONLY+=("$arg") ;;
  esac
done

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

if [ "$FRESH" -eq 1 ] && [ "$LIST_ONLY" -eq 0 ]; then
  # `down -v` SUPPRIME LES BASES. C'est justement le but.
  #
  # Le script `postgres/001-create-databases.sql` n'est exécuté qu'au PREMIER
  # démarrage du volume : `docker-entrypoint-initdb.d` est ignoré si les données
  # existent déjà. Sans `-v`, une base ajoutée au script n'apparaîtra jamais.
  echo "── Suppression des volumes (bases neuves) ──"
  compose down -v --remove-orphans
fi

COMMON_SERVICES=(
  identity-service
  user-service
  media-service
  notification-service
  payment-service
  promotion-service
  review-service
)

MARKETPLACE_SERVICES=(
  seller-service
  catalog-service
  inventory-service
  cart-service
  order-service
  return-refund-service
)

FOOD_SERVICES=(
  restaurant-service
  food-cart-service
  food-order-service
)

DELIVERY_SERVICES=(
  delivery-pricing-service
  delivery-service
  driver-service
  route-service
)

# VIDE DEPUIS D38 — LA PASSERELLE EST LE BFF.
#
# Les trois squelettes `client-bff`, `seller-bff` et `driver-bff` étaient
# construits par cette liste et joignables par personne : hors solution, hors
# routage, 9 routes sur 13 en 501. Le BFF réel vit dans la passerelle
# (`HBA.Gateway.Application/Bff/`, six suites de tests).
#
# Le tableau reste, vide, plutôt que d'être supprimé : les profils `bff` et
# `bff-only` ci-dessous continuent donc de démarrer la passerelle SEULE, ce qui
# est exactement ce qu'il faut pour travailler le BFF.
#
# ET IL S'EXPANSE PARTOUT EN `${TAB[@]+"${TAB[@]}"}`, JAMAIS EN `"${TAB[@]}"`.
#
# Sous `set -u`, le bash 3.2 de macOS traite un tableau VIDE exactement comme un
# tableau INEXISTANT : `"${BFF_SERVICES[@]}"` y échoue sur
#
#     BFF_SERVICES[@]: unbound variable
#
# Le garder vide plutôt que de le supprimer ne suffisait donc pas — c'était même
# la même panne, décalée : `./dev-up.sh --all` tombait avant de construire quoi
# que ce soit. La forme `${TAB[@]+…}` n'expanse que si le tableau a au moins un
# élément, et fonctionne aussi bien sur bash 3.2 que sur bash 5.
BFF_SERVICES=()

# L'ordre suit les dépendances de démarrage : ce n'est pas nécessaire à la
# construction, mais un échec précoce tombe alors sur le service le plus en
# amont — celui qu'on veut voir échouer en premier.
case "$PROFILE" in
  core)
    SERVICES=(
      identity-service
      user-service
      media-service
      seller-service
      catalog-service
      inventory-service
      cart-service
      order-service
      payment-service
      notification-service
      gateway
    )
    ;;
  common)
    SERVICES=("${COMMON_SERVICES[@]}" gateway)
    ;;
  marketplace)
    SERVICES=("${COMMON_SERVICES[@]}" "${MARKETPLACE_SERVICES[@]}" gateway)
    ;;
  marketplace-only)
    SERVICES=("${MARKETPLACE_SERVICES[@]}" gateway)
    ;;
  food)
    SERVICES=(
      "${COMMON_SERVICES[@]}"
      "${MARKETPLACE_SERVICES[@]}"
      delivery-pricing-service
      delivery-service
      "${FOOD_SERVICES[@]}"
      gateway
    )
    ;;
  food-only)
    SERVICES=(
      delivery-pricing-service
      delivery-service
      "${FOOD_SERVICES[@]}"
      gateway
    )
    ;;
  delivery)
    SERVICES=(
      "${COMMON_SERVICES[@]}"
      seller-service
      catalog-service
      inventory-service
      cart-service
      order-service
      "${DELIVERY_SERVICES[@]}"
      gateway
    )
    ;;
  delivery-only)
    SERVICES=("${DELIVERY_SERVICES[@]}" gateway)
    ;;
  bff)
    SERVICES=(${BFF_SERVICES[@]+"${BFF_SERVICES[@]}"} gateway)
    ;;
  bff-only)
    SERVICES=(${BFF_SERVICES[@]+"${BFF_SERVICES[@]}"} gateway)
    ;;
  all)
    SERVICES=(
      "${COMMON_SERVICES[@]}"
      "${MARKETPLACE_SERVICES[@]}"
      "${FOOD_SERVICES[@]}"
      "${DELIVERY_SERVICES[@]}"
      ${BFF_SERVICES[@]+"${BFF_SERVICES[@]}"}
      gateway
    )
    ;;
esac

# Une liste explicite l'emporte sur l'ordre par défaut : on construit ce qui est
# demandé, dans l'ordre demandé.
if [ ${#ONLY[@]} -gt 0 ]; then
  SERVICES=("${ONLY[@]}")
fi

TOTAL=${#SERVICES[@]}

if [ "$LIST_ONLY" -eq 1 ]; then
  printf '%s\n' "${SERVICES[@]}"
  echo
  echo "$TOTAL service(s)."
  exit 0
fi

INDEX=0

for service in "${SERVICES[@]}"; do
  INDEX=$((INDEX + 1))
  echo
  echo "── [$INDEX/$TOTAL] $service ──"
  if [ "$NO_CACHE" -eq 1 ]; then
    compose build --no-cache "$service"
  else
    compose build "$service"
  fi
done

if [ "$BUILD_ONLY" -eq 1 ]; then
  echo
  echo "Construction terminée ($TOTAL images). Démarrage non demandé."
  exit 0
fi

echo
echo "── Démarrage ──"
# Les images sont déjà construites : `up` ne fait plus que démarrer, et les
# `depends_on: condition: service_healthy` séquencent l'attente de postgres,
# redis et kafka.
if [ ${#ONLY[@]} -gt 0 ] || [ "$PROFILE" != all ]; then
  compose up -d "${SERVICES[@]}"
else
  compose up -d
fi

# ── Sujets Kafka ───────────────────────────────────────────────────────────
#
# APRÈS `up`, ET SANS FAIRE ÉCHOUER LE DÉMARRAGE.
#
# Les sujets ne peuvent être créés qu'une fois le courtier debout, donc pas
# avant. Et un échec ici ne doit pas condamner une pile par ailleurs saine :
# sans sujets, la plateforme fonctionne — elle est seulement bruyante, et
# dépendante du premier producteur pour la création. D'où le `|| true`.
#
# On vérifie d'abord que le courtier tourne : `./dev-up.sh gateway` ne démarre
# que la passerelle, et attendre soixante secondes un Kafka qu'on n'a pas
# demandé serait une punition gratuite.
echo
if compose ps --status running --services 2>/dev/null | grep -qx kafka; then
  "$ROOT_DIR/scripts/kafka-topics.sh" || {
    echo "  Sujets non créés. La pile démarre quand même ;" >&2
    echo "    relancer plus tard : ./scripts/kafka-topics.sh" >&2
  }
else
  echo "── Sujets Kafka : courtier non démarré, étape ignorée."
  echo "   Après un démarrage complet : ./scripts/kafka-topics.sh"
fi

echo
echo "Passerelle : http://localhost:8080/health/ready"
echo "Sujets     : ./scripts/kafka-topics.sh --list"
echo "Kafka UI   : http://localhost:8090"
echo "Journaux   : docker compose -f docker-compose.dev.yml logs -f gateway"
