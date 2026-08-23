#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# ENGENDRE UNE PAIRE DE CLÉS PAR HÔTE POUR L'IDENTITÉ D'APPELANT gRPC.
#
# À EXÉCUTER UNE FOIS, PUIS À CHAQUE ROTATION. LA SORTIE CONTIENT DES SECRETS.
#
# Ce que le script écrit :
#   • `<sortie>/<hote>.key`      — PKCS#8, clé PRIVÉE. Ne quitte jamais l'hôte.
#   • `<sortie>/identites.env`   — les variables à coller dans le `.env` du
#                                  compose : une clé privée par hôte, et LE
#                                  registre public, identique pour tous.
#
# LE RÉPERTOIRE DE SORTIE N'EST PAS DANS LE DÉPÔT PAR DÉFAUT, ET NE DOIT PAS
#    Y ENTRER. `.gitignore` couvre `*.key` et `*.env` ; ce script ne compte pas
#    dessus et écrit hors du dépôt.
#
# POURQUOI P-256 ET PAS Ed25519.
#
# Ed25519 serait plus simple et plus rapide. `ECDsa.ImportPkcs8PrivateKey` du
# framework .NET 8 ne le connaît pas : il faudrait une bibliothèque de plus
# (BouncyCastle) sur les dix-neuf hôtes, pour une signature qu'aucun tiers ne
# vérifiera jamais. P-256 est dans le framework, et `openssl` l'engendre sans
# option exotique.
#
# Usage :
#   scripts/generer-identites-internes.sh [repertoire-de-sortie]
#   scripts/generer-identites-internes.sh --vers-env infra/docker/.env
#
# LA SECONDE FORME EXISTE PARCE QUE LA PREMIERE SE RECOPIE MAL.
#
# Quinze lignes de base64 de plusieurs centaines de caracteres, collees a la
# main dans un .env : une seule tronquee et le service concerne repond
# Unauthenticated a tous ses appelants, sans que rien ne designe le fichier.
# `--vers-env` ecrit les variables en place, en remplacant celles qui existent
# deja et en conservant tout le reste du fichier.
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

CIBLE_ENV=""

if [[ "${1:-}" == "--vers-env" ]]; then
  CIBLE_ENV="${2:?--vers-env attend un chemin de fichier .env}"
  shift 2
fi

SORTIE="${1:-$HOME/.hba-identites}"

HOTES=(
  HBA.Catalog.Api
  HBA.Commerce.Api
  HBA.Communication.Api
  HBA.Delivery.Core.Api
  HBA.Delivery.Dispatch.Api
  HBA.Delivery.Driver.Api
  HBA.Delivery.Pricing.Api
  HBA.Delivery.Proof.Api
  HBA.Delivery.Route.Api
  HBA.Delivery.Tracking.Api
  HBA.Engagement.Api
  HBA.Financial.Api
  HBA.Food.Cart.Api
  HBA.Food.Order.Api
  HBA.Food.Restaurant.Api
  HBA.Gateway.Api
  HBA.Identity.Api
  HBA.Inventory.Api
  HBA.Marketplace.ReturnRefund.Api
  HBA.Media.Api
  HBA.Merchants.Api
  HBA.Order.Api
  HBA.Promotions.Api
  HBA.Users.Api
)

command -v openssl >/dev/null || { echo "openssl est requis." >&2; exit 1; }

# 077 AVANT DE CRÉER QUOI QUE CE SOIT.
#
# Posé après coup, il laisserait une fenêtre — courte, mais réelle — pendant
# laquelle les clés privées seraient lisibles par tout compte de la machine.
umask 077
mkdir -p "$SORTIE"

REGISTRE=""

for hote in "${HOTES[@]}"; do
  privee="$SORTIE/$hote.key"

  if [[ ! -f "$privee" ]]; then
    openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
      -outform DER -out "$privee" 2>/dev/null
  fi

  # `openssl` rend du DER binaire ; .NET attend du base64 de ce même DER.
  # `base64 -w0` n'existe pas sur macOS — d'où `tr -d '\n'`, qui marche partout.
  b64_privee="$(base64 < "$privee" | tr -d '\n')"
  b64_publique="$(openssl pkey -inform DER -in "$privee" -pubout -outform DER \
    | base64 | tr -d '\n')"

  # `HBA.Order.Api` → `HBA_ORDER_API`, le nom de la variable d'environnement.
  variable="$(echo "$hote" | tr '.a-z' '_A-Z')"

  echo "INTERNAL_KEY_$variable=$b64_privee" >> "$SORTIE/identites.env.tmp"

  REGISTRE="${REGISTRE:+$REGISTRE;}$hote=$b64_publique"
done

{
  echo "# Engendré par scripts/generer-identites-internes.sh."
  echo "# SECRETS. Ce fichier ne va dans aucun dépôt."
  echo
  cat "$SORTIE/identites.env.tmp"
  echo
  echo "# Registre des clés PUBLIQUES — la MÊME valeur pour les hôtes."
  echo "INTERNAL_PUBLIC_KEYS=$REGISTRE"
} > "$SORTIE/identites.env"

rm -f "$SORTIE/identites.env.tmp"

echo "Écrit : $SORTIE/identites.env"

if [[ -z "$CIBLE_ENV" ]]; then
  echo
  echo "Reste à faire :"
  echo "   1. copier le contenu dans le .env de infra/docker/ ;"
  echo "   2. redémarrer les hôtes ENSEMBLE — voir l'encadré de compose.services.yml."
  exit 0
fi

# ── Écriture en place ────────────────────────────────────────────────────────
#
# ON REECRIT LE FICHIER, ON NE L'AJOUTE PAS EN QUEUE.
#
# Ajouter en queue marche la premiere fois et casse a la rotation : le fichier
# porterait deux INTERNAL_KEY_X, et selon l'outil qui le lit, c'est la premiere
# ou la derniere qui gagne. Ici chaque variable est retiree puis reposee.
touch "$CIBLE_ENV"
TEMPORAIRE="$(mktemp)"

grep -v -E '^(INTERNAL_KEY_[A-Z_]+|INTERNAL_PUBLIC_KEYS)=' "$CIBLE_ENV" > "$TEMPORAIRE" || true

{
  echo
  echo "# Identités d'appelant gRPC — engendrées le $(date -u '+%Y-%m-%d %H:%M UTC')"
  echo "# par scripts/generer-identites-internes.sh. Ne pas éditer à la main."
  grep -E '^INTERNAL_KEY_' "$SORTIE/identites.env"
  grep -E '^INTERNAL_PUBLIC_KEYS=' "$SORTIE/identites.env"
} >> "$TEMPORAIRE"

cat "$TEMPORAIRE" > "$CIBLE_ENV"
rm -f "$TEMPORAIRE"
chmod 600 "$CIBLE_ENV"

echo "Mis à jour : $CIBLE_ENV"
echo
echo "Reste à faire : redémarrer les hôtes ENSEMBLE."
echo "Un déploiement service par service produit une fenêtre d'Unauthenticated"
echo "croisés — voir l'encadré d'identity-service dans compose.services.yml."
