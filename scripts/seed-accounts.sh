#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# JEU DE COMPTES DE DÉVELOPPEMENT — 5 partenaires, 5 livreurs, 5 clients.
#
# PAR LA PASSERELLE, ET NON EN BASE.
#
# Écrire directement dans postgres serait plus rapide et plus fragile : il
# faudrait dupliquer les invariants du domaine (hachage du mot de passe,
# statuts, clés étrangères entre treize bases), et le jeu se périmerait à la
# première migration.
#
# En passant par les routes réelles, ce script EXERCE la plateforme. Quand il
# échoue, c'est qu'un parcours est cassé — et c'est une information.
#
# IDEMPOTENT PAR TOLÉRANCE, PAS PAR VÉRIFICATION.
#
# Relancé, il retente tout ; les créations déjà faites échouent en 409 et sont
# ignorées. Il ne cherche pas à deviner ce qui existe : sur un jeu de données
# de développement, réessayer coûte moins cher que maintenir un état.
#
# ═══════════════════════════════════════════════════════════════════════════
# CE SCRIPT MENTAIT, ET C'EST LA PREMIÈRE CHOSE QU'IL FAUT SAVOIR DE LUI.
#
# Sa version précédente redirigeait vers /dev/null la sortie de `kyb/approve`,
# `activate`, `open`, `submit` et `approve` — c'est-à-dire de TOUTES les étapes
# qui font passer un compte de « créé » à « en service ». Un échec y était
# rigoureusement indiscernable d'un succès, et il annonçait « vendeur créé,
# 1 boutique » sur un jeu de données entièrement dormant.
#
# Ce qui échouait, en silence, à chaque exécution :
#
#   • `POST /{id}/kyb/approve` → 409 « aucune pièce KYB à valider » :
#     `Seller.ApproveKyb()` refuse un dossier sans document, et le script n'en
#     déposait aucun ;
#   • `POST /{id}/activate` → 409 « le KYB doit être validé », puis « les
#     coordonnées de reversement sont requises » : le script n'en fixait pas.
#     Tous les vendeurs restaient donc `Pending` ;
#   • `POST /stores/{id}/open` → 409 « rattachez un lieu d'expédition » :
#     `Store.Open()` l'exige, et aucun lieu n'était créé. Toutes les boutiques
#     restaient `Draft` ;
#   • `POST /restaurants/{id}/submit` → 409, pour TROIS motifs cumulés
#     (horaires, lieu de collecte, dossier de reversement) ;
#   • `POST /admin/restaurants/{id}/approve` → 409 « n'attend pas de
#     validation », puisque la soumission n'avait pas eu lieu.
#
# C'est exactement le défaut qu'on corrige dans le code depuis des semaines :
# un `Result` qu'on ne regarde pas. Le voici corrigé ici aussi — voir `call`,
# et la section « Conformité » qui RELIT tout ce qui vient d'être écrit.
#
# Usage :
#   ./scripts/seed-accounts.sh                 sur http://localhost:8080
#   API=https://... ./scripts/seed-accounts.sh ailleurs
# ═══════════════════════════════════════════════════════════════════════════

API="${API:-http://localhost:8080}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@hba.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin123!}"

# Mot de passe commun à tous les comptes semés. Ce fichier est versionné : il ne
# doit jamais servir ailleurs qu'en local.
PWD_ALL="Passw0rd!"

# DEUX FORMATS DE TÉLÉPHONE, ET CE N'EST PAS UNE ÉTOURDERIE.
#
# Les comptes, les boutiques et les établissements acceptent n'importe quel
# numéro de 8 à 20 caractères. Les LIEUX D'EXPÉDITION, eux, passent par
# `BeninGeography.NormalizePhone`, qui exige +229 suivi de DIX chiffres — la
# numérotation béninoise depuis le passage à 10 chiffres. Un « +22997000001 »
# (huit chiffres) y est rejeté avec « un numéro joignable sur place est
# obligatoire », message qui ne dit pas que c'est la LONGUEUR qui cloche.
LOC_PHONE="+2290197000001"

command -v jq >/dev/null || { echo "jq est requis (brew install jq)." >&2; exit 2; }

# Un identifiant quelconque, pour les champs que le domaine exige sans les
# vérifier. `uuidgen` n'existe pas partout ; /proc non plus. On garde les deux,
# plutôt qu'un échec au dixième compte sur une machine de collègue.
new_guid() {
  if command -v uuidgen >/dev/null; then
    uuidgen | tr '[:upper:]' '[:lower:]'
  elif [ -r /proc/sys/kernel/random/uuid ]; then
    cat /proc/sys/kernel/random/uuid
  else
    printf '%08x-%04x-4%03x-8%03x-%012x\n' \
      "$((RANDOM * RANDOM))" "$RANDOM" "$((RANDOM % 4096))" "$((RANDOM % 4096))" \
      "$((RANDOM * RANDOM * RANDOM))"
  fi
}

# ═══════════════════════════════════════════════════════════════════════════
# LE LIMITEUR DE DÉBIT REFUSAIT CE SCRIPT DÈS LE CINQUIÈME PARTENAIRE — ET
#    C'EST LE SCRIPT QUI AVAIT TORT.
#
# `RateLimiting:Auth` vaut `PermitLimit 10` sur `WindowSeconds 60` dans
# l'`appsettings.json` de la passerelle, et les routes `/api/auth/**` portent
# `RateLimiterPolicy: "auth"`. Dix tentatives par minute, c'est la protection
# contre l'énumération de mots de passe. Elle N'EST PAS le défaut, et on ne la
# relâche pas pour le confort d'un amorçage : ce serait affaiblir la production
# pour gagner trois minutes en développement.
#
# ET LA PARTITION EST L'ADRESSE IP, PAS LE COMPTE.
#
# `RateLimitingExtensions.PartitionKey` prend d'abord le claim `sub` du jeton —
# mais `register` et `login` sont ANONYMES : il n'y a pas de jeton. Les quarante
# et quelques appels d'authentification du script partagent donc UN SEUL
# compteur, `auth:ip:…`. Semer quinze comptes avec quinze identités distinctes
# n'y change strictement rien.
#
# Ce qu'on observait : 429 au cinquième partenaire, puis `login` rendant une
# chaîne vide, puis toutes les étapes suivantes parties sans en-tête
# d'autorisation — d'où la pluie de 401, zéro livreur, zéro client, et une
# conformité entièrement rouge.
#
# Deux mécanismes, complémentaires :
#
#   1. UNE CADENCE qui espace les appels d'authentification pour ne jamais
#      atteindre la limite — c'est le mécanisme normal, celui qui doit suffire ;
#   2. UN RÉESSAI SUR 429, filet de sécurité pour le cas où la fenêtre est déjà
#      entamée par autre chose : une application ouverte sur la même pile, une
#      exécution précédente interrompue, un collègue derrière le même NAT.
# ═══════════════════════════════════════════════════════════════════════════

# Recopiées de RateLimiting:Auth. Si elles changent là-bas, elles doivent
# changer ici : le script ne les devine pas, la passerelle ne les publie pas.
AUTH_PERMIT_LIMIT=10
AUTH_WINDOW_SECONDS=60

# SEPT SECONDES, ET NON SIX.
#
# 60/10 donnerait « un appel toutes les six secondes ». C'est faux d'un cheveu,
# et le cheveu suffit : la fenêtre est FIXE (`GetFixedWindowLimiter`), pas
# glissante. Des appels espacés d'exactement 6 s aux instants 0, 6, … 60 sont
# ONZE dans une même fenêtre de 60 s — un de trop, et le refus revient, une fois
# par minute, de façon apparemment aléatoire. Avec 7 s, au plus 9 appels par
# fenêtre quel que soit l'instant où celle-ci a commencé : une place d'avance,
# sans jamais avoir à connaître la phase du serveur.
AUTH_MIN_INTERVAL=$(( AUTH_WINDOW_SECONDS / AUTH_PERMIT_LIMIT + 1 ))

# COMBIEN D'APPELS D'AUTHENTIFICATION, EXACTEMENT — pour pouvoir annoncer la
# durée avant de commencer plutôt que de laisser deviner.
#
#    1  connexion administrateur
# + 30  inscription + connexion des 15 comptes (5 partenaires, 5 livreurs, 5 clients)
# + 10  reconnexion de conformité des 5 partenaires et des 5 livreurs
# ─────
#   41  Les 5 clients ne se reconnectent plus : voir la conformité, plus bas.
AUTH_APPELS_ESTIMES=41

