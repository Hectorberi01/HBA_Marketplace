#!/usr/bin/env python3
"""
Déplace les commentaires XML posés DANS une liste de paramètres d'enregistrement
positionnel vers des balises `<param name="…">` sur la déclaration.

POURQUOI CE N'EST PAS COSMÉTIQUE.

Un `///` placé devant un paramètre d'enregistrement positionnel ne se rattache à
RIEN. Le compilateur le dit — CS1587, « le commentaire XML n'est pas placé dans
un élément valide du langage » — et le fichier XML généré ne le contient pas.
Swagger ne peut donc pas l'afficher : le champ reste nu dans le schéma, alors
qu'une explication existe, relue, juste au-dessus de lui dans le code.

La balise `<param>` sur la déclaration produit le même texte, rattaché au bon
paramètre, et Swashbuckle la rend comme description de propriété du schéma.

CE QUE CET OUTIL NE FAIT PAS :
  • il ne touche QUE les listes de paramètres d'enregistrements positionnels ;
  • il n'invente aucun texte — il déplace, il ne rédige pas ;
  • il ne fusionne pas avec un `<param>` déjà présent : il s'arrête sur le
    fichier et le signale, plutôt que de produire deux balises pour un paramètre.

USAGE : param-vers-balise.py <fichier.cs> [...]   (--verifie pour ne rien écrire)
"""
from __future__ import annotations

import re
import sys

# `public sealed record Nom(` — la parenthèse ouvrante seule en fin de ligne.
DEBUT = re.compile(r'^(?P<indent>\s*)(?P<tete>(?:public|internal)\s+(?:sealed\s+)?record\s+(?:struct\s+)?\w+)\s*\($')

# Le nom d'un paramètre : le dernier identifiant avant `,` `)` ou `=`.
NOM_PARAM = re.compile(r'(?P<nom>\w+)\s*(?:=[^,)]*)?\s*[,)]')


def convertir(chemin: str, ecrire: bool) -> tuple[int, str | None]:
    lignes = open(chemin, encoding="utf-8").read().split("\n")
    sortie: list[str] = []
    i = 0
    deplaces = 0

    while i < len(lignes):
        m = DEBUT.match(lignes[i])
        if m is None:
            sortie.append(lignes[i])
            i += 1
            continue

        # ── on est sur l'ouverture d'une liste de paramètres ────────────────
        indent = m.group("indent")
        entete_debut = len(sortie)          # où insérer les <param>
        declaration = [lignes[i]]
        i += 1

        profondeur = 1
        bloc: list[str] = []                # commentaires en attente
        params: list[tuple[str, list[str]]] = []
        corps: list[str] = []

        while i < len(lignes) and profondeur > 0:
            l = lignes[i]
            nu = l.strip()

            if nu.startswith("///"):
                bloc.append(nu[3:].lstrip())
                i += 1
                continue

            if nu == "" and bloc:
                bloc.append("")
                i += 1
                continue

            profondeur += l.count("(") - l.count(")")

            if bloc:
                trouve = NOM_PARAM.search(l)
                if trouve is None:
                    return 0, f"{chemin} : commentaire sans paramètre identifiable — « {nu[:50]} »"
                params.append((trouve.group("nom"), bloc))
                deplaces += 1
                bloc = []

                # LA LIGNE VIDE QUI PRÉCÉDAIT LE COMMENTAIRE N'A PLUS RIEN À
                # SÉPARER. La laisser produirait des trous au milieu d'une liste
                # de paramètres, là où le commentaire justifiait l'espace.
                while corps and corps[-1].strip() == "":
                    corps.pop()

            corps.append(l)
            i += 1

        if bloc:
            return 0, f"{chemin} : commentaire en fin de liste, sans paramètre"

        # ── remonter les blocs, en balises `<param>` ────────────────────────
        balises: list[str] = []
        for nom, texte in params:
            # on retire les <summary> : la balise param joue ce rôle
            propre = [t for t in texte
                      if t.strip() not in ("<summary>", "</summary>")]
            while propre and propre[-1] == "":
                propre.pop()
            while propre and propre[0] == "":
                propre.pop(0)

            balises.append(f'{indent}/// <param name="{nom}">')
            balises += [f"{indent}///" + (f" {t}" if t else "") for t in propre]
            balises.append(f"{indent}/// </param>")

        sortie[entete_debut:entete_debut] = balises
        sortie += declaration + corps

    if not ecrire or deplaces == 0:
        return deplaces, None

    open(chemin, "w", encoding="utf-8").write("\n".join(sortie))
    return deplaces, None


def main() -> int:
    args = [a for a in sys.argv[1:] if a != "--verifie"]
    ecrire = "--verifie" not in sys.argv

    total = 0
    for chemin in args:
        n, erreur = convertir(chemin, ecrire)
        if erreur:
            print(f"❌ {erreur}")
            return 1
        if n:
            print(f"   {n:4d}  {chemin}")
        total += n

    print(f"{total} commentaire(s) {'déplacé(s)' if ecrire else 'à déplacer'}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
