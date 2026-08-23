#!/usr/bin/env bash
#
# Construit l'App Bundle (.aab) Android de l'application cliente HbaExpress.
#
#   ./scripts/build-appbundle.sh staging
#   ./scripts/build-appbundle.sh prod
#   ./scripts/build-appbundle.sh staging --build-number 12
#   ./scripts/build-appbundle.sh prod --yes            # sans confirmation (CI)
#
# Le script CONSTRUIT seulement : le téléversement sur Play Console reste manuel,
# et donc délibéré. Voir scripts/README.md pour la marche à suivre ensuite.

set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/_common.sh"

parse_args "$(basename "$0")" "$@"
cd "$ROOT_DIR"

# ─────────────────────────────────────────────────────────────────────────────
# GARDE-FOU DE SIGNATURE — bloquant, et c'est voulu.
#
# Sans `android/key.properties`, `app/build.gradle.kts` retombe sur le keystore de
# DÉBOGAGE : sa clé est publique et son mot de passe est littéralement « android ».
# Play Console refuse un tel binaire. L'ancien script se contentait d'un
# avertissement — noyé dans plusieurs minutes de logs Gradle, il passait inaperçu
# et l'on ne découvrait le problème qu'au téléversement.
#
# On vérifie aussi que le keystore DÉSIGNÉ existe réellement : un chemin périmé
# dans key.properties produit exactement le même échec silencieux.
# ─────────────────────────────────────────────────────────────────────────────
KEY_PROPS="$ROOT_DIR/android/key.properties"
[ -f "$KEY_PROPS" ] || die "android/key.properties introuvable." \
  "L'AAB serait signé avec la clé de DÉBOGAGE et refusé par Play Console." \
  "Attendu : storeFile, storePassword, keyAlias, keyPassword."

KEYSTORE_PATH="$(grep -E '^storeFile=' "$KEY_PROPS" | head -1 | cut -d= -f2- | tr -d '\r')"
[ -n "$KEYSTORE_PATH" ] || die "storeFile absent de android/key.properties."
[ -f "$KEYSTORE_PATH" ] || die "Keystore introuvable : $KEYSTORE_PATH" \
  "Le chemin de key.properties ne mène à aucun fichier." \
  "Sans le keystore d'origine, aucune mise à jour ne pourra plus être publiée."

confirm_or_abort "Android"

info "→ flutter pub get"
flutter pub get

flutter build appbundle \
  --release \
  --flavor "$RELEASE_FLAVOR" \
  --dart-define=API_BASE_URL="$API_URL" \
  --build-number="$BUILD_NUMBER"

PRODUCED="build/app/outputs/bundle/${RELEASE_FLAVOR}Release/app-${RELEASE_FLAVOR}-release.aab"
[ -f "$PRODUCED" ] || die "AAB introuvable : $PRODUCED" "Le build a échoué."

mkdir -p "$DIST_DIR"
VERSION="$(read_version_name)"

# L'environnement fait partie du NOM DU FICHIER, pas seulement du contenu : c'est
# la seule chose qui distingue deux artefacts par ailleurs identiques.
OUT="$DIST_DIR/HbaExpress-android-${ENVIRONMENT}-${VERSION}+${BUILD_NUMBER}.aab"
cp "$PRODUCED" "$OUT"
MANIFEST="$(write_manifest "$OUT" "Android (AAB)" "$ENVIRONMENT" "$API_URL" "$VERSION" "$BUILD_NUMBER")"

echo
ok "AAB      : $OUT"
ok "Manifeste: $MANIFEST"
echo
info "Suite : Play Console → Test → sélectionner la piste → Créer une release"
info "        puis téléverser ce fichier .aab."
