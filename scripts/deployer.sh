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
# 3. LA POUSSÉE VERS LE DÉPÔT NU DU VPS.
#
# POURQUOI UN DÉPÔT GIT SUR LE VPS, ET NON UN CONTEXTE DOCKER.
#
# Ce script parlait au démon Docker du VPS par SSH. Coolify a repris ce rôle :
# c'est LUI qui construit et démarre, et il le fait depuis un dépôt Git qu'il
# clone. Deux outils qui pilotent le même démon se marcheraient dessus — l'un
# arrêterait ce que l'autre vient de lancer, sans que ni l'un ni l'autre ne le
# signale.
#
# Le dépôt est donc NU et SUR LE VPS, alimenté par `git push`. Aucune source ne
# transite par GitHub, et Coolify clone depuis la machine où il tourne.
#
# CE QUE CE SCRIPT NE FAIT PLUS, ET QUI EST DÉSORMAIS À COOLIFY :
#   - construire les images ;
#   - démarrer, arrêter, redémarrer les conteneurs ;
#   - le proxy TLS et le certificat de api.hba-express.com ;
#   - les vingt-trois variables, qui vivent dans son interface.
# ═════════════════════════════════════════════════════════════════════════════
titre "Destination SSH"

# ON RÉSOUT L'ALIAS AVANT DE S'EN SERVIR, ET ON VÉRIFIE OÙ IL MÈNE.
#
# `ssh -G` rend la configuration EFFECTIVE sans ouvrir de connexion. Le contrôle
# qui compte est la comparaison d'adresse : un `Host` réécrit un jour vers une
# autre machine pousserait la production ailleurs, et rien ne le dirait.
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
  rouge "  Ajouter l'alias à ~/.ssh/config, ou en désigner un autre :"
  rouge "      HBA_SSH_$(printf '%s' "$CIBLE" | tr '[:lower:]' '[:upper:]')=<alias> ./scripts/deployer.sh ${CIBLE}"
  exit 1
fi

# ── Les options que Coolify a reprises ───────────────────────────────────────
#
# ON REFUSE PLUTÔT QUE D'IGNORER. Une option acceptée qui ne fait rien est pire
# qu'une option refusée : on croit avoir migré, et la base n'a pas bougé.
if [ "$MIGRER" = "1" ]; then
  rouge "REFUS : --migrer n'est pas câblé sur le chemin Coolify."
  rouge "  Coolify démarre les services ; il n'applique pas les migrations."
  rouge "  À lancer à la main, un service à la fois, une fois la pile debout :"
  rouge "      ssh ${DESTINATION} docker exec -e DATABASE__MIGRATEONLY=true \\"
  rouge "          hba-identity-service dotnet HBA.Identity.Api.dll"
  rouge "  Voir docs/RUNBOOK-COMPOSE.md — la liste des seize services porteurs."
  exit 1
fi

if [ "$SUJETS" = "1" ]; then
  rouge "REFUS : --sujets n'est pas câblé sur le chemin Coolify."
  rouge '  Le script des sujets Kafka passe par docker compose, que Coolify'
  rouge "  pilote désormais lui-même. À lancer à la main :"
  rouge "      ssh ${DESTINATION} docker exec hba-kafka kafka-topics --help"
  exit 1
fi

if [ "$SANS_BUILD" = "1" ]; then
  info "--sans-build sans effet ici : c'est Coolify qui décide de reconstruire."
fi

# ── Le dépôt nu, créé au premier passage ─────────────────────────────────────
DEPOT_DISTANT="${HBA_DEPOT_DISTANT:-depots/hba.git}"
REMOTE="vps-${CIBLE}"
BRANCHE_DISTANTE="${HBA_BRANCHE_DISTANTE:-main}"

titre "Dépôt nu sur ${HOTE}"
if ! ssh "$DESTINATION" "test -d ${DEPOT_DISTANT}" 2>/dev/null; then
  info "absent — création de ~/${DEPOT_DISTANT}"
  ssh "$DESTINATION" "mkdir -p ${DEPOT_DISTANT} && git init --bare --initial-branch=${BRANCHE_DISTANTE} ${DEPOT_DISTANT}" \
    || { rouge "création impossible — git est-il installé sur le VPS ?"; exit 1; }
else
  info "présent : ~/${DEPOT_DISTANT}"
fi