# Réessai sur 429 : quatre tentatives à 20 s couvrent 80 s, soit plus d'une
# fenêtre entière — donc au moins un renouvellement complet du quota. Si cela ne
# suffit pas, ce n'est plus un problème de débit, et insister masquerait la
# vraie cause.
RETRY_429_TENTATIVES=4
RETRY_429_ATTENTE=20

# La politique `otp` porte une fenêtre de 300 s : un `Retry-After` long peut
# être parfaitement légitime. On plafonne quand même, pour qu'un en-tête
# aberrant ne fasse pas dormir le script une heure sans que rien ne l'explique.
RETRY_429_PLAFOND=300

# LES EN-TÊTES DE RÉPONSE VONT DANS UN FICHIER, PAS SUR LA SORTIE STANDARD.
#
# `req` rend le CORPS sur stdout ; un `-i` y mêlerait les en-têtes et `jq` n'en
# tirerait plus rien. `-D` les dépose à côté, où `retry_after` lit `Retry-After`
# sans polluer ce que l'appelant capture.
HDRS=$(mktemp "${TMPDIR:-/tmp}/hba-seed-headers.XXXXXX")

# L'ÉTAT DE CADENCE VIT DANS UN FICHIER, ET CE N'EST PAS UN CAPRICE.
#
# `T=$(register_and_login …)` exécute la fonction dans un SOUS-SHELL. Une
# variable globale mise à jour là-dedans est perdue au retour : l'horodatage du
# dernier appel serait remis à zéro à chaque compte, la cadence ne freinerait
# jamais rien, et le 429 reviendrait — sans qu'aucune ligne du script ait l'air
# fausse. C'est le même piège que les `say` capturés par `$(…)`, vu plus bas.
# Un fichier, lui, traverse les sous-shells.
CADENCE=$(mktemp "${TMPDIR:-/tmp}/hba-seed-cadence.XXXXXX")
printf '0\n' >"$CADENCE"

trap 'rm -f "$HDRS" "$CADENCE"' EXIT

# Attend, s'il le faut, que l'intervalle minimal soit écoulé depuis le dernier
# appel d'authentification.
#
# L'ATTENTE S'ANNONCE, SUR L'ERREUR STANDARD.
#
# Muet, le script paraît figé une minute durant : on l'interrompt, on relance, et
# la fenêtre repart entamée — l'interruption fabrique le problème qu'elle croyait
# constater. Et c'est bien `>&2` : `T=$(login …)` capture la sortie STANDARD, une
# ligne d'attente y atterrirait comme jeton.
cadence_auth() {
  local quoi="$1" precedent maintenant reste
  precedent=$(cat "$CADENCE" 2>/dev/null || echo 0)
  [[ "$precedent" =~ ^[0-9]+$ ]] || precedent=0

  maintenant=$(date +%s)
  reste=$(( AUTH_MIN_INTERVAL - (maintenant - precedent) ))

  # Le temps passé dans les appels NON authentifiés compte : entre deux
  # connexions, le script écrit des boutiques, des lieux, des cartes. La cadence
  # ne rajoute que ce qui manque, pas sept secondes pleines.
  if [ "$precedent" -gt 0 ] && [ "$reste" -gt 0 ]; then
    printf '  ⏳ %ss avant %s — cadence imposée par le limiteur (%s appels/%ss sur /api/auth/*)\n' \
      "$reste" "$quoi" "$AUTH_PERMIT_LIMIT" "$AUTH_WINDOW_SECONDS" >&2
    sleep "$reste"
  fi

  date +%s >"$CADENCE"
}

# Combien de secondes attendre après un 429.
#
# ON HONORE `Retry-After`, MAIS ON NE LE CROIT PAS SUR PAROLE.
#
# La passerelle l'écrit avec `(int)retryAfter.TotalSeconds` : la troncature rend
# « 0 » quand il reste 900 ms, et repartir aussitôt reprendrait un 429. D'où la
# seconde ajoutée, et le plancher. La RFC autorise aussi une DATE au lieu d'un
# nombre ; cette passerelle n'en écrit pas, mais un proxy intercalé pourrait —
# d'où le contrôle « uniquement des chiffres » avant tout calcul.
retry_after() {
  local v
  v=$( { grep -i '^retry-after:' "$HDRS" 2>/dev/null || true; } \
       | tail -n1 | tr -d '\r' | awk '{print $2}' )

  if [[ ! "$v" =~ ^[0-9]+$ ]]; then
    echo "$RETRY_429_ATTENTE"
    return 0
  fi

  [ "$v" -gt "$RETRY_429_PLAFOND" ] && v="$RETRY_429_PLAFOND"
  v=$(( v + 1 ))
  [ "$v" -lt 2 ] && v=2
  echo "$v"
}

