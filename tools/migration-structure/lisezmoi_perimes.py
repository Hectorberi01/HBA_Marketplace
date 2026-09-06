# -*- coding: utf-8 -*-
"""
LES LISEZMOI QUI DECRIVENT UN DEPOT QUI N'EXISTE PLUS.

L'arborescence canonique est tenue partout : 638 dossiers sans code portent un
`LISEZMOI.md` qui dit ce qui ira dedans et ou la chose vit AUJOURD'HUI. C'est
utile — a condition que la seconde moitie soit vraie.

Elle ne l'est plus pour 32 d'entre eux. Ils ont ete ecrits avant trois lots qui
ont deplace exactement ce qu'ils citent :

    `shared/contracts/HBA.<X>.Contracts.Grpc`     dissous par le lot D
    `HBA.Shared.Infrastructure.Idempotency`        descendu dans 7 services
    `HBA.Shared.Infrastructure.Outbox` / `.Inbox`  descendus dans chaque service

CE QUE CA COUTE, ET POURQUOI CE N'EST PAS DE LA COSMETIQUE. Un lecteur qui suit
« ou ca vit aujourd'hui » arrive nulle part. Et ce depot vient de payer ce
defaut deux fois en une semaine : une ligne de documentation decrivant une sonde
absente a trompe mon propre script de reparation et pose onze CS0246 ; un bloc
de doc decrivant une methode retiree survivait dans food-cart. Un fichier qui
promet ce qu'il n'a pas est le defaut que ce depot traque partout ailleurs.

CE QUE CE SCRIPT NE FAIT PAS : il ne juge pas si le dossier vide doit exister.
Cette question a ete tranchee — l'arborescence complete est tenue partout. Il
remet seulement les textes d'aplomb.
"""
import io, os, shutil

RAC = os.path.expanduser("~/mnt/HBA")

PIED = """
---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
"""

CLIENTS = """# `Grpc/Clients/`

Vide dans **{projet}**, et ce n'est pas un manque : ce service n'appelle aucun
voisin en gRPC. Sa table d'autorisations le dit aussi — `FrozenSet<string>.Empty`
ou l'absence d'entree dans `AutorisationsGrpc`.

**Ce qui va ici :** les adaptateurs `I<X>ModuleApi` vers le stub gRPC, un par
voisin appele, avec le `<Protobuf GrpcServices="Client">` qui va avec dans le
csproj.

**Ou ca vit aujourd'hui :** chez CHAQUE appelant, dans son propre
`Infrastructure/Grpc/Clients/` — quinze projets en ont un. Les enveloppes
partagees `shared/contracts/HBA.<X>.Contracts.Grpc` ont ete dissoutes par le
lot D : un adaptateur appartient a celui qui appelle, pas au domaine appele.
"""

MAPPERS = """# `Grpc/Mappers/`

Vide dans **{projet}**.

**Ce qui va ici :** les traductions proto <-> enregistrements de contrats, POSEES
A COTE DU CLIENT OU DU SERVEUR QUI S'EN SERT.

**Ou ca vit aujourd'hui :** chez chaque appelant qui en a besoin
(`Infrastructure/Grpc/Mappers/`) et chez chaque serveur (`<Service>.Api/Grpc/Mappers/`).
Les enveloppes partagees `shared/contracts/HBA.<Domaine>.Contracts.Grpc`, qui
portaient une traduction unique par domaine, ont ete dissoutes par le lot D.

**Pourquoi ce dossier est vide ici :** ce projet n'a ni client gRPC a nourrir,
ni serveur a servir — ou son serveur vit dans son projet `.Api`, avec ses
propres mappings.
"""

IDEMPOTENCE = """# `Idempotency/`

Vide dans **{projet}** : ce service ne tient pas de table d'idempotence.

**Ce qui va ici :** `IdempotencyRecord`, sa configuration EF, le depot, la purge
et l'enregistrement — pour un service qui expose des routes annotees
`AllowIdempotency()` ou qui doit dedupliquer des commandes rejouees.

**Ou ca vit aujourd'hui :** dans les SEPT services qui en tiennent une —
identity, users, merchants, catalog, promotions, payments, notifications — chacun
dans son propre `Idempotency/`. Seul le port `IIdempotencyStore` reste au socle,
parce que `IdempotencyEndpointFilter` (couche HTTP partagee) le resout sur chaque
route annotee.
"""

