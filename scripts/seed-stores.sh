#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# UNE BOUTIQUE POUR CHAQUE VENDEUR — DIAGNOSTIC D'ABORD, CRÉATION ENSUITE.
#
# `seed-accounts.sh` FAIT DÉJÀ CELA. CE SCRIPT NE LE REMPLACE PAS.
#
# `create_store` y existe, avec le lieu d'expédition et l'ouverture. Si vous
# n'avez pas encore semé, lancez `./scripts/seed-accounts.sh` : il exerce la
# plateforme entière, et son échec est une information.
#
# Celui-ci sert au cas précis où les comptes existent mais l'écran d'activités
# reste vide — « Aucune activité n'est rattachée à ce compte ». Rejouer
# l'amorçage complet pour cela ferait passer trois cents appels pour en corriger
# cinq.
#
# ═══════════════════════════════════════════════════════════════════════════
# POURQUOI CE SCRIPT COMMENCE PAR DIAGNOSTIQUER, ET NON PAR CRÉER.
#
# `CreateStoreCommand` refuse avec `sellers.store.seller_not_active` tant que le
# dossier vendeur n'est pas ACTIF — et un dossier ne devient actif qu'après
# approbation du KYB. Un script qui se contenterait de POSTer récolterait cinq
# refus identiques et laisserait chercher la panne dans l'application.
#
# La cause la plus fréquente d'un écran d'activités vide n'est donc PAS l'absence
# de boutique : c'est un dossier vendeur resté en attente. Ce script le dit, et
# propose de l'approuver avec le jeton admin.
#
# IL RELIT AVANT DE CRÉER, ET C'EST INDISPENSABLE.
#
# `CreateStoreCommand` n'impose AUCUNE unicité de nom : sans cette relecture, une
# relance fabriquerait une boutique de plus à chaque exécution, et le vendeur en
# aurait trois au bout de trois lancements. La leçon vient du script de
# catégories, qui a affirmé « tout est publié » après dix croix rouges.
# ═══════════════════════════════════════════════════════════════════════════
#
# Usage :
#   ./scripts/seed-stores.sh                     sur http://localhost:8080
#   API=https://…  ./scripts/seed-stores.sh      ailleurs
#   APPROUVER_KYB=1 ./scripts/seed-stores.sh     approuve les dossiers en attente
# ═══════════════════════════════════════════════════════════════════════════

API="${API:-http://localhost:8080}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin123!}"
PWD_ALL="${PWD_ALL:-Passw0rd!}"

# APPROBATION DU KYB NON AUTOMATIQUE PAR DÉFAUT.
#
# Approuver un dossier est une décision de modération. La rendre implicite dans
# un script d'amorçage habituerait à ce qu'elle se fasse sans regard — et le jour
# où ce script tournerait ailleurs qu'en local, il validerait des vendeurs réels.
APPROUVER_KYB="${APPROUVER_KYB:-0}"

command -v jq >/dev/null || { echo "jq est requis."; exit 1; }

# ── Les vendeurs et la boutique attendue ──────────────────────────────────
#
# LES RESTAURATEURS FIGURENT ICI AUSSI, ET CE N'EST PAS UNE ERREUR.
#
# Un établissement ne peut entrer en service sans dossier de reversement, et ce
# dossier EST un vendeur. `vendeur.food` a donc un `sellerId` — il peut porter
# une boutique marchandise, même si son activité principale est la restauration.
# On ne lui en crée pas : sa vitrine est sa carte. Il est listé pour que le
# diagnostic couvre tous les comptes, pas pour recevoir un magasin.
VENDEURS=(
  # e-mail|nom de la boutique|téléphone|commune|latitude|longitude
  "vendeur.market@hba.local|Awa Électronique — Cotonou|+22997000001|COTONOU|6.3703|2.3912"
  "vendeur.mixte@hba.local|Fatou Commerce — Calavi|+22997000003|ABOMEY_CALAVI|6.4489|2.3556"
  "vendeur.2boutiques@hba.local|Koffi Textile — Porto-Novo|+22997000004|PORTO_NOVO|6.4969|2.6289"
  "vendeur.2boutiques@hba.local|Koffi Textile — Bohicon|+22997000004|BOHICON|7.1781|2.0667"
  "vendeur.2restos@hba.local|Chez Adjo — Parakou|+22997000005|PARAKOU|9.3372|2.6303"
)

# Les comptes sans boutique à créer, listés pour le diagnostic seulement.
DIAGNOSTIC_SEUL=("vendeur.food@hba.local")

