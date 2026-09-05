#!/usr/bin/env bash
# ==============================================================================
# LES IDENTITES gRPC INTERNES — UNE PAIRE DE CLES PAR SERVICE.
#
#     ./scripts/generer-identites-internes.sh [fichier-de-sortie]
#
# CE QUI ETAIT CASSE : CE SCRIPT N'EXISTAIT PAS.
#
# `docs/RUNBOOK-PROD.md`, `docs/RUNBOOK-COMPOSE.md` et les dix-neuf messages
# d'erreur de `docker-compose.prod.yml` le nomment — « identite gRPC de
# seller-service absente — scripts/generer-identites-internes.sh ». Le depot ne
# portait que `check-all.sh` et `verifier-env-compose.sh`.
#
# L'operateur remplissait donc les dix-neuf variables a la main, avec la seule
# commande que les runbooks montrent ailleurs : `openssl rand -base64 32`. Cela
# rend 32 octets ALEATOIRES, pas une cle PKCS#8. Le compose etait satisfait — la
# variable est presente et non vide — et le service demarrait.
#
# LE SYMPTOME ARRIVAIT DES SEMAINES PLUS TARD, LOIN DE LA CAUSE.
#
# `IdentiteInterne.Signer` fait `ImportPkcs8PrivateKey(Convert.FromBase64String(...))`
# a l'interieur d'un `GetOrAdd` — donc au PREMIER APPEL gRPC de l'hote, jamais au
# demarrage. Trente-deux octets aleatoires donnent :
#
#     CryptographicException: ASN1 corrupted data
#       ---> The provided data is tagged with 'Universal' class value '4',
#            but it should have been 'Universal' class value '16'.
#
# c'est-a-dire « j'attendais une SEQUENCE DER (0x30), j'ai lu autre chose ».
# L'exception remonte non geree : 500 opaque sur la route appelante, et rien
# dans le message ne parle de configuration.
#
# TOUS LES APPELS INTERNES DE LA PLATEFORME SONT CONCERNES, pas seulement celui
# qu'on regarde : la meme cle sert a signer tous les RPC sortants de l'hote.
#
# CE QUE CE SCRIPT PRODUIT.
#
#   . Une cle EC P-256 par hote, en PKCS#8 DER encode en base64 sur UNE ligne :
#     c'est exactement ce que `ImportPkcs8PrivateKey` attend.
#   . Le registre `INTERNAL_PUBLIC_KEYS`, au format `nom=base64;nom=base64`,
#     ou base64 est un SubjectPublicKeyInfo DER — ce que
#     `ImportSubjectPublicKeyInfo` attend cote verification.
#
# P-256 ET NON P-384. `Signer` et `Verifier` hachent en SHA-256 ; la courbe doit
# lui correspondre, et le commentaire de `InternalCallClientInterceptor` parle
# explicitement d'« une signature P-256 ».
#
# AUCUNE VALEUR N'EST AFFICHEE. Le fichier est ecrit en 0600 et le terminal ne
# recoit que des NOMS. C'est la meme regle que `verifier-env-compose.sh`, et
# elle vaut pour toute modification future de ce fichier.
#
# CE QU'IL NE FAIT PAS :
#
#   . il ne DEPLOIE rien. Le contenu se recopie dans le secret `ENV_FILE` ;
#   . il n'ecrase pas un fichier existant. Regenerer les cles coupe tous les
#     appels internes jusqu'au redeploiement COMPLET : les dix-neuf services
#     doivent recevoir le nouveau registre en meme temps, sinon un hote signe
#     avec une cle que les autres ne connaissent pas encore ;
#   . il ne connait pas notification-service ni return-refund-service. Ils ne
#     sont pas dans `docker-compose.prod.yml` (voir `ComposeProd.Bloques`), donc
#     pas dans cette liste. Les ajouter ici sans les deployer mettrait dix-neuf
#     registres d'accord sur un hote qui n'existe pas.
# ==============================================================================
set -euo pipefail

