#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
TOUTE `ProjectReference` DOIT DÉSIGNER UN PROJET QUI EXISTE.

CE QUI A PRODUIT CE CONTRÔLE, LE 28 AOÛT.

`dispatch-service`, `tracking-service` et `proof-of-delivery-service` ont été
retirés du dépôt (D42, D43). L'inventaire de retrait couvrait neuf points — la
solution, le compose, les autorisations gRPC, les topics Kafka, les scripts, les
manifestes — TOUS côté production. Aucun ne regardait `tests/`, précisément parce
qu'un projet de test « n'est déployé nulle part ». C'est ce raisonnement qui a
laissé trois `ProjectReference` mortes dans `HBA.Delivery.UnitTests`.

ET LE SYMPTÔME DÉSIGNE LA MAUVAISE CAUSE. MSBuild rend un AVERTISSEMENT MSB9008
— « le projet référencé n'existe pas » — puis compile quand même, et échoue
ensuite sur les `using` en CS0234 : « le nom d'espace de noms n'existe pas ». On
lit cinq erreurs qui parlent d'espaces de noms, et la ligne qui dit la vraie
cause est un warning au milieu.

POURQUOI LE CONTRÔLE DE SOLUTION NE L'A PAS VU (aujourd'hui `tools/HBA.Controls`,
naguère `check-solution.py`). Il vérifie la cohérence de `HBA.sln`
— que chaque projet listé existe, qu'aucun GUID n'est orphelin. Or ce projet de
test N'EST PAS dans la solution : il n'y avait donc rien à vérifier de son côté.
Les deux contrôles sont complémentaires, et c'est l'espace entre eux qui a laissé
passer le défaut.

CE CONTRÔLE NE REGARDE PAS LA SOLUTION. Il part des `.csproj` du disque — tous,
y compris ceux qu'aucune solution ne référence.
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Dossiers sans intérêt : artefacts de compilation, dépendances, et le cimetière.
IGNORES = ("/obj/", "/bin/", "/node_modules/", "/_to_delete/", "/.git/")


def csproj_du_depot() -> list[str]:
    trouves = []
    for dossier, sous, fichiers in os.walk(RACINE):
        chemin = dossier.replace(os.sep, "/") + "/"
        if any(i in chemin for i in IGNORES):
            sous[:] = []
            continue
        for f in fichiers:
            if f.endswith(".csproj"):
                trouves.append(os.path.join(dossier, f))
    return sorted(trouves)


# ═══════════════════════════════════════════════════════════════════════════════
# ON NE PASSE PLUS PAR UN ANALYSEUR XML, ET C'EST UN RECUL ASSUMÉ.
#
# `xml.etree.ElementTree` a besoin de `pyexpat`. Sur le Python 3.14 de Homebrew,
# ce module est absent : le script mourait sur
#
#     ImportError: No module named expat; use SimpleXMLTreeBuilder instead
#
# — une trace de vingt lignes, au milieu de `check-all.sh`, pour un contrôle qui
# n'a besoin que d'une chose : la valeur de l'attribut `Include` des
# `ProjectReference`. Un contrôle qui ne tourne pas ne contrôle rien, et une
# dépendance à un module optionnel de l'interpréteur est une raison de ne pas
# tourner qu'on ne choisit pas.
#
# CE QUE CE RECUL COÛTE : une expression régulière ne comprend pas le XML. Elle
# lirait un `ProjectReference` placé dans un commentaire, ou dans un
# `ItemGroup` conditionné par un `Condition` faux. Les deux existent en MSBuild.
# En pratique, aucun csproj de ce dépôt n'en contient — et le prix d'un faux
# positif ici est de désigner une référence qui existe, pas d'en manquer une.
# ═══════════════════════════════════════════════════════════════════════════════
INCLUDE = re.compile(
    r"<ProjectReference\b[^>]*?\bInclude\s*=\s*[\"']([^\"']+)[\"']",
    re.IGNORECASE)


def references(csproj: str) -> list[str]:
    """Les chemins bruts des `ProjectReference`, tels qu'écrits dans le fichier."""
    try:
        with open(csproj, encoding="utf-8") as f:
            texte = f.read()
    except OSError as erreur:
        raise RuntimeError(f"illisible : {erreur}") from erreur

    brutes = [m.group(1) for m in INCLUDE.finditer(texte)]
    return brutes


def main() -> int:
    fautes: list[str] = []
    projets = csproj_du_depot()
    total_refs = 0

    for csproj in projets:
        try:
            brutes = references(csproj)
        except RuntimeError as erreur:
            fautes.append(f"{os.path.relpath(csproj, RACINE)} : {erreur}")
            continue

        for brute in brutes:
            total_refs += 1

            # MSBuild écrit ses chemins avec des antislashs, y compris sous Unix.
            relatif = brute.replace("\\", os.sep)
            cible = os.path.normpath(os.path.join(os.path.dirname(csproj), relatif))

            if not os.path.isfile(cible):
                fautes.append(
                    f"{os.path.relpath(csproj, RACINE)}\n"
                    f"       référence {brute}\n"
                    f"       → {os.path.relpath(cible, RACINE)} n'existe pas.\n"
                    f"       MSBuild rendra MSB9008 puis échouera sur les `using` en CS0234 : "
                    f"le message d'erreur désignera des espaces de noms, pas cette ligne.")

    print()
    if fautes:
        print("❌ Références de projet mortes")
        for faute in fautes:
            print(f"     {faute}")
        print()
        print(f"{len(projets)} projet(s), {total_refs} référence(s), "
              f"{len(fautes)} morte(s).")
        return 1

    print(f"{len(projets)} projet(s) examiné(s), {total_refs} référence(s) de projet, "
          f"0 cible manquante.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
