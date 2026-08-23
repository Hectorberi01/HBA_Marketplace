#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
CE QUE L'EXTRACTION A LAISSÉ DERRIÈRE : LES CONSOMMATEURS D'ÉVÉNEMENTS.

ON A DÉMÉNAGÉ LES MODULES, PAS CE QUI LES RELIAIT.

Dans le monolithe, les ponts entre modules vivaient dans la composition root
(`Marketplace.Api/Integration`) — le seul endroit ayant le droit de connaître
deux mondes à la fois. En extrayant un module vers son service, on emporte son
domaine, son application, sa persistance et ses routes. Le fichier qui le
RELIAIT aux autres reste dans le monolithe.

Le premier cas trouvé : identity-service publiait consciencieusement
`UserRegisteredIntegrationEvent`, et personne ne l'écoutait. Un compte se créait
dans `identity.users`, aucune ligne n'apparaissait dans `users.profiles`.

CETTE PANNE-LÀ EST TOTALEMENT MUETTE.

Un événement sans destinataire ne se plaint pas. Le producteur réussit, le
courtier stocke, et rien ne signale que le fait publié n'a produit aucun effet.
Ni le compilateur, ni les tests unitaires, ni les journaux. On le découvre en
regardant une table vide et en se demandant pourquoi.

CE QUE CE SCRIPT COMPARE, ET SA LIMITE.

Il recense les `IIntegrationEventHandler<X>` de chaque côté et signale les X
consommés dans le monolithe et plus dans HBA. Il ne dit PAS si c'est grave :
beaucoup de ces événements appartiennent à des modules non encore extraits
(Search, Disputes, Shipping, Products), et leur consommateur reviendra avec eux.

C'est une LISTE À TRIER, pas une liste d'erreurs. Sa valeur est de la rendre
visible plutôt que de la laisser se découvrir une table vide à la fois.

ET PENDANT TOUT CE TEMPS, IL BALAYAIT UN DOSSIER QUI N'EXISTE PAS.

Le côté HBA était lu depuis `<dépôt>/src` — le chemin du monolithe, supprimé par
la réorganisation en monorepo. `os.walk` sur un dossier absent ne lève pas, il
n'itère pas : le script comptait « 0 événement consommé dans HBA ». Il en compte
65 depuis la réparation.

Deux conséquences se cachaient derrière ce zéro :
  • si le monolithe avait été présent, TOUS ses événements auraient été déclarés
    « perdus » — cent faux positifs d'un coup ;
  • la détection des noms d'événements AMBIGUS, qui ne dépend que de HBA,
    balayait le même chemin fantôme et ne pouvait rien trouver. Elle était de
    surcroît sautée d'office quand le monolithe manquait, c'est-à-dire toujours.

Les racines viennent maintenant de `scripts/racines_source.py`, qui LÈVE quand un
dossier déclaré manque. `MONOLITHE`, lui, reste volontairement hors dépôt et son
absence reste une information, pas une panne.

Usage :
    python3 scripts/check-event-consumers.py
    python3 scripts/check-event-consumers.py --strict   # sort 1 s'il en reste
