#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
FERMETURE TRANSITIVE DES `COPY` — CE QUE CHAQUE IMAGE OUBLIE D'EMBARQUER.

LA RESTAURATION RÉUSSIT, LA COMPILATION TOMBE, ET LE MESSAGE MENT.

Chaque Dockerfile copie `shared` puis les quelques `*.Contracts` d'autres
services dont il a besoin. Le piège : un projet de `shared` peut lui-même
référencer un projet qui vit DANS un service — c'est le cas de tous les clients
gRPC, qui référencent les contrats du service qu'ils appellent.

Personne n'écrit cette dépendance : elle est transitive. Et son absence ne
produit pas d'erreur franche :

    warning MSB9008: the referenced project does not exist
    error CS0234: le namespace 'Contracts' n'existe pas dans 'HBA.Orders'

Le premier n'est qu'un AVERTISSEMENT, noyé dans la sortie. Le second envoie
chercher un problème de namespace dans du code qui compile parfaitement en
local. On a perdu deux constructions là-dessus — commerce-service, puis
communication-service, la seconde causée par l'ajout d'un simple client gRPC.

CE CONTRÔLE COÛTE UNE SECONDE, UNE CONSTRUCTION D'IMAGE EN COÛTE CENT.

Le script part du projet nommé par `dotnet restore`, suit toutes les
`ProjectReference` de proche en proche, et vérifie que chaque projet atteint
tombe bien dans l'un des chemins `COPY` du Dockerfile.

Usage :
    python3 scripts/check-dockerfiles.py
    python3 scripts/check-dockerfiles.py order-service

Sort 1 s'il manque quelque chose : utilisable en CI, et surtout avant un
`dev-up.sh` qui dure une demi-heure.
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
SERVICES = os.path.join(ROOT, 'services')
APPS = os.path.join(ROOT, 'apps')


# Voir l'encadré de `check-di.py` : « src/services » n'existe plus depuis la
# réorganisation, et les services sont désormais rangés par univers. Ce script
# levait un FileNotFoundError, que `check-all.sh` affichait comme un échec de
# contrôle ordinaire.
def images_du_depot():
    """
    Tout ce qui a un Dockerfile : « univers/nom-du-service », puis « apps/nom ».

    `apps/` A ÉTÉ AJOUTÉ APRÈS COUP, ET SON ABSENCE A LAISSÉ PASSER UNE PANNE.

    Le script ne parcourait que `services/`. La passerelle n'ayant longtemps
    dépendu d'aucun projet partagé, cela ne se voyait pas — jusqu'à ce que le
    contrôle de révocation (ISSUE-022) lui donne sa première référence vers
    `shared/`, que son Dockerfile ne copiait pas. `dotnet build` local ne pouvait
    rien en dire : il voit tout le dépôt. Seul un `docker build` l'aurait montré.
    """
    trouves = []

    for univers in sorted(os.listdir(SERVICES)):
        dossier = os.path.join(SERVICES, univers)
        if not os.path.isdir(dossier):
            continue
        for nom in sorted(os.listdir(dossier)):
            if os.path.isdir(os.path.join(dossier, nom)):
                trouves.append((os.path.join(univers, nom), os.path.join(dossier, nom)))

    if os.path.isdir(APPS):
        for nom in sorted(os.listdir(APPS)):
            dossier = os.path.join(APPS, nom)
            if os.path.isdir(dossier):
                trouves.append((os.path.join('apps', nom), dossier))

    return trouves


def demande(service, wanted):
    """L'argument de ligne de commande reste le nom court."""
    return not wanted or service in wanted or os.path.basename(service) in wanted


def project_references(csproj):
    """Chemins absolus des projets référencés par un .csproj."""
    folder = os.path.dirname(csproj)
    try:
        with open(csproj, encoding='utf-8') as handle:
            content = handle.read()
    except OSError:
        return []

    return [
        os.path.normpath(os.path.join(folder, include.replace('\\', '/')))
        for include in re.findall(r'<ProjectReference\s+Include="([^"]+)"', content)
    ]


def sans_arguments(texte):
    """
    Remplace les `ARG NOM=valeur` par leur valeur dans le reste du fichier.

    SANS CELA, UN DOCKERFILE PARAMÉTRÉ EST LU COMME S'IL NE COPIAIT RIEN.

    `apps/api-gateway/Dockerfile` écrit `COPY ${BFF}/src/...` et
    `dotnet restore ${BFF}/src/...`. Comparer ces chaînes telles quelles à des
    chemins du dépôt ne rend jamais vrai : le contrôle passerait en annonçant zéro
    problème sur un fichier qu'il n'a pas compris. Un contrôle qui se tait à tort
    est pire que pas de contrôle.
    """
    for nom, valeur in re.findall(r'^ARG\s+(\w+)=(\S+)', texte, re.M):
        texte = texte.replace('${%s}' % nom, valeur).replace('$%s' % nom, valeur)

    return texte


def copied_paths(dockerfile_text):
    """Sources des `COPY`, hors `COPY --from=…` qui recopie une étape."""
    return [
        match.group(1)
        for match in re.finditer(r'^COPY\s+(\S+)\s+\S+\s*$', dockerfile_text, re.M)
        if not match.group(1).startswith('--from')
    ]


def check(dossier):
    dockerfile = os.path.join(dossier, 'Dockerfile')
    if not os.path.isfile(dockerfile):
        return []

    with open(dockerfile, encoding='utf-8') as handle:
        text = sans_arguments(handle.read())

    copies = copied_paths(text)

    entry = re.search(r'dotnet restore (\S+)', text)
    if not entry:
        return [('—', 'aucun `dotnet restore` trouvé dans le Dockerfile')]

    stack = [os.path.normpath(os.path.join(ROOT, entry.group(1)))]
    seen = set()
    missing = []

    while stack:
        project = stack.pop()
        if project in seen:
            continue
        seen.add(project)

        relative = os.path.relpath(project, ROOT)

        # Le projet est-il dans l'un des chemins copiés ?
        inside = any(
            relative == copy or relative.startswith(copy.rstrip('/') + '/')
            for copy in copies)

        if not inside:
            missing.append((relative, 'non copié par le Dockerfile'))
            continue

        if not os.path.isfile(project):
            missing.append((relative, 'référencé mais absent du dépôt'))
            continue

        stack.extend(project_references(project))

    return missing


def main():
    wanted = sys.argv[1:]

    total = 0
    checked = 0

    for etiquette, dossier in images_du_depot():
        if not demande(etiquette, wanted):
            continue
        if not os.path.isfile(os.path.join(dossier, 'Dockerfile')):
            continue
        problems = check(dossier)
        checked += 1
        if not problems:
            continue
        print('❌ %s' % etiquette)
        for relative, reason in sorted(set(problems)):
            total += 1
            print('     %s' % relative)
            print('       → %s' % reason)
            print('       COPY %s ./%s' % (os.path.dirname(relative), os.path.dirname(relative)))

    print()
    print('%d Dockerfile(s) vérifié(s), %d projet(s) manquant(s).' % (checked, total))
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
