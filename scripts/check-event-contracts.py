#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
La règle ADDITIVE des contrats d'événements (décision D32).

═══════════════════════════════════════════════════════════════════════════════
CE QUE CE CONTRÔLE EMPÊCHE, ET POURQUOI RIEN D'AUTRE NE LE VOIT.

Un événement d'intégration n'est pas un objet interne : il est sérialisé, écrit
en base dans l'outbox, publié sur Kafka, et relu par d'autres services — parfois
plusieurs minutes plus tard, parfois par une version déployée la semaine passée.

Renommer un de ses champs compile. Le supprimer compile. Ajouter un champ
`required` compile. Et rien ne casse à l'exécution non plus : `JsonSerializer`
lit ce qu'il reconnaît, ignore le reste, et rend un objet aux champs manquants à
`null`. Le gestionnaire s'exécute sur une charge amputée, écrit un effet faux, et
la seule trace est un span vert.

D'où la convention : **on n'ajoute que des champs OPTIONNELS**. Une rupture crée
un NOUVEAU type d'événement — `OrderConfirmedV2` — jamais une version 2 du même.

POURQUOI UN INSTANTANÉ VERSIONNÉ PLUTÔT QU'UNE ANALYSE.

La question n'est pas « à quoi ressemble ce contrat aujourd'hui » — un compilateur
le sait. Elle est « en quoi a-t-il changé depuis la dernière fois ». Cela demande
une mémoire, et cette mémoire doit être relue en revue : c'est le fichier
`docs/contrats-evenements.json`, versionné avec le code qu'il décrit.

Une modification légitime met à jour l'instantané dans le MÊME commit, et le
relecteur voit exactement ce qui a bougé. C'est le point : rendre la rupture
visible, pas l'interdire.
═══════════════════════════════════════════════════════════════════════════════

Usage :
    python3 scripts/check-event-contracts.py              # vérifie
    python3 scripts/check-event-contracts.py --accepter   # met l'instantané à jour
