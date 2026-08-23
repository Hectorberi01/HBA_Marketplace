#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
OÙ VIT LE CODE DE CE DÉPÔT — ET POURQUOI CETTE CONSTANTE A DÛ ÊTRE ÉCRITE.

DEUX CONTRÔLES ONT RENDU « RIEN À SIGNALER » PENDANT TOUTE LEUR VIE.

`check-grpc-stubs.py` et `check-event-consumers.py` balayaient `<dépôt>/src`.
Ce dossier n'a JAMAIS existé dans le monorepo : les deux scripts viennent du
monolithe, où tout le C# tenait sous `src/`. Ici le code vit sous `services/`,
`shared/` et `apps/`.

`os.walk` sur un dossier absent ne lève pas. Il ne produit simplement aucune
itération. Le contrôle affichait donc « 0 client concerné, 0 méthode
bouchonnée » — et ce zéro se lisait comme « tout va bien » alors qu'il voulait
dire « je n'ai rien regardé ». Derrière, deux clients gRPC entièrement
bouchonnés : la marchandise retournée n'était jamais remise en stock, et aucune
course de retour n'était jamais créée.

C'est le TROISIÈME script atteint du même défaut : `check-di.py` levait un
FileNotFoundError sur `src/services`, `check-config-and-guards.py` concluait
qu'aucune section de configuration n'était déclarée, `check-usings.py`
imprimait « 0 fichiers C# analysés ». Chacun a été réparé dans son coin, avec
sa propre constante. La quatrième fois, il n'y aura plus de coin à réparer.

CE QUE CE MODULE GARANTIT, ET CE QU'IL NE GARANTIT PAS.

Il garantit qu'un balayage de dossier ABSENT s'arrête et le DIT. Il ne garantit
pas qu'un balayage présent regarde la bonne chose : un motif regex trop étroit
rendra toujours zéro, et ce zéro-là reste indiscernable d'un dépôt sain. Le
silence qu'on ferme ici est celui du chemin, pas celui du critère.

Il ne couvre pas non plus les chemins délibérément HORS dépôt — le monolithe de
référence de `check-event-consumers.py` par exemple, qui est absent la plupart
du temps et dont l'absence est une information, pas une panne. Ceux-là se
gardent à la main avec `os.path.isdir`.

Usage :
    import racines_source

    for chemin in racines_source.fichiers_cs():
        ...

    # Dans main(), pour transformer l'exception en verdict lisible :
    return racines_source.protege(mon_travail)
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))

# CES TROIS-LÀ, ET PAS « src ». Voir l'en-tête : le dossier `src/` du
# monolithe n'existe pas ici. Si une quatrième racine de code apparaît un jour
# (`libs/`, `tools/`…), c'est ICI qu'on l'ajoute, une seule fois.
RACINES_SOURCES = ('services', 'shared', 'apps')

# Artefacts de compilation et fichiers mis de côté : les analyser produirait des
# doublons de tout (les `.cs` générés sous `obj/` recopient les vrais) et des
# alertes sur du code qu'on a justement décidé de retirer.
IGNORES = ('/obj/', '/bin/', '/_to_delete/', '/node_modules/')


class RacineIntrouvable(Exception):
    """
    LEVÉE PLUTÔT QUE RENDRE ZÉRO — C'EST TOUT L'INTÉRÊT DU MODULE.

    Un contrôle qui ne trouve pas ses fichiers doit s'écrouler bruyamment. Rendre
    une liste vide le fait passer pour un contrôle qui a regardé et n'a rien vu,
    et cette confusion-là a coûté des mois de faux calme.
    """


def dossiers_sources(racine=RACINE, racines=RACINES_SOURCES):
    """
    Chemins absolus des racines de code, VÉRIFIÉS.

    Lève `RacineIntrouvable` dès qu'une racine déclarée manque, en nommant celle
    qui manque et le chemin exact cherché — un message qui se corrige sans avoir
    à ouvrir le script.
    """
    trouves = []
    absents = []

    for nom in racines:
        chemin = os.path.join(racine, nom)
        (trouves if os.path.isdir(chemin) else absents).append(chemin)

    if absents:
        raise RacineIntrouvable(
            'Racine(s) de code introuvable(s) :\n'
            + '\n'.join('     %s' % c for c in absents)
            + '\n\n'
            "Ce contrôle balaie « %s » depuis %s.\n"
            "Il s'ARRÊTE plutôt que de rendre zéro : un balayage sur un chemin\n"
            "inexistant ne lève pas, il n'itère pas — et son compteur à zéro se\n"
            "lit comme « rien à signaler » alors qu'il veut dire « rien regardé ».\n"
            "Si l'arborescence a bougé, corrigez RACINES_SOURCES dans\n"
            "scripts/racines_source.py — une seule fois, pour tous les contrôles."
            % (', '.join(racines), racine))

    return trouves


def fichiers_cs(racine=RACINE, racines=RACINES_SOURCES):
    """
    Tous les `.cs` du dépôt, chemins absolus triés, hors artefacts.

    Lève `RacineIntrouvable` si une racine manque, ET si le balayage complet ne
    rend aucun fichier : un dépôt C# sans un seul `.cs` n'est pas un dépôt sain,
    c'est un balayage qui a raté sa cible.
    """
    trouves = []

    for dossier_racine in dossiers_sources(racine, racines):
        for dossier, sous_dossiers, noms in os.walk(dossier_racine):
            sous_dossiers[:] = [d for d in sous_dossiers
                                if d not in ('obj', 'bin', '.git', 'node_modules', '_to_delete')]
            marque = '/' + dossier.replace(os.sep, '/').strip('/') + '/'
            if any(ignore in marque for ignore in IGNORES):
                continue
            for nom in noms:
                if nom.endswith('.cs'):
                    trouves.append(os.path.join(dossier, nom))

    if not trouves:
        raise RacineIntrouvable(
            'Aucun fichier .cs sous %s (racines : %s).\n\n'
            "Les dossiers existent mais sont vides pour ce contrôle. On refuse de\n"
            "rendre un verdict « rien à signaler » sur zéro fichier lu."
            % (racine, ', '.join(racines)))

    return sorted(trouves)


def relatif(chemin, racine=RACINE):
    """Chemin affichable : relatif au dépôt, séparateurs POSIX."""
    return os.path.relpath(chemin, racine).replace(os.sep, '/')


def protege(travail):
    """
    Exécute `travail()` et convertit `RacineIntrouvable` en ÉCHEC lisible (code 2).

    CODE 2, ET NON 1. `check-all.sh` ne distingue que « passe » de « échoue »,
    mais l'humain qui lit le terminal, lui, doit savoir qu'il ne s'agit pas d'une
    anomalie du code analysé : le contrôle n'a pas pu s'exécuter du tout.
    """
    try:
        return travail()
    except RacineIntrouvable as erreur:
        print()
        print('❌ CE CONTRÔLE N\'A RIEN PU ANALYSER — il échoue au lieu de se taire.')
        print()
        print(erreur)
        return 2


def _autotest():
    """`python3 scripts/racines_source.py` : vérifie que le module voit le dépôt."""
    fichiers = fichiers_cs()
    print('Racines : %s' % ', '.join(relatif(d) for d in dossiers_sources()))
    print('%d fichier(s) .cs visibles depuis %s.' % (len(fichiers), RACINE))
    return 0


if __name__ == '__main__':
    sys.exit(protege(_autotest))
