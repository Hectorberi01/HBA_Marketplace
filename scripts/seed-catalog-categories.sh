#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# DIX CATÉGORIES RACINES DE CATALOGUE — jeu de développement.
#
# CE SONT LES CATÉGORIES DU MARKETPLACE, PAS LES SECTIONS D'UNE CARTE.
#
# Deux notions portent le mot « catégorie » dans cette plateforme, et elles
# n'ont ni le même propriétaire ni la même portée :
#
#   • catalog-service — l'arbre des catégories PRODUIT, commun à toute la
#     plateforme, administré par l'exploitation. C'est celui-ci.
#   • food-service    — les SECTIONS d'une carte (« Entrées », « Grillades »),
#     propres à UN restaurant, créées par le restaurateur.
#     Voir `POST /api/food/partner/restaurants/{id}/menus/{id}/categories`.
#
# SANS CATÉGORIE PUBLIÉE, AUCUN PRODUIT NE PEUT ÊTRE CRÉÉ.
#
# `CreateProductCommand` exige une catégorie, et l'assistant de l'application
# vendeur en propose la liste. Sur une base neuve elle est vide : le vendeur
# arrive sur une étape 1 sans choix possible, et rien à l'écran ne dit que le
# manque est côté exploitation, pas côté boutique.
#
# CRÉER NE SUFFIT PAS — IL FAUT PUBLIER.
#
# Une catégorie naît en brouillon. `POST /categories/{id}/publish` la met en
# service. Le script fait les deux, et RELIT à la fin : une catégorie créée mais
# restée brouillon est exactement le genre de demi-succès qui fait chercher la
# panne dans l'application.
#
# AUCUN `attributeSchema`, ET C'EST DÉLIBÉRÉ.
#
# Le champ existe et il est VALIDÉ : un schéma posé ici contraindrait tous les
# produits de la catégorie à porter les attributs déclarés, et une création qui
# ne les fournit pas serait refusée. Sur un jeu de développement dont le but est
# justement de pouvoir créer des produits sans friction, la contrainte se
# rajoute plus tard, catégorie par catégorie, quand on sait ce qu'on veut
# imposer.
#
# IDEMPOTENT PAR LECTURE, ET NON PAR TOLÉRANCE DU 409.
#
# `seed-accounts.sh` retente tout et ignore les 409 — un choix assumé pour des
# comptes. Ici on LIT d'abord la liste existante et on ne recrée que ce qui
# manque. La raison est concrète : le nom se transforme en `slug` unique, et
# deux exécutions produiraient soit un 409 muet, soit — si le slug diffère d'un
# accent — un DOUBLON que personne ne remarquerait avant de voir deux fois
# « Beauté & soins » dans l'assistant.
#
# Usage :
#   ./scripts/seed-catalog-categories.sh
#   API=https://... ADMIN_EMAIL=... ADMIN_PASSWORD=... ./scripts/seed-catalog-categories.sh
# ═══════════════════════════════════════════════════════════════════════════

API="${API:-http://localhost:8080}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin123!}"

command -v jq >/dev/null 2>&1 || { echo "jq est requis (brew install jq)." >&2; exit 1; }

# LA LECTURE ET L'ÉCRITURE N'ONT PAS LE MÊME CHEMIN, ET C'EST VOULU.
#
#   • `/api/catalog/categories`       — `MapGroup`, chaque route `AllowAnonymous`.
#     Une vitrine qu'on ne peut pas parcourir sans compte ne convertit personne.
#   • `/api/catalog/admin/categories` — `MapAdminGroup`, donc `RequireRole("Admin",
#     "Moderator")`.
#
# Le segment `/admin` n'a longtemps été qu'un mot : le groupe n'exigeait qu'un
# jeton, celui que délivre n'importe quelle inscription acheteur. Le référentiel
# de la place de marché était donc ouvert en écriture à tous — y compris la
# SUPPRESSION d'une catégorie, qui emporte le rattachement de tous les produits
# qui la référencent, chez tous les vendeurs, d'un seul appel.
#
# SE TROMPER DE CHEMIN NE DONNE PAS 404 MAIS 405. `/api/catalog/categories`
# existe — en GET seulement. ASP.NET trouve la route, refuse la méthode, et rend
# un corps VIDE : ni code d'erreur métier, ni indice. C'est exactement l'erreur
# que la première version de ce script a commise.
CATEGORIES_LECTURE="/api/catalog/categories"
CATEGORIES_ECRITURE="/api/catalog/admin/categories"

# ── Les dix ────────────────────────────────────────────────────────────────
#
# Racines uniquement : l'arbre accepte des enfants (`parentId`), mais une
# hiérarchie inventée ici serait à refaire dès que le catalogue réel se dessine.
# Dix racines suffisent à débloquer la création de produits, et ne préjugent de
# rien.
CATEGORIES=(
  "Téléphonie & accessoires"
  "Informatique"
  "Électroménager"
  "Mode & vêtements"
  "Chaussures & maroquinerie"
  "Beauté & soins"
  "Alimentation & boissons"
  "Maison & décoration"
  "Bébé & enfant"
  "Sport & loisirs"
)

