#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
CLIENTS gRPC : QUELLES MÉTHODES NE CONTACTENT JAMAIS LE SERVEUR ?

UN BOUCHON COMPILE, NE LÈVE PAS, ET MENT.

    public Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken ct = default)
        => Task.FromResult<OfferSummary?>(null);

Cette méthode satisfait l'interface. Le conteneur la résout. L'appelant reçoit
« cette offre n'existe pas » — et conclut que l'offre n'existe pas, alors qu'elle
n'a jamais été demandée à personne.

C'est pire qu'une `NotImplementedException` : celle-là se voit au premier appel.

CE QUE CE CONTRÔLE A TROUVÉ LA PREMIÈRE FOIS.

Sept méthodes bouchonnées dans deux clients. L'une d'elles, `GetOfferAsync`, est
appelée par `AddItemToCartCommandHandler` — c'est-à-dire qu'AUCUN article ne
pouvait entrer dans un panier. Le premier geste du parcours client, avant même
le checkout et le paiement.

Un audit précédent avait conclu « la couche synchrone est saine » parce que
chaque client déclaré avait un serveur en face. Il vérifiait l'existence du
serveur, pas le fait que le client lui parle.

ET ENSUITE, CE CONTRÔLE A MENTI À SON TOUR, PENDANT TOUTE SA VIE.

Il balayait `<dépôt>/src` — le chemin du monolithe. Ce dossier n'existe pas
ici : le code vit sous `services/`, `shared/` et `apps/`. `os.walk` sur un
dossier absent ne lève pas, il n'itère pas. Le contrôle imprimait donc
« 0 client concerné, 0 méthode bouchonnée » à chaque exécution depuis le
premier jour, et ce zéro se lisait comme « tout va bien ».

Ce qu'il taisait : `InventoryGrpcClient.ProcessReturnedStockAsync` rendait
`Task.FromResult(Result.Success())` — la marchandise retournée n'entrait
JAMAIS en stock — et `DeliveryGrpcClient.CreateReturnDeliveryAsync` fabriquait
une chaîne `RET-DELIVERY-{guid}` — aucune course d'enlèvement n'était jamais
créée, et le client recevait un numéro qui ne correspondait à rien.

Les racines de balayage viennent désormais de `scripts/racines_source.py`, qui
LÈVE quand un dossier déclaré manque. Un contrôle qui ne trouve pas ses fichiers
échoue en code 2 au lieu de rendre un zéro rassurant.

LE PIRE BOUCHON N'A PAS DE MÉTHODE BOUCHONNÉE : IL N'A PAS DE CLIENT DU TOUT.

Le critère historique — « le corps ne mentionne pas le champ client » — suppose
qu'un champ client existe. Les deux classes ci-dessus n'en avaient AUCUN : pas
de champ, pas de constructeur, rien que des expressions-corps. C'est le bouchon
le plus complet, donc le plus dangereux, et c'était précisément celui qui
passait entre les mailles.

Une classe `*GrpcClient` sans un seul champ injecté est donc signalée EN TANT
QUE TELLE, avant même l'examen de ses méthodes.

CE QU'IL REGARDE, ET SA LIMITE.

Une méthode publique d'une classe `*GrpcClient` dont le corps ne mentionne aucun
des champs de la classe (ni aucun paramètre de constructeur primaire). Il ne juge
pas la justesse de l'appel, seulement sa présence. Une méthode qui délègue à une
autre méthode du même client est signalée à tort — c'est un faux positif assumé,
préférable au silence.

Il ne dit RIEN de ce qui appelle le bouchon : c'est une liste à trier, et le tri
demande de savoir qui dépend de la méthode. Le garde-fou qui, lui, refuse de
démarrer, se pose dans l'installeur du module (voir `ReturnRefundModuleInstaller`
et `PaymentsModuleInstaller`).

Usage :
    python3 scripts/check-grpc-stubs.py
    python3 scripts/check-grpc-stubs.py --strict   # sort 1 s'il reste un bouchon
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import racines_source

ROOT = racines_source.RACINE

# Corps qui trahissent un bouchon plutôt qu'un appel réseau.
BOUCHONS = ('Task.FromResult', 'NotImplementedException', 'Array.Empty', 'return null;')

CLASSE_CLIENT = re.compile(r'class\s+(\w*GrpcClient)\b')
METHODE = re.compile(r'\n    public (?:async )?(?:override )?Task<?[^\n(]*?>?\s+(\w+)\s*\(')

