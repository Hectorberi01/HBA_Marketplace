#!/usr/bin/env bash
# ==============================================================================
# CE QUE LE FICHIER D'ENVIRONNEMENT DOIT PORTER, VERIFIE AVANT DE PARTIR.
#
#     ./scripts/verifier-env-compose.sh <compose.yml> [fichier.env]
#
# CE QUI ETAIT CASSE : COMPOSE NE NOMME QU'UNE VARIABLE A LA FOIS.
#
# `${VAR:?...}` arrete l'interpolation a la PREMIERE variable manquante. Sur un
# fichier neuf auquel il manque neuf cles, cela fait neuf allers-retours vers un
# VPS, chacun precede d'un envoi de sources. Le message est juste, il est
# simplement servi au compte-gouttes. Ce controle les liste toutes, en une
# passe, sans rien envoyer nulle part.
#
# POURQUOI EN BASH ET NON EN PYTHON. Ce depot ne porte plus de script .py de
# controle ; le portage precedent a supprime celui-ci SANS retirer l'appel dans
# `ansible/deployer-prod.yml`. La tache echouait donc sur « can't open file »,
# et `no_log: true` cachait ce message : le deploiement accusait des variables
# manquantes alors que le verificateur lui-meme etait absent.
#
# IL NE FAUT JAMAIS IMPRIMER UNE VALEUR.
#
# Ce script lit un fichier qui porte tous les mots de passe de production. Il
# n'affiche donc que des NOMS de variables. Aucun chemin de code ne doit rendre
# autre chose — c'est la seule regle qui compte ici, et elle vaut pour toute
# modification future. C'est aussi elle qui autorise `no_log: false` cote
# Ansible, sans quoi l'echec redeviendrait muet.
#
# CE QU'IL NE VERIFIE PAS :
#
#   . que les valeurs soient les BONNES. Un mot de passe present mais faux
#     passe ce controle et echoue a la connexion. DEUX EXCEPTIONS : la cle de
#     protection des secrets, dont la TAILLE est verifiee, et les identites
#     gRPC, dont le FORMAT l'est — voir plus bas.
#   . les variables a valeur par defaut (`${VAR:-...}`) : leur absence est
#     prevue.
#   . que le compose lui-meme soit coherent : on lit ses references, pas sa
#     structure.
# ==============================================================================
set -uo pipefail

# Posee par le workflow dans le fichier lui-meme, apres cette verification.
# La reclamer ici serait une fausse alerte.
FOURNIES_AILLEURS=("HBA_TAG")

# Deux noms pour une seule cle : identity-service signe avec l'une, les autres
# verifient avec l'autre. Une divergence rend 401 partout, sans erreur.
COUPLE_A="AUTHENTICATION__SIGNINGKEY"
COUPLE_B="JWT__SIGNINGKEY"

usage() {
  echo "usage: verifier-env-compose.sh <compose.yml> [fichier.env]" >&2
  exit 2
}

[ $# -ge 1 ] && [ $# -le 2 ] || usage

compose="$1"
[ -f "$compose" ] || { echo "compose introuvable : $compose" >&2; exit 1; }

# Les references OBLIGATOIRES, hors lignes de commentaire.
mapfile -t requises < <(
  grep -v '^[[:space:]]*#' "$compose" \
    | grep -oE '\$\{[A-Za-z_][A-Za-z0-9_]*:\?' \
    | sed -e 's/^\${//' -e 's/:?$//' \
    | sort -u
)

# Retirer celles qui sont fournies ailleurs.
gardees=()
for nom in "${requises[@]}"; do
  ignorer=0
  for fournie in "${FOURNIES_AILLEURS[@]}"; do
    [ "$nom" = "$fournie" ] && ignorer=1
  done
  [ "$ignorer" -eq 0 ] && gardees+=("$nom")
done
requises=("${gardees[@]}")

# ─────────────────────────────────────────────────────────────────────────────
# UN SEUL ARGUMENT : LA LISTE, SANS RIEN COMPARER.
# Utile pour remplir un gabarit ou une interface : sinon la liste se recopie a
# la main depuis le compose, et une variable oubliee ne se voit qu'au demarrage.
# ─────────────────────────────────────────────────────────────────────────────
if [ $# -eq 1 ]; then
  echo "${#requises[@]} variable(s) obligatoire(s) dans $compose :"
  printf '    %s\n' "${requises[@]}"
  echo "  $COUPLE_A et $COUPLE_B doivent porter la MEME valeur."
  exit 0
fi

env_file="$2"
[ -f "$env_file" ] || {
  echo "fichier d'environnement introuvable : $env_file" >&2
  exit 1
}

declare -A valeurs=()
while IFS= read -r ligne || [ -n "$ligne" ]; do
  ligne="${ligne#"${ligne%%[![:space:]]*}"}"
  case "$ligne" in ''|'#'*) continue ;; esac
  case "$ligne" in *=*) ;; *) continue ;; esac
  nom="${ligne%%=*}"
  nom="${nom%"${nom##*[![:space:]]}"}"
  valeurs["$nom"]="${ligne#*=}"
done < "$env_file"

absentes=(); vides=(); dollars=()

for nom in "${requises[@]}"; do
  if [ -z "${valeurs[$nom]+x}" ]; then
    absentes+=("$nom")
  else
    valeur="${valeurs[$nom]}"
    [ -z "${valeur//[[:space:]]/}" ] && vides+=("$nom")
  fi
done

# ─────────────────────────────────────────────────────────────────────────────
# UN `$` DANS UNE VALEUR EST LU PAR COMPOSE COMME UNE REFERENCE.
#
# Compose interpole AUSSI le fichier d'environnement. Un mot de passe contenant
# `$abc` devient une variable nommee `abc`, absente, donc vide :
#
#     WARN The "abc" variable is not set. Defaulting to a blank string.
#
# Le service part alors avec un mot de passe TRONQUE, et echoue a la connexion
# sur une erreur qui parle d'authentification, pas de `$`. L'echappement est
# `$$` : on retire d'abord les paires, ce qui reste est un dollar isole.
# ─────────────────────────────────────────────────────────────────────────────
for nom in "${!valeurs[@]}"; do
  reste="${valeurs[$nom]//\$\$/}"
  case "$reste" in *'$'*) dollars+=("$nom") ;; esac