say()  { printf '  %s\n' "$*"; }
step() { printf '\n── %s\n' "$*"; }
warn() { printf '  %s\n' "$*" >&2; }

http_code() { printf '%s' "$1" | tail -n1; }
http_body() { printf '%s' "$1" | sed '$d'; }

req() {
  local method="$1" path="$2" token="${3:-}" body="${4:-}"
  local args=(-sS -m 30 -X "$method" -w '\n%{http_code}' -H 'Accept: application/json')
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "$body" ]  && args+=(-H 'Content-Type: application/json' -d "$body")
  curl "${args[@]}" "$API$path"
}

# Rend le corps sur succès ; sur échec, DIT quoi a échoué avec le code et le
# début du corps — c'est là que vit le code d'erreur métier.
call() {
  local label="$1" method="$2" path="$3" token="${4:-}" body="${5:-}"
  local r code
  r=$(req "$method" "$path" "$token" "$body") || true
  code=$(http_code "$r")
  [[ "$code" =~ ^[0-9]{3}$ ]] || code="000"

  if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
    http_body "$r"
    return 0
  fi

  {
    printf '  ✗ %s → HTTP %s\n' "$label" "$code"
    printf '      %s\n' "$(http_body "$r" | tr -d '\n' | cut -c1-200)"
    case "$code" in
      000) printf '      → la passerelle ne répond pas.\n' ;;
      401) printf '      → jeton absent ou expiré.\n' ;;
      403) printf '      → rôle insuffisant : ces routes sont sous MapAdminGroup.\n' ;;
      404) printf '      → route absente : la passerelle est-elle à jour ?\n' ;;
      # Le corps d'un 405 est vide : sans cette ligne, l'erreur n'apprend rien.
      405) printf '      → le chemin existe, la méthode non. Écriture = %s\n' "$CATEGORIES_ECRITURE" ;;
      409) printf '      → conflit : une catégorie porte déjà ce slug.\n' ;;
      502|503) printf '      → catalog-service est à terre.\n' ;;
    esac
  } >&2
  return 1
}

# ── Préambule ──────────────────────────────────────────────────────────────
#
# Diagnostiquer avant de tenter : sans ce contrôle, une passerelle éteinte et un
# mot de passe faux produisent le même message.
step "Préambule"
r=$(curl -sS -m 20 -w '\n%{http_code}' "$API/health/ready" 2>&1) || {
  warn "✗ $API injoignable en 20 s."
  warn "  docker compose -f docker-compose.dev.yml ps gateway"
  exit 1
}
[ "$(http_code "$r")" = "200" ] || warn "/health/ready répond $(http_code "$r") — un amont est peut-être à terre."
say "passerelle : $API"

step "Connexion administrateur"
# UN SEUL appel d'authentification : le limiteur `auth` (10/60 s, partitionné
# par IP sur les routes anonymes) n'est pas un sujet ici, contrairement à
# `seed-accounts.sh` qui en fait une quarantaine et doit s'imposer une cadence.
r=$(req POST /api/auth/login "" "$(jq -nc --arg e "$ADMIN_EMAIL" --arg p "$ADMIN_PASSWORD" \
      '{email:$e, password:$p, mfaCode:null}')") || true
if [ "$(http_code "$r")" != "200" ]; then
  warn "✗ connexion $ADMIN_EMAIL → HTTP $(http_code "$r")"
  http_body "$r" | head -5 | sed 's/^/    /' >&2
  exit 1
fi
TOKEN=$(http_body "$r" | jq -r '.tokens.accessToken // .accessToken // empty')
[ -n "$TOKEN" ] || { warn "✗ jeton absent de la réponse de connexion."; exit 1; }
say "connecté : $ADMIN_EMAIL"

# ── Ce qui existe déjà ─────────────────────────────────────────────────────
#
# Lecture ANONYME (`GET /api/catalog/categories` est `AllowAnonymous`), et non
# filtrée sur le statut : les brouillons d'une exécution précédente y sont, ce
# qui permet justement de les publier au lieu d'en créer de nouveaux.
step "État actuel du catalogue"
EXISTANT=$(call "lecture des catégories" GET "$CATEGORIES_LECTURE" "") || exit 1
NB_AVANT=$(printf '%s' "$EXISTANT" | jq 'length')
say "$NB_AVANT catégorie(s) présente(s)"

# ── Création et publication ────────────────────────────────────────────────
step "Création et publication"

CREEES=0; REPRISES=0; PUBLIEES=0

