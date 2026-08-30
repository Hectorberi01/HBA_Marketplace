#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# APPLIQUER LES MIGRATIONS SUR LA PILE DEPLOYEE PAR COOLIFY.
#
#     ./scripts/migrer-prod.sh prod
#     ./scripts/migrer-prod.sh prod --seulement identity-service
#
# ═══════════════════════════════════════════════════════════════════════════════
# POURQUOI UN SCRIPT SEPARE, ET NON UN CROCHET DE COOLIFY.
#
# Le bloc « Deployment lifecycle » de Coolify offre UNE commande dans UN
# conteneur. Nous avons seize services porteurs d'un DbContext, chacun avec son
# assembly et sa base ; une commande executee DANS un conteneur n'a pas acces au
# demon Docker et ne peut donc pas en piloter quinze autres. Le remplissage du
# champ — `php artisan migrate` — dit a quel genre d'application il s'adresse.
#
# POURQUOI `compose run` ET NON `docker exec`.
#
# `exec` exige un conteneur qui tourne, et un nom stable pour le designer.
# Coolify nomme `identity-service-<uuid>`, suffixe qui change a chaque
# deploiement. `compose run` part du FICHIER : il fabrique un conteneur jetable
# a partir de l'image deja construite, l'execute, et le retire. Il fonctionne
# meme quand plus rien ne tourne — ce qui est precisement le cas quand la base
# est vide et que tous les services viennent de mourir.
#
# `Database:MigrateOnly` applique les migrations puis REND LA MAIN : aucun port
# ne s'ouvre, le processus sort avec le code 0. Sans ce reglage, `run`
# demarrerait un serveur web et n'en reviendrait jamais.
#
# CE QUE CE SCRIPT NE FAIT PAS :
#   - il ne cree ni les bases ni les roles : voir scripts/db/creer-bases.sh ;
#   - il ne verifie pas que les migrations soient a jour vis-a-vis des entites ;
#   - il ne sait pas revenir en arriere. EF Core sait descendre d'une migration,
#     mais pas defaire une colonne supprimee avec ses donnees.
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

VPS_STAGING="193.168.145.162"
VPS_PROD="79.137.35.129"
DESTINATION_STAGING="${HBA_SSH_STAGING:-hba-staging}"
DESTINATION_PROD="${HBA_SSH_PROD:-ovh-server}"

rouge()  { printf '\033[31m%s\033[0m\n' "$*" >&2; }
vert()   { printf '\033[32m%s\033[0m\n' "$*"; }
titre()  { printf '\n\033[1m═══ %s ═══\033[0m\n' "$*"; }
info()   { printf '    %s\n' "$*"; }

CIBLE="${1:-}"
case "$CIBLE" in
  prod)    HOTE="$VPS_PROD";    DESTINATION="$DESTINATION_PROD" ;;
  staging) HOTE="$VPS_STAGING"; DESTINATION="$DESTINATION_STAGING" ;;
  *) rouge "usage: ./scripts/migrer-prod.sh <prod|staging> [--depuis <service>] [--seulement <service>] [--oui]"; exit 2 ;;
esac
shift

SEULEMENT=""
DEPUIS=""
SANS_DEMANDER=0
while [ $# -gt 0 ]; do
  case "$1" in
    --seulement) SEULEMENT="${2:-}"; shift 2 ;;
    --depuis)    DEPUIS="${2:-}"; shift 2 ;;
    --oui)       SANS_DEMANDER=1; shift ;;
    *) rouge "option inconnue : $1"; exit 2 ;;
  esac
done

# ── La destination SSH, verifiee comme dans deployer.sh ──────────────────────
CONFIG_SSH="$(ssh -G "$DESTINATION" 2>/dev/null || true)"
HOTE_RESOLU="$(printf '%s\n' "$CONFIG_SSH" | awk '/^hostname /{print $2; exit}')"
if [ "$HOTE_RESOLU" != "$HOTE" ]; then
  rouge "REFUS : « ${DESTINATION} » ne mène pas à la machine attendue pour ${CIBLE}."
  rouge "  attendu : ${HOTE}   résolu : ${HOTE_RESOLU:-<rien>}"
  exit 1
fi