# ── Enveloppe HTTP ─────────────────────────────────────────────────────────
#
# Rend le corps sur la sortie standard et le code HTTP sur la dernière ligne,
# ce qui permet à l'appelant de décider quoi faire d'un 409 sans que le script
# s'arrête sur `set -e`.
#
# SEUL LE 429 EST RÉESSAYÉ.
#
# Un 401, un 409, un 500 remontent tels quels à l'appelant, qui les tolère ou les
# crie — exactement comme avant. Réessayer aveuglément changerait une panne en
# lenteur, et ce script vient de passer des semaines à apprendre à ne plus
# recouvrir un échec.
req() {
  local method="$1" path="$2" token="${3:-}" body="${4:-}"
  local args=(-sS -X "$method" "$API$path" -H 'Content-Type: application/json')
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "$body" ] && args+=(-d "$body")

  local tentative=0 r rc code attente
  while : ; do
    tentative=$(( tentative + 1 ))

    # La cadence ne vise QUE les routes portant la politique `auth`. Lectures et
    # écritures sont partitionnées par `sub` — 200 et 60 par minute et PAR
    # COMPTE — et le script en est très loin : les ralentir n'achèterait rien.
    case "$path" in
      /api/auth/*) cadence_auth "$method $path" ;;
    esac

    : >"$HDRS"
    rc=0
    # `|| rc=$?` : sans lui, `set -e` tuerait le script sur une passerelle
    # injoignable, au lieu de laisser `call` et `login` le DIRE.
    r=$(curl "${args[@]}" -D "$HDRS" -w '\n%{http_code}' 2>/dev/null) || rc=$?
    code=$(tail -n1 <<<"$r")

    if [ "$code" != "429" ] || [ "$tentative" -ge "$RETRY_429_TENTATIVES" ]; then
      printf '%s\n' "$r"
      return "$rc"
    fi

    attente=$(retry_after)
    warn "⏳ 429 sur $method $path — quota épuisé ($AUTH_PERMIT_LIMIT appels/${AUTH_WINDOW_SECONDS}s)."
    warn "   attente ${attente}s, puis tentative $(( tentative + 1 ))/$RETRY_429_TENTATIVES."
    sleep "$attente"
  done
}

# Dernière ligne = code HTTP ; le reste = corps.
http_code() { tail -n1 <<<"$1"; }
http_body() { sed '$d' <<<"$1"; }

# LE CODE EST VÉRIFIÉ NUMÉRIQUE AVANT D'ÊTRE COMPARÉ.
#
# Quand curl n'atteint personne, la réponse est vide et `http_code` rend une
# chaîne vide : `[ "" -ge 200 ]` est une ERREUR DE SYNTAXE bash, pas un faux.
# Elle s'affichait telle quelle au milieu du jeu, sans dire que la passerelle
# était injoignable.
ok() {
  local c; c=$(http_code "$1")
  [[ "$c" =~ ^[0-9]{3}$ ]] || return 1
  [ "$c" -ge 200 ] && [ "$c" -lt 300 ]
}

say()  { printf '  %s\n' "$*"; }
step() { printf '\n── %s\n' "$*"; }

# LES FONCTIONS QUI RENDENT UN IDENTIFIANT PARLENT SUR L'ERREUR STANDARD.
#
# `S=$(create_seller …)` capture la SORTIE STANDARD. Un `say` posé dans une de
# ces fonctions finissait donc DANS la variable : `$S` valait
# « ✗ vendeur … → 409 <uuid> », et cette bouillie repartait comme identifiant
# dans les cinq appels suivants, qui répondaient 404 sur un chemin illisible.
# Le défaut existait déjà dans la version précédente ; il ne se voyait pas,
# parce que la sortie de ces cinq appels partait dans /dev/null.
warn() { printf '  %s\n' "$*" >&2; }

# ═══════════════════════════════════════════════════════════════════════════
# LE REMPLAÇANT DE `>/dev/null`.
#
# Rend le corps sur la sortie standard en cas de succès. En cas d'échec, il DIT
# quoi a échoué, avec le code HTTP et le début du corps — c'est presque toujours
# là que se trouve le code d'erreur métier (`sellers.kyb.no_documents`,
# `food.restaurant.payout_required`), qui nomme la cause exacte.
#
# LES CODES « TOLÉRÉS » NE SONT PAS DES ÉCHECS.
#
# Sur une relance, une création déjà faite rend 409. Le signaler en rouge à
# chaque exécution apprendrait à ne plus lire les rouges — et c'est ainsi qu'un
# vrai échec passe inaperçu. Un code toléré rend 2 : rien à l'écran, et pas de
# corps exploitable pour l'appelant, qui sait alors qu'il doit relire.
#
# Usage : call "<libellé>" MÉTHODE /chemin "<jeton>" "<corps>" "<codes tolérés>"
# ═══════════════════════════════════════════════════════════════════════════
call() {
  local label="$1" method="$2" path="$3" token="${4:-}" body="${5:-}" tolerated="${6:-}"
  local r code

  # `|| true` : une passerelle injoignable fait sortir curl en erreur, et
  # `set -e` tuerait le script au milieu du jeu sans rien dire de plus.
  r=$(req "$method" "$path" "$token" "$body") || true
  code=$(http_code "$r")
  [[ "$code" =~ ^[0-9]{3}$ ]] || code="000"

  if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
    http_body "$r"
    return 0
  fi

  case " $tolerated " in
    *" $code "*) return 2 ;;
  esac

  {
    printf '  ✗ %s → HTTP %s\n' "$label" "$code"
    printf '      %s\n' "$(http_body "$r" | tr -d '\n' | cut -c1-200)"
    case "$code" in
      000) printf '      → la passerelle ne répond pas.\n' ;;
      401) printf '      → jeton absent, expiré, ou signé avec une autre clé.\n' ;;
      403) printf '      → rôle insuffisant : cet appel exige Admin.\n' ;;
      404) printf '      → route absente (passerelle à jour ?) ou ressource non visible.\n' ;;
      # Le réessai de `req` a déjà tenu bon : si un 429 arrive jusqu'ici, la
      # fenêtre est saturée par un AUTRE émetteur, ou la limite a été abaissée.
      429) printf '      → limiteur de débit : réessais épuisés. Vérifier RateLimiting dans\n'
           printf '        appsettings.json, et qui d'\''autre tape sur cette pile.\n' ;;
      502|503) printf '      → le service amont est à terre.\n' ;;
    esac
  } >&2
  return 1
}

# ── Préambule : la plateforme répond-elle ? ────────────────────────────────
#
# DIAGNOSTIQUER AVANT DE TENTER.
#
# Sans ce contrôle, une passerelle éteinte, un service à terre et un mot de
# passe faux produisent le MÊME message — « connexion admin impossible ». On
# cherche alors du côté du compte alors que rien n'écoute.
preflight() {
  local r code
  # VINGT SECONDES, ET NON CINQ.
  #
  # `/health/ready` sonde les treize clusters amont. Quand plusieurs sont à
  # terre, chaque tentative attend son propre délai de connexion et la réponse
  # dépasse largement cinq secondes. Avec un délai trop court, curl abandonne et
  # le script annonce « la passerelle ne répond pas » — alors qu'elle répond très
  # bien, et qu'elle est justement en train de dire QUI ne répond pas.
  #
  # C'est l'erreur que ce préambule était censé éviter : un diagnostic qui
  # désigne le mauvais coupable.
  r=$(curl -sS -m 20 -w '\n%{http_code}' "$API/health/ready" 2>&1) || {
    echo "  ✗ $API injoignable — ni réponse ni refus en 20 s." >&2
    echo "    docker compose -f docker-compose.dev.yml ps gateway" >&2
    exit 1
  }
  code=$(http_code "$r")
  if [ "$code" != "200" ]; then
    echo "  la passerelle répond $code sur /health/ready." >&2
    http_body "$r" | head -8 | sed 's/^/    /' >&2
    echo "    → un ou plusieurs services amont sont à terre. On continue :" >&2
    echo "      les étapes qui en dépendent échoueront en 502, en le disant." >&2
  fi
}

# ── Connexion ──────────────────────────────────────────────────────────────
#
# En cas d'échec, on DIT le code et le corps. Un jeton vide rendu en silence
# ferait échouer les vingt étapes suivantes sans jamais nommer la cause.
login() {
  local email="$1" password="$2" r code
  r=$(req POST /api/auth/login "" "$(jq -nc --arg e "$email" --arg p "$password" \
        '{email:$e, password:$p, mfaCode:null}')") || true
  code=$(http_code "$r")
  if ! ok "$r"; then
    {
      echo "  ✗ connexion $email → HTTP $code"
      http_body "$r" | head -5 | sed 's/^/    /'
      case "$code" in
        000) echo "    → la passerelle ne répond pas." ;;
        401) echo "    → identifiants refusés, ou le compte n'existe pas encore." ;;
        404) echo "    → route absente : la passerelle est-elle à jour ?" ;;
        # C'ÉTAIT LA CAUSE DE TOUS LES 401 QUI SUIVAIENT. Un jeton vide rendu
        # ici partait dans vingt appels sans en-tête d'autorisation, et le script
        # accusait les identifiants alors que c'était le débit.
        429) echo "    → limiteur de débit : la cadence et les réessais n'ont pas suffi." ;;
        502|503) echo "    → identity-service est à terre ou n'a pas démarré." ;;
      esac
    } >&2
    echo ""; return
  fi
  http_body "$r" | jq -r '.tokens.accessToken // .accessToken // empty'
}

# Inscrit puis connecte. Un compte déjà présent (409) est simplement reconnecté.
register_and_login() {
  local first="$1" last="$2" email="$3" phone="$4"
  call "inscription $email" POST /api/auth/register "" "$(jq -nc \
      --arg f "$first" --arg l "$last" --arg e "$email" --arg p "$phone" --arg w "$PWD_ALL" \
      '{firstName:$f, lastName:$l, email:$e, phoneNumber:$p, password:$w}')" \
      "409" >/dev/null || true
  login "$email" "$PWD_ALL"
}

# ═══════════════════════════════════════════════════════════════════════════
# LE DOSSIER VENDEUR, JUSQU'À « ACTIF » — CINQ APPELS, PAS DEUX.
#
# « ACTIF » EST UN ÉTAT CONQUIS, PAS UN DRAPEAU.
#
# `Seller.Activate()` refuse tant que le KYB n'est pas VÉRIFIÉ et tant qu'aucun
# COMPTE DE REVERSEMENT n'est enregistré ; `Seller.ApproveKyb()` refuse un
# dossier sans PIÈCE. Il faut donc, dans cet ordre :
#
#   inscription → compte de reversement → pièce KYB → validation KYB → activation
#
# La version précédente sautait les deux du milieu et jetait la sortie des deux
# derniers. Résultat : tous les vendeurs restaient `Pending`, donc
# `CreateStoreCommand` refusait leurs boutiques (« seul un vendeur actif peut
# ouvrir une boutique ») — et cela ne se voyait nulle part.
#
# CET ÉTAT « ACTIF » EST CE QUI REND LE RESTAURATEUR PAYABLE.
#
# La route de rattachement du dossier de reversement d'un établissement exige un
# dossier actif. Un restaurateur est donc nécessairement un VENDEUR — même s'il
# n'ouvre aucune boutique. Ce n'est pas un contournement : c'est la conséquence
# directe de `Restaurant.PayoutSellerId`, qui indexe tout le reversement Food sur
# un identifiant de vendeur.
# ═══════════════════════════════════════════════════════════════════════════
# ═══════════════════════════════════════════════════════════════════════════
# TÉLÉVERSE UNE PIÈCE KYB — UN VRAI FICHIER, UN VRAI `mediaId`.
#
# LE `mediaId` FICTIF NE PASSE PLUS, ET C'EST TOUT L'INTÉRÊT.
#
# Ce script tirait un GUID au hasard, en le documentant : « ni son existence ni
# son appartenance ne sont vérifiées […] en production, c'est un trou ». Le trou
# est refermé — `AddKybDocumentCommandHandler` demande désormais à media-service
# à qui appartient le fichier, et de quelle nature il est. Le raccourci échouerait
# maintenant en « média introuvable », et le dossier resterait sans pièce : ni
# validation KYB, ni activation, donc aucune boutique ouverte.
#
# UN PDF MINIMAL, PAS UN FICHIER VIDE. `UploadValidation` lit les MAGIC BYTES
# et rend le type RÉEL — un fichier vide ou renommé est refusé, précisément pour
# qu'un `Content-Type` déclaré ne serve pas de laissez-passer.
#
# Le propriétaire déclaré est `(Seller, sellerId)` : c'est la convention que le
# handler applique, et l'identifiant du VENDEUR, pas celui du compte.
# ═══════════════════════════════════════════════════════════════════════════
upload_kyb_media() {
  local seller_id="$1" token="$2" shop="$3" fichier r code corps

  fichier=$(mktemp "${TMPDIR:-/tmp}/hba-kyb-XXXXXX")
  printf '%%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%%%EOF\n' >"$fichier"

  # `|| true` : une passerelle injoignable ne doit pas tuer le script sous `set -e`.
  r=$(curl -sS -X POST \
        "$API/api/v1/media?ownerType=Seller&ownerId=$seller_id&mediaType=SellerDocument" \
        -H "Authorization: Bearer $token" \
        -F "file=@$fichier;type=application/pdf" \
        -w '\n%{http_code}' 2>/dev/null) || true

  rm -f "$fichier"

  code=$(http_code "$r")
  [[ "$code" =~ ^[0-9]{3}$ ]] || code="000"

  if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
    corps=$(http_body "$r")
    # Réponse brute aujourd'hui ; `.data` couvre le jour où media passera à
    # l'enveloppe du §25 comme les six services déjà migrés.
    jq -r '.mediaId // .data.mediaId // empty' <<<"$corps"
    return 0
  fi

  warn "✗ téléversement de la pièce KYB de « $shop » → HTTP $code"
  echo ""
}

create_seller() {
  local token="$1" shop="$2" body id pieces media_id

  body=$(call "dossier vendeur « $shop »" POST /api/merchants "$token" \
        "$(jq -nc --arg s "$shop" '{shopName:$s, commissionRate:null, metadata:null}')" \
        "409") || true

  if [ -z "$body" ]; then
    # Déjà vendeur (409), ou échec déjà signalé : on relit son dossier.
    body=$(call "relecture du dossier vendeur « $shop »" GET /api/merchants/me "$token") \
      || { echo ""; return; }
  fi

  id=$(jq -r '.id // empty' <<<"$body")
  [ -n "$id" ] || { warn "✗ dossier vendeur « $shop » : aucun identifiant rendu"; echo ""; return; }

  # Le compte de reversement. Réécrit à chaque exécution : c'est idempotent, et
  # le vérifier coûterait un appel de plus pour le même résultat.
  call "compte de reversement de « $shop »" PUT "/api/merchants/$id/payout-account" "$token" \
    "$(jq -nc --arg n "$LOC_PHONE" --arg t "$shop" \
       '{provider:"MtnMomo", accountNumber:$n, accountName:$t}')" >/dev/null || true

  # UNE VRAIE PIÈCE, TÉLÉVERSÉE — PLUS UN GUID TIRÉ AU HASARD.
  #
  # Ce bloc envoyait `mediaId: $(new_guid)` et documentait pourquoi cela passait :
  # « ni son existence ni son appartenance ne sont vérifiées ». Ce n'est plus vrai.
  # Le handler interroge media-service, et un identifiant inventé rendrait
  # désormais 404 — le dossier resterait sans pièce, donc jamais validé, donc
  # jamais actif, et aucune boutique ne pourrait ouvrir.
  #
  # La pièce n'est déposée QUE si le dossier n'en a pas : sans cette relecture,
  # chaque relance du script empilerait un justificatif de plus.
  pieces=$(jq -r '.kybDocuments | length' <<<"$body" 2>/dev/null || echo 0)
  if [ "${pieces:-0}" = "0" ]; then
    media_id=$(upload_kyb_media "$id" "$token" "$shop")

    if [ -n "$media_id" ]; then
      call "pièce KYB de « $shop »" POST "/api/merchants/$id/kyb/documents" "$token" \
        "$(jq -nc --arg m "$media_id" '{type:"BusinessRegistry", mediaId:$m}')" >/dev/null || true
    else
      # Sans pièce, les deux appels suivants échoueront — autant dire ici POURQUOI,
      # plutôt que de laisser lire « validation KYB → HTTP 409 » sans cause.
      warn "  → sans pièce, « $shop » restera Pending : ni KYB validé, ni activation."
    fi
  fi

  # KYB ET ACTIVATION SONT DES GESTES D'ADMINISTRATEUR.
  #
  # Le vendeur ne peut pas s'auto-approuver — c'est tout l'intérêt du contrôle.
  # Le script porte donc deux jetons : celui du partenaire pour ce qui lui
  # appartient, celui de l'admin pour ce qui l'arbitre.
  call "validation KYB de « $shop »"  POST "/api/merchants/$id/kyb/approve" "$ADMIN_TOKEN" >/dev/null || true
  call "activation de « $shop »"      POST "/api/merchants/$id/activate"    "$ADMIN_TOKEN" >/dev/null || true

  echo "$id"
}

# ═══════════════════════════════════════════════════════════════════════════
# UN LIEU D'EXPÉDITION — L'ADRESSE RÉELLE OÙ LE LIVREUR SE PRÉSENTE.
#
# SANS LUI, NI BOUTIQUE NI RESTAURANT NE PEUVENT ENTRER EN SERVICE.
#
# `Store.Open()` et `Restaurant.SubmitForApproval()` l'exigent tous deux, pour la
# même raison écrite des deux côtés : sans point de retrait, HBA Delivery ne peut
# pas bâtir la course, et l'acheteur découvrirait le blocage après avoir payé.
#
# CRÉÉ AVEC LE JETON ADMIN : `POST /api/inventory/locations` est dans le
# groupe administrateur, et c'est délibéré — un delta de stock ou la suppression
# d'un lieu ne sont pas des gestes d'inscrit lambda.
#
# ON RELIT AVANT DE CRÉER. Cette route n'est pas idempotente : relancé, le
# script accumulerait un lieu de plus par exécution, tous valides, tous
# indiscernables.
#
# `Address.Create` exige commune (parmi les 77), point de repère, POSITION GPS et
# téléphone à dix chiffres. Aucune de ces quatre valeurs n'est décorative : ce
# sont celles dont le livreur et le calcul de distance se servent.
# ═══════════════════════════════════════════════════════════════════════════
fulfillment_location() {
  local owner="$1" commune="$2" landmark="$3" lat="$4" lon="$5" body id

  # RECHERCHE PAR POINT DE REPÈRE, ET NON « LE PREMIER DE LA LISTE ».
  #
  # Un vendeur à deux boutiques a deux lieux distincts. Reprendre le premier
  # venu ferait partir les colis d'Akpakpa depuis Godomey — une erreur qui ne se
  # voit qu'au moment où un livreur se présente à la mauvaise porte.
  body=$(call "lieux existants de $owner" GET "/api/inventory/owners/$owner/locations" "$ADMIN_TOKEN") || true
  [ -n "$body" ] || body='[]'
  id=$(jq -r --arg l "$landmark" \
        'if type=="array" then (map(select(.landmark == $l)) | .[0].id // empty) else empty end' \
        <<<"$body" 2>/dev/null || echo "")
  if [ -n "$id" ]; then echo "$id"; return; fi

  body=$(call "lieu de collecte « $landmark »" POST /api/inventory/locations "$ADMIN_TOKEN" \
        "$(jq -nc --arg o "$owner" --arg c "$commune" --arg l "$landmark" --arg p "$LOC_PHONE" \
           --argjson lat "$lat" --argjson lon "$lon" \
           '{type:"SellerAddress", ownerId:$o, commune:$c, quartier:null, landmark:$l,
             line:null, latitude:$lat, longitude:$lon, contactPhone:$p}')") || { echo ""; return; }

  jq -r '.id // empty' <<<"$body"
}

# ═══════════════════════════════════════════════════════════════════════════
# BOUTIQUE : création, lieu d'expédition, ouverture.
#
# ON RELIT LA LISTE AVANT DE CRÉER, ET C'EST INDISPENSABLE ICI.
#
# `CreateStoreCommand` n'impose AUCUNE unicité de nom : relancé, le script
# fabriquait une « Awa Électronique — Cotonou » de plus à chaque exécution.
# Le vendeur à une boutique en aurait eu trois au bout de trois lancements, et
# la vérification de conformité ci-dessous aurait signalé un écart… causé par
# le script lui-même.
# ═══════════════════════════════════════════════════════════════════════════
create_store() {
  local token="$1" seller="$2" name="$3" phone="$4" commune="$5" lat="$6" lon="$7" body id lieu

  body=$(call "boutiques existantes" GET "/api/merchants/$seller/stores" "$token") || true
  [ -n "$body" ] || body='[]'
  id=$(jq -r --arg n "$name" \
        'if type=="array" then (map(select(.name == $n)) | .[0].id // empty) else empty end' \
        <<<"$body" 2>/dev/null || echo "")

  if [ -z "$id" ]; then
    body=$(call "boutique « $name »" POST "/api/merchants/$seller/stores" "$token" \
          "$(jq -nc --arg n "$name" --arg p "$phone" \
             '{name:$n, contactPhone:$p, contactEmail:null}')") || { echo ""; return; }
    id=$(jq -r '.id // empty' <<<"$body")
  fi

  [ -n "$id" ] || { warn "✗ boutique « $name » : aucun identifiant rendu"; echo ""; return; }

  lieu=$(fulfillment_location "$seller" "$commune" "$name" "$lat" "$lon")
  if [ -n "$lieu" ]; then
    call "lieu d'expédition de « $name »" PUT "/api/merchants/$seller/stores/$id/location" "$token" \
      "$(jq -nc --arg l "$lieu" '{fulfillmentLocationId:$l}')" >/dev/null || true
  else
    warn "« $name » restera fermée : aucun lieu d'expédition n'a pu être créé."
  fi

  call "ouverture de « $name »" POST "/api/merchants/$seller/stores/$id/open" "$token" >/dev/null || true
  echo "$id"
}

# ═══════════════════════════════════════════════════════════════════════════
# UN ÉTABLISSEMENT, DU BROUILLON AU SERVICE.
#
# LE VOLET FOOD ÉTAIT SILENCIEUSEMENT MORT, ET LES DEUX MOITIÉS DU DÉFAUT
# SE COUVRAIENT L'UNE L'AUTRE.
#
# Côté API : `Restaurant.SubmitForApproval()` exige des horaires, un lieu de
# collecte et un dossier de reversement. Les trois commandes applicatives
# existaient — `SetServiceHoursCommand`, `AttachRestaurantLocationCommand`,
# `AttachRestaurantPayoutSellerCommand` — et AUCUNE n'était exposée par une
# route. `submit` répondait donc 409 à tout établissement, sans exception.
#
# Côté script : `submit` et `approve` partaient dans /dev/null, et la ligne
# suivante annonçait « restaurant créé ». Aucun établissement n'est jamais entré
# en service, la vitrine HBA Food est restée vide, et rien ne l'a dit.
#
# Les trois routes ont été ajoutées dans le groupe partenaire (`FoodEndpoints`),
# avec contrôle de propriété : appartenance au personnel + permission des
# réglages, et pour le dossier de reversement, la preuve qu'il appartient au
# PORTEUR DU JETON et qu'il est actif.
#
# L'ORDRE CI-DESSOUS N'EST PAS INTERCHANGEABLE : le lieu de collecte doit
# appartenir au dossier de reversement, donc celui-ci se rattache d'abord.
# ═══════════════════════════════════════════════════════════════════════════
create_restaurant() {
  local token="$1" name="$2" phone="$3" seller="$4" commune="$5" lat="$6" lon="$7" body id lieu

  body=$(call "établissement « $name »" POST /api/food/partner/restaurants "$token" \
        "$(jq -nc --arg n "$name" --arg p "$phone" \
           '{name:$n, description:null, phone:$p}')" "409") || true

  if [ -z "$body" ]; then
    body=$(call "relecture de l'établissement « $name »" GET /api/food/partner/me "$token") \
      || { echo ""; return; }
    id=$(jq -r '.restaurantId // empty' <<<"$body")
  else
    id=$(jq -r '.id // empty' <<<"$body")
  fi

  [ -n "$id" ] || { warn "✗ établissement « $name » : aucun identifiant rendu"; echo ""; return; }

  # 1. Les horaires. Sans eux, `CanAcceptOrders` refuserait TOUJOURS : le maquis
  #    serait validé, visible, et n'accepterait jamais rien.
  call "horaires de « $name »" PUT "/api/food/partner/restaurants/$id/service-hours" "$token" \
    "$(jq -nc '{hours: (["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"]
                        | map({day: ., opensAt: "10:00", closesAt: "22:00"}))}')" >/dev/null || true

  # 2. Le dossier qui encaisse. AVANT le lieu : c'est lui qui dit à qui ce lieu
  #    a le droit d'appartenir.
  if [ -n "$seller" ]; then
    call "dossier de reversement de « $name »" PUT "/api/food/partner/restaurants/$id/payout-seller" "$token" \
      "$(jq -nc --arg s "$seller" '{sellerId:$s}')" >/dev/null || true
  else
    warn "« $name » n'entrera pas en service : aucun dossier vendeur à rattacher."
  fi

  # 3. Le lieu de collecte, propriété du dossier ci-dessus.
  if [ -n "$seller" ]; then
    lieu=$(fulfillment_location "$seller" "$commune" "$name" "$lat" "$lon")
    if [ -n "$lieu" ]; then
      call "lieu de collecte de « $name »" PUT "/api/food/partner/restaurants/$id/location" "$token" \
        "$(jq -nc --arg l "$lieu" '{fulfillmentLocationId:$l}')" >/dev/null || true
    fi
  fi

  # 4. Soumission, puis validation. 409 toléré sur une relance : un établissement
  #    déjà en service n'est plus soumettable, et n'attend plus de validation.
  call "soumission de « $name »" POST "/api/food/partner/restaurants/$id/submit" "$token" "" "409" >/dev/null || true

  # L'approbation publie l'événement qui donne le rôle FoodPartner.
  #
  # LE 409 TOLÉRÉ ICI RECOUVRE DEUX SITUATIONS OPPOSÉES.
  #
  # `food.restaurant.not_pending` est rendu aussi bien par un établissement DÉJÀ
  # validé (relance normale) que par un établissement resté « Draft » parce que
  # la soumission ci-dessus a échoué — horaires, dossier de reversement ou lieu
  # de collecte manquants, chacun avalé par son `|| true`. Dans le second cas
  # `Approve()` n'est jamais atteint, aucun événement n'est levé, et le rôle
  # FoodPartner n'arrive jamais.
  #
  # On ne durcit pas ici : crier à chaque relance apprendrait à ne plus lire.
  # C'est la conformité, en fin de script, qui tranche — elle relit le STATUT et
  # exige le RÔLE, et ne peut confondre les deux cas.
  call "validation de « $name »" POST "/api/food/admin/restaurants/$id/approve" "$ADMIN_TOKEN" "" "409" >/dev/null || true

  echo "$id"
}

# Une carte minimale : un menu, une catégorie, deux plats.
#
# ON RELIT AVANT D'ÉCRIRE. Ces trois routes ne sont pas idempotentes : sans ce
# garde-fou, chaque relance ajouterait une « Carte du jour » de plus, et le
# restaurant finirait avec cinq cartes identiques que rien ne distingue.
create_menu() {
  local token="$1" rid="$2" d1="$3" p1="$4" d2="$5" p2="$6" body menu cat existant

  body=$(call "carte existante" GET "/api/food/partner/restaurants/$rid/menu" "$token") || true
  [ -n "$body" ] || body='{}'
  existant=$(jq -r '(.menus // []) | length' <<<"$body" 2>/dev/null || echo 0)
  if [ "${existant:-0}" != "0" ]; then
    say "carte déjà en place (${existant} menu(s)) — inchangée"
    return
  fi

  body=$(call "menu de la carte" POST "/api/food/partner/restaurants/$rid/menus" "$token" \
        '{"name":"Carte du jour","displayOrder":1}') || return
  menu=$(jq -r '.id // empty' <<<"$body")

  body=$(call "catégorie « Plats »" POST "/api/food/partner/restaurants/$rid/menus/$menu/categories" "$token" \
        '{"name":"Plats","displayOrder":1}') || return
  cat=$(jq -r '.id // empty' <<<"$body")

  for pair in "$d1|$p1" "$d2|$p2"; do
    call "plat « ${pair%%|*} »" POST "/api/food/partner/restaurants/$rid/categories/$cat/items" "$token" \
      "$(jq -nc --arg n "${pair%%|*}" --argjson p "${pair##*|}" \
         '{name:$n, basePrice:$p}')" >/dev/null || true
  done

  say "carte : ${d1}, ${d2}"
}

# ═══════════════════════════════════════════════════════════════════════════

# ═══════════════════════════════════════════════════════════════════════════
# ON ANNONCE LA DURÉE AVANT DE COMMENCER.
#
# Ce jeu ne peut PAS aller plus vite : quarante et un appels d'authentification
# à dix par minute font structurellement plusieurs minutes. Ne pas le dire, c'est
# laisser croire à un blocage au premier silence — et une interruption relance le
# script sur une fenêtre déjà entamée, donc pire qu'avant.
# ═══════════════════════════════════════════════════════════════════════════
step "Cadence"
say "Le limiteur « auth » de la passerelle accepte $AUTH_PERMIT_LIMIT appels par ${AUTH_WINDOW_SECONDS}s,"
say "tous comptes confondus : sur les routes anonymes, la partition est l'adresse IP."
say "Ce jeu en demande environ $AUTH_APPELS_ESTIMES → un appel toutes les ${AUTH_MIN_INTERVAL}s."
say "Durée attendue : ~$(( AUTH_APPELS_ESTIMES * AUTH_MIN_INTERVAL / 60 )) min $(( AUTH_APPELS_ESTIMES * AUTH_MIN_INTERVAL % 60 ))s d'attente délibérée, plus le temps des appels."
say "Chaque attente s'affiche (« ⏳ … »). Un silence prolongé, LUI, serait anormal."

step "Administrateur"
preflight
ADMIN_TOKEN=$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")
if [ -z "$ADMIN_TOKEN" ]; then
  {
    echo ""
    echo "  Le compte admin est semé au DÉMARRAGE d'identity-service, à partir de"
    echo "  ADMIN__EMAIL et ADMIN__PASSWORD. S'il n'existe pas, c'est que le service"
    echo "  n'a pas démarré avec cet amorçage — souvent parce que son image date"
    echo "  d'avant. À vérifier dans cet ordre :"
    echo ""
    echo "    docker compose -f docker-compose.dev.yml ps identity-service"
    echo "    docker compose -f docker-compose.dev.yml logs identity-service | grep -i 'amorçage\|admin\|migration'"
  } >&2
  exit 1
fi
say "connecté : $ADMIN_EMAIL"

# ── Les cinq profils partenaires ───────────────────────────────────────────
#
# CINQ COMPTES POUR DEUX « TYPES ».
#
# Marketplace et food sont les deux seuls types. Les trois autres comptes sont
# des cas de MULTIPLICITÉ : deux boutiques, deux établissements, et le compte
# mixte. Ce sont eux qui éprouvent le sélecteur d'activité de HBA Partner, que
# les deux premiers ne sollicitent jamais.

step "1/5 — Vendeur marketplace"
T=$(register_and_login Awa Koffi vendeur.market@hba.local "+22997000001")
S=$(create_seller "$T" "Awa Électronique")
create_store "$T" "$S" "Awa Électronique — Cotonou" "+22997000001" "Cotonou" 6.3667 2.4333 >/dev/null
say "vendeur.market@hba.local — 1 boutique"

step "2/5 — Vendeur food"
T=$(register_and_login Kossi Adjovi vendeur.food@hba.local "+22997000002")
# UN DOSSIER VENDEUR SANS BOUTIQUE. Ce n'est pas une contradiction : le
# dossier est le VÉHICULE DE PAIEMENT du restaurant, pas une échoppe. Sans lui,
# `Restaurant.SubmitForApproval` refuse — « encaisser sans pouvoir reverser ».
S=$(create_seller "$T" "Chez Kossi")
R=$(create_restaurant "$T" "Chez Kossi" "+22997000002" "$S" "Cotonou" 6.3712 2.4180)
if [ -n "$R" ]; then create_menu "$T" "$R" "Poulet braisé" 3500 "Poisson grillé" 4000; fi
say "vendeur.food@hba.local — 1 restaurant"

step "3/5 — Vendeur marketplace ET food"
T=$(register_and_login Fatou Diallo vendeur.mixte@hba.local "+22997000003")
S=$(create_seller "$T" "Fatou Commerce")
create_store "$T" "$S" "Fatou Commerce — Calavi" "+22997000003" "Abomey-Calavi" 6.4489 2.3556 >/dev/null
R=$(create_restaurant "$T" "Le Comptoir de Fatou" "+22997000003" "$S" "Abomey-Calavi" 6.4501 2.3570)
if [ -n "$R" ]; then create_menu "$T" "$R" "Riz au gras" 2500 "Attiéké poisson" 3000; fi
say "vendeur.mixte@hba.local — 1 boutique + 1 restaurant"

step "4/5 — Vendeur à DEUX boutiques"
T=$(register_and_login Ibrahim Sow vendeur.2boutiques@hba.local "+22997000004")
S=$(create_seller "$T" "Sow Distribution")
create_store "$T" "$S" "Sow Distribution — Akpakpa" "+22997000004" "Cotonou" 6.3600 2.4500 >/dev/null
create_store "$T" "$S" "Sow Distribution — Godomey" "+22997000014" "Abomey-Calavi" 6.3900 2.3300 >/dev/null
say "vendeur.2boutiques@hba.local — 2 boutiques sous un même vendeur"

step "5/5 — Restaurateur à DEUX établissements (un seul est représentable)"
T=$(register_and_login Marie Hounkpe vendeur.2restos@hba.local "+22997000005")
S=$(create_seller "$T" "Maquis Marie")
R=$(create_restaurant "$T" "Maquis Marie — Centre" "+22997000005" "$S" "Porto-Novo" 6.4969 2.6289)
if [ -n "$R" ]; then create_menu "$T" "$R" "Pâte rouge" 2000 "Igname pilée" 2800; fi
say "vendeur.2restos@hba.local — 1er établissement"

# ═══════════════════════════════════════════════════════════════════════════
# LE SECOND ÉTABLISSEMENT NE PEUT TOUJOURS PAS ÊTRE CRÉÉ, ET CE N'EST PAS
# UNE ROUTE QUI MANQUE.
#
# Le refus est posé à QUATRE endroits indépendants, dont deux irréversibles :
#
#   1. `FoodConfigurations` : `HasIndex(r => r.OwnerUserId).IsUnique()` — un
#      index UNIQUE en base. Le lever demande une migration ;
#   2. `RegisterRestaurantCommand` : conflit explicite « ce compte a déjà un
#      établissement » ;
#   3. `IFoodModuleApi.GetStaffMembershipAsync` rend UNE appartenance
#      (`FirstOrDefault`). La faire rendre une LISTE change une interface que
#      `FoodGrpcClient` implémente aussi — donc une RPC de plus dans le proto,
#      et tous ses appelants à reprendre ;
#   4. `GetMyRestaurantQuery` rend UN `PartnerRestaurantView`, que le BFF
#      Restaurant consomme au singulier pour bâtir son tableau de bord.
#
# CE QUI EST DÉJÀ PRÊT, EN REVANCHE, C'EST L'AUTORISATION.
#
# Chaque route partenaire porte déjà `{restaurantId}` dans son chemin, et
# `DenyUnlessStaffAsync` compare `membership.RestaurantId != restaurantId`. Le
# jour où l'appartenance devient une liste, cette garde devient « l'une de mes
# appartenances vise-t-elle cet établissement ? » — une ligne. Le travail n'est
# pas dans les routes, il est dans le MODÈLE DE PERSONNEL et dans le contrat
# gRPC qui l'expose.
#
# On ne le force donc pas ici : un jeu de développement n'a pas à justifier une
# migration de schéma et un changement de contrat inter-services.
# ═══════════════════════════════════════════════════════════════════════════
say "second établissement NON créé : index unique sur Restaurant.OwnerUserId,"
say "  appartenance de personnel unique, et contrat partenaire au singulier."

# ── Cinq livreurs ──────────────────────────────────────────────────────────
step "Livreurs"
vehicles=(Motorcycle Motorcycle Bicycle Car Motorcycle)
for i in 1 2 3 4 5; do
  T=$(register_and_login "Livreur$i" Hba "livreur$i@hba.local" "+2299710000$i")
  [ -n "$T" ] || continue

  body=$(call "inscription livreur$i" POST /api/delivery/drivers/register "$T" "$(jq -nc \
        --arg n "Livreur$i Hba" --arg p "+2299710000$i" --arg v "${vehicles[$((i-1))]}" \
        '{fullName:$n, phone:$p, vehicle:$v}')" "409") || true

  # ═══════════════════════════════════════════════════════════════════════
  # LA VÉRIFICATION SE REJOUE À CHAQUE PASSAGE, ET C'EST LE CORRECTIF.
  #
  # Elle vivait DANS le `if [ -n "$body" ]`, donc uniquement quand
  # l'inscription venait de réussir. Sur une relance, `register` rend 409,
  # `call` rend un corps vide, et la branche « déjà inscrit » sautait le
  # `verify`. Si la vérification avait échoué au premier passage — delivery-service
  # pas encore levé, jeton admin sans rôle Dispatcher — AUCUNE exécution
  # ultérieure ne la rattrapait. Le livreur restait « PendingVerification »,
  # `Driver.Verify()` n'était jamais atteint, l'événement jamais levé, et le
  # rôle jamais attribué. Le script affichait « déjà inscrit », en vert.
  #
  # `Verify()` est idempotente côté domaine (un compte déjà actif rend
  # succès sans relever l'événement) : la rejouer ne coûte rien.
  #
  # MAIS ELLE NE RATTRAPE PAS UN RÔLE MANQUANT SUR UN COMPTE DÉJÀ ACTIF —
  # justement parce qu'elle ne relève alors aucun événement. Voir la section
  # de conformité, qui le signale et dit quoi faire.
  # ═══════════════════════════════════════════════════════════════════════
  did=$(jq -r '.id // empty' <<<"${body:-}" 2>/dev/null || echo "")

  if [ -z "$did" ]; then
    # Déjà inscrit : on relit son dossier pour retrouver l'identifiant.
    body=$(call "dossier du livreur$i" GET /api/delivery/drivers/me "$T" "" "404") || true
    did=$(jq -r '.id // empty' <<<"${body:-}" 2>/dev/null || echo "")
  fi

  if [ -n "$did" ]; then
    call "vérification du livreur$i" POST "/api/delivery/ops/drivers/$did/verify" "$ADMIN_TOKEN" >/dev/null || true
    say "livreur$i@hba.local — ${vehicles[$((i-1))]}, vérifié"
  else
    warn "  ✗ livreur$i@hba.local — aucun dossier livreur : ni créé, ni relisible."
  fi
done

# ── Cinq clients ───────────────────────────────────────────────────────────
#
# Rien à faire de plus : l'inscription attribue « Buyer ».
#
# ON GARDE LEURS JETONS, PARCE QUE LA CONFORMITÉ LES REDEMANDAIT POUR RIEN.
#
# Elle se reconnectait aux cinq clients trente secondes après les avoir inscrits,
# pour vérifier… qu'ils peuvent se connecter. Cinq appels d'authentification qui
# refaisaient la démonstration qui venait d'avoir lieu — sur un quota de dix par
# minute, c'est une demi-fenêtre dépensée à ne rien apprendre.
JETONS_CLIENTS=()
step "Clients"
for i in 1 2 3 4 5; do
  T=$(register_and_login "Client$i" Hba "client$i@hba.local" "+2299720000$i")
  JETONS_CLIENTS[$i]="$T"
  [ -n "$T" ] && say "client$i@hba.local" || say "✗ client$i@hba.local"
done

# ═══════════════════════════════════════════════════════════════════════════
# CONFORMITÉ — ON RELIT CE QU'ON VIENT D'ÉCRIRE.
#
# UN SCRIPT D'AMORÇAGE QUI NE VÉRIFIE PAS SON PROPRE TRAVAIL NE VAUT PAS
# MIEUX QU'UN `>/dev/null`.
#
# Les sections ci-dessus disent ce qu'elles ont TENTÉ. Celle-ci dit ce qui EST,
# lu par les routes de lecture, avec les mêmes jetons qu'une application réelle.
# C'est le seul endroit du script dont la sortie mérite confiance.
#
# Attendu, compte par compte :
#   • vendeur     → dossier « Active »
#   • boutique    → « Open » (Draft = lieu d'expédition manquant)
#   • restaurant  → « Active » (Draft/PendingApproval = un préalable manque)
#   • livreur     → compte « Active » (PendingVerification = non vérifié)
# ═══════════════════════════════════════════════════════════════════════════
# DEUX COMPTEURS, ET C'EST TOUTE LA DIFFÉRENCE ENTRE « MANQUE » ET « CASSÉ ».
#
# Un seul compteur forcerait à choisir entre deux mensonges : soit le second
# établissement de Marie compte comme une panne, et le script sort en erreur à
# CHAQUE exécution — au bout d'une semaine, plus personne ne lit le code de
# retour ; soit on baisse l'attendu à 1, et le manque disparaît des écrans.
#
# Un manque CONNU et argumenté n'est pas un échec d'amorçage. Il est affiché,
# nommé, et n'empêche pas la sortie zéro.
ECARTS=0
MANQUES_CONNUS=0

# ═══════════════════════════════════════════════════════════════════════════
# LES RÔLES DU JETON — CE QUE L'APPLICATION PARTENAIRE VOIT VRAIMENT.
#
# C'EST LE CONTRÔLE QUI MANQUAIT, ET IL A COÛTÉ UNE JOURNÉE.
#
# La conformité relisait les statuts — vendeur « Active », boutique « Open »,
# livreur « Active » — et déclarait l'amorçage réussi. Tout était vert. Mais
# aucun des cinq vendeurs ne portait le rôle `Seller` : l'événement
# « vendeur inscrit » n'avait jamais atteint identity-service, et personne ne
# regardait de ce côté. L'application vendeur rendait 403 sur un jeu de comptes
# annoncé conforme.
#
# Un statut n'est pas une autorisation. Le dossier vit dans merchant-service,
# le rôle dans identity-service, et RIEN ne garantit que les deux se soient
# parlé — c'est justement tout l'enjeu du pont d'événements.
#
# On lit le jeton lui-même : c'est exactement ce que la passerelle inspectera.
# Le jeton est frais (émis par le `login` ci-dessus), donc un rôle absent ici
# est un rôle absent EN BASE — pas un jeton périmé.
#
# `ClaimTypes.Role` sort du générateur sous le nom court « role », mais la carte
# de correspondance peut être vidée : on accepte aussi l'URI longue. Une claim
# unique est rendue par System.Text.Json comme une chaîne, plusieurs comme un
# tableau — d'où le `[.[]?]` défensif.
# ═══════════════════════════════════════════════════════════════════════════
roles_du_jeton() {
  local jwt="$1" charge

  # Le `tr -d` retire tout blanc : un retour à la ligne fausserait le calcul du
  # remplissage ci-dessous, et la charge serait rendue indécodable.
  charge=$(cut -d. -f2 <<<"$jwt" | tr -d '[:space:]')
  # base64url → base64, et remplissage : sans lui, `base64 -d` refuse la charge.
  charge=$(tr '_-' '/+' <<<"$charge")
  case $(( ${#charge} % 4 )) in 2) charge="$charge==" ;; 3) charge="$charge=" ;; esac

  # `|| true` OBLIGATOIRE : le script tourne sous `set -euo pipefail`. Un
  # jeton tronqué fait échouer `base64 -d`, et l'échec d'une substitution de
  # commande tuerait tout le script — au moment précis où il essaie de DIRE
  # qu'un rôle manque. L'outil de diagnostic ne doit jamais être ce qui casse.
  base64 -d <<<"$charge" 2>/dev/null \
    | jq -r '[(.role // .["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] // empty)]
             | flatten | .[]' 2>/dev/null \
    | sort | tr '\n' ' ' || true
}

# Exige un rôle sur un jeton frais, et DIT quoi faire s'il manque.
exiger_role() {
  local email="$1" jwt="$2" attendu="$3" porte

  porte=$(roles_du_jeton "$jwt") || porte=""

  if [[ " $porte" == *" $attendu "* ]]; then
    return 0
  fi

  warn "  ✗ $email — rôle « $attendu » ABSENT (porte : ${porte:-aucun})."
  warn "    → l'application partenaire rendra 403 sur toutes ses routes."
  warn "    → le pont d'événements n'a pas abouti. Vérifier, dans l'ordre :"
  warn "        docker compose -f docker-compose.dev.yml logs identity-service | grep -i 'rôle'"
  warn "        docker compose -f docker-compose.dev.yml logs merchant-service | grep -i 'outbox'"
  warn "        SELECT type, attempt_count, error FROM sellers.outbox_messages WHERE processed_on_utc IS NULL;"
  ECARTS=$((ECARTS + 1))
  return 1
}

verifier_partenaire() {
  local email="$1" boutiques_attendues="$2" restos_attendus="$3"
  local t body sid statut ouvertes total resume rstatut

  # ═══════════════════════════════════════════════════════════════════════
  # ICI, ON NE RÉUTILISE PAS LE JETON DE L'AMORÇAGE — ET C'EST UN REFUS
  #    RAISONNÉ, PAS UN OUBLI.
  #
  # Économiser cet appel serait tentant : cinq de gagnés sur un quota de dix
  # par minute. Mais un JWT est un INSTANTANÉ. Celui de l'amorçage a été émis
  # AVANT `create_seller` et AVANT la validation de l'établissement, donc avant
  # les événements qui posent `Seller` et `FoodPartner`. Le réutiliser ferait
  # crier « RÔLE SELLER ABSENT » sur cinq comptes parfaitement corrects — cinq
  # faux écarts, dans le seul bloc du script dont la sortie mérite confiance.
  #
  # Un contrôle qui invente des pannes se fait ignorer aussi vite qu'un
  # `>/dev/null`. Ces cinq connexions sont donc payées, à la cadence imposée.
  # (Les CLIENTS, eux, n'ont aucun rôle à porter : leur jeton est réutilisé.)
  # ═══════════════════════════════════════════════════════════════════════
  t=$(login "$email" "$PWD_ALL")
  if [ -z "$t" ]; then
    say "✗ $email — connexion impossible"
    ECARTS=$((ECARTS + 1)); return
  fi

  resume=""

  body=$(call "dossier de $email" GET /api/merchants/me "$t" "" "404") || true
  if [ -n "$body" ]; then
    sid=$(jq -r '.id // empty' <<<"$body")
    statut=$(jq -r '.status // "?"' <<<"$body")
    resume="vendeur $statut"
    [ "$statut" = "Active" ] || ECARTS=$((ECARTS + 1))

    # LE DOSSIER EXISTE : LE RÔLE DEVAIT SUIVRE. Le rôle est posé dès
    # l'INSCRIPTION, pas à l'approbation du KYB — un dossier même « Pending »
    # doit donc déjà porter `Seller`.
    if exiger_role "$email" "$t" "Seller"; then
      resume="$resume, rôle Seller"
    else
      resume="$resume, RÔLE SELLER ABSENT"
    fi
  else
    sid=""; resume="aucun dossier vendeur"
    ECARTS=$((ECARTS + 1))
  fi

  if [ -n "$sid" ]; then
    body=$(call "boutiques de $email" GET "/api/merchants/$sid/stores" "$t") || body="[]"
    total=$(jq -r 'if type=="array" then length else 0 end' <<<"$body")
    ouvertes=$(jq -r 'if type=="array" then ([.[] | select(.status=="Open")] | length) else 0 end' <<<"$body")
    resume="$resume, $ouvertes/$total boutique(s) ouverte(s)"
    [ "$ouvertes" = "$boutiques_attendues" ] || ECARTS=$((ECARTS + 1))
  fi

  body=$(call "établissement de $email" GET /api/food/partner/me "$t" "" "404") || true
  if [ -n "$body" ]; then
    rstatut=$(jq -r '.status // "?"' <<<"$body")
    resume="$resume, restaurant $rstatut"
    [ "$rstatut" = "Active" ] || ECARTS=$((ECARTS + 1))

    # ICI LE RÔLE SUIT L'APPROBATION, pas le dépôt du dossier — contrairement
    # au vendeur. Un établissement encore « PendingApproval » n'a donc pas
    # légitimement le rôle : on ne l'exige que sur un restaurant validé.
    if [ "$rstatut" = "Active" ]; then
      exiger_role "$email" "$t" "FoodPartner" \
        && resume="$resume, rôle FoodPartner" \
        || resume="$resume, RÔLE FOODPARTNER ABSENT"
    fi

    # Au-delà d'un établissement, ce n'est pas l'amorçage qui a échoué : c'est
    # la plateforme qui ne sait pas encore le représenter. Manque connu.
    if [ "$restos_attendus" -gt 1 ]; then
      resume="$resume (1 sur $restos_attendus — multi-établissement non supporté)"
      MANQUES_CONNUS=$((MANQUES_CONNUS + 1))
    fi
  elif [ "$restos_attendus" != "0" ]; then
    resume="$resume, AUCUN restaurant (attendu : $restos_attendus)"
    ECARTS=$((ECARTS + 1))
  fi

  say "$email — $resume"
}

step "Conformité — relecture par les routes de lecture"

verifier_partenaire vendeur.market@hba.local     1 0
verifier_partenaire vendeur.food@hba.local       0 1
verifier_partenaire vendeur.mixte@hba.local      1 1
verifier_partenaire vendeur.2boutiques@hba.local 2 0
# DEUX ATTENDUS, UN SEUL VÉRIFIABLE : l'écart est compté, et c'est voulu. Le
# jour où le multi-établissement arrivera, cette ligne cessera d'elle-même de
# signaler un écart. Baisser l'attendu à 1 ferait disparaître le manque des
# écrans — c'est précisément ce qu'on reproche à `>/dev/null`.
verifier_partenaire vendeur.2restos@hba.local    0 2

# MÊME RAISON QUE POUR LES PARTENAIRES : jeton neuf obligatoire. Celui de
# l'amorçage a été émis avant `drivers/{id}/verify`, donc avant l'événement qui
# attribue `Driver`. Le recycler ferait déclarer le rôle absent sur cinq livreurs
# en parfait état de marche.
for i in 1 2 3 4 5; do
  T=$(login "livreur$i@hba.local" "$PWD_ALL")
  if [ -z "$T" ]; then say "✗ livreur$i@hba.local — connexion impossible"; ECARTS=$((ECARTS + 1)); continue; fi
  BODY=$(call "compte livreur$i" GET /api/delivery/drivers/me "$T" "" "404") || true
  [ -n "$BODY" ] || BODY='{}'
  STATUT=$(jq -r '.accountStatus // "aucun dossier"' <<<"$BODY" 2>/dev/null || echo "aucun dossier")

  # ICI LE RÔLE SUIT LA VÉRIFICATION, et c'est justifié : conduire pour la
  # plateforme suppose des pièces contrôlées. Un compte « PendingVerification »
  # n'a donc pas à porter `Driver` — on n'exige le rôle que sur un compte actif.
  ROLE=""
  if [ "$STATUT" = "Active" ]; then
    exiger_role "livreur$i@hba.local" "$T" "Driver" \
      && ROLE=", rôle Driver" || ROLE=", RÔLE DRIVER ABSENT"
  fi

  say "livreur$i@hba.local — $STATUT$ROLE"
  [ "$STATUT" = "Active" ] || ECARTS=$((ECARTS + 1))
done

# ═══════════════════════════════════════════════════════════════════════════
# LES CLIENTS NE SE RECONNECTENT PLUS, ET LEUR CONTRÔLE Y GAGNE.
#
# La version précédente refaisait cinq `login` pour constater qu'ils
# aboutissaient — déjà prouvé à l'inscription, cinq minutes plus tôt. Cinq
# appels sur un quota de dix par minute, dépensés à réapprendre le connu.
#
# On réutilise donc le jeton déjà obtenu, et on s'en sert pour poser une
# question que le `login` ne posait pas : la PASSERELLE l'accepte-t-elle ?
# `GET /api/users/me` traverse l'authentification JWT puis user-service ; il
# relève du limiteur `read`/`write`, partitionné par `sub` — donc gratuit du
# point de vue du quota `auth`. Un contrôle plus fort pour zéro appel de moins.
#
# 404 TOLÉRÉ, ET SEULEMENT 404. Le profil User naît d'un événement émis à
# l'inscription ; sur une pile qui vient de démarrer, il peut n'être pas encore
# projeté. Ce n'est pas le compte qui manque. Un 401 ou un 502, en revanche,
# comptent comme un écart — exactement comme « connexion impossible » avant.
# ═══════════════════════════════════════════════════════════════════════════
for i in 1 2 3 4 5; do
  T="${JETONS_CLIENTS[$i]:-}"
  if [ -z "$T" ]; then
    say "✗ client$i@hba.local — aucun jeton obtenu à l'inscription"
    ECARTS=$((ECARTS + 1)); continue
  fi

  RC=0
  BODY=$(call "profil de client$i" GET /api/users/me "$T" "" "404") || RC=$?
  case "$RC" in
    0) say "client$i@hba.local — jeton accepté, profil lu" ;;
    2) say "client$i@hba.local — jeton accepté (profil pas encore projeté)" ;;
    *) say "✗ client$i@hba.local — jeton refusé ou profil illisible (voir ✗ ci-dessus)"
       ECARTS=$((ECARTS + 1)) ;;
  esac
done

step "Terminé"
say "mot de passe commun : $PWD_ALL"
say "admin : $ADMIN_EMAIL / $ADMIN_PASSWORD"

if [ "$MANQUES_CONNUS" -gt 0 ]; then
  say "· $MANQUES_CONNUS manque(s) CONNU(S) : multi-établissement non supporté."
  say "  Voir l'encadré « le second établissement ne peut toujours pas être créé »."
fi

if [ "$ECARTS" -eq 0 ]; then
  say "✓ jeu de données CONFORME : tout ce qui a été demandé et qui est"
  say "  représentable aujourd'hui a été relu et vérifié."
  exit 0
fi

# SORTIE NON NULLE : un jeu partiellement semé doit se voir à l'écran ET dans
# le code de retour, sans quoi un enchaînement CI le déclarerait réussi.
say "$ECARTS écart(s) entre l'attendu et le relu — voir les ✗ ci-dessus."
say "  Ce ne sont PAS les manques connus : quelque chose n'a pas été semé."
exit 1