for nom in "${CATEGORIES[@]}"; do
  # Recherche par NOM exact. Le slug serait plus robuste, mais il est calculé
  # par le domaine : le recalculer ici dupliquerait une règle qui vit
  # ailleurs — et deux normalisations voisines finissent toujours par diverger.
  id=$(printf '%s' "$EXISTANT" | jq -r --arg n "$nom" '.[] | select(.name == $n) | .id' | head -n1)

  if [ -n "$id" ] && [ "$id" != "null" ]; then
    REPRISES=$(( REPRISES + 1 ))
    say "= $nom (existe déjà)"
  else
    body=$(jq -nc --arg n "$nom" '{name:$n, parentId:null, imageUrl:null, attributeSchema:null}')
    reponse=$(call "création de « $nom »" POST "$CATEGORIES_ECRITURE" "$TOKEN" "$body") || continue
    id=$(printf '%s' "$reponse" | jq -r '.id // empty')
    if [ -z "$id" ]; then
      warn "  ✗ « $nom » créée mais l'identifiant est absent de la réponse."
      continue
    fi
    CREEES=$(( CREEES + 1 ))
    say "+ $nom"
  fi

  # Publication systématique, y compris pour une catégorie reprise : elle peut
  # être restée en brouillon d'une exécution interrompue. `PublishCategory` est
  # idempotent sur une catégorie déjà publiée.
  if call "publication de « $nom »" POST "$CATEGORIES_ECRITURE/$id/publish" "$TOKEN" \
       '{"includeDescendants":true}' >/dev/null; then
    PUBLIEES=$(( PUBLIEES + 1 ))
  fi
done

# ── Conformité ─────────────────────────────────────────────────────────────
#
# UN SCRIPT QUI NE RELIT PAS SON TRAVAIL NE VAUT PAS MIEUX QU'UN SILENCE.
#
# On redemande la liste au serveur — pas la variable qu'on a en mémoire — et on
# affiche le STATUT de chacune. Une catégorie créée mais restée en brouillon
# n'apparaîtrait nulle part dans l'application, et le script aurait pourtant
# annoncé « 10 créées ».
step "Conformité — ce que le serveur rend maintenant"

APRES=$(call "relecture des catégories" GET "$CATEGORIES_LECTURE" "") || exit 1

# `CategoryStatus` : Draft · Published · Archived. On affiche la valeur telle que
# le serveur la rend — la traduire ici ferait diverger l'affichage du script de
# ce qu'on lira dans les journaux et dans la base.
printf '%s' "$APRES" | jq -r 'sort_by(.name)[] | "  \(.status | .[0:9]) · \(.name)  [\(.slug)]"'

NON_PUBLIEES=$(printf '%s' "$APRES" | jq '[.[] | select(.status != "Published")] | length')

printf '\n'
say "créées : $CREEES · reprises : $REPRISES · publications réussies : $PUBLIEES"

# ═══════════════════════════════════════════════════════════════════════════
# ON VÉRIFIE LA PRÉSENCE AVANT LE STATUT, ET C'EST L'ORDRE QUI COMPTE.
#
# La première version ne comptait que les non-publiées. Sur une exécution où
# les DIX créations avaient échoué en 405, la liste rendue était vide, « zéro
# non-publiée » était rigoureusement exact, et le script concluait « toutes les
# catégories sont publiées » — après avoir affiché dix croix rouges.
#
# C'est le défaut qu'on corrige dans le code depuis des semaines, arrivé dans
# l'outil censé le débusquer : une vérification qui répond à une question voisine
# de celle qu'on croyait poser. « Aucune n'est en brouillon » n'est pas « toutes
# sont là ».
#
# On cherche donc CHAQUE nom attendu dans ce que le serveur vient de rendre.
# ═══════════════════════════════════════════════════════════════════════════
MANQUANTES=()
for nom in "${CATEGORIES[@]}"; do
  trouve=$(printf '%s' "$APRES" | jq -r --arg n "$nom" '[.[] | select(.name == $n)] | length')
  [ "$trouve" -eq 0 ] && MANQUANTES+=("$nom")
done

if [ "${#MANQUANTES[@]}" -gt 0 ]; then
  warn ""
  warn "✗ ${#MANQUANTES[@]} catégorie(s) sur ${#CATEGORIES[@]} sont ABSENTES du catalogue :"
  for nom in "${MANQUANTES[@]}"; do warn "    · $nom"; done
  warn "  Les causes sont plus haut, avec leur code HTTP."
  exit 1
fi

if [ "$NON_PUBLIEES" -gt 0 ]; then
  warn ""
  warn "$NON_PUBLIEES catégorie(s) existent mais ne sont PAS publiées."
  warn "  Elles resteront invisibles dans la vitrine acheteur."
  warn "  Relancer le script republiera ce qui existe déjà."
  exit 1
fi

say "les ${#CATEGORIES[@]} catégories attendues sont présentes et publiées."
printf '\n'
say "Vérifier côté vendeur : Activités → boutique → Mes produits → « Ajouter un produit »."
say "L'étape 1 de l'assistant doit désormais proposer ces catégories."