# ═════════════════════════════════════════════════════════════════════════════
# OU COOLIFY POSE LA PILE.
#
# Il ecrit le compose et le fichier d'environnement sous
# /data/coolify/applications/<uuid>/. L'uuid est celui de la ressource, visible
# dans l'URL de son interface. On l'exige plutot que de le deviner : deux
# ressources dans ce dossier, et un choix silencieux migrerait la mauvaise pile.
# ═════════════════════════════════════════════════════════════════════════════
BASE_COOLIFY="/data/coolify/applications"

# ═════════════════════════════════════════════════════════════════════════════
# COOLIFY TOURNE EN root, NOUS NON.
#
# CE QUI ETAIT CASSE : ce script listait /data/coolify/applications en tant que
# `ubuntu` et n'obtenait RIEN — pas une erreur, une liste vide. Il en concluait
# « 0 candidate(s) » et demandait un uuid, alors que le probleme n'etait pas
# l'ambiguite mais le droit de lecture.
#
# Le compte du deploiement est dans le groupe `docker`, ce qui suffit pour
# parler au demon, mais pas pour lire les fichiers que Coolify ecrit. Et le
# compose est lu par le CLIENT compose, donc par nous.
#
# On teste, et l'on ANNONCE ce qu'on emploie. Une elevation silencieuse est
# pire qu'un refus : on ne saurait pas, plus tard, que ce script s'execute en
# root sur la machine de production.
# ═════════════════════════════════════════════════════════════════════════════
# `ls -d` ET NON `test -r` : `test` EST UNE PRIMITIVE DU SHELL.
#
# CE QUI ETAIT CASSE : la sonde employait `sudo -n test -r <dossier>`. `sudo`
# cherche un EXECUTABLE ; selon le systeme, `test` n'en est pas un, et la sonde
# echouait alors que `sudo -n true` repondait « ok ». Le script accusait les
# droits la ou le probleme etait sa propre commande — et son message envoyait
# corriger un sudoers qui fonctionnait deja.
#
# `ls -d` est un binaire partout, et distingue « absent » de « interdit ».
SUDO="${HBA_SUDO-}"
if [ -z "${HBA_SUDO+defini}" ]; then
  if ssh "$DESTINATION" "ls -d ${BASE_COOLIFY}" >/dev/null 2>&1; then
    SUDO=""
    info "lecture directe de ${BASE_COOLIFY}"
  elif ssh "$DESTINATION" "sudo -n ls -d ${BASE_COOLIFY}" >/dev/null 2>&1; then
    SUDO="sudo"
    info "élévation : les commandes distantes passeront par sudo"
  else
    rouge "${BASE_COOLIFY} : ni lisible, ni accessible par sudo."
    rouge ""
    rouge "  Ce que dit la machine, mot pour mot :"
    ssh "$DESTINATION" "ls -d ${BASE_COOLIFY} 2>&1; sudo -n ls -d ${BASE_COOLIFY} 2>&1" \
      | sed 's/^/      /' >&2 || true
    rouge ""
    rouge "  Si le dossier est ABSENT, Coolify a nettoyé la pile — c'est la même"
    rouge "  cause que la disparition des conteneurs, et il faut redéployer avant"
    rouge "  de migrer."
    exit 1
  fi
fi

UUID="${HBA_COOLIFY_UUID:-}"
if [ -z "$UUID" ]; then
  CANDIDATS="$(ssh "$DESTINATION" "${SUDO} ls -1 ${BASE_COOLIFY} 2>/dev/null" || true)"
  NOMBRE="$(printf '%s\n' "$CANDIDATS" | grep -c . || true)"
  if [ "$NOMBRE" = "1" ]; then
    UUID="$(printf '%s\n' "$CANDIDATS" | tr -d '[:space:]')"
    info "ressource déduite : ${UUID}"
  else
    rouge "Impossible de déduire la ressource Coolify (${NOMBRE} candidate(s))."
    printf '%s\n' "$CANDIDATS" | sed 's/^/    /' >&2
    rouge "  Préciser : HBA_COOLIFY_UUID=<uuid> ./scripts/migrer-prod.sh ${CIBLE}"
    exit 1
  fi
fi

