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
  if ! python3 -m venv "$VENV" 2>/tmp/hba-venv-erreur.txt; then
    echo >&2
    echo "La création de l'environnement a échoué." >&2
    sed 's/^/    /' /tmp/hba-venv-erreur.txt >&2
    echo >&2
    if grep -q "ensurepip" /tmp/hba-venv-erreur.txt 2>/dev/null; then
      echo "  `ensurepip` manque à cet interpréteur." >&2
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
    python3 -m venv "$VENV"
  fi
fi

"$VENV/bin/python3" -m pip install --quiet --upgrade pip
echo "Installation : ${BIBLIOTHEQUES[*]}"
"$VENV/bin/python3" -m pip install --quiet "${BIBLIOTHEQUES[@]}"

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