HDRS=$(mktemp); trap 'rm -f "$HDRS"' EXIT

req() {
  local method="$1" path="$2" token="${3:-}" body="${4:-}"
  local args=(-sS -X "$method" "$API$path" -H 'Content-Type: application/json')
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "$body" ] && args+=(-d "$body")
  local r rc=0
  r=$(curl "${args[@]}" -D "$HDRS" -w '\n%{http_code}' 2>/dev/null) || rc=$?
  printf '%s\n' "$r"
  return "$rc"
}

http_code() { tail -n1 <<<"$1"; }
http_body() { sed '$d' <<<"$1"; }
say()   { printf '  %s\n' "$*"; }
step()  { printf '\n── %s\n' "$*"; }
warn()  { printf '  %s\n' "$*" >&2; }

# `call` DIT POURQUOI IL A ÉCHOUÉ, avec le code métier du corps : c'est là que
# se trouve `sellers.store.seller_not_active`, qui nomme la cause exacte.
call() {
  local label="$1" method="$2" path="$3" token="${4:-}" body="${5:-}" tolere="${6:-}"
  local r code
  r=$(req "$method" "$path" "$token" "$body") || true
  code=$(http_code "$r"); [[ "$code" =~ ^[0-9]{3}$ ]] || code="000"

  if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
    http_body "$r"; return 0
  fi
  if [ -n "$tolere" ] && [[ " $tolere " == *" $code "* ]]; then
    return 2
  fi

  {
    echo "  ✗ $label → HTTP $code"
    http_body "$r" | jq -r '.detail // .title // .error // empty' 2>/dev/null \
      | head -2 | sed 's/^/    /'
    http_body "$r" | jq -r '.code // .errorCode // empty' 2>/dev/null \
      | head -1 | sed 's/^/    code : /'
    case "$code" in
      000) echo "    → la passerelle ne répond pas ($API)." ;;
      403) echo "    → ce compte n'a pas le rôle Seller. Voir scripts/grant-partner-roles.sql." ;;
      502|503) echo "    → merchant-service est à terre ou refuse de démarrer." ;;
    esac
  } >&2
  return 1
}

login() {
  local email="$1" r
  r=$(req POST /api/auth/login "" \
        "$(jq -nc --arg e "$email" --arg p "$PWD_ALL" '{email:$e,password:$p,mfaCode:null}')") || true
  if [[ ! "$(http_code "$r")" =~ ^2 ]]; then
    warn "✗ connexion $email → HTTP $(http_code "$r")"
    echo ""; return
  fi
  http_body "$r" | jq -r '.tokens.accessToken // .accessToken // empty'
}

# ── Diagnostic d'un vendeur : existe-t-il, et est-il actif ? ───────────────
#
# Rend « sellerId|statut|kyb » sur la sortie standard, ou une chaîne vide.
diagnostiquer() {
  local email="$1" token body
  token=$(login "$email"); [ -n "$token" ] || { echo ""; return; }

  body=$(call "dossier de $email" GET /api/merchants/me "$token") || { echo ""; return; }
  jq -r '[(.id // ""), (.status // "?"), (.kybStatus // "?")] | join("|")' <<<"$body"
}

# ── Un lieu d'expédition, sans lequel la boutique ne peut pas OUVRIR ──────
#
# `Store.Open()` L'EXIGE. Une boutique créée sans lieu reste fermée, donc
# absente de la vitrine — et le vendeur ne comprend pas pourquoi son magasin
# n'apparaît nulle part. Le lieu se crée côté inventory-service, pas merchant.
#
# LE TÉLÉPHONE EST AU FORMAT +229 SUIVI DE DIX CHIFFRES, différent de celui
# des boutiques : `BeninGeography.NormalizePhone` l'impose depuis le passage à
# dix chiffres. Un numéro à huit chiffres est refusé ici et accepté ailleurs.
lieu_expedition() {
  local token="$1" seller="$2" commune="$3" repere="$4" body id

  body=$(call "lieux de $seller" GET "/api/inventory/owners/$seller/locations" "$token") || true
  [ -n "$body" ] || body='[]'
  id=$(jq -r --arg r "$repere" \
        'if type=="array" then (map(select(.landmark == $r)) | .[0].id // empty) else empty end' \
        <<<"$body" 2>/dev/null || echo "")
  if [ -n "$id" ]; then echo "$id"; return; fi

  body=$(call "lieu « $repere »" POST /api/inventory/locations "$token" \
        "$(jq -nc --arg c "$commune" --arg r "$repere" \
           '{type:"SellerAddress", commune:$c, quartier:null, landmark:$r,
             line:null, contactPhone:"+2290197000001"}')") || { echo ""; return; }
  jq -r '.id // empty' <<<"$body"
}

