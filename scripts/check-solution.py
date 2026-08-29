#!/usr/bin/env python3
r"""
═══════════════════════════════════════════════════════════════════════════════
LA SOLUTION RÉFÉRENCE-T-ELLE ENCORE CE QUI EXISTE ?

ÉCRIT PARCE QUE `HBA.sln` A CASSÉ LE BUILD, ET QU'AUCUN CONTRÔLE NE LE VOYAIT.

Le retrait des quatre squelettes food (D30) a supprimé vingt blocs `Project` et
leurs lignes de configuration — mais PAS leurs lignes d'imbrication, restées dans
`GlobalSection(NestedProjects)`. MSBuild s'arrête net :

    HBA.sln(2849): error MSB5023: Un projet avec le GUID « {FBE95272-…} » est
    répertorié comme étant imbriqué sous le projet « {0A6FC087-…} », mais il
    n'existe pas dans la solution.

Zéro fichier C# en cause, zéro erreur de compilation : les quinze contrôles
passaient tous, et rien ne se construisait.

CE QUI REND CE DÉFAUT PARTICULIER : LA VÉRIFICATION S'EST TROMPÉE COMME LE
RETRAIT.

Le retrait cherchait les lignes d'imbrication avec `^\t\{` — UNE tabulation. Elles
en portent DEUX. La vérification écrite dans la foulée utilisait le MÊME motif, a
donc trouvé « zéro orphelin », et a confirmé une suppression qui n'avait pas eu
lieu. Un contrôle qui partage l'hypothèse fausse du code qu'il contrôle ne
contrôle rien.

C'est la troisième fois dans ce dépôt : `check-braces.py` détectait CS1010 et se
taisait, `check-grpc-stubs.py` parcourait un `ROOT/src` inexistant. D'où la règle
appliquée ici : ce script ne suppose AUCUNE indentation (`\s*` partout) et ne
décide de rien à partir d'un motif de mise en forme.

CE QU'IL VÉRIFIE
  1. tout GUID cité en tête de ligne — configuration ou imbrication — est déclaré
     par un bloc `Project` ;
  2. tout GUID PARENT d'une imbrication l'est aussi ;
  3. `Project` / `EndProject` et `GlobalSection` / `EndGlobalSection` s'équilibrent ;
  4. chaque `.csproj` référencé existe sur le disque ;
  5. aucun `.csproj` du dépôt n'est absent de la solution — l'oubli inverse, qui
     ne casse pas le build mais laisse un projet jamais compilé en intégration.

CE QU'IL NE VÉRIFIE PAS : que la solution se construise. Il lit un fichier texte ;
il ne remplace pas `dotnet build`.
═══════════════════════════════════════════════════════════════════════════════
"""
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOLUTION = os.path.join(RACINE, "HBA.sln")

DECLARATION = re.compile(
    r'Project\("\{[0-9A-Fa-f-]+\}"\)\s*=\s*"[^"]*",\s*"([^"]*)",\s*"\{([0-9A-Fa-f-]+)\}"')

# Aucune contrainte d'indentation : c'est précisément l'hypothèse qui a échoué.
A_GAUCHE = re.compile(r"^\s*\{([0-9A-Fa-f-]+)\}\s*(?:\.|=)")
IMBRICATION = re.compile(r"^\s*\{([0-9A-Fa-f-]+)\}\s*=\s*\{([0-9A-Fa-f-]+)\}\s*$")

IGNORES = ("_to_delete", "obj", "bin", "node_modules", ".git")


def projets_du_disque():
    """Tous les .csproj du dépôt, en chemin relatif à la racine."""
    trouves = set()
    for dossier, sous, fichiers in os.walk(RACINE):
        sous[:] = [d for d in sous if d not in IGNORES and not d.startswith(".")]
        for fichier in fichiers:
            if fichier.endswith(".csproj"):
                trouves.add(os.path.relpath(os.path.join(dossier, fichier), RACINE))
    return trouves


def main():
    if not os.path.exists(SOLUTION):
        print("· HBA.sln introuvable — contrôle sauté.")
        return 0

    with open(SOLUTION, encoding="utf-8") as flux:
        texte = flux.read()

    lignes = texte.split("\n")
    declares = {}
    for chemin, guid in DECLARATION.findall(texte):
        declares[guid.upper()] = chemin

    anomalies = []

    for numero, ligne in enumerate(lignes, 1):
        gauche = A_GAUCHE.match(ligne)
        if gauche and gauche.group(1).upper() not in declares:
            anomalies.append(
                (numero, "GUID {%s} cité mais déclaré par aucun bloc Project" % gauche.group(1)))

        nid = IMBRICATION.match(ligne)
        if nid and nid.group(2).upper() not in declares:
            anomalies.append(
                (numero, "imbriqué sous {%s}, qui n'existe pas dans la solution" % nid.group(2)))

    ouverts = len(re.findall(r'^Project\("', texte, re.M))
    fermes = len(re.findall(r"^EndProject\s*$", texte, re.M))
    if ouverts != fermes:
        anomalies.append((0, "%d « Project » pour %d « EndProject »" % (ouverts, fermes)))

    if texte.count("GlobalSection(") != texte.count("EndGlobalSection"):
        anomalies.append((0, "GlobalSection et EndGlobalSection ne s'équilibrent pas"))

    # 4 et 5 : la solution et le disque doivent décrire le même dépôt.
    sur_disque = projets_du_disque()
    references = set()
    for guid, chemin in declares.items():
        if not chemin.endswith(".csproj"):
            continue                      # dossier de solution
        relatif = chemin.replace("\\", os.sep)
        references.add(relatif)
        if not os.path.exists(os.path.join(RACINE, relatif)):
            anomalies.append((0, "référencé mais absent du disque : " + chemin))

    orphelins = sorted(sur_disque - references)

    print()
    print("  %d projet(s) et dossier(s) déclarés, %d .csproj sur le disque."
          % (len(declares), len(sur_disque)))

    if orphelins:
        print()
        print("  ── Sur le disque, absents de la solution")
        print("     (ils ne sont compilés par aucune intégration continue)")
        for chemin in orphelins:
            print("       ⓘ " + chemin)

    print()
    for numero, message in anomalies:
        print("  ❌ " + (("ligne %d — " % numero) if numero else "") + message)

    print("%d anomalie(s) de solution." % len(anomalies))
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
