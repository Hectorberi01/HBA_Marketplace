#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# LES OUTILS PYTHON DES CONTRÔLES, DANS UN ENVIRONNEMENT DU PROJET.
#
#     ./scripts/preparer-outils.sh
#
# À lancer une fois par poste, et à relancer si un contrôle réclame une
# bibliothèque absente.
#
# ═══════════════════════════════════════════════════════════════════════════════
# POURQUOI UN ENVIRONNEMENT VIRTUEL, ET NON `pip install`.
#
# Le Python de Homebrew refuse les installations globales depuis la PEP 668 :
#
#     error: externally-managed-environment
#
# Trois contournements circulent, et deux sont mauvais.
#
#   • `--break-system-packages` : le message d'erreur lui-même prévient que cela
#     peut casser l'installation Homebrew. Un poste de développement qu'on
#     répare au lieu de développer coûte plus cher que ce qu'il économise.
#
#   • `brew install` : PyYAML et python-hcl2 n'ont pas de formule. Ce conseil ne
#     s'applique pas ici.
#
#   • Un environnement virtuel DU PROJET : les bibliothèques vivent dans
#     `.venv/`, à côté du dépôt, ignorées par Git. Rien de global n'est touché,
#     et deux projets aux exigences différentes cohabitent.
#
# CE QUE CE SCRIPT NE FAIT PAS :
#   - il n'installe ni .NET, ni Docker, ni kustomize ;
#   - il n'épingle aucune version : ces bibliothèques ne servent qu'à LIRE des
#     fichiers du dépôt, et une montée de version qui casserait un contrôle se
#     verrait immédiatement, à l'exécution suivante.
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

VENV="$ROOT_DIR/.venv"

# ═══════════════════════════════════════════════════════════════════════════════
# ON CHERCHE UN INTERPRÉTEUR CAPABLE, AU LIEU D'EXIGER `python3`.
#
# Le `python3` du PATH n'est pas forcément utilisable. Sur ce poste, c'est le
# 3.14 de Homebrew, livré SANS `ensurepip` ni `pyexpat` : `python3 -m venv`
# échoue, et `xml.etree` est inutilisable. Ce n'est pas un cas exotique — une
# formule fraîchement compilée ou un `--HEAD` produit régulièrement ça.
#
# Plutôt que d'exiger de l'utilisateur qu'il répare son Python avant de pouvoir
# lancer un contrôle, on essaie les interpréteurs présents dans l'ordre du plus
# probable. `/usr/bin/python3` — celui d'Apple, livré avec les outils de
# développement Xcode — est le dernier recours et le plus fiable : il porte
# toujours `ensurepip`.
#
# LE CRITÈRE EST « SAIT CRÉER UN ENVIRONNEMENT », PAS « EXISTE ». Un interpréteur
# qui répond à `--version` mais échoue sur `-m venv` ne sert à rien ici, et c'est
# exactement le piège qui a coûté ces deux échecs.
# ═══════════════════════════════════════════════════════════════════════════════
trouver_interpreteur() {
  local candidat essai
  for candidat in python3.13 python3.12 python3.11 python3 /usr/bin/python3; do
    command -v "$candidat" >/dev/null 2>&1 || continue
    # ON ESSAIE POUR DE VRAI, DANS UN DOSSIER JETABLE.
    #
    # Le critère « `import ensurepip` réussit » ne suffit PAS : sur Debian, le
    # module s'importe alors que les roues qu'il doit poser ont été retirées du
    # paquet. L'import passe, `-m venv` échoue. Un critère qui approuve un
    # interpréteur inutilisable ne vaut rien — c'est la même faute que celle
    # qu'on corrige partout ailleurs dans ce dépôt.
    #
    # Créer un environnement d'essai coûte une seconde. Le faire est la seule
    # preuve.
    essai="$(mktemp -d)"
    if "$candidat" -m venv "$essai/v" >/dev/null 2>&1 \
       && [ -x "$essai/v/bin/python3" ] \
       && "$essai/v/bin/python3" -m pip --version >/dev/null 2>&1; then
      rm -rf "$essai"
      echo "$candidat"
      return 0
    fi
    rm -rf "$essai"
  done
  return 1
}

