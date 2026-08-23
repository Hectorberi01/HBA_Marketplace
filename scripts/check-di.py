#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
DÉPENDANCES INTER-MODULES : QUI RÉCLAME UNE API QUE PERSONNE NE FOURNIT ?

CE QUE LA SÉPARATION EN SERVICES A CASSÉ SANS RIEN DIRE.

Dans le monolithe, `ICartModuleApi`, `IShippingModuleApi`, `IOrderingModuleApi`
étaient enregistrées par leur module, dans le même processus. Un gestionnaire
d'Ordering pouvait injecter l'API de Cart : elle était là.

Après découpage, le gestionnaire est parti dans order-service et l'API est
restée dans commerce-service. Le code compile toujours — l'interface vit dans un
projet `*.Contracts` que les deux référencent. Seul le conteneur s'en aperçoit,
et seulement s'il valide :

    Unable to resolve service for type 'HBA.Commerce.Contracts.ICartModuleApi'
    while attempting to activate 'PlaceOrderCommandHandler'.

ET IL S'EN APERÇOIT PAR PAQUETS.

`ValidateOnBuild` rend une AggregateException : « Inner Exception #1 » veut dire
qu'il y en a d'autres, et la sortie console tronque. On corrige alors une
dépendance, on reconstruit l'image — dix minutes — pour découvrir la suivante.
Ce script les liste TOUTES d'un coup, sans compiler.

CE QU'IL SAIT, ET COMMENT IL LE SAIT.

Il croise trois choses par service :
  • les `private readonly IXxxModuleApi` — l'injection par constructeur ;
  • les `AddScoped<IXxxModuleApi, …>` — la fourniture locale ;
  • les `AddXxxGrpcClient(…)` — la fourniture par le réseau.

La correspondance client gRPC → interface est déclarée ci-dessous plutôt que
devinée : le nom du client ne permet pas de la déduire (`AddMerchantsGrpcClient`
fournit `ISellerModuleApi`, `AddProductsGrpcClient` vit dans le projet Catalog).
Ajouter un client gRPC sans compléter cette table produirait un faux positif.

Ce n'est pas un conteneur : il ne voit ni les enregistrements par réflexion, ni
les fabriques. Il attrape la classe d'erreurs qui nous a coûté trois démarrages.

Usage :
    python3 scripts/check-di.py
    python3 scripts/check-di.py order-service
═══════════════════════════════════════════════════════════════════════════════
"""

import collections
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
SERVICES = os.path.join(ROOT, 'services')


# « services/ », ET SUR DEUX NIVEAUX — CE SCRIPT LEVAIT UN FileNotFoundError.
#
# Il datait du monolithe, où les services étaient à plat sous `src/services/`.
# La réorganisation en monorepo a supprimé `src/` et regroupé les services par
# univers : `services/common/`, `services/marketplace/`, `services/food/`,
# `services/delivery/`. `os.listdir` tombait donc sur un chemin inexistant, et
# `check-all.sh` comptait ce plantage comme un simple « contrôle en échec » —
# indiscernable d'une vraie dépendance non résolue.
def services_relatifs():
    """Chemins relatifs des services, sous la forme « univers/nom-du-service »."""
    trouves = []
    for univers in sorted(os.listdir(SERVICES)):
        dossier = os.path.join(SERVICES, univers)
        if not os.path.isdir(dossier):
            continue
        for nom in sorted(os.listdir(dossier)):
            if os.path.isdir(os.path.join(dossier, nom)):
                trouves.append(os.path.join(univers, nom))
    return trouves


def demande(service, wanted):
    """L'argument de ligne de commande reste le nom court : `check-di.py order-service`."""
    return not wanted or service in wanted or os.path.basename(service) in wanted