DOSSIER="${BASE_COOLIFY}/${UUID}"
if ! ssh "$DESTINATION" "${SUDO} test -f ${DOSSIER}/docker-compose.prod.yml && ${SUDO} test -f ${DOSSIER}/.env"; then
  rouge "Le compose ou le fichier d'environnement manque dans ${DOSSIER}."
  rouge "  Un déploiement Coolify doit avoir réussi au moins une fois."
  exit 1
fi

# ── La liste, derivee du depot et jamais recopiee ────────────────────────────
PYTHON_LOCAL="$ROOT_DIR/.venv/bin/python3"
[ -x "$PYTHON_LOCAL" ] || PYTHON_LOCAL="python3"
if [ -n "$SEULEMENT" ]; then
  SERVICES="$SEULEMENT"
else
  SERVICES="$("$PYTHON_LOCAL" ./scripts/services-a-migrer.py)"
  # REPRENDRE OU L'ON S'EST ARRETE, SANS RECOPIER LA LISTE.
  #
  # Le script s'arrete a la premiere faute. Relancer depuis le debut refait
  # passer les migrations deja appliquees — EF les ignore, mais chaque service
  # coute un demarrage de conteneur, et l'on relit quinze succes pour trouver
  # l'echec. `--depuis <service>` reprend a partir de celui-la, dans le meme
  # ordre.
  if [ -n "$DEPUIS" ]; then
    if ! printf '%s\n' "$SERVICES" | grep -qx "$DEPUIS"; then
      rouge "« ${DEPUIS} » n'est pas dans la liste des services à migrer."
      printf '%s\n' "$SERVICES" | sed 's/^/    /' >&2
      exit 2
    fi
    SERVICES="$(printf '%s\n' "$SERVICES" | sed -n "/^${DEPUIS}\$/,\$p")"
  fi
fi
NOMBRE_SERVICES="$(printf '%s\n' "$SERVICES" | grep -c . || true)"

# ═════════════════════════════════════════════════════════════════════════════
# LE COMPOSE DU VPS EST CELUI DU DERNIER DEPLOIEMENT, PAS CELUI DU DEPOT.
#
# CE QUI ETAIT CASSE : ce script derive la liste des services du compose LOCAL,
# et lance `compose run` contre le compose que COOLIFY a ecrit. Les deux
# divergent des qu'on modifie le generateur sans redeployer. Compose repond
# alors « no such service: payment-service » — exact, et parfaitement muet sur
# la cause : le service existe, il n'a simplement pas encore ete deploye.
#
# L'ordre est donc : pousser, laisser Coolify deployer, PUIS migrer. On le
# verifie plutot que de le documenter.
# ═════════════════════════════════════════════════════════════════════════════
SERVICES_DISTANTS="$(ssh "$DESTINATION" \
  "${SUDO} grep -E '^  [a-z0-9-]+:' ${DOSSIER}/docker-compose.prod.yml" 2>/dev/null \
  | sed 's/^ *//; s/:.*//' || true)"

ABSENTS=""
for service in $SERVICES; do
  if ! printf '%s\n' "$SERVICES_DISTANTS" | grep -qx "$service"; then
    ABSENTS="${ABSENTS}${service} "
  fi
done

if [ -n "$ABSENTS" ]; then
  rouge "REFUS : ces services ne sont pas dans le compose déployé sur ${HOTE} :"
  for service in $ABSENTS; do rouge "    ${service}"; done
  rouge ""
  rouge "  Le compose du VPS est celui du DERNIER déploiement Coolify. Il ne"
  rouge "  connaît pas encore les changements du dépôt."
  rouge ""
  rouge "  Dans l'ordre : git push origin main, attendre que le déploiement"
  rouge "  finisse dans Coolify, puis relancer cette commande."
  exit 1
fi

titre "Migrations ${CIBLE} — ${NOMBRE_SERVICES} service(s)"
info "hôte      : ${DESTINATION} (${HOTE})"
info "ressource : ${UUID}"
printf '%s\n' "$SERVICES" | sed 's/^/      /'

if [ "$CIBLE" = "prod" ] && [ "$SANS_DEMANDER" = "0" ]; then
  printf '    Taper « oui » pour appliquer : '
  read -r reponse
  [ "$reponse" = "oui" ] || { rouge "annulé."; exit 1; }
