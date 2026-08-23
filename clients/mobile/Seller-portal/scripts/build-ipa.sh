#!/usr/bin/env bash
#
# Génère l'IPA (iOS) de HbaExpress PRO pour STAGING ou PROD.
#
# L'URL de l'API (BFF vendeur) est injectée au build via --dart-define. C'est ce
# qui distingue un binaire staging d'un binaire prod : SANS ce flag, l'app partirait
# sur l'URL par défaut (staging) — un build de release doit donc TOUJOURS le fixer.
#
# Usage :
#   ./scripts/build-ipa.sh staging
#   ./scripts/build-ipa.sh prod
#   ./scripts/build-ipa.sh all                # les deux
#   ./scripts/build-ipa.sh prod 42            # + numéro de build (App Store : unique et croissant)
#   API_BASE_URL_OVERRIDE=https://... ./scripts/build-ipa.sh staging   # surcharge l'URL
#
# Prérequis : Xcode + signature configurée (Runner → Signing & Capabilities →
# Automatically manage signing + Team). Voir RELEASE_IOS.md.

set -euo pipefail

# URLs du BFF vendeur par environnement.
STAGING_URL="https://seller.marketplace-staging.hba-marketplace.fr"
PROD_URL="https://seller.hba-express.org"

# Racine du projet = dossier parent de ce script.
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

  # Surcharge éventuelle.
  api_url="${API_BASE_URL_OVERRIDE:-$api_url}"

  echo ""
  echo "═══════════════════════════════════════════════════════════════"
  echo "  Build IPA — $env_name"
  echo "  API_BASE_URL : $api_url"
  [ -n "$build_number" ] && echo "  build-number : $build_number"
  echo "═══════════════════════════════════════════════════════════════"

  # --flavor : mappé sur le SCHEME Xcode du même nom (staging / prod), chacun avec
  # son bundle ID → deux fiches App Store Connect. Voir MULTI_ENV.md pour créer les
  # schemes/configs (partie Xcode, à faire une fois).
  local args=(build ipa --release --flavor "$env_name" --dart-define=API_BASE_URL="$api_url")
  # STAGING est un build RELEASE qui vise le serveur de TEST : le garde-fou
  # anti-staging de l'app le bloquerait au démarrage sans cet opt-in explicite.
  # On ne l'ajoute JAMAIS pour prod (qui doit rester protégé).
  [ "$env_name" = "staging" ] && args+=(--dart-define=ALLOW_STAGING_RELEASE=true)
  # Export APP-STORE (distribution) : produit aps-environment=production, requis
  # pour que les notifications push fonctionnent sur TestFlight / App Store. Sans
  # ça, l'export sortait en development et l'appareil n'obtenait aucun jeton APNs.
  args+=(--export-options-plist "$ROOT_DIR/ios/ExportOptions.plist")
  [ -n "$build_number" ] && args+=(--build-number="$build_number")

  flutter "${args[@]}"

  # Récupère l'IPA produit et le copie sous un nom explicite par environnement.
  local produced
  produced="$(ls -t build/ios/ipa/*.ipa 2>/dev/null | head -1 || true)"
  if [ -z "$produced" ]; then
    echo "❌ Aucun IPA généré. Causes probables :"
    echo "   • schemes Xcode staging/prod absents (voir MULTI_ENV.md) ;"
    echo "   • signature non configurée (ouvre ios/Runner.xcworkspace → Signing)."
    exit 1
  fi

  local version
  version="$(grep -E '^version:' pubspec.yaml | awk '{print $2}')"
  local out="$DIST_DIR/HbaExpressPro-${env_name}-${version}.ipa"
  cp "$produced" "$out"
  echo "✅ $env_name → $out"
}

ENVIRONMENT="${1:-}"
BUILD_NUMBER="${2:-}"

if [ -z "$ENVIRONMENT" ]; then
  echo "Usage: $0 <staging|prod|all> [build_number]"
  exit 1
fi

echo "→ flutter pub get + pod install"
flutter pub get
( cd ios && pod install )

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
echo "🎉 Terminé. IPA dans : $DIST_DIR"
ls -1 "$DIST_DIR"/*.ipa 2>/dev/null || true