# ═══════════════════════════════════════════════════════════════════════════
step "1/3 — Diagnostic des dossiers vendeurs"

declare -A DOSSIER=()
declare -A JETON=()
EN_ATTENTE=()

for email in $(printf '%s\n' "${VENDEURS[@]}" | cut -d'|' -f1) "${DIAGNOSTIC_SEUL[@]}"; do
  [ -n "${DOSSIER[$email]:-}" ] && continue

  token=$(login "$email")
  if [ -z "$token" ]; then
    DOSSIER[$email]="—|connexion refusée|—"
    continue
  fi
  JETON[$email]="$token"

  info=$(diagnostiquer "$email")
  if [ -z "$info" ]; then
    DOSSIER[$email]="—|aucun dossier|—"
    say "✗ $email — aucun dossier vendeur (GET /api/merchants/me n'a rien rendu)"
    continue
  fi

  DOSSIER[$email]="$info"
  IFS='|' read -r sid statut kyb <<<"$info"
  say "$email → statut $statut, KYB $kyb"

  # « Pending » N'EST PAS UNE PANNE, C'EST UN ÉTAT D'ATTENTE LÉGITIME.
  if [ "$statut" != "Active" ]; then
    EN_ATTENTE+=("$email|$sid")
  fi
done

# ═══════════════════════════════════════════════════════════════════════════
if [ ${#EN_ATTENTE[@]} -gt 0 ]; then
  step "2/3 — ${#EN_ATTENTE[@]} dossier(s) non actif(s)"

  if [ "$APPROUVER_KYB" != "1" ]; then
    warn "Ces vendeurs ne peuvent PAS créer de boutique :"
    warn '  CreateStoreCommand refuse avec sellers.store.seller_not_active.'
    for e in "${EN_ATTENTE[@]}"; do warn "    • ${e%%|*}"; done
    warn ""
    warn "  C'est la cause la plus fréquente de « Aucune activité n'est rattachée"
    warn "  à ce compte » : le dossier existe, il n'est pas approuvé."
    warn ""
    warn "  Pour les approuver — décision de modération, donc explicite :"
    warn "    APPROUVER_KYB=1 $0"
    warn ""
    warn "  Le script continue pour les vendeurs déjà actifs."
  else
    # L'ADMIN NE SE CONNECTE PAS AVEC `login` : son mot de passe n'est pas
    # celui des comptes semés. Une seule variable de trop, et les sept
    # approbations repartaient avec un jeton vide — donc en 401, imputés au
    # dossier plutôt qu'à l'identifiant.
    reponse=$(req POST /api/auth/login "" \
      "$(jq -nc --arg e "$ADMIN_EMAIL" --arg p "$ADMIN_PASSWORD" \
         '{email:$e,password:$p,mfaCode:null}')") || true
    ADMIN=$(http_body "$reponse" | jq -r '.tokens.accessToken // .accessToken // empty')

    if [ -z "$ADMIN" ]; then
      warn "✗ connexion admin impossible ($ADMIN_EMAIL) — aucun dossier ne sera approuvé."
    else
      for entree in "${EN_ATTENTE[@]}"; do
        email="${entree%%|*}"; sid="${entree##*|}"
        [ -n "$sid" ] && [ "$sid" != "—" ] || continue

        # DEUX GESTES, ET DANS CET ORDRE. `activate` seul échoue tant que le
        # KYB n'est pas approuvé ; `kyb/approve` seul ne suffit pas toujours à
        # basculer le statut. Les 409 sont tolérés : un dossier déjà approuvé ne
        # doit pas faire rougir la sortie.
        call "KYB de $email" POST "/api/merchants/$sid/kyb/approve" "$ADMIN" "" "409 400" >/dev/null || true
        call "activation de $email" POST "/api/merchants/$sid/activate" "$ADMIN" "" "409 400" >/dev/null || true

        info=$(diagnostiquer "$email")
        DOSSIER[$email]="$info"
        say "$email → $(cut -d'|' -f2 <<<"$info")"
      done
    fi
  fi
fi

# ═══════════════════════════════════════════════════════════════════════════
step "3/3 — Boutiques"

for ligne in "${VENDEURS[@]}"; do
  IFS='|' read -r email nom tel commune lat lon <<<"$ligne"

  info="${DOSSIER[$email]:-}"
  IFS='|' read -r sid statut kyb <<<"${info:-—|inconnu|—}"

  if [ -z "$sid" ] || [ "$sid" = "—" ]; then
    warn "⊘ « $nom » ignorée : $email n'a pas de dossier vendeur."
    continue
  fi
  if [ "$statut" != "Active" ]; then
    warn "⊘ « $nom » ignorée : dossier de $email en statut $statut."
    continue
  fi

  token="${JETON[$email]:-}"
  [ -n "$token" ] || token=$(login "$email")
  [ -n "$token" ] || { warn "⊘ « $nom » : pas de jeton."; continue; }

  # ── Relire AVANT de créer ────────────────────────────────────────────────
  body=$(call "boutiques de $email" GET "/api/merchants/$sid/stores" "$token") || true
  [ -n "$body" ] || body='[]'
  id=$(jq -r --arg n "$nom" \
        'if type=="array" then (map(select(.name == $n)) | .[0].id // empty) else empty end' \
        <<<"$body" 2>/dev/null || echo "")

  if [ -n "$id" ]; then
    say "= « $nom » existe déjà"
  else
    body=$(call "création de « $nom »" POST "/api/merchants/$sid/stores" "$token" \
          "$(jq -nc --arg n "$nom" --arg p "$tel" \
             '{name:$n, contactPhone:$p, contactEmail:null}')") || continue
    id=$(jq -r '.id // empty' <<<"$body")
    [ -n "$id" ] || { warn "✗ « $nom » : aucun identifiant rendu."; continue; }
    say "+ « $nom » créée"
  fi

  lieu=$(lieu_expedition "$token" "$sid" "$commune" "$nom")
  if [ -n "$lieu" ]; then
    call "lieu de « $nom »" PUT "/api/merchants/$sid/stores/$id/location" "$token" \
      "$(jq -nc --arg l "$lieu" '{fulfillmentLocationId:$l}')" "409" >/dev/null || true
  else
    warn "« $nom » restera FERMÉE : aucun lieu d'expédition."
  fi

  call "ouverture de « $nom »" POST "/api/merchants/$sid/stores/$id/open" "$token" "409" >/dev/null || true
done

# ═══════════════════════════════════════════════════════════════════════════
# CONFORMITÉ — CE QUI EXISTE VRAIMENT, RELU APRÈS COUP.
#
# UN SCRIPT QUI NE RELIT PAS SON TRAVAIL NE VAUT PAS MIEUX QU'UN SILENCE.
#
# Le script de catégories a affirmé « toutes les catégories sont publiées » après
# dix croix rouges, parce qu'il comptait les non-publiées d'une liste vide. Cette
# section ne compte rien : elle NOMME ce que la plateforme rend.
# ═══════════════════════════════════════════════════════════════════════════
step "Conformité"

manquantes=0
for email in $(printf '%s\n' "${VENDEURS[@]}" | cut -d'|' -f1 | sort -u); do
  info="${DOSSIER[$email]:-}"
  sid=$(cut -d'|' -f1 <<<"${info:-}")
  [ -n "$sid" ] && [ "$sid" != "—" ] || { warn "✗ $email : pas de dossier"; manquantes=$((manquantes+1)); continue; }

  token="${JETON[$email]:-}"
  [ -n "$token" ] || token=$(login "$email")
  body=$(call "relecture de $email" GET "/api/merchants/$sid/stores" "$token") || true
  [ -n "$body" ] || body='[]'

  n=$(jq -r 'if type=="array" then length else 0 end' <<<"$body")
  if [ "$n" = "0" ]; then
    warn "✗ $email : AUCUNE boutique"
    manquantes=$((manquantes+1))
  else
    printf '  %s : %s boutique(s)\n' "$email" "$n"
    jq -r 'if type=="array" then .[] | "      • \(.name) — \(.status // "?")" else empty end' <<<"$body"
  fi
done

echo
if [ "$manquantes" -eq 0 ]; then
  echo "Chaque vendeur attendu a au moins une boutique."
  echo "Rouvrez l'application : l'écran d'activités doit maintenant les lister."
else
  echo "$manquantes vendeur(s) sans boutique — relisez les lignes ✗ ci-dessus."
  echo "Un dossier non actif est la cause la plus probable : APPROUVER_KYB=1 $0"
  exit 1
fi
