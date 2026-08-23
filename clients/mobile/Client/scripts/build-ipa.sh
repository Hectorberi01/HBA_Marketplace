#!/usr/bin/env bash
#
# Construit l'archive iOS (.ipa) de l'application cliente HbaExpress.
#
#   ./scripts/build-ipa.sh staging
#   ./scripts/build-ipa.sh prod
#   ./scripts/build-ipa.sh staging --build-number 12
#   ./scripts/build-ipa.sh prod --yes                 # sans confirmation (CI)
#
# Le script CONSTRUIT seulement : l'envoi vers App Store Connect se fait ensuite
# avec Transporter ou Xcode. Voir scripts/README.md.

set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/_common.sh"

parse_args "$(basename "$0")" "$@"
cd "$ROOT_DIR"

[ "$(uname -s)" = "Darwin" ] || die "Un build iOS exige macOS." "Système détecté : $(uname -s)."
command -v xcodebuild >/dev/null 2>&1 || die "Xcode introuvable (xcodebuild absent du PATH)."
command -v pod >/dev/null 2>&1 || die "CocoaPods introuvable." "Installation : sudo gem install cocoapods"

confirm_or_abort "iOS"

info "→ flutter pub get"
flutter pub get

# ─────────────────────────────────────────────────────────────────────────────
# `pod install` APRÈS `flutter pub get`, et jamais l'inverse.
#
# CocoaPods lit `ios/Flutter/Generated.xcconfig`, que `flutter pub get` produit.
# Inverser l'ordre — ou lancer `pod install` après un `flutter clean` — échoue sur
# « Generated.xcconfig must exist ». L'erreur ne dit rien de la cause réelle.
# ─────────────────────────────────────────────────────────────────────────────
info "→ pod install"
( cd ios && pod install )

# ─────────────────────────────────────────────────────────────────────────────
# `--export-options-plist` : signature de DISTRIBUTION imposée.
#
# Sans lui, l'export pouvait sortir signé en développement — l'application
# s'installe et fonctionne, mais n'obtient aucun jeton APNs : plus une seule
# notification, sans le moindre message d'erreur. La vérification en fin de
# script détectait déjà le cas, mais après plusieurs minutes de compilation.
# ─────────────────────────────────────────────────────────────────────────────
BUILD_ARGS=(build ipa --release --flavor "$RELEASE_FLAVOR"
  --dart-define=API_BASE_URL="$API_URL"
  --export-options-plist "$ROOT_DIR/ios/ExportOptions.plist"
  --build-number="$BUILD_NUMBER")
# Vide pour prod : le garde-fou anti-recette y reste actif.
[ "$ENVIRONMENT" = "staging" ] && BUILD_ARGS+=(--dart-define=ALLOW_STAGING_RELEASE=true)

flutter "${BUILD_ARGS[@]}"

PRODUCED="$(ls -t build/ios/ipa/*.ipa 2>/dev/null | head -1 || true)"
[ -n "$PRODUCED" ] || die "Aucun IPA généré." \
  "Causes fréquentes :" \
  "  • signature non configurée → ouvrez ios/Runner.xcworkspace, onglet Signing & Capabilities ;" \
  "  • scheme « $RELEASE_FLAVOR » absent du projet Xcode ;" \
  "  • capacité « Associated Domains » ou « Push Notifications » non activée sur l'App ID."

mkdir -p "$DIST_DIR"
VERSION="$(read_version_name)"

OUT="$DIST_DIR/HbaExpress-ios-${ENVIRONMENT}-${VERSION}+${BUILD_NUMBER}.ipa"
cp "$PRODUCED" "$OUT"
MANIFEST="$(write_manifest "$OUT" "iOS (IPA)" "$ENVIRONMENT" "$API_URL" "$VERSION" "$BUILD_NUMBER")"

echo
ok "IPA      : $OUT"
ok "Manifeste: $MANIFEST"
echo

# ─────────────────────────────────────────────────────────────────────────────
# Rappel APNs. `Runner.entitlements` porte « aps-environment = development » pour
# les builds locaux ; Xcode y substitue « production » lors d'un archivage signé
# avec un profil de distribution. La substitution dépend du profil réellement
# utilisé — elle n'est donc pas garantie, et son échec est SILENCIEUX : l'app
# s'installe, fonctionne, et ne reçoit simplement jamais aucune notification.
# ─────────────────────────────────────────────────────────────────────────────
info "Vérification APNs de l'archive :"
if unzip -p "$OUT" 'Payload/*.app/embedded.mobileprovision' 2>/dev/null \
     | strings | grep -q '<string>production</string>'; then
  ok "  profil de distribution (aps-environment = production)"
else
  warn "  aps-environment ne semble PAS être « production »."
  warn "  Les notifications push resteront muettes sur ce binaire."
fi

echo
info "Suite : Transporter (ou Xcode → Organizer) → téléverser cet .ipa,"
info "        puis App Store Connect → TestFlight."
info "Sur Apple, le build soumis à l'examen est CHOISI parmi ceux de TestFlight :"
info "vérifiez le manifeste avant de sélectionner, les binaires sont indiscernables."
