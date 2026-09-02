#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
LES SERVICES QUI PORTENT DES MIGRATIONS, DERIVES DU DEPOT.

    ./scripts/services-a-migrer.py            un nom de service compose par ligne

POURQUOI DERIVER PLUTOT QUE LISTER.

`scripts/deployer.sh` portait cette liste en dur, seize noms recopies a la main.
Une liste ecrite a la main derive : un service ajoute n'y entre pas, un service
qui perd son DbContext y reste. Dans les deux cas rien ne casse — on migre un
service de trop, ou l'on en oublie un, et l'oubli ne se voit qu'au premier appel
qui touche une table absente.

Le critere est celui du code : un appel a `MigrateHbaDatabaseAsync`. C'est la
methode que `DatabaseMigrationExtensions` expose et que chaque hote appelle dans
son `Program`. Aucun service ne peut migrer sans elle.

CE QUE CE SCRIPT NE VERIFIE PAS :

  • que les migrations soient a jour vis-a-vis des entites. `dotnet ef migrations
    add` reste un geste manuel, et rien ici ne le rappelle.
  • l'ORDRE. Chaque service migre SA base ; ceux qui partagent une base
    (food-cart, food-order et restaurant sur hba_food) se suivent sans se gener,
    le verrou consultatif d'EF s'en charge.
═══════════════════════════════════════════════════════════════════════════════
"""
import io
import json
import os
import subprocess
import sys

import yaml

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARQUEUR = "MigrateHbaDatabaseAsync"


def porte_des_migrations(dossier):
    for d, sous, noms in os.walk(dossier):
        sous[:] = [s for s in sous if s not in ("obj", "bin", "_to_delete")]
        for nom in noms:
            if not nom.endswith(".cs"):
                continue
            texte = io.open(os.path.join(d, nom), encoding="utf-8",
                            errors="replace").read()
            if MARQUEUR in texte:
                return True
    return False


def main():
    chemin = os.path.join(RACINE, "docker-compose.prod.yml")
    compose = yaml.safe_load(io.open(chemin, encoding="utf-8").read())

    # ═══════════════════════════════════════════════════════════════════════
    # LE DOSSIER VIENT DE `ci-affected.py`, PLUS DE `build:`.
    #
    # CE SCRIPT S'EST TU LE 1er SEPTEMBRE 2026, ET C'ETAIT MA FAUTE.
    #
    # Il deduisait le dossier de chaque service de son `build:` dans le compose
    # de production. Le jour ou `generer-compose-prod.py` a cesse d'emettre
    # `build:` — les images viennent desormais du registre — ce script n'a plus
    # trouve AUCUN service, et a rendu « aucun service porteur de migrations ».
    #
    # L'ECHEC A ETE FRANC, et c'est la seule bonne nouvelle : le script refuse
    # plutot que de rendre une liste vide qu'un appelant aurait prise pour
    # « rien a migrer ». Sans ce garde, la migration de production aurait ete
    # sautee en silence.
    #
    # La source est maintenant `ci-affected.py --tous`, qui donne service ->
    # Dockerfile : la meme table que la matrice de la CI, que
    # `publier-images.sh` et que le controle des noms d'images du generateur.
    # Le lien avec le compose se fait par le NOM D'IMAGE, seul champ qui reste.
    # ═══════════════════════════════════════════════════════════════════════
    try:
        matrice = json.loads(subprocess.check_output(
            [sys.executable, os.path.join("scripts", "ci-affected.py"), "--tous"],
            cwd=RACINE, text=True))
    except (subprocess.SubprocessError, ValueError, OSError) as e:
        print("impossible de lire ci-affected.py (%s)" % type(e).__name__,
              file=sys.stderr)
        return 1
    dockerfiles = {e["service"]: e["dockerfile"] for e in matrice}

    trouves = []
    for nom, valeur in sorted((compose.get("services") or {}).items()):
        image = str(valeur.get("image") or "")
        if "/" not in image:
            continue                      # redis, kafka, minio : pas de code a nous
        publie = image.split("/")[-1].split(":")[0]
        if publie not in dockerfiles:
            continue                      # rembg : construit sur place, pas de DbContext
        build = {"dockerfile": dockerfiles[publie]}
        # LE DOSSIER DU SERVICE, ET SURTOUT PAS LA RACINE DU DEPOT.
        #
        # CE QUI ETAIT CASSE : ce script lisait `build.dockerfile` et prenait son
        # dossier parent. `rembg` n'a PAS de `dockerfile` — juste un `context` —
        # donc la chaine vide donnait la racine du depot, dont le parcours
        # trouve `MigrateHbaDatabaseAsync` quelque part. `rembg`, un service de
        # detourage d'images en Python, entrait dans la liste des migrations.
        #
        # On prend donc `dockerfile` s'il existe, `context` sinon — et l'on
        # REFUSE la racine, qui ne peut designer aucun service en particulier.
        relatif = os.path.dirname(build.get("dockerfile") or "") or build.get("context") or ""
        dossier = os.path.normpath(os.path.join(RACINE, relatif))
        if dossier == RACINE:
            print("le dossier de %s se resout a la racine du depot : le critere "
                  "y trouverait n'importe quoi" % nom, file=sys.stderr)
            return 1
        if not os.path.isdir(dossier):
            print("dossier introuvable pour %s : %s" % (nom, dossier),
                  file=sys.stderr)
            return 1
        if porte_des_migrations(dossier):
            trouves.append(nom)

    if not trouves:
        print("aucun service porteur de migrations — le critere a-t-il change ?",
              file=sys.stderr)
        return 1

    # identity-service d'abord : c'est lui qui amorce le compte administrateur,
    # et le voir passer en premier dit tout de suite si la base repond.
    if "identity-service" in trouves:
        trouves.remove("identity-service")
        trouves.insert(0, "identity-service")

    print("\n".join(trouves))
    return 0


if __name__ == "__main__":
    sys.exit(main())