done

# -----------------------------------------------------------------------------
# LA CLE DE PROTECTION DES SECRETS DOIT FAIRE 32 OCTETS UNE FOIS DECODEE.
#
# CE QUI ETAIT CASSE. `docs/RUNBOOK-PROD.md` et `docs/RUNBOOK-COMPOSE.md` la
# faisaient generer par `openssl rand -hex 32`, qui rend 64 caracteres
# HEXADECIMAUX. L'hexadecimal est un sous-ensemble de l'alphabet base64 et 64
# est un multiple de 4 : le decodage REUSSIT et donne 48 octets. AES-256 en
# exige 32.
#
# Consequence observee en production : les quatorze services demarraient, la
# connexion fonctionnait, et `POST /api/v1/auth/register` rendait un 500 opaque
# — la seule route qui construisait le protecteur. Une variable presente et non
# vide passait ce controle-ci sans un mot.
#
# CE QUE CE CONTROLE NE COUVRE PAS : que ce soit la BONNE cle. Une valeur de 32
# octets differente de celle de notification-service passe ici, et les codes
# partent chiffres avec une cle que le destinataire ne connait pas.
#
# AUCUNE VALEUR N'EST AFFICHEE — seulement un nombre d'octets, ce qui est la
# seule chose a corriger et ne revele rien.
# -----------------------------------------------------------------------------
BASE64_32=("SECURITY__SECRETPROTECTION__KEY")

tailles=()
for nom in "${BASE64_32[@]}"; do
  [ -n "${valeurs[$nom]+x}" ] || continue
  valeur="${valeurs[$nom]}"
  [ -z "${valeur//[[:space:]]/}" ] && continue

  octets=$(printf '%s' "$valeur" | base64 -d 2>/dev/null | wc -c | tr -d ' ')
  [ "${octets:-0}" -eq 32 ] && continue

  if printf '%s' "$valeur" | grep -qiE '^[0-9a-f]{64}$'; then
    tailles+=("$nom : 64 caracteres hexadecimaux, soit ${octets:-0} octets une fois relus en base64 (openssl rand -hex 32 ; il faut openssl rand -base64 32)")
  else
    tailles+=("$nom : ${octets:-0} octet(s) une fois decodee, 32 attendus (openssl rand -base64 32)")
  fi
done

# -----------------------------------------------------------------------------
# LES IDENTITES gRPC DOIVENT ETRE DES CLES, PAS DE L'ALEA.
#
# CE QUI ETAIT CASSE. `scripts/generer-identites-internes.sh` — cite par les
# deux runbooks et par les dix-neuf messages d'erreur du compose — n'existait
# pas. Les cles ont donc ete produites avec la seule commande que les runbooks
# montrent ailleurs : `openssl rand -base64 32`. Cela rend 32 octets
# ALEATOIRES ; `ImportPkcs8PrivateKey` attend une SEQUENCE DER.
#
# Le compose etait satisfait — variable presente et non vide — et le service
# demarrait. `IdentiteInterne.Signer` ne decode la cle qu'au PREMIER appel gRPC
# de l'hote : le defaut se manifestait donc par un 500 opaque sur une route
# appelante, des semaines apres la mise en production, avec
# « ASN1 corrupted data » pour tout indice.
#
# CE QUE CE CONTROLE VERIFIE : que chaque cle privee se decode en base64 ET se
# lise comme une cle EC, et que chaque entree du registre public se lise comme un
# SubjectPublicKeyInfo. C'est openssl qui tranche, pas une heuristique de
# longueur.
#
# IL ACCEPTE LES DEUX ENCODAGES DER, ET C'EST DELIBERE — parce que le service
# accepte les deux. `openssl genpkey -outform DER` rend du SEC1 (121 octets),
# `openssl pkcs8 -topk8 -outform DER` du PKCS#8 (138 octets) ; c'est la meme cle,
# meme secret, meme cle publique.
#
# LES DEUX CONTROLES DOIVENT ACCEPTER EXACTEMENT LA MEME CHOSE, ET ILS ONT DEJA
# DIVERGE. Ce controle-ci utilisait `openssl pkey -inform DER`, qui lit les deux
# formes ; le controle de demarrage du service n'acceptait que le PKCS#8. La CI
# disait donc « toutes renseignees » sur un fichier que le premier conteneur
# refusait au demarrage — un feu vert suivi d'un echec, ce qui est pire qu'un
# feu rouge. Un controle qui ne distingue pas ce que le consommateur distingue
# ne controle rien : c'est le consommateur qui a ete aligne, pas ce script.
#
# CE QU'IL NE COUVRE PAS : que les cles publiques du registre CORRESPONDENT aux
# privees distribuees. Une paire depareillee passe ici et rend Unauthenticated
# en face. Le seul moyen sur est de les engendrer ensemble, ce que le script
# fait.
#
# AUCUNE VALEUR N'EST AFFICHEE — des noms, et le motif du refus.
# -----------------------------------------------------------------------------
cles_illisibles=()