fi

# ═════════════════════════════════════════════════════════════════════════════
# UN SERVICE A LA FOIS, ET ON S'ARRETE A LA PREMIERE FAUTE.
#
# Plusieurs services partagent une base — food-cart, food-order et restaurant
# sur hba_food. Les lancer en parallele ferait migrer la meme base depuis trois
# processus : le verrou consultatif d'EF evite la corruption, pas l'attente ni
# le desordre dans les journaux.
#
# S'arreter au premier echec est deliberé : une migration qui echoue laisse
# souvent la base a mi-chemin, et enchainer les quinze suivantes rendrait le
# diagnostic illisible.
# ═════════════════════════════════════════════════════════════════════════════
# ═════════════════════════════════════════════════════════════════════════════
# LE RESEAU DE COOLIFY, DECLARE `external`.
#
# CE QUI ETAIT CASSE : `compose run` echouait sur
#
#     network <uuid> declared as external, but could not be found
#
# Coolify reecrit le compose pour y ajouter un reseau portant l'uuid de la
# ressource, marque `external: true` — « il existe deja, je ne le cree pas ».
# C'est LUI qui le cree, au deploiement. Quand la pile a ete nettoyee, le reseau
# est parti avec, et plus rien ne peut le recreer avant le prochain deploiement.
#
# On le cree donc si besoin, et ON LE DIT. Un reseau Docker vide ne coute rien
# et l'operation est idempotente.
#
# CE QUE CELA NE COUVRE PAS : le reseau fabrique ici n'a pas les etiquettes que
# Coolify pose sur les siens. Il fonctionne pour connecter des conteneurs, mais
# Coolify pourrait ne pas le reconnaitre comme lui appartenant — et donc ne pas
# le nettoyer un jour. C'est un objet local, sans donnee : le pire cas est un
# reseau orphelin dans `docker network ls`.
# ═════════════════════════════════════════════════════════════════════════════
if ! ssh "$DESTINATION" "${SUDO} docker network inspect ${UUID}" >/dev/null 2>&1; then
  info "réseau « ${UUID} » absent — création (normalement le rôle de Coolify)"
  ssh "$DESTINATION" "${SUDO} docker network create ${UUID}" >/dev/null || {
    rouge "création du réseau impossible."
    exit 1
  }
fi

COMPOSE_DISTANT="${SUDO} docker compose --env-file ${DOSSIER}/.env -p ${UUID} -f ${DOSSIER}/docker-compose.prod.yml"

for service in $SERVICES; do
  titre "migration : ${service}"
  # `--no-deps` : ON NE DEMARRE QUE CE SERVICE.
  #
  # CE QUI ETAIT CASSE : sans lui, `compose run` respecte `depends_on` et monte
  # toute la cascade — jusqu'a tenter de TIRER des images absentes du VPS :
  #
  #     failed to resolve reference "ghcr.io/hectorberi01/media-service:prod": not found
  #
  # Le compose porte `build:` ET `image:` ; `run` ne construit pas, il tire. Une
  # image qu'aucun deploiement n'a laissee sur la machine fait donc echouer une
  # migration qui n'avait aucun besoin de ce service.
  #
  # Et une migration n'a besoin de RIEN d'autre : `MigrateOnly` sort avant
  # `app.Run()`, donc avant que le moindre consommateur Kafka ou client Redis ne
  # se connecte. Seule la base est requise, et elle est sur un autre VPS.
  if ! ssh -t "$DESTINATION" "${COMPOSE_DISTANT} run --rm --no-deps --no-TTY -e DATABASE__MIGRATEONLY=true ${service}"; then
    rouge "La migration de ${service} a échoué — arrêt."
    rouge "  Les migrations déjà appliquées le RESTENT : reprendre avec"
      rouge "      ./scripts/migrer-prod.sh ${CIBLE} --depuis ${service}"
    exit 1
  fi
  vert "${service} : migré"
done

vert "les ${NOMBRE_SERVICES} migrations sont appliquées"
info "vérifier : ssh ${DESTINATION} docker run --rm -it postgres:16-alpine \\"
info "             psql -h 10.20.0.2 -U hba_identity -d hba_identity -c '\\dt'"
