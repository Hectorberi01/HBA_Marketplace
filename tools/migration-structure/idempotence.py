#!/usr/bin/env python3
"""
L'IDEMPOTENCE DESCEND DANS LES SEPT SERVICES QUI EN TIENNENT UNE TABLE.

MEME FORME QUE L'OUTBOX ET L'INBOX : l'entite, sa configuration, le depot et la
purge appartiennent au service, parce que la table `idempotency_records` est
creee par SES migrations. Le PORT reste au socle.

POURQUOI SEPT ET NON VINGT-SIX. `IdempotencyConfiguration` n'est appliquee que par
sept contextes ; les dix-neuf autres n'ont pas la table. Copier l'entite chez eux
donnerait un type que rien ne mappe — du code mort deguise en structure. Leur
`Idempotency/LISEZMOI.md` continue de dire ou vit l'implementation.

CE QUI RESTE, ET CE N'EST PAS UN DETAIL : `IIdempotencyStore`. C'est
`IdempotencyEndpointFilter`, dans la couche HTTP partagee, qui le resout sur
CHAQUE route annotee `AllowIdempotency()`. L'idempotence de ce depot ne sert pas
qu'a Kafka : elle protege aussi les requetes rejouees par un client mobile sur un
reseau instable. Descendre le port couperait ce chemin-la.

CONSEQUENCE EF, LA TROISIEME DE LA SEMAINE : les instantanes de modele de ces
sept services referencent « HBA.Shared.Infrastructure.Idempotency.IdempotencyRecord ».
A regenerer dans le meme `dotnet ef migrations add` que l'audit et l'outbox — le
diff de schema sera vide, la table ne change pas.
"""
import os, re, io, sys, shutil

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
SI = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure")

COPIES = [
    ("Idempotency/IdempotencyRecord.cs",        "Idempotency/IdempotencyRecord.cs"),
    ("Idempotency/IdempotencyConfiguration.cs", "Idempotency/IdempotencyConfiguration.cs"),
    ("Idempotency/EfIdempotencyStore.cs",       "Idempotency/IdempotencyRepository.cs"),
    ("Idempotency/IdempotencyPurger.cs",        "Idempotency/IdempotencyCleanupService.cs"),
    ("Idempotency/IdempotencyRegistration.cs",  "Idempotency/DependencyInjection.cs"),
]

ENTETE = """// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.
//
// La table `idempotency_records` de CE service est creee par SES migrations :
// l'entite qui la decrit lui appartient. Le socle n'en garde que le port,
// `IIdempotencyStore`, que `IdempotencyEndpointFilter` resout sur chaque route
// annotee `AllowIdempotency()`.
//
// A REGENERER : l'instantane de modele de ce service reference encore le type du
// socle sous forme de chaine. Il compile et les migrations s'appliquent — mais
// modele et instantane divergent jusqu'a un `dotnet ef migrations add`, au diff
// de schema vide.
// ═════════════════════════════════════════════════════════════════════════════
"""


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def racine_de_namespace(dossier):
    declares = []
    for f in fichiers_cs(dossier):
        m = re.search(r'^namespace\s+([\w\.]+)', io.open(f, encoding="utf-8", errors="replace").read(), re.M)
        if m: declares.append(m.group(1).split("."))
    if not declares: return None
    commun = declares[0]
    for d in declares[1:]:
        n = 0
        while n < min(len(commun), len(d)) and commun[n] == d[n]: n += 1
        commun = commun[:n]
    while len(commun) > 1 and os.path.isdir(os.path.join(dossier, commun[-1])):
        commun = commun[:-1]
    return ".".join(commun)


def concernes():
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) != "src":
                continue
            for x in sorted(dirs):
                if not x.endswith(".Infrastructure"):
                    continue
                infra = os.path.join(d, x)
                utilise, contexte = False, None
                for f in fichiers_cs(infra):
                    s = io.open(f, encoding="utf-8", errors="replace").read()
                    if "new IdempotencyConfiguration()" in s or "AddIdempotence<" in s:
                        utilise = True
                    m = re.search(r'class\s+(\w+)\s*:\s*ModuleDbContext', s)
                    if m and not contexte:
                        contexte = m.group(1)
                if utilise and contexte:
                    yield infra, x.replace("HBA.", "").replace(".Infrastructure", "").replace(".", ""), contexte


def main():
    liste = list(concernes())
    for infra, court, contexte in liste:
        print(f"  {os.path.basename(infra):46} {contexte}")
    print(f"{len(liste)} services")
    if not SEC:
        print("\nSIMULATION.")
        return

    for infra, court, contexte in liste:
        racine = racine_de_namespace(infra)
        ns = f"{racine}.Idempotency"
        for source, destination in COPIES:
            texte = io.open(os.path.join(SI, source), encoding="utf-8").read()
            texte = texte.replace("<TDbContext>", "").replace("TDbContext", contexte)
            texte = re.sub(r'\n\s*where ' + re.escape(contexte) + r' : [^\n]*\n', "\n", texte)
            texte = re.sub(r'^namespace\s+[\w\.]+;', ENTETE + f"\nnamespace {ns};", texte, flags=re.M)
            texte = "using HBA.Shared.Infrastructure.Idempotency;\n" \
                    "using HBA.Shared.Infrastructure.Persistence;\n" \
                    f"using {racine}.Persistence.DbContext;\n" + texte
            if destination.endswith("IdempotencyRecord.cs"):
                texte = texte.replace("public sealed class IdempotencyRecord",
                                      "internal sealed class IdempotencyRecord : IEnregistrementDIdempotence")
            if destination.endswith("DependencyInjection.cs"):
                texte = texte.replace("public static IServiceCollection AddIdempotence(this IServiceCollection services)",
                                      f"public static IServiceCollection AjouterIdempotence{court}(this IServiceCollection services)")
            chemin = os.path.join(infra, destination.replace("/", os.sep))
            os.makedirs(os.path.dirname(chemin), exist_ok=True)
            l = os.path.join(os.path.dirname(chemin), "LISEZMOI.md")
            if os.path.exists(l):
                os.remove(l)
            io.open(chemin, "w", encoding="utf-8").write(texte)

        # les appelants pointent vers le module local
        for f in fichiers_cs(infra):
            if os.sep + "Idempotency" + os.sep in f:
                continue
            s = io.open(f, encoding="utf-8").read()
            avant = s
            s = s.replace(f"AddIdempotence<{contexte}>()", f"AjouterIdempotence{court}()")
            if "new IdempotencyConfiguration()" in s or f"AjouterIdempotence{court}()" in s:
                if not re.search(r'^using ' + re.escape(ns) + r';', s, re.M):
                    u = list(re.finditer(r'^using\s+[^\n]+;\s*$', s, re.M))
                    s = (s[:u[-1].end()] + f"\nusing {ns};" + s[u[-1].end():]) if u else f"using {ns};\n" + s
            s = re.sub(r'^using\s+HBA\.Shared\.Infrastructure\.Idempotency\s*;\s*\n',
                       "using HBA.Shared.Infrastructure.Idempotency;\n", s, flags=re.M)
            if s != avant:
                io.open(f, "w", encoding="utf-8").write(s)

    print(f"{len(liste)} services pourvus.")


if __name__ == "__main__":
    main()