if command -v openssl >/dev/null 2>&1; then
  for nom in "${!valeurs[@]}"; do
    case "$nom" in INTERNAL_KEY_*) ;; *) continue ;; esac
    valeur="${valeurs[$nom]}"
    [ -z "${valeur//[[:space:]]/}" ] && continue

    if ! printf '%s' "$valeur" | openssl base64 -d -A 2>/dev/null \
         | openssl pkey -inform DER -noout >/dev/null 2>&1; then
      cles_illisibles+=("$nom : ni base64 ni cle EC lisible, dans aucun des deux encodages DER (scripts/generer-identites-internes.sh)")
    fi
  done

  registre="${valeurs[INTERNAL_PUBLIC_KEYS]:-}"
  if [ -n "${registre//[[:space:]]/}" ]; then
    entrees=0
    mauvaises=0
    ancien_ifs="$IFS"
    IFS=';'
    for entree in $registre; do
      [ -z "$entree" ] && continue
      entrees=$((entrees + 1))
      corps="${entree#*=}"
      if [ "$corps" = "$entree" ] || ! printf '%s' "$corps" | openssl base64 -d -A 2>/dev/null \
           | openssl pkey -pubin -inform DER -noout >/dev/null 2>&1; then
        mauvaises=$((mauvaises + 1))
      fi
    done
    IFS="$ancien_ifs"
    if [ "$mauvaises" -gt 0 ]; then
      cles_illisibles+=("INTERNAL_PUBLIC_KEYS : $mauvaises entree(s) sur $entrees illisibles (format attendu : nom=base64;nom=base64)")
    fi
  fi
else
  echo "openssl absent : le format des identites gRPC n'a PAS ete verifie." >&2
fi

probleme=0

if [ "${#cles_illisibles[@]}" -gt 0 ]; then
  echo "${#cles_illisibles[@]} identite(s) gRPC au mauvais format — les services refuseront de" >&2
  echo "demarrer (RefuserUneClePriveeIllisible, avant l ouverture des ports) :" >&2
  printf '    %s\n' "${cles_illisibles[@]}" >&2
  echo >&2
  probleme=1
fi

if [ "${#tailles[@]}" -gt 0 ]; then
  echo "${#tailles[@]} cle(s) au mauvais format — le service demarrera et echouera a la premiere inscription :" >&2
  printf '    %s\n' "${tailles[@]}" >&2
  echo >&2
  probleme=1
fi

if [ "${#dollars[@]}" -gt 0 ]; then
  echo "ATTENTION : ${#dollars[@]} valeur(s) contiennent un \$ non echappe —" >&2
  echo "Compose les lira comme des references de variable :" >&2
  printf '    %s\n' "${dollars[@]}" >&2
  echo "    Doubler le dollar : \$ devient \$\$ dans le fichier." >&2
  echo >&2
  probleme=1
fi

if [ -n "${valeurs[$COUPLE_A]+x}" ] && [ -n "${valeurs[$COUPLE_B]+x}" ]; then
  if [ "${valeurs[$COUPLE_A]}" != "${valeurs[$COUPLE_B]}" ]; then
    echo "$COUPLE_A et $COUPLE_B different — elles doivent etre IDENTIQUES." >&2
    probleme=1
  fi
fi

if [ "${#absentes[@]}" -gt 0 ]; then
  echo "${#absentes[@]} variable(s) absente(s) de $env_file :" >&2
  printf '    %s\n' "${absentes[@]}" >&2
  probleme=1
fi

if [ "${#vides[@]}" -gt 0 ]; then
  echo "${#vides[@]} variable(s) presente(s) mais vide(s) :" >&2
  printf '    %s\n' "${vides[@]}" >&2
  probleme=1
fi

[ "$probleme" -eq 0 ] || exit 1

echo "${#requises[@]} variable(s) obligatoire(s), toutes renseignees."
