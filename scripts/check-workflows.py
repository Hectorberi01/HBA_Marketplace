#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UN WORKFLOW MAL FORMÉ NE SE PLAINT PAS — IL NE TOURNE PAS.

C'EST LA PANNE LA PLUS TRAÎTRE DE TOUTE LA CHAÎNE.

GitHub Actions n'exécute pas un workflow dont le YAML est invalide : aucune
exécution n'apparaît dans l'onglet Actions, aucune notification ne part, aucun
statut ne remonte sur la PR. On croit la CI verte alors qu'elle n'a jamais
démarré — et l'on s'en aperçoit au pire moment, en cherchant pourquoi une
régression est passée.

Le défaut rencontré en écrivant `ci.yml` : un `- name:` contenant « : » sans
guillemets, que YAML lit comme un mapping imbriqué. Le fichier paraît
parfaitement lisible.

Ce que le contrôle vérifie :
  • le YAML se charge ;
  • il y a au moins un job ;
  • chaque `needs` désigne un job qui existe — une faute de frappe y produit un
    job qui n'est JAMAIS exécuté, sans erreur ;
  • chaque étape a un `uses` ou un `run`.

Usage :
    python3 scripts/check-workflows.py
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import glob
import os
import sys

try:
    import yaml
except ImportError:  # pragma: no cover
    print("  PyYAML absent — contrôle ignoré.")
    sys.exit(0)

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))


def main() -> int:
    fichiers = sorted(glob.glob(os.path.join(RACINE, ".github", "workflows", "*.yml")))
    fichiers += sorted(glob.glob(os.path.join(RACINE, ".github", "workflows", "*.yaml")))

    if not fichiers:
        print("  Aucun workflow — rien à vérifier.")
        return 0

    fautes = 0

    for chemin in fichiers:
        court = os.path.relpath(chemin, RACINE)

        try:
            with open(chemin, encoding="utf-8") as f:
                document = yaml.safe_load(f)
        except yaml.YAMLError as erreur:
            premiere = str(erreur).splitlines()[0]
            print(f"  ❌ {court} : YAML invalide — {premiere}")
            print("       GitHub n'exécutera RIEN, et ne le dira nulle part.")
            fautes += 1
            continue

        jobs = (document or {}).get("jobs") or {}

        if not jobs:
            print(f"  ❌ {court} : aucun job")
            fautes += 1
            continue

        for nom, job in jobs.items():
            besoins = job.get("needs") or []
            if isinstance(besoins, str):
                besoins = [besoins]

            for besoin in besoins:
                if besoin not in jobs:
                    print(f"  ❌ {court} : le job « {nom} » dépend de « {besoin} », "
                          f"qui n'existe pas — il ne s'exécuterait jamais")
                    fautes += 1

            for i, etape in enumerate(job.get("steps") or []):
                if "uses" not in etape and "run" not in etape:
                    titre = etape.get("name", f"étape {i}")
                    print(f"  ❌ {court} : « {nom} / {titre} » n'a ni `uses` ni `run`")
                    fautes += 1

    if fautes:
        print(f"  {len(fichiers)} workflow(s), {fautes} défaut(s).")
        return 1

    print(f"  {len(fichiers)} workflow(s) valide(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