═══════════════════════════════════════════════════════════════════════════════
"""

import collections
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import racines_source

HBA = racines_source.RACINE

# CELUI-CI POINTE VOLONTAIREMENT HORS DU DÉPÔT, ET SON ABSENCE EST NORMALE.
#
# C'est l'ancien monolithe de référence, gardé à côté du monorepo. Il n'est pas
# versionné ici et manque la plupart du temps : `travail()` le teste et le dit.
# Ne pas confondre avec les racines du dépôt lui-même, qui, elles, DOIVENT
# exister — voir `racines_source.py` et le commentaire de `consommateurs_hba`.
MONOLITHE = os.path.normpath(os.path.join(HBA, '..', 'src'))

HANDLER = re.compile(r'IIntegrationEventHandler<\s*([A-Za-z]+IntegrationEvent)\s*>')

# Modules dont on SAIT qu'ils n'ont pas encore été extraits. Leurs consommateurs
# reviendront avec eux ; les lister comme des pertes serait du bruit.
NON_EXTRAITS = {
    'Search': ('ProductCreatedIntegrationEvent', 'ProductOfferCreatedIntegrationEvent',
               'ProductOfferPriceChangedIntegrationEvent', 'ProductOfferStatusChangedIntegrationEvent',
               'ReviewRejectedIntegrationEvent'),
    'Disputes': ('DisputeOpenedIntegrationEvent', 'DisputeResolvedIntegrationEvent'),
    'Shipping': ('ShipmentReadyForPickupIntegrationEvent',),
    'Products/Offers': ('ProductStatusChangedIntegrationEvent', 'ProductVariantDeactivatedIntegrationEvent',
                        'StockReplenishedIntegrationEvent', 'StoreOpenedIntegrationEvent',
                        'StoreClosedIntegrationEvent', 'ProductMediaRemovedIntegrationEvent'),
}


def consumers(root):
    """
    Événements consommés sous `root`, et les fichiers qui les consomment.

    RÉSERVÉ AU MONOLITHE. Pour le dépôt HBA, passer par `consommateurs_hba()` :
    cette fonction-ci rend silencieusement un dictionnaire vide sur un chemin
    inexistant, ce qui est acceptable pour une référence externe optionnelle et
    ne l'est pas pour le code qu'on prétend contrôler.
    """
    found = collections.defaultdict(set)
    for folder, _, names in os.walk(root):
        if '/obj/' in folder or '/bin/' in folder:
            continue
        for name in names:
            if not name.endswith('.cs'):
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                for event in HANDLER.findall(handle.read()):
                    found[event].add(name)
    return found


def consommateurs_hba():
    """
    Événements consommés DANS CE DÉPÔT.

    C'EST ICI QUE LE CONTRÔLE NE REGARDAIT RIEN.

    L'appel était `consumers(os.path.join(HBA, 'src'))`. Ce dossier n'existe pas
    dans le monorepo — il vient du monolithe. `os.walk` sur un chemin absent
    n'itère pas et ne lève pas : le script concluait donc que HBA ne consommait
    AUCUN événement, et présentait par conséquent TOUS les événements du
    monolithe comme des consommateurs perdus… sauf que le monolithe étant lui
    aussi absent, il s'arrêtait avant, sur « comparaison impossible ». Deux
    silences superposés, et un verdict qui n'avait jamais rien vérifié.

    Même chose pour la détection des noms d'événements ambigus, plus bas : elle
    balayait le même chemin fantôme et ne pouvait rien trouver.
    """
    trouves = collections.defaultdict(set)
    for chemin in racines_source.fichiers_cs():
        with open(chemin, encoding='utf-8', errors='ignore') as handle:
            for event in HANDLER.findall(handle.read()):
                trouves[event].add(os.path.basename(chemin))
    return trouves


DECLARATION = re.compile(r'record ([A-Za-z]+IntegrationEvent)\b')
NAMESPACE = re.compile(r'^namespace ([\w.]+)', re.M)


def doublons():
    """
    UN MÊME ÉVÉNEMENT DÉCLARÉ DANS DEUX ESPACES EST UNE BOMBE À RETARDEMENT.

    L'enveloppe Kafka ne transporte que le NOM court — « order.confirmed ». Le
    consommateur retrouve le type en balayant les assemblies chargées. Si deux y
    répondent, le vainqueur dépendait de l'ordre de chargement.

    Et si un gestionnaire est enregistré pour L'AUTRE type,
    `IIntegrationEventHandler<T>` ne correspond pas, aucun gestionnaire n'est
    trouvé, et l'événement passe SANS EFFET ni erreur.

    La résolution est désormais déterministe et signalée à l'exécution, mais le
    duplicata reste une dette : deux contrats pour un même fait finissent par
    diverger.
    """
    par_nom = collections.defaultdict(set)

    for chemin in racines_source.fichiers_cs():
        with open(chemin, encoding='utf-8', errors='ignore') as handle:
            content = handle.read()
        espace = NAMESPACE.search(content)
        for event in DECLARATION.findall(content):
            par_nom[event].add(espace.group(1) if espace else '?')

    return {nom: espaces for nom, espaces in par_nom.items() if len(espaces) > 1}


def travail():
    """
    LA COMPARAISON EST OPTIONNELLE ; LE RESTE DU CONTRÔLE NE L'EST PAS.

    Le corps du script sortait sur-le-champ quand le monolithe était absent —
    c'est-à-dire presque toujours. Il emportait avec lui la détection des noms
    d'événements AMBIGUS, qui ne dépend que du dépôt HBA et n'avait aucune raison
    d'être conditionnée à la présence d'un dossier externe.

    Désormais : ce qui se vérifie sans le monolithe se vérifie toujours ; seule
    la liste des consommateurs perdus est sautée, et le dit.
    """
    strict = '--strict' in sys.argv

    hba = consommateurs_hba()
    a_traiter = []
    attendus = []

    if os.path.isdir(MONOLITHE):
        mono = consumers(MONOLITHE)
        perdus = sorted(set(mono) - set(hba))

        connus = {event: module
                  for module, events in NON_EXTRAITS.items()
                  for event in events}

        a_traiter = [event for event in perdus if event not in connus]
        attendus = [event for event in perdus if event in connus]

        if a_traiter:
            print('❌ Consommateurs perdus, LES DEUX CÔTÉS ÉTANT EXTRAITS :')
            for event in a_traiter:
                print('     %s' % event)
                for name in sorted(mono[event]):
                    print('         monolithe : %s' % name)
            print()

        if attendus:
            print('· Attendus — le module consommateur n\'est pas encore extrait :')
            par_module = collections.defaultdict(list)
            for event in attendus:
                par_module[connus[event]].append(event)
            for module, events in sorted(par_module.items()):
                print('     %-16s %s' % (module, ', '.join(sorted(events))))
            print()
    else:
        print('· Monolithe introuvable (%s) — comparaison des consommateurs perdus'
              ' sautée.' % MONOLITHE)
        print('  Le reste du contrôle porte sur le dépôt HBA seul et s\'exécute'
              ' normalement.')
        print()

    ambigus = doublons()

    if ambigus:
        print('Événements déclarés dans PLUSIEURS espaces de noms :')
        for nom, espaces in sorted(ambigus.items()):
            print('     %-44s %s' % (nom, ', '.join(sorted(espaces))))
        print('   → L\'enveloppe Kafka ne porte que le nom court. Un gestionnaire')
        print('     enregistré pour l\'un de ces types n\'est jamais appelé si le')
        print('     consommateur résout l\'autre — sans erreur.')
        print()

    print('%d événement(s) consommé(s) dans HBA, %d perte(s) à traiter, %d attendue(s), '
          '%d nom(s) ambigu(s).'
          % (len(hba), len(a_traiter), len(attendus), len(ambigus)))

    return 1 if (strict and a_traiter) else 0


if __name__ == '__main__':
    sys.exit(racines_source.protege(travail))
