#!/usr/bin/env python3
"""
LOT B DE LA MIGRATION gRPC — sortir les serveurs des assemblages de contrats.

CE QUE CE SCRIPT FAIT, ET POURQUOI IL EXISTE PLUTOT QU'UN DEPLACEMENT A LA MAIN.

Douze des dix-sept serveurs gRPC vivaient dans `shared/contracts/HBA.<X>.Contracts.Grpc`,
c'est-a-dire dans l'assemblage que TOUS les consommateurs referencent. Les dix
services qui consomment merchant.proto liaient ainsi l'implementation de
seller-service. Les cinq autres serveurs vivaient deja dans leur `.Api`, mais dans
`GrpcServices/` et non `Grpc/Services/`.

Deux conventions pour une meme chose : c'est ce que ce script supprime.

IL EST REJOUABLE. Il ne fait rien si les fichiers cibles existent deja.

CE QU'IL NE FAIT PAS : il ne touche NI aux clients, NI aux mappings, NI aux
enregistrements — c'est le lot C. Il rend seulement publics les mappings restes
`internal`, sans quoi le serveur deplace ne les verrait plus depuis son nouvel
assemblage.
"""
import os, re, io, sys, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def sans_chaines_ni_commentaires(s):
    """Remplace chaines et commentaires par des espaces, en gardant les positions.

    Le comptage d'accolades ne doit pas voir celles qui vivent dans un litteral
    ou dans un encadre de commentaire — ce depot en contient, et ils expliquent
    justement du code.
    """
    out = list(s)
    i, n = 0, len(s)
    while i < n:
        c = s[i]
        if c == '"' and s[i:i + 3] == '"""':
            j = s.find('"""', i + 3)
            j = n if j < 0 else j + 3
            for k in range(i, j):
                if s[k] != "\n":
                    out[k] = " "
            i = j
        elif c == '"':
            j = i + 1
            while j < n and s[j] != '"':
                j += 2 if s[j] == "\\" else 1
            j = min(j + 1, n)
            for k in range(i, j):
                if s[k] != "\n":
                    out[k] = " "
            i = j
        elif c == "'":
            j = i + 1
            while j < n and s[j] != "'":
                j += 2 if s[j] == "\\" else 1
            j = min(j + 1, n)
            for k in range(i, j):
                if s[k] != "\n":
                    out[k] = " "
            i = j
        elif s[i:i + 2] == "//":
            j = s.find("\n", i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = " "
            i = j
        elif s[i:i + 2] == "/*":
            j = s.find("*/", i)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if s[k] != "\n":
                    out[k] = " "
            i = j
        else:
            i += 1
    return "".join(out)


def bloc_du_type(source, nom):
    """Rend (debut, fin) du type `nom`, commentaire d'en-tete compris."""
    nu = sans_chaines_ni_commentaires(source)
    m = re.search(r'\b(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+' + nom + r'\b', nu)
    if not m:
        return None

    # remonter au debut de la declaration (modificateurs), puis au bloc de
    # commentaires contigu : la raison d'une classe voyage avec elle.
    debut = source.rfind("\n", 0, m.start())
    debut = 0 if debut < 0 else debut + 1
    lignes = source[:debut].split("\n")
    i = len(lignes) - 1
    while i > 0:
        precedente = lignes[i - 1].strip()
        if precedente.startswith("///") or precedente.startswith("//") or precedente.startswith("["):
            i -= 1
        else:
            break
    debut = len("\n".join(lignes[:i]))
    if debut:
        debut += 1

    ouvrante = nu.find("{", m.end())
    profondeur, j = 0, ouvrante
    while j < len(nu):
        if nu[j] == "{":
            profondeur += 1
        elif nu[j] == "}":
            profondeur -= 1
            if profondeur == 0:
                return (debut, j + 1)
        j += 1
    return None


def racine_de_namespace(dossier):
    """Prefixe commun des namespaces DECLARES — jamais le nom du csproj.

    Sept projets de ce depot ont un nom de csproj qui contredit leur namespace
    racine (HBA.Order.Infrastructure declare HBA.Orders.*). S'y fier a coute une
    journee pendant la migration Kafka : la classe se retrouvait dans un espace
    de noms qui masquait un type du meme nom, et l'erreur tombait a vingt
    fichiers de la.
    """
    declares = []
    for f in fichiers_cs(dossier):
        s = io.open(f, encoding="utf-8", errors="replace").read()
        m = re.search(r'^namespace\s+([\w\.]+)\s*;', s, re.M) or re.search(r'^namespace\s+([\w\.]+)', s, re.M)
        if m:
            declares.append(m.group(1).split("."))
    if not declares:
        return None
    commun = declares[0]
    for d in declares[1:]:
        n = 0
        while n < min(len(commun), len(d)) and commun[n] == d[n]:
            n += 1
        commun = commun[:n]
    if not commun:
        return None

    # LE PREFIXE COMMUN N'EST PAS LA RACINE QUAND TOUT LE CODE VIT DANS UN DOSSIER.
    #
    # `HBA.Catalog.Api` ne declare qu'un seul namespace : `HBA.Catalog.Api.Endpoints`,
    # parce que tous ses fichiers sont sous `Endpoints/`. Le prefixe commun vaut donc
    # « ...Api.Endpoints », et poser le serveur sous `...Api.Endpoints.Grpc.Services`
    # le rangerait dans un espace de noms qui n'a rien a voir avec lui.
    #
    # On retire donc les segments de queue qui sont, en fait, des NOMS DE DOSSIER du
    # projet. Ce qui reste est la racine reelle, deduite du code et de l'arborescence
    # — jamais du nom du csproj, qui ment dans sept projets de ce depot.
    while len(commun) > 1 and os.path.isdir(os.path.join(dossier, commun[-1])):
        commun = commun[:-1]

    return ".".join(commun)


def main():
    # 1. qui expose quoi : app.MapInternalGrpcService<X>() nomme le proprietaire
    proprietaire = {}
    for base in ("services", "bff"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            if os.path.basename(f) != "Program.cs":
                continue
            s = io.open(f, encoding="utf-8", errors="replace").read()
            for m in re.finditer(r'MapInternalGrpcService<(\w+)>', s):
                proprietaire[m.group(1)] = f

    # 2. ou vit chaque serveur aujourd'hui
    emplacement = {}
    for base in ("shared", "services"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            s = io.open(f, encoding="utf-8", errors="replace").read()
            nu = sans_chaines_ni_commentaires(s)
            for m in re.finditer(r'class\s+(\w+)\s*:\s*[\w\.]*?(\w+)\.(\w+)Base\b', nu):
                if m.group(1) in proprietaire:
                    emplacement[m.group(1)] = f

    print(f"serveurs exposes : {len(proprietaire)} — localises : {len(emplacement)}")
    manquants = set(proprietaire) - set(emplacement)
    if manquants:
        print("  NON LOCALISES :", manquants)

    plan = []
    for classe, programme in sorted(proprietaire.items()):
        source = emplacement.get(classe)
        if not source:
            continue
        dossier_api = os.path.dirname(programme)
        racine_ns = racine_de_namespace(dossier_api)
        cible = os.path.join(dossier_api, "Grpc", "Services", classe + ".cs")
        plan.append((classe, source, cible, racine_ns, programme))

    for classe, source, cible, ns, _ in plan:
        deja = "  (deja en place)" if os.path.abspath(source) == os.path.abspath(cible) else ""
        print(f"  {classe:28} {os.path.relpath(source, RACINE):72} -> {os.path.relpath(cible, RACINE)}  ns={ns}.Grpc.Services{deja}")

    if not SEC:
        print("\nSIMULATION. Relancer avec --ecrire pour appliquer.")
        return

    deplaces = 0
    namespaces_disparus = set()
    for classe, source, cible, racine_ns, programme in plan:
        if os.path.abspath(source) == os.path.abspath(cible):
            continue
        s = io.open(source, encoding="utf-8").read()
        bornes = bloc_du_type(s, classe)
        if not bornes:
            print(f"  ECHEC: bloc introuvable pour {classe} dans {source}")
            continue
        debut, fin = bornes
        bloc = s[debut:fin]

        usings = re.findall(r'^using\s+[^\n]+;\s*$', s, re.M)
        ns_source = re.search(r'^namespace\s+([\w\.]+)', s, re.M).group(1)

        # LE `using` DU NAMESPACE D'ORIGINE N'EST AJOUTE QUE S'IL SURVIT.
        #
        # Pour les douze serveurs qui viennent de `shared/contracts`, il faut : le
        # stub genere et le mapping restent la-bas, dans un AUTRE assemblage.
        # Pour les cinq qui etaient deja dans leur `.Api`, sous `GrpcServices/`,
        # l'ajouter serait une erreur de compilation immediate — ce namespace-la
        # disparait avec le deplacement, puisqu'il ne contenait que ce fichier.
        vient_des_contrats = os.sep + "contracts" + os.sep in source
        if vient_des_contrats and f"using {ns_source};" not in usings:
            usings.append(f"using {ns_source};")

        entete = (
            "// ═════════════════════════════════════════════════════════════════════════════\n"
            f"// DEPLACE DEPUIS `{ns_source}` (lot B de la migration gRPC).\n"
            "//\n"
            "// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES\n"
            "// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient\n"
            "// l'implementation de seller-service ; les huit qui consomment order.proto\n"
            "// liaient celle d'order-service. Aucun ne s'en servait.\n"
            "//\n"
            "// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.\n"
            "// L'assemblage de contrats ne porte plus que le stub genere, le client et son\n"
            "// enregistrement — le lot C descendra ces deux-la chez les appelants.\n"
            "//\n"
            "// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours\n"
            "// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes\n"
            "// intercepteurs. Un deplacement de fichier ne rend rien plus sur.\n"
            "// ═════════════════════════════════════════════════════════════════════════════\n"
        )

        contenu = "\n".join(sorted(set(usings))) + "\n\n" + entete + f"\nnamespace {racine_ns}.Grpc.Services;\n\n" + bloc.rstrip() + "\n"
        os.makedirs(os.path.dirname(cible), exist_ok=True)
        io.open(cible, "w", encoding="utf-8").write(contenu)

        reste = (s[:debut] + s[fin:]).rstrip() + "\n"
        nu_reste = sans_chaines_ni_commentaires(reste)
        if re.search(r'\b(class|record|struct|interface|enum)\s+\w+', nu_reste):
            io.open(source, "w", encoding="utf-8").write(reste)
        else:
            os.remove(source)

        # `using` du nouveau namespace dans le Program.cs qui mappe le serveur
        p = io.open(programme, encoding="utf-8").read()
        besoin = f"using {racine_ns}.Grpc.Services;"
        if besoin not in p:
            derniere = list(re.finditer(r'^using\s+[^\n]+;\s*$', p, re.M))
            if derniere:
                pos = derniere[-1].end()
                p = p[:pos] + "\n" + besoin + p[pos:]
            else:
                p = besoin + "\n" + p
            io.open(programme, "w", encoding="utf-8").write(p)

        if not vient_des_contrats:
            namespaces_disparus.add(ns_source)

        deplaces += 1

    # UN `using` VERS UN NAMESPACE QUI N'EXISTE PLUS EST UNE ERREUR, PAS UN RESIDU.
    #
    # Les cinq serveurs deja dans leur `.Api` vivaient sous `<...>.Api.GrpcServices`,
    # un namespace qui ne contenait qu'eux. Apres deplacement il n'existe plus, et le
    # `using` reste dans `Program.cs` : CS0246 au premier build, a un endroit qui ne
    # parle pas du deplacement.
    if namespaces_disparus:
        encore_declares = set()
        for base in ("services", "bff", "shared"):
            for f in fichiers_cs(os.path.join(RACINE, base)):
                for m in re.finditer(r'^namespace\s+([\w\.]+)', io.open(f, encoding="utf-8", errors="replace").read(), re.M):
                    encore_declares.add(m.group(1))
        morts = namespaces_disparus - encore_declares
        for base in ("services", "bff"):
            for f in fichiers_cs(os.path.join(RACINE, base)):
                s2 = io.open(f, encoding="utf-8").read()
                avant = s2
                for ns in morts:
                    s2 = re.sub(r'^using\s+' + re.escape(ns) + r'\s*;\s*\n', '', s2, flags=re.M)
                if s2 != avant:
                    io.open(f, "w", encoding="utf-8").write(s2)
        print(f"  namespaces disparus, `using` retires : {sorted(morts)}")

    # les mappings restes `internal` ne sont plus visibles depuis le nouvel assemblage
    publics = 0
    for p in sorted(os.listdir(os.path.join(RACINE, "shared", "contracts"))):
        if not p.endswith(".Contracts.Grpc"):
            continue
        for f in fichiers_cs(os.path.join(RACINE, "shared", "contracts", p)):
            s = io.open(f, encoding="utf-8").read()
            s2 = re.sub(r'^internal(\s+static\s+class\s+\w*(?:Mapping|Parsing|Conversion)\w*)',
                        r'public\1', s, flags=re.M)
            if s2 != s:
                io.open(f, "w", encoding="utf-8").write(s2)
                publics += 1

    print(f"\n{deplaces} serveurs deplaces, {publics} fichiers de mapping rendus publics.")


if __name__ == "__main__":
    main()


def reparer_les_namespaces_englobants():
    """CE QUE LE DEPLACEMENT CASSE SANS RIEN AFFICHER : LA RESOLUTION PAR NAMESPACE ENGLOBANT.

    `UsersGrpcService` vivait dans `HBA.Users.Contracts.Grpc` et ecrivait
    `IUsersModuleApi` sans `using` — le type est dans `HBA.Users.Contracts`, un
    namespace PARENT, et C# le resout tout seul. Depose dans
    `HBA.Users.Api.Grpc.Services`, ce parent n'est plus sur le chemin : CS0246, sur
    une ligne qui n'a pas change.

    C'est exactement ce qui a coute quarante-cinq fichiers pendant la migration
    Kafka. On ajoute donc, pour chaque serveur deplace, un `using` vers chaque
    ancetre de son ancien namespace qui n'est pas deja un ancetre du nouveau.
    """
    declares = set()
    for base in ("services", "bff", "shared"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            for m in re.finditer(r'^namespace\s+([\w\.]+)', io.open(f, encoding="utf-8", errors="replace").read(), re.M):
                declares.add(m.group(1))

    repares = 0
    for base in ("services", "bff"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            if os.sep + "Grpc" + os.sep + "Services" + os.sep not in f:
                continue
            s = io.open(f, encoding="utf-8").read()
            m = re.search(r'DEPLACE DEPUIS `([\w\.]+)`', s)
            if not m:
                continue
            ancien = m.group(1).split(".")
            nouveau = re.search(r'^namespace\s+([\w\.]+)', s, re.M).group(1).split(".")

            ancetres_nouveau = {".".join(nouveau[:i]) for i in range(1, len(nouveau) + 1)}
            manquants = []
            for i in range(1, len(ancien) + 1):
                ns = ".".join(ancien[:i])
                if ns in ancetres_nouveau or ns not in declares:
                    continue
                if re.search(r'^using\s+' + re.escape(ns) + r'\s*;', s, re.M):
                    continue
                manquants.append(ns)

            if not manquants:
                continue
            derniere = list(re.finditer(r'^using\s+[^\n]+;\s*$', s, re.M))[-1]
            ajout = "\n" + "\n".join(f"using {ns};" for ns in manquants)
            s = s[:derniere.end()] + ajout + s[derniere.end():]
            io.open(f, "w", encoding="utf-8").write(s)
            repares += 1
            print(f"  englobants rendus explicites : {os.path.relpath(f, RACINE)} -> {manquants}")

    print(f"{repares} fichiers repares.")