# Le remote emploie l'ALIAS, seul nom qui porte le port et la clé.
URL_DISTANTE="${DESTINATION}:${DEPOT_DISTANT}"
if git remote get-url "$REMOTE" >/dev/null 2>&1; then
  ACTUELLE="$(git remote get-url "$REMOTE")"
  if [ "$ACTUELLE" != "$URL_DISTANTE" ]; then
    info "remote « ${REMOTE} » réaligné : ${ACTUELLE} → ${URL_DISTANTE}"
    git remote set-url "$REMOTE" "$URL_DISTANTE"
  fi
else
  git remote add "$REMOTE" "$URL_DISTANTE"
  info "remote « ${REMOTE} » créé"
fi

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
# 5. LA POUSSÉE, PUIS LE DÉCLENCHEMENT.
#
# PAS DE `--force`. Un refus de non-fast-forward veut dire que le VPS porte un
# commit que le poste n'a pas — donc que quelqu'un a poussé autre chose, ou
# qu'on tente de redéployer un commit ANTÉRIEUR. Écraser en silence ferait
# disparaître ce qui tourne, sans trace de ce qui a été remplacé.
# ═════════════════════════════════════════════════════════════════════════════
titre "Poussée du commit ${SHA}"
if ! git push "$REMOTE" "HEAD:refs/heads/${BRANCHE_DISTANTE}"; then
  rouge "La poussée a été refusée."
  rouge "  Si c'est un refus de non-fast-forward, le VPS porte un commit absent"
  rouge "  d'ici. Regarder ce qui y est avant de forcer quoi que ce soit :"
  rouge "      git fetch ${REMOTE} && git log --oneline ${REMOTE}/${BRANCHE_DISTANTE} -5"
  exit 1
fi
vert "poussé sur ~/${DEPOT_DISTANT} (${BRANCHE_DISTANTE})"

# ── Le déclenchement ─────────────────────────────────────────────────────────
#
# ON EMPLOIE L'URL DE WEBHOOK ENTIÈRE, PAS UN CHEMIN D'API RECONSTRUIT.
#
# Coolify affiche, pour chaque ressource, une URL de déploiement complète. La
# reconstruire à partir d'un identifiant supposerait une forme d'API qui change
# d'une version à l'autre — et une URL fausse rend 404, ce qui ressemble à un
# déploiement qui n'a rien à faire.
if [ -n "${HBA_COOLIFY_WEBHOOK:-}" ]; then
  titre "Déclenchement Coolify"
  if [ -z "${HBA_COOLIFY_TOKEN:-}" ]; then
    rouge "HBA_COOLIFY_WEBHOOK est posé mais HBA_COOLIFY_TOKEN manque."
    rouge "  Le webhook exige un jeton d'API : Keys & Tokens → API tokens."
    exit 1
  fi
  CODE="$(curl -sS -o /tmp/hba-coolify.out -w '%{http_code}' \
            -H "Authorization: Bearer ${HBA_COOLIFY_TOKEN}" \
            "${HBA_COOLIFY_WEBHOOK}")" || CODE="000"
  if [ "$CODE" != "200" ] && [ "$CODE" != "201" ]; then
    rouge "Coolify a répondu ${CODE}. Réponse :"
    sed 's/^/    /' /tmp/hba-coolify.out >&2 || true
    rouge "  Le commit EST poussé : le déploiement peut se lancer depuis l'interface."
    exit 1
  fi
  vert "déploiement demandé à Coolify"
  info "suivre : l'onglet Deployments de la ressource"
else
  titre "À faire dans Coolify"
  info "Aucun HBA_COOLIFY_WEBHOOK posé — le commit est poussé, rien n'est déclenché."
  info "Déployer depuis l'interface, ou poser une fois pour toutes :"
  info "    export HBA_COOLIFY_WEBHOOK='<URL de déploiement de la ressource>'"
  info "    export HBA_COOLIFY_TOKEN='<jeton d API>'"
fi

titre "Rappel"
info "Les variables obligatoires se règlent dans Coolify, pas ici :"
PYTHON_LOCAL="$ROOT_DIR/.venv/bin/python3"
[ -x "$PYTHON_LOCAL" ] || PYTHON_LOCAL="python3"
"$PYTHON_LOCAL" ./scripts/verifier-env-compose.py "$COMPOSE" || true

vert "commit ${SHA} disponible pour ${CIBLE} (${HOTE})"
