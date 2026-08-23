#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
QUELS SERVICES UN CHANGEMENT AFFECTE-T-IL VRAIMENT ?

CE CALCUL EXISTE POUR NE PAS RECONSTRUIRE QUINZE IMAGES À CHAQUE COMMIT.

C'est le seul gain immédiat de la découpe, et un pipeline qui reconstruit tout
l'annule : corriger une faute de frappe dans restaurant-service ne doit pas
republier l'image des paiements.

ET IL EST DÉRIVÉ DU GRAPHE RÉEL, PAS D'UNE LISTE DE CHEMINS.

La tentation est d'écrire, par service, la liste des dossiers qui le concernent.
Cette liste devient fausse au premier `<ProjectReference>` ajouté — et le défaut
est SILENCIEUX dans le mauvais sens : le service n'est pas reconstruit, l'image
publiée reste l'ancienne, et la correction qu'on croit déployée ne l'est pas.

On lit donc les `.csproj`, transitivement, exactement comme `check-dockerfiles.py`.

TROIS FICHIERS AFFECTENT TOUT LE MONDE.

`Directory.Build.props`, `Directory.Packages.props` et la solution changent le
cadre cible ou les versions de paquets de chaque projet. Les traiter comme un
changement ordinaire laisserait passer une montée de version d'EF Core sans
reconstruire quoi que ce soit.

Usage :
    python3 scripts/ci-affected.py origin/main
    python3 scripts/ci-affected.py --tous          # force la liste complète
    python3 scripts/ci-affected.py --liste         # les services connus
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROJECT_REF = re.compile(r'<ProjectReference\s+Include="([^"]+)"')

# Un changement dans l'un de ces fichiers reconstruit tout.
GLOBAUX = ("Directory.Build.props", "Directory.Packages.props", "HBA.sln")

# Dossiers dont un changement ne peut affecter aucune image.
SANS_EFFET = ("docs/", "k8s/", "infra/", "tests/", ".github/", "scripts/", "_to_delete/")


def references(csproj: str) -> list[str]:
    try:
        with open(csproj, encoding="utf-8", errors="ignore") as f:
            contenu = f.read()
    except OSError:
        return []

    return [
        os.path.normpath(os.path.join(os.path.dirname(csproj), inc.replace("\\", "/")))
        for inc in PROJECT_REF.findall(contenu)
    ]


def images() -> dict[str, dict]:
    """
    Les images construites par le dépôt : nom → { dockerfile, dossiers }.

    `dossiers` est l'ensemble des répertoires de projets compilés dans l'image,
    calculé transitivement depuis le `.csproj` de l'hôte.
    """
    trouvees: dict[str, dict] = {}

    for base in ("services", "apps"):
        racine = os.path.join(RACINE, base)
        for dossier, sous, fichiers in os.walk(racine):
            if "Dockerfile" not in fichiers:
                continue
            sous[:] = []                                  # un Dockerfile par service

            nom = os.path.basename(dossier)
            hotes = [
                os.path.join(d, f)
                for d, _, fs in os.walk(dossier)
                for f in fs
                if f.endswith(".Api.csproj") and "/obj/" not in d
            ]
            if not hotes:
                continue

            vus: set[str] = set()
            pile = list(hotes)
            while pile:
                courant = pile.pop()
                if courant in vus or not os.path.isfile(courant):
                    continue
                vus.add(courant)
                pile.extend(references(courant))

            trouvees[nom] = {
                "dockerfile": os.path.relpath(os.path.join(dossier, "Dockerfile"), RACINE),
                "dossiers": sorted(
                    os.path.relpath(os.path.dirname(p), RACINE) + "/" for p in vus),
            }

    return trouvees


def modifies(base: str) -> list[str]:
    rendu = subprocess.run(
        ["git", "diff", "--name-only", f"{base}...HEAD"],
        cwd=RACINE, capture_output=True, text=True, check=False)

    if rendu.returncode != 0:
        # ON RECONSTRUIT TOUT PLUTÔT QUE RIEN.
        #
        # Une base introuvable — premier commit, historique tronqué par un clone
        # peu profond — ne doit pas se traduire par « aucun service affecté ».
        # Ce serait un pipeline vert qui ne publie rien.
        print(f"# base « {base} » injoignable, on reconstruit tout", file=sys.stderr)
        return ["*"]

    return [l for l in rendu.stdout.splitlines() if l.strip()]


def affectes(fichiers: list[str], catalogue: dict[str, dict]) -> list[str]:
    if "*" in fichiers or any(f in GLOBAUX for f in fichiers):
        return sorted(catalogue)

    pertinents = [f for f in fichiers if not f.startswith(SANS_EFFET)]

    touches = set()
    for nom, info in catalogue.items():
        for f in pertinents:
            if any(f.startswith(d) for d in info["dossiers"]):
                touches.add(nom)
                break

    return sorted(touches)


def main() -> int:
    catalogue = images()

    if "--liste" in sys.argv:
        for nom, info in sorted(catalogue.items()):
            print(f"{nom:26} {len(info['dossiers']):2} projet(s)  {info['dockerfile']}")
        return 0

    if "--tous" in sys.argv:
        cibles = sorted(catalogue)
    else:
        base = next((a for a in sys.argv[1:] if not a.startswith("-")), "origin/main")
        cibles = affectes(modifies(base), catalogue)

    # Format attendu par `strategy.matrix` de GitHub Actions.
    sortie = [{"service": n, "dockerfile": catalogue[n]["dockerfile"]} for n in cibles]
    print(json.dumps(sortie))
    return 0


if __name__ == "__main__":
    sys.exit(main())
