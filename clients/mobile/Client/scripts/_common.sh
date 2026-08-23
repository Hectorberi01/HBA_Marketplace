#!/usr/bin/env bash
#
# Fonctions communes aux scripts de livraison (Android et iOS).
# Ce fichier est SOURCÉ, jamais exécuté directement.
#
# ─────────────────────────────────────────────────────────────────────────────
# MODÈLE DE LIVRAISON : UNE SEULE FICHE PAR PLATEFORME.
#
# Un seul identifiant applicatif est publié — `com.hbaexpress.client` côté Google,
# `fr.hbamarket.app` côté Apple. Staging et production ne se distinguent QUE par
# l'URL du backend, injectée au build par `--dart-define=API_BASE_URL`.
#
# Le flavor `staging` (identifiants suffixés `.staging`) existe toujours dans le
# projet, mais il ne sert plus qu'au DÉVELOPPEMENT LOCAL, pour installer les deux
# environnements côte à côte sur un même appareil. Il n'est plus livré aux stores.
#
# Conséquence qui commande tout le reste de ce fichier : deux binaires destinés à
# des environnements différents sont désormais INDISTINGUABLES à l'œil nu. D'où le
# nommage explicite des artefacts et le manifeste écrit à côté de chacun.
# ─────────────────────────────────────────────────────────────────────────────

# URLs du BFF mobile (hôte « m. » ; les routes sont sous /mobile).
readonly STAGING_URL="https://m.marketplace-staging.hba-marketplace.fr"
readonly PROD_URL="https://m.hba-express.org"

# Flavor livré aux stores. Voir l'encadré ci-dessus.
readonly RELEASE_FLAVOR="prod"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT_DIR
readonly DIST_DIR="$ROOT_DIR/dist"

# ── Sorties ──────────────────────────────────────────────────────────────────

die() {
  printf '\n\033[1;31m✖ %s\033[0m\n' "$1" >&2
  shift
  for line in "$@"; do printf '  %s\n' "$line" >&2; done
  exit 1
}

warn() { printf '\033[1;33m%s\033[0m\n' "$1" >&2; }
info() { printf '  %s\n' "$1"; }
ok()   { printf '\033[1;32m✔ %s\033[0m\n' "$1"; }

banner() {
  printf '\n\033[1m'
  printf '═%.0s' {1..66}; printf '\n  %s\n' "$1"
  printf '═%.0s' {1..66}; printf '\033[0m\n'
}

# ── Opt-in « release visant la recette » ─────────────────────────────────────
#
# L'application REFUSE de démarrer si un build de release vise un serveur de test,
# à moins de cet accord explicite. Le binaire de recette portant l'identifiant de
# production, c'est le seul verrou qui empêche qu'il soit soumis en production par
# inadvertance : sans le flag, il ne se lance même pas.
#
# On ne l'ajoute JAMAIS pour prod. Ce serait désarmer le garde-fou pour tout le
# monde, y compris pour un build de production dont l'URL aurait été mal saisie.

staging_dart_defines() {
  [ "$ENVIRONMENT" = "staging" ] && printf '%s' '--dart-define=ALLOW_STAGING_RELEASE=true'
}

# ── Environnement → URL ──────────────────────────────────────────────────────

resolve_api_url() {
  case "$1" in
    staging) printf '%s' "$STAGING_URL" ;;
    prod)    printf '%s' "$PROD_URL" ;;
    *) die "Environnement inconnu : $1" "Attendu : staging ou prod." ;;
  esac
}

# ── Version et numéro de build ───────────────────────────────────────────────
#
# `pubspec.yaml` porte « version: <nom>+<build> ». Le nombre après « + » devient
# le versionCode Android ET le build number iOS.
#
# Les deux stores REFUSENT un téléversement dont le numéro a déjà servi, et
# l'erreur n'apparaît qu'APRÈS un build complet. C'est pour cela que ce script
# l'incrémente lui-même : c'est l'oubli le plus coûteux de la chaîne.

