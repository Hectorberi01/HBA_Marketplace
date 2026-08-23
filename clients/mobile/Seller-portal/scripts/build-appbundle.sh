#!/usr/bin/env bash
#
# Génère l'AAB (Android App Bundle) de HbaExpress PRO pour STAGING ou PROD.
#
# L'URL de l'API (BFF vendeur) est injectée au build via --dart-define. SANS ce
# flag, l'app partirait sur l'URL par défaut (staging) — un build de release doit
# donc TOUJOURS le fixer.
#
# Usage :
#   ./scripts/build-appbundle.sh staging
#   ./scripts/build-appbundle.sh prod
#   ./scripts/build-appbundle.sh all                # les deux
#   ./scripts/build-appbundle.sh prod 42            # + numéro de build (Play : unique et croissant)
#   API_BASE_URL_OVERRIDE=https://... ./scripts/build-appbundle.sh staging   # surcharge l'URL
#
# Prérequis : signature de release configurée dans android/key.properties
# (storeFile, storePassword, keyAlias, keyPassword). SANS ce fichier, l'AAB est
# signé avec la clé de DÉBOGAGE et sera REFUSÉ par le Play Store (le build gradle
# le signale bruyamment). Voir CICD_MOBILE.md.

set -euo pipefail

STAGING_URL="https://seller.marketplace-staging.hba-marketplace.fr"
PROD_URL="https://seller.hba-express.org"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

DIST_DIR="$ROOT_DIR/dist"
mkdir -p "$DIST_DIR"

build_one() {
  local env_name="$1"
  local build_number="${2:-}"
  local api_url

  case "$env_name" in
    staging) api_url="$STAGING_URL" ;;
    prod)    api_url="$PROD_URL" ;;
    *) echo "❌ Environnement inconnu : $env_name (attendu: staging|prod)"; exit 1 ;;
  esac

  api_url="${API_BASE_URL_OVERRIDE:-$api_url}"

  # Avertissement si la signature de release n'est pas configurée.
  if [ ! -f "android/key.properties" ]; then
    echo " android/key.properties ABSENT : l'AAB sera signé avec la clé de DÉBOGAGE"
    echo "    → REFUSÉ par le Play Store. Voir CICD_MOBILE.md pour le configurer."
  fi

  echo ""
  echo "═══════════════════════════════════════════════════════════════"
  echo "  Build AAB — $env_name"
  echo "  API_BASE_URL : $api_url"
  [ -n "$build_number" ] && echo "  build-number : $build_number"
  echo "═══════════════════════════════════════════════════════════════"

  # --flavor : staging et prod ont un applicationId distinct (deux fiches Play).
  local args=(build appbundle --release --flavor "$env_name" --dart-define=API_BASE_URL="$api_url")
  # STAGING est un build RELEASE qui vise le serveur de TEST : le garde-fou
  # anti-staging de l'app le bloquerait au démarrage sans cet opt-in explicite.
  # On ne l'ajoute JAMAIS pour prod (qui doit rester protégé).
  [ "$env_name" = "staging" ] && args+=(--dart-define=ALLOW_STAGING_RELEASE=true)
  [ -n "$build_number" ] && args+=(--build-number="$build_number")

  flutter "${args[@]}"

  # Avec un flavor, l'AAB atterrit dans bundle/<flavor>Release/app-<flavor>-release.aab.
  local produced="build/app/outputs/bundle/${env_name}Release/app-${env_name}-release.aab"
  if [ ! -f "$produced" ]; then
    echo "❌ AAB introuvable ($produced) — le build a échoué."
    exit 1
  fi

  local version
  version="$(grep -E '^version:' pubspec.yaml | awk '{print $2}')"
  local out="$DIST_DIR/HbaExpressPro-${env_name}-${version}.aab"
  cp "$produced" "$out"
  echo "✅ $env_name → $out"
}

ENVIRONMENT="${1:-}"
BUILD_NUMBER="${2:-}"

if [ -z "$ENVIRONMENT" ]; then
  echo "Usage: $0 <staging|prod|all> [build_number]"
  exit 1
fi

echo "→ flutter pub get"
flutter pub get

case "$ENVIRONMENT" in
  all)
    build_one staging "$BUILD_NUMBER"
    build_one prod "$BUILD_NUMBER"
    ;;
  staging|prod)
    build_one "$ENVIRONMENT" "$BUILD_NUMBER"
    ;;
  *)
    echo "❌ Argument invalide : $ENVIRONMENT (attendu: staging|prod|all)"
    exit 1
    ;;
esac

echo ""
echo "🎉 Terminé. AAB dans : $DIST_DIR"
ls -1 "$DIST_DIR"/*.aab 2>/dev/null || true
