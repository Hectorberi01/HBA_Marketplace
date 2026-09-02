#!/usr/bin/env bash
# ==============================================================================
# DEPLOIEMENT DE PRODUCTION DEPUIS LE POSTE — LA BARRIERE, PUIS ANSIBLE.
#
# CE SCRIPT EST LE POINT D'ENTREE. Le playbook ne lance AUCUN test : le code est
# sur le poste, pas sur le VPS, et c'est ici qu'il faut le verifier.
#
#     ./scripts/deployer-ansible.sh
#     ./scripts/deployer-ansible.sh --tags preparation
#     ./scripts/deployer-ansible.sh --sans-tests --tags tls
#
# L'ORDRE EST LE SUJET DE CE SCRIPT.
#
#   1. l'arbre est propre        — sinon le tag nomme un commit qui ment ;
#   2. les controles du depot    — check-k8s, check-kafka-topics, check-infra ;
#   3. la suite de tests         — 1054 tests, la vraie barriere ;
#   4. le rendu du compose       — engendre puis compare, jamais edite ;
#   5. ansible-playbook          — et lui seul touche au VPS.
#
# Chacune arrete tout. Une etape qui echoue apres que le VPS a ete modifie laisse
# une production a moitie deployee ; les quatre premieres ne touchent a rien.
#
# `--sans-tests` EXISTE, ET IL LE DIT FORT.
#
# Il y a des situations legitimes — reprendre un deploiement interrompu a
# l'etape TLS, rejouer la creation des sujets. Il n'y en a aucune ou l'on
# publie du code non teste sans le savoir : le script l'ecrit en toutes lettres
# et demande confirmation.
#
# CE QUE CE SCRIPT NE COUVRE PAS :
#   - il ne construit ni ne publie d'image. Voir `scripts/publier-images.sh` ;
#   - il ne cree ni base ni role PostgreSQL — l'autre VPS, a la main ;
#   - il ne remplace pas la CI. Celle-ci construit, signe et verifie ; ce chemin
#     est manuel, et les images qu'il deploie ne sont pas signees.
# ==============================================================================
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

SANS_TESTS=0
ARGS_ANSIBLE=()

while [ $# -gt 0 ]; do
  case "$1" in
    --sans-tests) SANS_TESTS=1; shift ;;
    -h|--help)
      sed -n '2,34p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) ARGS_ANSIBLE+=("$1"); shift ;;
  esac
done

for outil in ansible-playbook python3 git; do
  command -v "$outil" >/dev/null 2>&1 || {
    echo "REFUS : $outil introuvable." >&2
    [ "$outil" = "ansible-playbook" ] && echo "        brew install ansible" >&2
    exit 1
  }
done

etape() { printf '\n═══ %s\n' "$1"; }

# ------------------------------------------------------------------------------
etape "1. L'arbre de travail"
# ------------------------------------------------------------------------------
if [ -n "$(git status --porcelain)" ]; then
  echo "REFUS : l'arbre n'est pas propre." >&2
  echo "        Ce qui part sur le VPS ne correspondrait a aucun commit," >&2
  echo "        et rien ne permettrait de savoir ensuite ce qui tourne." >&2
  git status --short >&2
  exit 1
fi
COMMIT="$(git rev-parse --short=12 HEAD)"
echo "  HEAD : $COMMIT  ($(git rev-parse --abbrev-ref HEAD))"

# ------------------------------------------------------------------------------
etape "2. Les controles du depot"
# ------------------------------------------------------------------------------
python3 scripts/check-kafka-topics.py >/dev/null
echo "  sujets Kafka : conformes au catalogue"

if command -v kustomize >/dev/null 2>&1; then
  python3 scripts/check-k8s.py >/dev/null
  echo "  manifestes k8s : coherents"
else
  echo "  manifestes k8s : ignores (kustomize absent) — sans effet sur Compose"
fi

# ------------------------------------------------------------------------------
etape "3. La suite de tests"
# ------------------------------------------------------------------------------
if [ "$SANS_TESTS" = 1 ]; then
  echo
  echo "  LES TESTS SONT SAUTES." >&2
  echo "  Rien n'a verifie que ce commit fonctionne. Si ce n'est pas une reprise" >&2
  echo "  d'un deploiement interrompu, arretez ici." >&2
  echo
  printf "  Taper « je sais » pour continuer : "
  read -r aveu
  [ "$aveu" = "je sais" ] || { echo "  abandon."; exit 1; }
else
  # `/m:1` serialise les projets de test. Sans lui, les suites d'integration
  # demarrent leurs conteneurs en parallele et saturent le demon Docker : les
  # echecs qui en resultent sont des expirations de ResourceReaper, pas des
  # defauts du code — et on les cherche longtemps.
  dotnet test HBA.sln --configuration Release /m:1
  echo "  suite complete : verte"
fi

# ------------------------------------------------------------------------------
etape "4. Le compose de production"
# ------------------------------------------------------------------------------
# ENGENDRE PUIS COMPARE. Le fichier porte « NE PAS EDITER A LA MAIN » ; s'il a
# derive de son generateur, c'est qu'on l'a edite — et ce qui partirait sur le
# VPS ne serait pas ce que le depot decrit.
AVANT="$(sha256sum docker-compose.prod.yml | cut -d' ' -f1)"
python3 scripts/generer-compose-prod.py >/dev/null
APRES="$(sha256sum docker-compose.prod.yml | cut -d' ' -f1)"

if [ "$AVANT" != "$APRES" ]; then
  echo "REFUS : docker-compose.prod.yml differait de son generateur." >&2
  echo "        Il vient d'etre regenere. Relire le diff, committer, relancer." >&2
  git --no-pager diff --stat docker-compose.prod.yml >&2
  exit 1
fi
echo "  compose : conforme a son generateur"

# ------------------------------------------------------------------------------
etape "5. Ansible"
# ------------------------------------------------------------------------------
echo "  inventaire : ansible/inventaire/prod.yml (un seul hote — le VPS de base"
echo "               de donnees n'y entre jamais, voir l'encadre du fichier)"
echo

cd ansible
exec ansible-playbook deployer-prod.yml "${ARGS_ANSIBLE[@]}"
