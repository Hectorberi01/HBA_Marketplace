#!/usr/bin/env python3
# ==============================================================================
# LA LISTE DES SERVICES A PUBLIER — DERIVEE, PUIS CONFRONTEE AU CALQUE PROD.
#
# Employe par `scripts/publier-images.sh`. Rend, une par ligne :
#     <service><TAB><chemin du Dockerfile>
#
# DEUX SOURCES, ET C'EST VOULU.
#
# `ci-affected.py --tous` sait quel Dockerfile appartient a quel service — c'est
# ce que la CI emploie pour sa matrice. `k8s/overlays/prod/kustomization.yaml`
# sait quelles images la production reclame. Les deux listes doivent coincider :
#
#   - une image reclamee par le calque SANS Dockerfile est une anomalie qui
#     arrete tout. Sinon le deploiement laisserait un pod en ImagePullBackOff,
#     sur un « denied » qui se lit comme un probleme de droits sur le registre ;
#   - un Dockerfile HORS du calque n'est pas une erreur : le service existe mais
#     n'est pas dans le lot deploye (notification-service aujourd'hui). Il est
#     nomme sur la sortie d'erreur, et ignore.
#
# CE QUE CE SCRIPT NE COUVRE PAS. Il ne verifie pas qu'un Dockerfile construit,
# ni que l'image obtenue est correcte : seulement que les deux inventaires du
# depot disent la meme chose.
# ==============================================================================

import json
import os
import re
import subprocess
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CALQUE = os.path.join(RACINE, "k8s", "overlays", "prod", "kustomization.yaml")


def main():
    seulement = {s for s in (sys.argv[1] if len(sys.argv) > 1 else "").split(",") if s}

    try:
        matrice = json.loads(subprocess.check_output(
            [sys.executable, os.path.join("scripts", "ci-affected.py"), "--tous"],
            cwd=RACINE, text=True))
    except (subprocess.SubprocessError, ValueError) as e:
        print("ANOMALIE ci-affected.py n'a rien rendu d'exploitable : %s" % e,
              file=sys.stderr)
        return 1

    paires = {e["service"]: e["dockerfile"] for e in matrice}

    if not os.path.exists(CALQUE):
        print("ANOMALIE calque introuvable : %s" % CALQUE, file=sys.stderr)
        return 1

    with open(CALQUE, encoding="utf-8") as f:
        attendues = set(re.findall(r"^  - name: hba/([a-z0-9-]+)$", f.read(), re.M))

    if not attendues:
        print("ANOMALIE aucune image lue dans %s" % CALQUE, file=sys.stderr)
        return 1

    manquantes = sorted(attendues - set(paires))
    if manquantes:
        print("ANOMALIE le calque prod reclame des images sans Dockerfile : %s"
              % ", ".join(manquantes), file=sys.stderr)
        return 1

    for s in sorted(set(paires) - attendues):
        print("  hors du calque prod, ignore : %s" % s, file=sys.stderr)

    inconnus = sorted(seulement - attendues)
    if inconnus:
        print("ANOMALIE --seulement nomme des services absents du calque prod : %s"
              % ", ".join(inconnus), file=sys.stderr)
        return 1

    retenus = attendues if not seulement else (attendues & seulement)
    for s in sorted(retenus):
        print("%s\t%s" % (s, paires[s]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