# Nom d'hote tel que `Assembly.GetEntryAssembly().GetName().Name` le rend, et
# tel que `AutorisationsGrpc` l'indexe. Le nom de variable est le meme, en
# majuscules, les points remplaces par des soulignes.
#
# L'ORDRE EST CELUI DU COMPOSE, pour que les deux listes se relisent ensemble.
HOTES=(
  "HBA.Identity.Api:INTERNAL_KEY_HBA_IDENTITY_API"
  "HBA.Users.Api:INTERNAL_KEY_HBA_USERS_API"
  "HBA.Media.Api:INTERNAL_KEY_HBA_MEDIA_API"
  "HBA.Merchants.Api:INTERNAL_KEY_HBA_MERCHANTS_API"
  "HBA.Catalog.Api:INTERNAL_KEY_HBA_CATALOG_API"
  "HBA.Inventory.Api:INTERNAL_KEY_HBA_INVENTORY_API"
  "HBA.Commerce.Api:INTERNAL_KEY_HBA_COMMERCE_API"
  "HBA.Order.Api:INTERNAL_KEY_HBA_ORDER_API"
  "HBA.Food.Restaurant.Api:INTERNAL_KEY_HBA_FOOD_RESTAURANT_API"
  "HBA.Food.Cart.Api:INTERNAL_KEY_HBA_FOOD_CART_API"
  "HBA.Food.Order.Api:INTERNAL_KEY_HBA_FOOD_ORDER_API"
  "HBA.Delivery.Pricing.Api:INTERNAL_KEY_HBA_DELIVERY_PRICING_API"
  "HBA.Delivery.Core.Api:INTERNAL_KEY_HBA_DELIVERY_CORE_API"
  "HBA.Delivery.Driver.Api:INTERNAL_KEY_HBA_DELIVERY_DRIVER_API"
  "HBA.Delivery.Route.Api:INTERNAL_KEY_HBA_DELIVERY_ROUTE_API"
  "HBA.Financial.Api:INTERNAL_KEY_HBA_FINANCIAL_API"
  "HBA.Engagement.Api:INTERNAL_KEY_HBA_ENGAGEMENT_API"
  "HBA.Promotions.Api:INTERNAL_KEY_HBA_PROMOTIONS_API"
  "HBA.Gateway.Api:INTERNAL_KEY_HBA_GATEWAY_API"
)

sortie="${1:-identites-internes-$(date +%Y%m%d%H%M%S).env}"

command -v openssl >/dev/null 2>&1 || {
  echo "openssl est introuvable." >&2
  exit 1
}

[ -e "$sortie" ] && {
  echo "ATTENTION : $sortie existe deja. Ce script n'ecrase rien." >&2
  echo "  Regenerer les cles coupe TOUS les appels internes jusqu'a ce que les" >&2
  echo "  dix-neuf services aient recu le nouveau registre. Choisir un autre nom" >&2
  echo "  de fichier, ou supprimer celui-ci en connaissance de cause." >&2
  exit 1
}

# `openssl base64 -A` et non `base64 -w0` : BSD (macOS) ne connait pas `-w`.
encoder() { openssl base64 -A; }

travail="$(mktemp -d)"
trap 'rm -rf "$travail"' EXIT
chmod 700 "$travail"

umask 077
: > "$sortie"
chmod 600 "$sortie"

{
  echo "# Identites gRPC internes — engendrees le $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "# UNE cle privee par service, UN registre public partage par tous."
  echo "# A recopier dans le secret ENV_FILE. Ne jamais committer ce fichier."
  echo
} >> "$sortie"

registre=""

for entree in "${HOTES[@]}"; do
  hote="${entree%%:*}"
  variable="${entree##*:}"

  privee="$travail/$hote.pkcs8.der"
  publique="$travail/$hote.spki.der"

  # PKCS#8 DER directement : pas de PEM intermediaire, donc pas d'en-tetes a
  # retirer ni de retours a la ligne a recoller.
  openssl genpkey -algorithm EC \
    -pkeyopt ec_paramgen_curve:P-256 \
    -outform DER -out "$privee" 2>/dev/null

  openssl pkey -inform DER -in "$privee" -pubout -outform DER -out "$publique" 2>/dev/null

  b64privee="$(encoder < "$privee")"
  b64publique="$(encoder < "$publique")"

  printf '%s=%s\n' "$variable" "$b64privee" >> "$sortie"

  # `nom=base64;nom=base64` — voir `IdentiteInterne.LireRegistre`.
  [ -n "$registre" ] && registre="$registre;"
  registre="$registre$hote=$b64publique"

  echo "  ok   $hote"
done

{
  echo
  echo "# Le registre est le MEME pour les dix-neuf services. Une entree illisible"
  echo "# n'est pas fatale : elle rend Unauthenticated pour ce seul appelant."
  printf 'INTERNAL_PUBLIC_KEYS=%s\n' "$registre"
} >> "$sortie"

echo
echo "${#HOTES[@]} identites engendrees dans $sortie (0600)."
echo "Aucune valeur n'a ete affichee. Verifier le format avec :"
echo "    ./scripts/verifier-env-compose.sh docker-compose.prod.yml $sortie"
