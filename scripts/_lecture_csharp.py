#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
LE PEU DE C# QUE LES CONTRÔLES DOIVENT SAVOIR LIRE.

CE MODULE EXISTE PARCE QUE DEUX CONTRÔLES ONT BESOIN DU MÊME DÉCOMPTE, ET QUE
    DEUX COPIES AURAIENT DIVERGÉ.

`check-permissions.py` retire les commentaires pour ne pas prendre un code de
permission cité dans un encadré pour une garde réelle. `check-implementations.py`
les retire pour ne pas rater une méthode dont la signature est séparée de son
corps par un commentaire — ce qui lui a valu dix-neuf faux positifs à sa première
exécution, sur du code qui compilait parfaitement.

Le second défaut est la preuve du premier : la même lecture naïve, faite deux
fois, se trompe deux fois. Elle est donc écrite une fois.

CE N'EST PAS UN ANALYSEUR C#, ET IL NE FAUT PAS LE PRENDRE POUR TEL. Il sait
distinguer une chaîne d'un commentaire, et rien de plus. Tout contrôle qui
s'appuie dessus doit dire, dans son propre en-tête, ce que sa lecture ne voit pas.
═══════════════════════════════════════════════════════════════════════════════
"""


def sans_commentaires(source):
    """
    Retire commentaires de ligne et de bloc, en respectant les chaînes.

    UN `str.replace` OU UNE EXPRESSION RÉGULIÈRE NE SUFFIT PAS ICI. Une chaîne
    peut contenir `//` (une URL, un chemin) et un commentaire peut contenir un
    guillemet. Il faut donc suivre l'état du lecteur caractère par caractère.
    Les chaînes sont CONSERVÉES : ce sont elles que l'on cherche.

    Ce qui n'est pas couvert : les chaînes interpolées `$"…{expr}…"` sont
    traitées comme des chaînes ordinaires, donc un `//` à l'intérieur d'une
    interpolation serait pris pour du texte. Aucun code de permission ne s'écrit
    de cette façon, et le faire serait déjà l'anomalie que ce contrôle refuse.
    """
    sortie = []
    i, n = 0, len(source)
    while i < n:
        c = source[i]
        if c == '"':
            # Chaîne textuelle @"…" : le seul échappement est "" .
            verbatim = i > 0 and source[i - 1] == '@'
            sortie.append(c)
            i += 1
            while i < n:
                if verbatim:
                    if source[i] == '"':
                        if i + 1 < n and source[i + 1] == '"':
                            sortie.append('""')
                            i += 2
                            continue
                        break
                else:
                    if source[i] == '\\' and i + 1 < n:
                        sortie.append(source[i:i + 2])
                        i += 2
                        continue
                    if source[i] == '"':
                        break
                    if source[i] == '\n':  # chaîne non terminée : on abandonne l'état
                        break
                sortie.append(source[i])
                i += 1
            if i < n:
                sortie.append(source[i])
                i += 1
            continue
        if c == "'":
            sortie.append(c)
            i += 1
            while i < n and source[i] != "'":
                if source[i] == '\\':
                    sortie.append(source[i])
                    i += 1
                if i < n:
                    sortie.append(source[i])
                    i += 1
            if i < n:
                sortie.append(source[i])
                i += 1
            continue
        if c == '/' and i + 1 < n and source[i + 1] == '/':
            while i < n and source[i] != '\n':
                i += 1
            continue
        if c == '/' and i + 1 < n and source[i + 1] == '*':
            i += 2
            while i + 1 < n and not (source[i] == '*' and source[i + 1] == '/'):
                i += 1
            i += 2
            continue
        sortie.append(c)
        i += 1
    return "".join(sortie)