# À TENIR À JOUR EN MÊME TEMPS QU'UN NOUVEAU CLIENT gRPC.
GRPC_PROVIDES = {
    'Identity': {'IIdentityModuleApi'},
    'Users': {'IUsersModuleApi'},
    'Media': {'IMediaModuleApi'},
    'Merchants': {'ISellerModuleApi'},
    'Products': {'IProductsModuleApi'},
    'Inventory': {'IInventoryModuleApi'},
    'Ordering': {'IOrderingModuleApi'},
    'Commerce': {'ICartModuleApi'},
    'Food': {'IFoodModuleApi'},
    'Delivery': {'IDeliveryModuleApi', 'IDeliveryDispatchApi'},

    # Le panier et la commande de restauration, séparés de ceux de la
    # marketplace. Le nom de l'API ne se déduit pas de celui du client :
    # `AddFoodOrdersGrpcClient` fournit `IMealOrderModuleApi`, parce que
    # l'agrégat s'appelle `MealOrder` — `FoodOrder` désignant déjà le ticket de
    # cuisine, dans restaurant-service.
    'FoodCarts': {'IFoodCartModuleApi'},
    'FoodOrders': {'IMealOrderModuleApi'},

    # AJOUTÉ AVEC LE BRANCHEMENT DE LA TARIFICATION (ISSUE-033, lot D28).
    #
    # cart-service injecte `IPromotionModuleApi` dans `PromotionPricingModuleApi`
    # et l'obtient par `AddPromotionGrpcClient`. Sans cette entrée, ce script
    # aurait signalé une dépendance non résolue parfaitement satisfaite — un faux
    # positif, et un contrôle qui crie pour rien finit ignoré.
    'Promotion': {'IPromotionModuleApi'},
}

INJECTED = re.compile(r'private readonly (I[A-Za-z]+ModuleApi)\b')
REGISTERED = re.compile(
    r'Add(?:Singleton|Scoped|Transient)<\s*(?:[\w.]*\.)?(I[A-Za-z]+ModuleApi)\s*[,>]')
GRPC_CLIENT = re.compile(r'Add(\w+)GrpcClient\s*\(')
PROJECT_REF = re.compile(r'<ProjectReference\s+Include="([^"]+)"')
INSTALLER_CALL = re.compile(r'new\s+(\w+Installer)\s*\(')
INSTALLER_FILE = re.compile(r'^(\w+Installer)\.cs$')


# ═══════════════════════════════════════════════════════════════════════════════
# L'UNITÉ D'ANALYSE EST LE PROCESSUS, PAS LE DOSSIER.
#
# Un dossier de `services/` n'est pas toujours un service qui tourne. Deux
# groupes sont CO-HÉBERGÉS : `HBA.Financial.Api` embarque payments, billing et
# wallet ; `HBA.Engagement.Api` embarque review, recommendation et wishlist.
#
# Analysé dossier par dossier, wallet-service paraissait réclamer cinq API que
# personne ne fournissait — `IPayoutModuleApi`, `ICommissionModuleApi`,
# `IOrderingModuleApi`, `IFoodModuleApi`, `ISellerModuleApi`. Les cinq sont
# fournies : les deux premières par les installeurs de payments et de billing,
# que le MÊME hôte appelle, les trois autres par les clients gRPC que ce même
# `Program.cs` enregistre. Cinq faux positifs, et un contrôle qui crie pour rien
# finit ignoré — c'est le seul échec qui compte.
#
# Le regroupement se déduit des `<ProjectReference>` de chaque `*.Api`, avec
# DEUX RESTRICTIONS SANS LESQUELLES IL RATISSE TOUT :
#
#   • on ne traverse pas `shared/` — les projets `*.Contracts.Grpc` y référencent
#     les contrats du service qu'ils appellent, ce qui reliait notification-service
#     à neuf autres services ;
#   • on ignore les projets `*.Contracts` — les référencer donne l'INTERFACE,
#     jamais l'enregistrement. Un hôte qui ne référence QUE les contrats d'un
#     module doit précisément être signalé, pas absous.
# ═══════════════════════════════════════════════════════════════════════════════
def project_references(csproj):
    try:
        with open(csproj, encoding='utf-8', errors='ignore') as handle:
            content = handle.read()
    except OSError:
        return []
    return [
        os.path.normpath(os.path.join(os.path.dirname(csproj), inc.replace('\\', '/')))
        for inc in PROJECT_REF.findall(content)
    ]


def dans_le_processus(csproj):
    relatif = os.path.relpath(csproj, ROOT)
    return (relatif.startswith('services' + os.sep)
            and not os.path.basename(csproj).endswith('.Contracts.csproj'))


