#!/usr/bin/env python3
"""
LES FICHIERS QUE LE SERVICE POSSEDE DEJA REJOIGNENT LEUR PLACE DANS L'ARBORESCENCE.

L'arborescence a ete posee au commit precedent, mais elle etait DECORATIVE : le
DbContext restait a plat dans `Persistence/`, les depots aussi, le cablage
d'outbox vivait sous `Messaging/Kafka/Outbox/`. Un dossier `Persistence/DbContext/`
vide a cote d'un `Persistence/UsersDbContext.cs` est pire que pas de dossier du
tout — il annonce une regle que le voisin dement.

LE NAMESPACE NE SUIT PAS LE DOSSIER, ET C'EST DELIBERE.

C# n'exige aucune correspondance entre dossier et espace de noms. Renommer les
namespaces de 125 fichiers deplaces casserait toutes les references qui les
resolvent par namespace englobant — c'est ce qui a coute quarante-cinq fichiers
pendant la migration Kafka, et deux lots gRPC attendent deja une compilation.

On deplace donc SANS toucher aux namespaces. Le jour ou l'on veut les aligner,
c'est un lot a part, a faire avec un SDK sous la main.
"""
import os, io, sys, shutil

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv


def projets():
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) == "src":
                for x in sorted(dirs):
                    if x.endswith(".Infrastructure"):
                        yield os.path.join(d, x)


def a_deplacer(p):
    """Rend (source, destination) pour ce projet."""
    pers = os.path.join(p, "Persistence")
    if os.path.isdir(pers):
        for f in sorted(os.listdir(pers)):
            if not f.endswith(".cs"):
                continue
            s = os.path.join(pers, f)
            if "DbContext" in f:
                yield s, os.path.join(pers, "DbContext", f)
            elif "Repository" in f or "Repositories" in f:
                yield s, os.path.join(pers, "Repositories", f)

    for source, cible in (
        (os.path.join(p, "Messaging", "Kafka", "Outbox"), os.path.join(p, "Persistence", "Outbox")),
        (os.path.join(p, "Messaging", "Kafka", "Inbox"), os.path.join(p, "Persistence", "Inbox")),
        (os.path.join(p, "Redis"), os.path.join(p, "Caching", "Redis", "Services")),
    ):
        if os.path.isdir(source):
            for f in sorted(os.listdir(source)):
                if f.endswith(".cs"):
                    yield os.path.join(source, f), os.path.join(cible, f)

    bj = os.path.join(p, "BackgroundJobs")
    if os.path.isdir(bj):
        for f in sorted(os.listdir(bj)):
            if f.endswith(".cs"):
                yield os.path.join(bj, f), os.path.join(bj, "Workers", f)


def main():
    total = 0
    for p in projets():
        mouvements = list(a_deplacer(p))
        if not mouvements:
            continue
        print(f"  {os.path.relpath(p, RACINE)} : {len(mouvements)} fichiers")
        total += len(mouvements)
        if not SEC:
            continue
        for s, c in mouvements:
            os.makedirs(os.path.dirname(c), exist_ok=True)
            shutil.move(s, c)
            # le marqueur n'a plus lieu d'etre : le dossier a du contenu
            lisezmoi = os.path.join(os.path.dirname(c), "LISEZMOI.md")
            if os.path.exists(lisezmoi):
                os.remove(lisezmoi)
        # dossiers sources vides : on les retire, sauf s'ils portent encore un marqueur
        for source in (os.path.join(p, "Messaging", "Kafka", "Outbox"),
                       os.path.join(p, "Messaging", "Kafka", "Inbox"),
                       os.path.join(p, "Redis")):
            if os.path.isdir(source) and not os.listdir(source):
                os.rmdir(source)

    print(f"\n{total} fichiers" + ("" if SEC else " — SIMULATION, relancer avec --ecrire"))


if __name__ == "__main__":
    main()