if ! PYTHON_HOTE="$(trouver_interpreteur)"; then
  echo "Aucun interpréteur Python capable de créer un environnement." >&2
  echo >&2
  echo "  Essayés : python3.13, python3.12, python3.11, python3, /usr/bin/python3" >&2
  echo "  Aucun n'a su creer un environnement fonctionnel." >&2
  echo >&2
  echo "  macOS : installer les outils de développement en ligne de commande," >&2
  echo "          qui fournissent /usr/bin/python3 :" >&2
  echo "            xcode-select --install" >&2
  echo "          ou une version stable via Homebrew :" >&2
  echo "            brew install python@3.12" >&2
  echo "  Debian / Ubuntu : sudo apt install python3-venv" >&2
  exit 1
fi

echo "Interpréteur : $PYTHON_HOTE ($("$PYTHON_HOTE" --version 2>&1))"

# Les bibliothèques, et ce que chacune débloque. Une liste sans justification
# devient une liste que personne n'ose élaguer.
BIBLIOTHEQUES=(
  "pyyaml"       # check-k8s, check-infra, check-kafka-topics : lecture de structure
  "python-hcl2"  # check-infra : la partie Terraform
)

if [ ! -d "$VENV" ]; then
  echo "Création de $VENV"
  # ON RATTRAPE L'ÉCHEC, PARCE QUE SA CAUSE N'EST PAS DANS SON MESSAGE.
  #
  # `python3 -m venv` échoue sur les Python où `ensurepip` est absent —
  # Debian et Ubuntu le livrent dans un paquet séparé. Le message brut est :
  #
  #     Error: Command '[.../python3, -m, ensurepip, ...]' returned
  #     non-zero exit status 1.
  #
  # Il nomme la commande, pas ce qui manque, et surtout pas comment y remédier.
  if ! "$PYTHON_HOTE" -m venv "$VENV" 2>/tmp/hba-venv-erreur.txt; then
    echo >&2
    echo "La création de l'environnement a échoué." >&2
    sed 's/^/    /' /tmp/hba-venv-erreur.txt >&2
    echo >&2
    if grep -q "ensurepip" /tmp/hba-venv-erreur.txt 2>/dev/null; then
      echo "  ensurepip manque a cet interpreteur." >&2
      echo "    Debian / Ubuntu : sudo apt install python3-venv" >&2
      echo "    macOS Homebrew  : il est normalement présent ; vérifier que" >&2
      echo "                      python3 est bien celui de Homebrew (which python3)" >&2
    fi
    echo >&2
    echo "  Sans environnement, les contrôles qui dépendent de PyYAML restent" >&2
    echo "  ignorés — le reste de check-all.sh fonctionne." >&2
    # Le dossier partiel gênerait la prochaine tentative.
    [ -d "$VENV" ] && mv "$VENV" "$VENV.echec-$(date +%s)" 2>/dev/null || true
    exit 1
  fi
else
  # « PRÉSENT » NE VEUT PAS DIRE « UTILISABLE ».
  #
  # Une création interrompue laisse un dossier avec un `bin/python3` et sans
  # `pip`. La fois suivante, ce script disait « déjà présent » puis échouait sur
  # `Operation not permitted: .../bin/pip` — un message qui parle de permissions
  # alors que le fichier n'existe simplement pas.
  if [ -x "$VENV/bin/python3" ] && "$VENV/bin/python3" -m pip --version >/dev/null 2>&1; then
    echo "Environnement déjà présent : $VENV"
  else
    echo "Environnement présent mais incomplet — il est écarté et refait."
    mv "$VENV" "$VENV.incomplet-$(date +%s)"
    "$PYTHON_HOTE" -m venv "$VENV"
  fi
fi

