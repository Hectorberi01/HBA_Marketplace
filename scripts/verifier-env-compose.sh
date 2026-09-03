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
#     passe ce controle et echoue a la connexion.
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

probleme=0

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