# Ce dont un client a besoin pour parler à quelqu'un : un champ injecté…
CHAMP = re.compile(r'private\s+(?:readonly\s+)?[^;=(){}\n]+?\s+(\w+)\s*[;=]')
# …ou un paramètre de constructeur primaire (C# 12), qui n'est pas un champ
# déclaré mais se référence exactement pareil dans les corps de méthodes.
CONSTRUCTEUR_PRIMAIRE = re.compile(r'class\s+\w*GrpcClient\s*\(([^)]*)\)')
PARAMETRE = re.compile(r'(\w+)\s*(?:,|$)')


def corps_de(source, depart):
    """Texte jusqu'à la prochaine méthode publique — approximation suffisante."""
    suivant = source.find('\n    public ', depart + 1)
    return source[depart:suivant if suivant > 0 else len(source)]


def collaborateurs(portion):
    """
    Noms référençables depuis les méthodes : champs et paramètres primaires.

    ON NE CHERCHE PLUS LE SEUL NOM `_client`. `OrderGrpcClient` appelle
    `_orders.`, `PaymentGrpcClient` appelle `_payments.` : figer le nom rendait
    le critère faux dès qu'un client était nommé d'après son interlocuteur, et un
    vrai appel passait alors pour un bouchon.
    """
    noms = set(CHAMP.findall(portion))

    primaire = CONSTRUCTEUR_PRIMAIRE.search(portion)
    if primaire and primaire.group(1).strip():
        for morceau in primaire.group(1).split(','):
            mots = re.findall(r'\w+', morceau)
            if mots:
                noms.add(mots[-1])

    return noms


def analyser(chemin):
    """
    Rend `(sans_collaborateur, methodes_bouchonnees)` pour un fichier.

    `sans_collaborateur` est le nom de la classe quand elle n'a AUCUN champ ni
    paramètre injecté — le bouchon intégral. Dans ce cas toutes ses méthodes sont
    listées, qu'elles ressemblent ou non à un bouchon : sans interlocuteur, aucune
    ne peut contacter quoi que ce soit.
    """
    with open(chemin, encoding='utf-8', errors='ignore') as handle:
        source = handle.read()

    premiere = CLASSE_CLIENT.search(source)
    if not premiere:
        return None, []

    # On ne regarde QUE la portion à partir de la première classe cliente : le
    # serveur, dans le même fichier, n'a évidemment pas de champ client.
    portion = source[premiere.start():]
    noms = collaborateurs(portion)

    methodes = [m.group(1) for m in METHODE.finditer(portion)]

    if not noms:
        return premiere.group(1), methodes

    trouves = []
    for m in METHODE.finditer(portion):
        corps = corps_de(portion, m.start())
        if any((nom + '.') in corps for nom in noms):
            continue
        if any(b in corps for b in BOUCHONS):
            trouves.append(m.group(1))

    return None, trouves


def travail():
    strict = '--strict' in sys.argv

    total = 0
    fichiers = 0
    integraux = 0

    for chemin in racines_source.fichiers_cs():
        classe_nue, bouchons = analyser(chemin)
        if not classe_nue and not bouchons:
            continue

        fichiers += 1
        print('%s' % racines_source.relatif(chemin))

        if classe_nue:
            integraux += 1
            print('     %s — BOUCHON INTÉGRAL : aucun champ client, aucun'
                  ' interlocuteur possible.' % classe_nue)

        for methode in bouchons:
            total += 1
            print('     %s — ne contacte jamais le serveur' % methode)

    print()
    print('%d client(s) concerné(s), %d bouchon(s) intégral(aux), %d méthode(s) bouchonnée(s).'
          % (fichiers, integraux, total))

    # POURQUOI CE CONTRÔLE RESTE INFORMATIF PAR DÉFAUT — ET CE QUI A CHANGÉ.
    #
    # Le raisonnement d'origine tient toujours : un bouchon peut être délibéré
    # tant que personne ne l'appelle depuis un autre service, et le script ne sait
    # pas qui appelle quoi. Le faire échouer d'office rendrait `check-all.sh`
    # rouge en permanence, ce qui est la meilleure façon de faire ignorer les
    # quatorze autres contrôles. C'est la LISTE qui compte.
    #
    # DEUX corrections, tout de même :
    #   • `--strict` existe désormais, comme pour check-event-consumers.py, pour
    #     qu'une CI qui le veut puisse bloquer sans qu'on ait à réécrire le script.
    #   • Une racine de balayage introuvable, elle, N'EST PAS informative : elle
    #     sort en code 2 via `racines_source.protege`. C'est exactement le silence
    #     qui a laissé passer les deux bouchons du service return-refund, et il ne
    #     doit plus jamais ressembler à un succès.
    return 1 if (strict and (total or integraux)) else 0


if __name__ == '__main__':
    sys.exit(racines_source.protege(travail))