pubspec_version_line() {
  grep -E '^version: ' "$ROOT_DIR/pubspec.yaml" | head -1 \
    || die "Aucune ligne « version: » dans pubspec.yaml."
}

# Nom de version seul (ex. « 1.1.9 »).
read_version_name() {
  local full; full="$(pubspec_version_line)"; full="${full#version: }"
  printf '%s' "${full%%+*}" | tr -d '[:space:]'
}

# Numéro de build courant (1 si « + » absent, ce que fait Flutter par défaut).
read_build_number() {
  local full; full="$(pubspec_version_line)"; full="${full#version: }"
  full="$(printf '%s' "$full" | tr -d '[:space:]')"
  if [[ "$full" == *+* ]]; then printf '%s' "${full#*+}"; else printf '1'; fi
}

# Écrit « version: <nom>+<build> » dans pubspec.yaml.
#
# On passe par un fichier temporaire plutôt que `sed -i` : la forme de cette
# option diffère entre BSD (macOS) et GNU, et un script de livraison n'a pas le
# droit de dépendre de laquelle des deux est installée.
write_build_number() {
  local new_build="$1"
  local name; name="$(read_version_name)"
  local tmp; tmp="$(mktemp)"
  sed -E "s|^version: .*|version: ${name}+${new_build}|" "$ROOT_DIR/pubspec.yaml" > "$tmp"
  mv "$tmp" "$ROOT_DIR/pubspec.yaml"
}

# ── Garde-fou : dépôt propre ─────────────────────────────────────────────────
#
# Un binaire qu'on ne peut pas rattacher à un commit précis est intraçable : en
# cas de régression signalée par un testeur, impossible de savoir ce qu'il exécute.
# On avertit sans bloquer — livrer depuis un dépôt modifié reste parfois légitime.

git_state() {
  if ! git -C "$ROOT_DIR" rev-parse --git-dir >/dev/null 2>&1; then
    printf 'hors dépôt git'; return
  fi
  local sha; sha="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || echo '?')"
  if [ -n "$(git -C "$ROOT_DIR" status --porcelain 2>/dev/null)" ]; then
    printf '%s (ARBRE DE TRAVAIL MODIFIÉ)' "$sha"
  else
    printf '%s (propre)' "$sha"
  fi
}

# ── Manifeste ────────────────────────────────────────────────────────────────
#
# Écrit à côté de chaque artefact. Il répond à LA question qu'on ne peut plus
# lire sur le binaire depuis qu'un seul identifiant sert aux deux environnements :
# « à quel serveur celui-ci parle-t-il ? »
#
# Le cas iOS le rend indispensable. Sur Google Play, la piste de test et la
# production reçoivent deux AAB distincts. Sur Apple, on SÉLECTIONNE le build à
# soumettre parmi ceux déjà présents sur TestFlight — deux binaires visuellement
# identiques, que seul leur numéro distingue.

write_manifest() {
  local artefact="$1" platform="$2" env_name="$3" api_url="$4" version="$5" build="$6"
  local manifest="${artefact%.*}.txt"
  cat > "$manifest" <<EOF
Artefact      : $(basename "$artefact")
Plateforme    : $platform
Environnement : $env_name
API_BASE_URL  : $api_url
Version       : $version
Numéro build  : $build
Flavor        : $RELEASE_FLAVOR
Commit        : $(git_state)
Construit le  : $(date '+%Y-%m-%d %H:%M:%S %z')
Machine       : $(hostname)
EOF
  printf '%s' "$manifest"
}

# ── Analyse des arguments ────────────────────────────────────────────────────
#
# Renseigne : ENVIRONMENT, API_URL, BUILD_NUMBER, ASSUME_YES.

ENVIRONMENT=""
API_URL=""
BUILD_NUMBER=""
ASSUME_YES="no"
BUMP="yes"

usage() {
  cat <<EOF
Usage : $1 <staging|prod> [options]

  staging   binaire pointant sur le backend de recette
  prod      binaire pointant sur le backend de production

Options :
  --build-number N   impose ce numéro (sinon : incrément automatique)
  --no-bump          réutilise le numéro courant de pubspec.yaml, sans l'incrémenter
  --url URL          remplace l'URL de l'API (stack locale, domaine de test…)
  --yes              ne demande aucune confirmation (intégration continue)
  -h, --help         affiche cette aide
EOF
}

