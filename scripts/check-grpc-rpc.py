#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UN RPC APPELÉ SANS CORPS DE SERVEUR REND `UNIMPLEMENTED` — ET RIEN NE LE DIT.

DEUX FOIS EN UNE JOURNÉE, DONT UNE QUI TUAIT TOUT LE PARCOURS REPAS.

  • `DeliveryApi.LookupQuote` : appelé par les deux checkouts, aucun corps.
    Le devis étant obligatoire pour un repas, AUCUNE COMMANDE DE REPAS NE
    POUVAIT ÊTRE PASSÉE.
  • `OrderApi.ListOrdersBySeller` : appelé par `SellerSalesCountHandler` à
    CHAQUE commande confirmée, aucun corps. L'exception partait avant que
    l'inbox ne soit marquée — donc rejeu du message jusqu'à épuisement — et
    `SalesCount` restait à zéro pour tous les vendeurs, c'est-à-dire le défaut
    même que ce handler avait été écrit pour fermer.

Les deux compilent. Les deux passent tous les autres contrôles du dépôt. Le
`.proto` déclare le RPC, `protoc` génère la méthode côté client ET une base
côté serveur dont les membres non surchargés lèvent `UNIMPLEMENTED` À
L'EXÉCUTION. Il n'y a aucun moment, entre l'éditeur et la production, où
quelque chose s'en aperçoit.

CE QU'IL VÉRIFIE

Pour chaque RPC déclaré dans un `.proto` COMPILÉ (`shared/proto/*/v1/`) :

  ❌ appelé par un client ET sans corps de serveur   → panne à l'exécution
  ⓘ  sans corps de serveur et sans appelant          → surface latente
  ⓘ  avec corps de serveur et sans appelant          → RPC mort

Seule la première catégorie fait échouer : c'est la seule qui casse quelque
chose aujourd'hui. Les deux autres sont un inventaire — le lot 9.1 les traite,
et les compter ici évite de les recompter à la main.

CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT

  • L'APPELANT APPLICATIF. Une enveloppe de `*.Contracts.Grpc` qui appelle un
    RPC compte comme un appelant, même si PERSONNE n'appelle l'enveloppe. Le
    RPC est alors « appelé » au sens de ce contrôle et « mort » en pratique.
    Remonter jusqu'à l'appelant métier demanderait de suivre les interfaces à
    travers l'injection de dépendances — c'est-à-dire d'écrire un compilateur.
    Conséquence assumée : on peut signaler en ❌ un RPC que personne n'appelle
    vraiment. C'est le bon sens de l'erreur : il RESTE à brancher ou à retirer.

  • LES FLUX. Seuls les RPC unaires sont reconnus, ce qui est tout le dépôt.

  • LES `.proto` NON COMPILÉS. `return_refund.proto` n'est référencé par aucun
    `<Protobuf Include=…>` : ses RPC n'existent ni en client ni en serveur, et
    les compter ici gonflerait l'inventaire sans rien apprendre.

Sort 1 s'il trouve un RPC appelé sans corps de serveur.
═══════════════════════════════════════════════════════════════════════════════
"""
import io
import os
import re
import sys

from _lecture_csharp import sans_commentaires

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git")

SERVICE = re.compile(r'^\s*service\s+(\w+)\s*\{', re.M)
RPC = re.compile(r'^\s*rpc\s+(\w+)\s*\(', re.M)

# `public sealed class X : Truc.MachinApiBase` — on retient « MachinApi ».
BASE = re.compile(r'class\s+\w+\s*:\s*[\w\.]*?(\w+)\.(\w+)Base\b')
SURCHARGE = re.compile(r'public\s+override\s+(?:async\s+)?Task\s*<[^>]*>\s+(\w+)\s*\(')

# `_client.NomAsync(` ET `_client.Nom(` : protoc génère les deux formes.
APPEL = re.compile(r'\b_client\.(\w+?)(?:Async)?\s*\(')


def fichiers(extension, depuis):
    for dossier, sous, noms in os.walk(os.path.join(RACINE, depuis)):
        sous[:] = [d for d in sous if d not in IGNORES]
        for n in noms:
            if n.endswith(extension):
                yield os.path.join(dossier, n)


def main():
    # ── 1. Ce que les contrats déclarent ────────────────────────────────────
    declares = {}
    for chemin in fichiers(".proto", "shared/proto"):
        source = io.open(chemin, encoding="utf-8").read()
        for service in SERVICE.finditer(source):
            nom = service.group(1)
            fin = source.find("\n}", service.end())
            for rpc in RPC.finditer(source[service.end():fin]):
                declares[(nom, rpc.group(1))] = os.path.relpath(chemin, RACINE)

    # ── 2. Ce que les serveurs implémentent ─────────────────────────────────
    #
    # LES COMMENTAIRES SONT RETIRÉS D'ABORD. Un encadré qui cite
    # « public override Task<X> Machin( » — il y en a dans ce dépôt, ils
    # expliquent justement ce qui manque — compterait sinon pour une
    # implémentation, et le contrôle se tairait sur le défaut qu'il vise.
    servis = set()
    for chemin in fichiers(".cs", "."):
        brut = io.open(chemin, encoding="utf-8", errors="replace").read()
        if "Base" not in brut:
            continue
        source = sans_commentaires(brut)
        services = {b.group(2) for b in BASE.finditer(source)}
        if not services:
            continue
        for surcharge in SURCHARGE.finditer(source):
            for service in services:
                servis.add((service, surcharge.group(1)))

    # ── 3. Ce que les clients appellent ─────────────────────────────────────
    appeles = set()
    for chemin in fichiers(".cs", "."):
        source = sans_commentaires(
            io.open(chemin, encoding="utf-8", errors="replace").read())
        for appel in APPEL.finditer(source):
            appeles.add(appel.group(1))

    casses, latents, morts = [], [], []
    for (service, rpc), proto in sorted(declares.items()):
        a_un_corps = (service, rpc) in servis
        est_appele = rpc in appeles

        if est_appele and not a_un_corps:
            casses.append((service, rpc, proto))
        elif not a_un_corps:
            latents.append((service, rpc))
        elif not est_appele:
            morts.append((service, rpc))

    print()
    print("  %d RPC déclarés dans les contrats compilés." % len(declares))
    print("  ⓘ %d sans corps de serveur et sans appelant — surface latente."
          % len(latents))
    print("  ⓘ %d implémentés et jamais appelés — RPC morts (lot 9.1)."
          % len(morts))
    print()

    for service, rpc, proto in casses:
        print("  ❌ %s.%s" % (service, rpc))
        print("       déclaré dans %s, appelé par un client, AUCUN corps de serveur." % proto)
        print("       À l'exécution : RpcException(Unimplemented), non rattrapée.")
        print("       Le brancher, ou retirer l'appel — pas laisser en l'état.")

    print("%d RPC appelé(s) sans corps de serveur." % len(casses))
    return 1 if casses else 0


if __name__ == "__main__":
    sys.exit(main())
