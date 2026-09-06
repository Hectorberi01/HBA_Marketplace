# -*- coding: utf-8 -*-
"""
LES INSTANTANES EF NOMMENT LEURS ENTITES PAR CHAINE.

`modelBuilder.Entity("HBA.Shared.Infrastructure.Outbox.OutboxMessage", …)` : le
nom complet du type, en TEXTE. Quatre entites techniques ont change d'espace de
noms en descendant dans chaque service — audit, outbox, inbox, idempotence — et
les vingt-quatre instantanes citent encore l'ancien.

CE QUE CA COUTE, ET C'EST LA PIRE FORME DE PANNE. Rien ne casse : le code
compile, les migrations deja ecrites s'appliquent par IDENTIFIANT, le service
demarre. Mais le prochain `dotnet ef migrations add` compare le modele a
l'instantane, ne retrouve pas les anciens types, et genere une SUPPRESSION de
table pour chacun — outbox, inbox, journal d'audit, idempotence.

CE QUE FAIT CE SCRIPT : le renommage que `dotnet ef` ecrirait lui-meme. C'est un
changement d'ESPACE DE NOMS et rien d'autre — les noms de CLASSE sont inchanges
(`ConsumerInboxEntry` s'appelle toujours ainsi, seul son fichier a ete renomme),
donc ni les colonnes, ni les index, ni les `ToTable` ne bougent.

CE QU'IL NE REMPLACE PAS, ET IL FAUT LE DIRE : il n'y a pas de SDK .NET sur ce
poste. Ce script rend l'instantane COHERENT avec le depot ; seul un
`dotnet ef migrations add` a diff VIDE le prouve. Tant que ce passage n'a pas eu
lieu, la correction est plausible, pas verifiee.

IL REFUSE PLUTOT QUE DE DEVINER : si un instantane cite un type que son projet
ne declare pas, le script s'arrete et le nomme. Un renommage silencieux vers un
type inexistant serait exactement le defaut qu'il pretend corriger.
"""
import io, os, re, collections, sys

RAC = os.path.expanduser("~/mnt/HBA")
CIBLES = ("OutboxMessage", "ConsumerInboxEntry", "IdempotencyRecord", "AuditEntry")
ANCIENS = {
    "OutboxMessage": "HBA.Shared.Infrastructure.Outbox.OutboxMessage",
    "ConsumerInboxEntry": "HBA.Shared.Infrastructure.Inbox.ConsumerInboxEntry",
    "IdempotencyRecord": "HBA.Shared.Infrastructure.Idempotency.IdempotencyRecord",
    "AuditEntry": "HBA.Shared.Infrastructure.Audit.AuditEntry",
}


def projet_de(dossier):
    p = dossier
    while p != RAC and not any(x.endswith(".csproj") for x in os.listdir(p)):
        p = os.path.dirname(p)
    return os.path.basename(p)


def main():
    # ── ou vit chaque type, aujourd'hui, projet par projet
    types = collections.defaultdict(dict)
    for b, d, fs in os.walk(os.path.join(RAC, "services")):
        if any(x in b for x in ("/bin/", "/obj/")):
            d[:] = []
            continue
        for f in fs:
            if not f.endswith(".cs"):
                continue
            s = io.open(os.path.join(b, f), encoding="utf-8", errors="ignore").read()
            ns = re.search(r"^namespace\s+([\w.]+)\s*;", s, re.M)
            if not ns:
                continue
            for c in re.findall(r"\bclass\s+(%s)\b" % "|".join(CIBLES), s):
                types[projet_de(b)][c] = ns.group(1) + "." + c

    refus, touches, remplaces = [], 0, 0
    for b, d, fs in os.walk(os.path.join(RAC, "services")):
        if any(x in b for x in ("/bin/", "/obj/")):
            d[:] = []
            continue
        for f in fs:
            if not f.endswith("ModelSnapshot.cs"):
                continue
            p = os.path.join(b, f)
            s = io.open(p, encoding="utf-8").read()
            proj = projet_de(b)
            neuf, n = s, 0
            for cls, ancien in ANCIENS.items():
                if ancien not in neuf:
                    continue
                nouveau = types.get(proj, {}).get(cls)
                if nouveau is None:
                    refus.append("%s : l'instantane cite `%s`, mais %s ne declare "
                                 "AUCUN `%s`." % (f, ancien, proj, cls))
                    continue
                c = neuf.count(ancien)
                neuf = neuf.replace(ancien, nouveau)
                n += c
            if n and not refus:
                io.open(p, "w", encoding="utf-8").write(neuf)
                touches += 1
                remplaces += n
                print("%-46s %2d reference(s)" % (f[:44], n))

    if refus:
        print("\nREFUS — rien n'a ete ecrit :")
        for r in refus:
            print("   " + r)
        sys.exit(1)

    print("\n%d instantane(s), %d reference(s) reecrite(s)." % (touches, remplaces))


main()