"""

import io
import json
import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
INSTANTANE = os.path.join(RACINE, 'docs', 'contrats-evenements.json')
IGNORES = ('obj', 'bin', '.git', '_to_delete', 'node_modules', 'tests')

# `public required Guid SellerId { get; init; }` — le `required` est ce qui compte.
PROPRIETE = re.compile(
    r'^\s*public\s+(?P<required>required\s+)?(?P<type>[\w<>?\[\],. ]+?)\s+(?P<nom>\w+)\s*\{\s*get;',
    re.M)


def evenements():
    """Rend {nom d'événement: {propriété: type}}, plus l'ensemble des requises."""
    trouves = {}
    requis = {}

    for dossier, sous, fichiers in os.walk(RACINE):
        sous[:] = [d for d in sous if d not in IGNORES]
        for fichier in fichiers:
            if not fichier.endswith('.cs'):
                continue

            chemin = os.path.join(dossier, fichier)
            try:
                source = io.open(chemin, encoding='utf-8').read()
            except OSError:
                continue

            for debut in re.finditer(r'public sealed record (\w+IntegrationEvent)\b', source):
                nom = debut.group(1)

                # Le corps du record, jusqu'à l'accolade fermante de premier niveau.
                reste = source[debut.end():]
                ouvrante = reste.find('{')
                if ouvrante < 0:
                    trouves.setdefault(nom, {})
                    requis.setdefault(nom, [])
                    continue

                profondeur = 0
                fin = len(reste)
                for i, c in enumerate(reste[ouvrante:], ouvrante):
                    if c == '{':
                        profondeur += 1
                    elif c == '}':
                        profondeur -= 1
                        if profondeur == 0:
                            fin = i
                            break

                corps = reste[ouvrante:fin]
                corps = re.sub(r'///[^\n]*', '', corps)
                corps = re.sub(r'//[^\n]*', '', corps)

                champs = {}
                obligatoires = []
                for m in PROPRIETE.finditer(corps):
                    champs[m.group('nom')] = m.group('type').strip()
                    if m.group('required'):
                        obligatoires.append(m.group('nom'))

                trouves[nom] = champs
                requis[nom] = sorted(obligatoires)

    return trouves, requis


def charger():
    if not os.path.exists(INSTANTANE):
        return None
    with io.open(INSTANTANE, encoding='utf-8') as handle:
        return json.load(handle)


def ecrire(actuels, requis):
    contenu = {
        '_lire': ("Instantané des contrats d'événements d'intégration. Tenu par "
                  "scripts/check-event-contracts.py. La convention est ADDITIVE (D32) : on n'ajoute "
                  "que des champs optionnels ; une rupture crée un NOUVEAU type d'événement. "
                  "Mettre ce fichier à jour dans le même commit que le changement, pour que le "
                  "relecteur voie exactement ce qui a bougé."),
        'evenements': {
            nom: {'champs': actuels[nom], 'requis': requis[nom]}
            for nom in sorted(actuels)
        },
    }
    os.makedirs(os.path.dirname(INSTANTANE), exist_ok=True)
    with io.open(INSTANTANE, 'w', encoding='utf-8') as handle:
        json.dump(contenu, handle, ensure_ascii=False, indent=2, sort_keys=False)
        handle.write('\n')


def main():
    accepter = '--accepter' in sys.argv[1:]
    actuels, requis = evenements()

    if accepter:
        ecrire(actuels, requis)
        print('Instantané mis à jour : %d événement(s).' % len(actuels))
        return 0

    reference = charger()
    if reference is None:
        ecrire(actuels, requis)
        print('Instantané absent : créé avec %d événement(s). Relire et committer.' % len(actuels))
        return 0

    connus = reference.get('evenements', {})
    ruptures = []
    ajouts = []

    for nom, attendu in sorted(connus.items()):
        if nom not in actuels:
            ruptures.append((nom, 'événement SUPPRIMÉ — ses consommateurs ne recevront plus rien'))
            continue

        champs = actuels[nom]
        for champ, type_attendu in sorted(attendu.get('champs', {}).items()):
            if champ not in champs:
                ruptures.append((
                    nom,
                    'champ « %s » RETIRÉ ou RENOMMÉ — un consommateur déployé le lit encore' % champ))
            elif champs[champ] != type_attendu:
                ruptures.append((
                    nom,
                    'champ « %s » : type passé de « %s » à « %s » — les charges en vol ne se '
                    'désérialiseront pas comme prévu' % (champ, type_attendu, champs[champ])))

        anciens = set(attendu.get('champs', {}))
        for champ in sorted(set(champs) - anciens):
            if champ in requis.get(nom, []):
                ruptures.append((
                    nom,
                    'champ « %s » ajouté en REQUIRED — un producteur déjà déployé ne le remplira '
                    'pas, et la désérialisation échouera' % champ))
            else:
                ajouts.append((nom, champ))

    nouveaux = sorted(set(actuels) - set(connus))

    print()
    print('  ── Ruptures de contrat')
    if ruptures:
        for nom, message in ruptures:
            print('     ❌ %s : %s' % (nom, message))
    else:
        print('     rien à signaler.')

    if ajouts:
        print()
        print('  ── Champs optionnels ajoutés (conformes à la convention)')
        for nom, champ in ajouts:
            print('     ⓘ %s.%s' % (nom, champ))

    if nouveaux:
        print()
        print('  ── Événements nouveaux')
        for nom in nouveaux:
            print('     ⓘ %s' % nom)

    print()
    print('%d événement(s) suivi(s), %d rupture(s) de contrat.' % (len(actuels), len(ruptures)))

    if ruptures:
        print()
        print("Si le changement est VOULU, il ne se glisse pas : `python3 scripts/check-event-contracts.py")
        print("--accepter` met l'instantané à jour, et la revue voit exactement ce qui a bougé.")

    return 1 if ruptures else 0


if __name__ == '__main__':
    sys.exit(main())
