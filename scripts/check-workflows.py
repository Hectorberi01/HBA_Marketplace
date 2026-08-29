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


def verifier_scripts_executables(fichiers) -> int:
    """Tout script lancé par `./` dans un workflow doit être exécutable dans Git.

    ═════════════════════════════════════════════════════════════════════════
    CE QUI EST ARRIVÉ.

    `scripts/check-all.sh` était enregistré en `100644`. La CI l'appelle par
    `./scripts/check-all.sh`, et le runner a répondu :

        ./scripts/check-all.sh: Permission denied
        Error: Process completed with exit code 126

    Le message parle de permission, ce qui envoie regarder les droits du runner
    ou ceux du dépôt — alors que la cause est un bit stocké dans l'index Git,
    invisible dans un diff et absent de tout affichage habituel.

    Le mode se perd facilement : un fichier réécrit par un outil, une copie
    depuis un système sans bit d'exécution, un `git add` après un
    `cp` maladroit. Rien ne le signale avant que la CI ne tombe.

    CE QUE CE CONTRÔLE NE COUVRE PAS.

    Il ne regarde que les `run:` qui commencent par `./`. Un script appelé via
    `bash script.sh` n'a pas besoin du bit, et n'est donc pas vérifié — c'est
    d'ailleurs la façon la plus robuste d'écrire un workflow. Il ne vérifie pas
    non plus que le script EXISTE, ni qu'il fonctionne.
    ═════════════════════════════════════════════════════════════════════════
    """
    import re
    import subprocess

    # Le mode vient de l'index Git, pas du système de fichiers : c'est celui-là
    # que le runner reçoit après un checkout.
    modes = {}
    r = subprocess.run(["git", "ls-files", "-s"], capture_output=True, text=True,
                       cwd=RACINE)
    for ligne in r.stdout.splitlines():
        champs = ligne.split("\t", 1)
        if len(champs) != 2:
            continue
        modes[champs[1]] = champs[0].split()[0]

    if not modes:
        print("  ❌ `git ls-files` n'a rien rendu — ce contrôle ne vérifie plus rien")
        return 1

    fautes = 0
    vus = 0
    for chemin in fichiers:
        court = os.path.relpath(chemin, RACINE)
        with open(chemin, encoding="utf-8") as f:
            contenu = f.read()
        for appel in re.findall(r"^\s*(?:-\s*)?run:\s*(\./\S+)", contenu, re.MULTILINE):
            cible = appel.lstrip("./")
            vus += 1
            mode = modes.get(cible)
            if mode is None:
                print(f"  ❌ {court} : lance `{appel}`, que Git ne suit pas")
                fautes += 1
            elif not mode.endswith("755"):
                print(f"  ❌ {court} : lance `{appel}`, enregistré en {mode} — "
                      f"le runner répondra « Permission denied » (code 126).")
                # LE CONSEIL COMPTE AUTANT QUE LE CONSTAT, ET LE PREMIER ÉTAIT
                # FRAGILE.
                #
                # Ce message recommandait `git update-index --chmod=+x`. Ça
                # corrige l'index — et le PROCHAIN `git add` de ce fichier le
                # défait, parce que `git add` relit le mode sur le DISQUE. Le
                # défaut revient alors sans que personne ne comprenne pourquoi.
                print(f"       chmod +x {cible} && git add {cible}")
                fautes += 1

    if vus == 0:
        print("  aucun script appelé par `./` dans les workflows")
    return fautes


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

    fautes += verifier_scripts_executables(fichiers)

    if fautes:
        print(f"  {len(fichiers)} workflow(s), {fautes} défaut(s).")
        return 1

    print(f"  {len(fichiers)} workflow(s) valide(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