def hotes():
    """Chaque `*.Api` de `services/`, avec les projets compilés dans son processus."""
    trouves = {}
    for dossier, _, noms in os.walk(SERVICES):
        if '/obj/' in dossier or '/bin/' in dossier:
            continue
        for nom in sorted(noms):
            if not nom.endswith('.Api.csproj'):
                continue
            depart = os.path.join(dossier, nom)
            vus, pile = set(), [depart]
            while pile:
                courant = pile.pop()
                if courant in vus or not os.path.isfile(courant):
                    continue
                if not dans_le_processus(courant):
                    continue
                vus.add(courant)
                pile.extend(project_references(courant))
            relatif = os.path.relpath(depart, SERVICES)
            trouves['/'.join(relatif.split(os.sep)[:2])] = sorted(
                os.path.dirname(p) for p in vus)
    return trouves


# UN INSTALLEUR PRÉSENT N'EST PAS UN INSTALLEUR APPELÉ.
#
# `BillingModuleInstaller.cs` contient `AddScoped<ICommissionModuleApi, …>`. Ce
# fichier est compilé dans l'hôte dès que le projet est référencé — mais rien ne
# s'enregistre tant que `Program.cs` n'écrit pas `new BillingModuleInstaller()`.
#
# Le contrôle a d'abord raté ce cas : en retirant l'appel du `Program.cs`
# financier, il continuait d'annoncer zéro dépendance non résolue, alors que le
# conteneur aurait refusé de démarrer. Les enregistrements portés par un fichier
# `*Installer.cs` ne comptent donc QUE si l'hôte nomme cet installeur.
#
# Limite assumée : un installeur qui en appelle un autre n'est pas suivi.
def scan(projets):
    injected = collections.defaultdict(set)
    provided = set()
    par_installeur = collections.defaultdict(set)
    installeurs_appeles = set()

    for projet in projets:
        for folder, _, names in os.walk(projet):
            if '/obj/' in folder or '/bin/' in folder:
                continue
            for name in names:
                if not name.endswith('.cs'):
                    continue
                with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                    content = handle.read()

                for api in INJECTED.findall(content):
                    injected[api].add(name)
                for client in GRPC_CLIENT.findall(content):
                    provided.update(GRPC_PROVIDES.get(client, set()))
                installeurs_appeles.update(INSTALLER_CALL.findall(content))

                installeur = INSTALLER_FILE.match(name)
                for api in REGISTERED.findall(content):
                    if installeur:
                        par_installeur[installeur.group(1)].add(api)
                    else:
                        provided.add(api)

    for installeur in installeurs_appeles:
        provided.update(par_installeur.get(installeur, set()))

    return {api: files for api, files in injected.items() if api not in provided}


def main():
    wanted = sys.argv[1:]
    total = 0
    checked = 0

    groupes = hotes()
    couverts = set()

    for hote, projets in sorted(groupes.items()):
        for projet in projets:
            couverts.add('/'.join(os.path.relpath(projet, SERVICES).split(os.sep)[:2]))

        if not demande(hote, wanted):
            continue
        checked += 1

        missing = scan(projets)
        if not missing:
            continue

        # Les modules co-hébergés sont nommés : « ❌ payment-service » seul
        # laisserait chercher dans le mauvais dossier une injection qui vit
        # chez wallet.
        modules = sorted({'/'.join(os.path.relpath(p, SERVICES).split(os.sep)[:2])
                          for p in projets})
        detail = '' if len(modules) == 1 else '  (héberge %s)' % ', '.join(
            os.path.basename(m) for m in modules)
        print('❌ %s%s' % (hote, detail))

        for api, files in sorted(missing.items()):
            total += 1
            print('     %s' % api)
            for name in sorted(files):
                print('         %s' % name)

    # UN MODULE QUE PERSONNE N'HÉBERGE N'EST PAS UN MODULE SAIN.
    #
    # Il ne peut plus produire de faux positif — mais il ne tourne nulle part,
    # et le silence ressemblerait trop à un succès.
    orphelins = sorted(set(services_relatifs()) - couverts)
    if orphelins:
        print()
        print('  ⓘ %d dossier(s) hébergé(s) par aucun *.Api :' % len(orphelins))
        for orphelin in orphelins:
            print('       %s' % orphelin)

    print()
    print('%d processus examiné(s), %d dépendance(s) non résolue(s).' % (checked, total))
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