# ═══════════════════════════════════════════════════════════════════════════════
# CHAQUE ÉCHEC DE `pip` EST RAPPORTÉ AVEC CE QUE `pip` A DIT.
#
# La première version laissait `set -e` sortir du script sur un `pip` en échec,
# et `--quiet` avait avalé le message. L'appelant lisait « La préparation a
# échoué (voir ci-dessus) » avec RIEN au-dessus — le pire message possible : il
# affirme qu'une explication existe, et il n'y en a pas.
#
# Les causes réelles sont variées et toutes muettes sous `--quiet` : pas de
# réseau, un miroir d'entreprise inaccessible, un certificat refusé, une roue
# incompatible avec l'architecture. Aucune ne se devine.
# ═══════════════════════════════════════════════════════════════════════════════
executer_pip() {
  local etiquette="$1"; shift
  local journal
  journal="$(mktemp)"
  if "$VENV/bin/python3" -m pip "$@" >"$journal" 2>&1; then
    rm -f "$journal"
    return 0
  fi
  echo >&2
  echo "Échec : ${etiquette}" >&2
  echo "  Commande : pip $*" >&2
  echo "  Ce que pip répond :" >&2
  sed 's/^/    /' "$journal" >&2
  echo >&2
  if grep -qiE "network|timed out|temporary failure|resolve|SSLError|CERTIFICATE" "$journal"; then
    echo "  Cela ressemble à un problème d'accès réseau ou de certificat." >&2
    echo "    Derrière un proxy : poser HTTPS_PROXY, ou pip config set global.proxy" >&2
  fi
  rm -f "$journal"
  return 1
}

# LA MISE À JOUR DE `pip` N'EST PAS FATALE, ET C'EST DÉLIBÉRÉ.
#
# Elle est confortable, pas nécessaire : les deux bibliothèques s'installent
# très bien avec le pip livré par `venv`. Or elle échoue dans des cas qui n'ont
# rien à voir avec le but — un système de fichiers qui refuse de remplacer
# `bin/pip`, un miroir qui ne sert pas la dernière version.
#
# Faire échouer toute la préparation pour ça arrêterait un déploiement à cause
# d'un confort.
if ! executer_pip "mise à jour de pip" install --upgrade pip; then
  echo "  La mise à jour de pip a échoué — on continue avec la version livrée." >&2
  echo >&2
fi
echo "Installation : ${BIBLIOTHEQUES[*]}"
executer_pip "installation des bibliothèques" install "${BIBLIOTHEQUES[@]}" || exit 1

# ON VÉRIFIE CE QU'ON VIENT D'INSTALLER, PAS CE QU'ON A DEMANDÉ.
#
# `pip install` peut réussir et poser une roue incompatible avec l'interpréteur.
# L'import est la seule preuve.
echo
echo "Vérification :"
manques=0
for module in yaml hcl2; do
  if "$VENV/bin/python3" -c "import ${module}" 2>/dev/null; then
    version="$("$VENV/bin/python3" -c "import ${module}; print(getattr(${module}, '__version__', 'version inconnue'))" 2>/dev/null || echo "?")"
    printf '    \033[32mok\033[0m      %-10s %s\n' "$module" "$version"
  else
    printf '    \033[31mABSENT\033[0m  %s\n' "$module"
    manques=$((manques + 1))
  fi
done

# `pyexpat` mérite un mot : son absence sur le Python 3.14 de Homebrew a fait
# tomber `check-refs.py` avec une trace de vingt lignes. Ce contrôle ne s'en sert
# plus, mais un autre pourrait vouloir `xml.etree` un jour.
if "$VENV/bin/python3" -c "import pyexpat" 2>/dev/null; then
  printf '    \033[32mok\033[0m      pyexpat\n'
else
  printf '    \033[33mabsent\033[0m  pyexpat — xml.etree ne fonctionnera pas dans cet interpréteur.\n'
  printf '            Aucun contrôle n%s en dépend aujourd%shui.\n' "'" "'"
fi

echo
if [ "$manques" -gt 0 ]; then
  echo "$manques bibliothèque(s) manquante(s) — les contrôles concernés resteront ignorés." >&2
  exit 1
fi

echo "Prêt. ./scripts/check-all.sh emploiera cet environnement automatiquement."