OUTBOX = """# `Persistence/Outbox/`

Vide dans **{projet}** : ce service ne publie rien par outbox.

**Ce qui va ici :** `OutboxMessage`, sa configuration EF, le depot, la purge,
l'enregistrement et le publieur d'evenements d'integration.

**Ou ca vit aujourd'hui :** dans les VINGT-QUATRE services qui publient, chacun
dans son propre `Persistence/Outbox/`. `HBA.Shared.Infrastructure.Outbox`
n'existe plus : le drain reste au socle (`ModuleDbContext`, `DrainageDOutbox`,
seule lecture de `OUTBOX_ENABLED`), parce qu'un evenement doit partir dans la
MEME transaction que le fait qui l'a produit — cette regle-la ne se duplique pas.
"""

INBOX = """# `Persistence/Inbox/`

Vide dans **{projet}** : ce service ne consomme aucun evenement d'integration.

**Ce qui va ici :** `InboxMessage`, sa configuration EF, le depot et la purge.

**Ou ca vit aujourd'hui :** dans les services qui consomment, chacun dans son
propre `Persistence/Inbox/`. `HBA.Shared.Infrastructure.Inbox` n'existe plus ;
seul le port `IConsumerInbox` reste au socle, parce que le dispatcher partage
verifie l'idempotence avant CHAQUE gestionnaire.

La purge, elle, n'existait nulle part avant ce lot : `consumer_inbox` grossissait
indefiniment dans les quinze services qui consomment.
"""

MARQUEURS = [
    ("shared/contracts/HBA.<X>.Contracts.Grpc", CLIENTS),
    ("Aujourd'hui, ils vivent dans `shared/contracts/HBA.<Domaine>.Contracts.Grpc", MAPPERS),
    ("HBA.Shared.Infrastructure.Idempotency", IDEMPOTENCE),
    ("HBA.Shared.Infrastructure.Outbox", OUTBOX),
    ("HBA.Shared.Infrastructure.Inbox", INBOX),
]


def projet_de(dossier):
    p = dossier
    while p != RAC:
        base = os.path.basename(p)
        if base.startswith("HBA.") and os.path.exists(os.path.join(p, base + ".csproj")):
            return base
        p = os.path.dirname(p)
    return os.path.basename(dossier)


def main():
    reecrits, supprimes = 0, 0

    for base, dirs, fs in os.walk(RAC):
        if any(x in base for x in ("/bin/", "/obj/", "/node_modules/", "/clients/", "/.git/")):
            dirs[:] = []
            continue

        # ── LES 26 `Infrastructure/Grpc/Services/`. Leur propre LISEZMOI dit
        # « ce qui va ici : RIEN — le serveur gRPC est la SURFACE du service ».
        # Un dossier dont la documentation annonce qu'il restera vide pour
        # toujours n'est pas une place tenue, c'est une fausse promesse. Le
        # serveur vit dans `<Service>.Api/Grpc/Services/`, la ou il doit etre.
        if (base.endswith(os.sep + os.path.join("Grpc", "Services"))
                and ".Infrastructure" in base
                and not dirs
                and fs == ["LISEZMOI.md"]):
            shutil.rmtree(base)
            supprimes += 1
            print("SUPPRIME  " + os.path.relpath(base, RAC))
            dirs[:] = []
            continue

        if "LISEZMOI.md" not in fs:
            continue

        chemin = os.path.join(base, "LISEZMOI.md")
        texte = io.open(chemin, encoding="utf-8").read()

        for marqueur, gabarit in MARQUEURS:
            if marqueur in texte:
                io.open(chemin, "w", encoding="utf-8").write(
                    gabarit.format(projet=projet_de(base)).rstrip() + "\n" + PIED)
                reecrits += 1
                print("REECRIT   " + os.path.relpath(chemin, RAC))
                break

    print("\n%d LISEZMOI remis d'aplomb, %d dossier(s) supprime(s)." % (reecrits, supprimes))


main()