parse_args() {
  local script_name="$1"; shift
  [ $# -eq 0 ] && { usage "$script_name"; exit 1; }

  ENVIRONMENT="$1"; shift
  case "$ENVIRONMENT" in
    -h|--help) usage "$script_name"; exit 0 ;;
    staging|prod) ;;
    *) die "Environnement invalide : $ENVIRONMENT" "Attendu : staging ou prod." ;;
  esac

  while [ $# -gt 0 ]; do
    case "$1" in
      --build-number) BUILD_NUMBER="${2:-}"; shift 2
        [[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]] || die "--build-number attend un entier." ;;
      --no-bump) BUMP="no"; shift ;;
      --url) API_URL="${2:-}"; shift 2 ;;
      --yes|-y) ASSUME_YES="yes"; shift ;;
      -h|--help) usage "$script_name"; exit 0 ;;
      *) die "Option inconnue : $1" "Lancez « $script_name --help »." ;;
    esac
  done

  [ -z "$API_URL" ] && API_URL="$(resolve_api_url "$ENVIRONMENT")"

  # ───────────────────────────────────────────────────────────────────────────
  # ON CALCULE LE NUMÉRO ICI, ON N'ÉCRIT PAS ENCORE.
  #
  # `pubspec.yaml` était modifié DÈS l'analyse des arguments, donc avant la
  # confirmation. Répondre « non » à l'invite laissait le fichier incrémenté :
  # trois hésitations d'affilée faisaient sauter le numéro de build de trois, et
  # le dépôt se retrouvait modifié sans qu'aucun binaire n'ait été produit.
  #
  # L'écriture est donc reportée à `commit_build_number`, appelée une fois la
  # livraison confirmée.
  # ───────────────────────────────────────────────────────────────────────────
  if [ -n "$BUILD_NUMBER" ]; then
    : # imposé par l'opérateur, rien à calculer
  elif [ "$BUMP" = "yes" ]; then
    BUILD_NUMBER="$(( $(read_build_number) + 1 ))"
  else
    BUILD_NUMBER="$(read_build_number)"
  fi
}

# Grave le numéro retenu dans pubspec.yaml. Appelée APRÈS confirmation.
#
# Écrit sans condition : avec `--no-bump`, la valeur est celle déjà présente, et
# la réécrire ne change rien. Une branche « ne pas écrire dans ce cas » n'aurait
# fait qu'ajouter un chemin de plus à se tromper.
commit_build_number() {
  write_build_number "$BUILD_NUMBER"
}

# ── Récapitulatif et confirmation ────────────────────────────────────────────

confirm_or_abort() {
  local platform="$1"
  banner "Livraison $platform — $ENVIRONMENT"
  info "API_BASE_URL  : $API_URL"
  info "Version       : $(read_version_name)+$BUILD_NUMBER"
  info "Flavor        : $RELEASE_FLAVOR  (identifiant de PRODUCTION)"
  info "Commit        : $(git_state)"
  echo

  # Le cas à signaler : un binaire portant l'identifiant de production, mais
  # branché sur la recette. Parfaitement légitime pour TestFlight ou une piste de
  # test fermé — désastreux s'il part en production. Rien ne le distingue à l'œil.
  if [ "$ENVIRONMENT" = "staging" ]; then
    warn "Ce binaire porte l'identifiant de PRODUCTION mais parle au backend de RECETTE."
    warn "À réserver aux pistes de test. Ne le soumettez jamais en production."
    echo
  fi

  if [ "$ASSUME_YES" != "yes" ]; then
    printf 'Continuer ? [o/N] '
    read -r answer
    case "$answer" in o|O|y|Y|oui|yes) ;; *) die "Interrompu." ;; esac
  fi

  # Le numéro n'est gravé qu'ICI : une livraison interrompue à l'invite laisse
  # pubspec.yaml intact.
  commit_build_number
}
