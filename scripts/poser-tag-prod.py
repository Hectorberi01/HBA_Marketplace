#!/usr/bin/env python3
"""Pose le meme tag d'image dans l'overlay de production ET celui des migrations.

═══════════════════════════════════════════════════════════════════════════════
CE QUI ETAIT CASSE : DOUZE COMMANDES A L'ORDRE INVERSABLE.

Le runbook demandait `kustomize edit set image` — six services, deux overlays,
douze invocations, chacune a lancer depuis le bon repertoire et dans le bon
sens. La syntaxe est `<nom dans le manifeste>=<image reelle>:<tag>`, et
l'inverser ne donne pas une erreur claire : `kustomize` ecrit une entree `images`
qui ne correspond a aucun conteneur, le build reussit, et les pods tirent
l'image d'origine. Un patch qui ne designe rien, encore.

Douze occasions de se tromper pour une valeur unique : le tag est le meme
partout, par construction. C'est une seule decision, pas douze.

CE QUE CE SCRIPT NE COUVRE PAS :
  - il ne verifie pas que l'image existe dans le registre. Un tag invente passe
    ici et echoue au tirage — ce qui est le bon echec, mais plus tard.
  - il ne touche pas aux overlays `staging` et `dev` : leurs tags leur
    appartiennent.
  - il ne commite rien.
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import re
import subprocess
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OVERLAYS = [
    os.path.join(RACINE, "k8s", "overlays", "prod", "kustomization.yaml"),
    os.path.join(RACINE, "k8s", "overlays", "migrations-prod", "kustomization.yaml"),
]

PLACEHOLDER = "REMPLACE-PAR-LA-PROMOTION"

# Le §13 interdit `latest` et impose une image immuable : SHA, ou semver.
INTERDITS = {"latest", "main", "master", "dev", "edge", PLACEHOLDER}


def valider(tag):
    if tag in INTERDITS:
        return ("« %s » n'identifie pas une image immuable : le meme nom designera "
                "un autre contenu demain, et un redemarrage de pod tirerait une "
                "version que personne n'a choisie (§13)." % tag)
    if not re.match(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", tag):
        return "« %s » n'est pas un tag d'image valide." % tag
    return None


def main():
    if len(sys.argv) != 2:
        print("usage: poser-tag-prod.py <tag>", file=sys.stderr)
        print("       le tag doit etre immuable : un SHA court ou un semver.",
              file=sys.stderr)
        return 2

    tag = sys.argv[1]
    faute = valider(tag)
    if faute:
        print("REFUS : " + faute, file=sys.stderr)
        return 1

    # Les services reellement deployes : la meme source que les Jobs de migration.
    services_yaml = os.path.join(RACINE, "k8s", "base", "services", "kustomization.yaml")
    vises = set()
    with open(services_yaml, encoding="utf-8") as f:
        for ligne in f:
            if ligne.lstrip().startswith("#"):
                continue
            m = re.match(r"^\s*-\s+([a-z0-9-]+-service)\s*$", ligne)
            if m:
                vises.add("hba/" + m.group(1))
    if not vises:
        print("aucun service deploye lu dans %s — rien n'a ete ecrit" % services_yaml,
              file=sys.stderr)
        return 1
    print("%d service(s) deploye(s) : %s"
          % (len(vises), ", ".join(sorted(s.replace("hba/", "") for s in vises))))

    total = 0
    for chemin in OVERLAYS:
        if not os.path.exists(chemin):
            print("introuvable : %s" % chemin, file=sys.stderr)
            return 1
        with open(chemin, encoding="utf-8") as f:
            contenu = f.read()

        # SEULS LES SERVICES DEPLOYES RECOIVENT LE TAG.
        #
        # L'overlay prod declare quatorze images, six services sont deployes. Poser
        # le tag sur les quatorze effacerait `REMPLACE-PAR-LA-PROMOTION` sur les
        # huit autres — or ce placeholder est precisement ce qui dit « ce service
        # n'a pas ete promu ». Le remplacer par un vrai tag donnerait a un futur
        # lecteur l'impression que tout est pret.
        lignes = contenu.split("\n")
        n = 0
        for i, ligne in enumerate(lignes):
            m = re.match(r"^  - name: (hba/[a-z0-9-]+)\s*$", ligne)
            if not m or m.group(1) not in vises:
                continue
            for j in (i + 1, i + 2):
                if j < len(lignes) and lignes[j].startswith("    newTag:"):
                    lignes[j] = '    newTag: "%s"' % tag
                    n += 1
        nouveau = "\n".join(lignes)
        if n == 0:
            print("aucun newTag dans %s — le format a change, rien n'a ete ecrit"
                  % chemin, file=sys.stderr)
            return 1

        with open(chemin, "w", encoding="utf-8") as f:
            f.write(nouveau)
        print("%-46s %2d tag(s) pose(s)" % (os.path.relpath(chemin, RACINE), n))
        total += n

    print("%d entree(s) au total, tag « %s »." % (total, tag))

    # On relance le controle : c'est lui qui dit si les deux overlays s'accordent.
    controle = os.path.join(RACINE, "scripts", "check-k8s.py")
    if os.path.exists(controle):
        print()
        r = subprocess.run([sys.executable, controle], capture_output=True, text=True)
        for ligne in r.stdout.splitlines():
            if "migration" in ligne.lower() or "❌" in ligne:
                print("   " + ligne.strip())
        print("check-k8s.py : %s" % ("d'accord" if r.returncode == 0
                                     else "DESACCORD (code %d)" % r.returncode))
        return r.returncode
    return 0


if __name__ == "__main__":
    sys.exit(main())
