#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# LA BARRIERE STATIQUE DU DEPOT.
#
# Ce script ne contient PLUS AUCUNE LOGIQUE DE CONTROLE. Les vingt-deux controles
# vivent dans `tools/HBA.Controls` ; il n'est qu'un point d'entree, garde parce
# que `ci.yml` et les habitudes l'appellent par ce nom.
#
# ═══════════════════════════════════════════════════════════════════════════════
# POURQUOI CE FICHIER A ETE VIDE PLUTOT QUE SUPPRIME, ET CE QU'IL A COUTE.
#
# Il orchestrait vingt appels a des scripts Python. Le 28 aout 2026, le commit
# f417fc5 « nettoyage » a supprime `scripts/check-di.py` et LAISSE la ligne qui
# l'appelait. `run` comptait un echec quand la commande echouait, `python3` sur
# un fichier absent echoue, et le script se terminait par `exit "$FAILED"`.
#
# LA BARRIERE NE POUVAIT DONC PLUS RENDRE 0, quel que soit l'etat du depot, et
# l'etape `check-all` de la CI echouait a chaque execution pour une raison
# etrangere au code — noyee au milieu de vingt controles.
#
# Une liste d'appels tenue a la main est exactement ce qui rend ce defaut
# possible. Le lanceur .NET, lui, tient sa liste dans un tableau que le
# compilateur verifie : un controle supprime ne compile plus, il ne se tait pas.
# ═══════════════════════════════════════════════════════════════════════════════
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTIL="$ROOT_DIR/tools/HBA.Controls"

# `dotnet` ABSENT FAIT ECHOUER LA BARRIERE, ET C'EST UN CHANGEMENT ASSUME.
#
# Tant que les controles etaient en Python, un poste sans SDK pouvait encore en
# lancer une partie, et l'absence de `dotnet` ne valait pas un echec. Ce n'est
# plus vrai : sans SDK, AUCUN controle ne tourne. Rendre 0 dans ce cas serait la
# forme la plus pure du vert silencieux — « les 0 controles passent ».
if ! command -v dotnet >/dev/null 2>&1; then
  echo "❌ dotnet introuvable — AUCUN controle n'a tourne."
  echo "   Les vingt-deux controles vivent dans tools/HBA.Controls et exigent"
  echo "   le SDK .NET 9. Sans lui, ce depot ne se construit pas non plus."
  exit 1
fi

exec dotnet run --project "$OUTIL" --verbosity quiet -- "$@"
