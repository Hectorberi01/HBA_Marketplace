#!/usr/bin/env python3
"""
LOT D — DISSOLUTION DES ASSEMBLAGES DE CONTRATS gRPC.

Apres ce script, `shared/` ne contient plus AUCUN projet gRPC : seulement
`shared/proto/<domaine>/v1/*.proto`, le contrat lui-meme.

CE QUE ÇA IMPLIQUE, ET QUI N'EST PAS UN DEPLACEMENT.

Chaque service compile desormais LUI-MEME le `.proto` dont il a besoin :
`GrpcServices="Server"` pour celui qu'il expose, `GrpcServices="Client"` pour
ceux qu'il appelle. Les types generes deviennent donc PROPRES A CHAQUE
ASSEMBLAGE — deux services qui compilent `merchant.proto` obtiennent deux types
CLR distincts, qui ne se rencontrent jamais puisque tout ce qui traverse une
frontiere d'assemblage passe par les enregistrements de `HBA.<X>.Contracts`.

`Access="Internal"` EST LA CONDITION POUR QUE ÇA TIENNE. Sans lui, un hote
compose qui reference deux assemblages ayant compile le meme proto verrait deux
types publics du meme nom complet : CS0433, a l'usage, loin de la cause. En
interne, la question ne se pose pas — et cela interdit au passage qu'un type
proto fuie hors du module gRPC, ce qui est la regle depuis le debut.

CONSEQUENCE SUR LES ACCESSIBILITES : un adaptateur public dont le constructeur
prend un stub interne ne compile pas (CS0051), un serveur public derivant d'une
base interne non plus (CS0060). Les adaptateurs, mappings et serveurs deviennent
donc `internal`. Seul `AjouterClientsGrpc<Service>()` reste public : c'est le
composition root, dans un autre assemblage, qui l'appelle.

CE QUE ÇA COUTE, ET IL FAUT LE SAVOIR : la traduction proto <-> contrats existe
desormais en un exemplaire PAR CONSOMMATEUR. `merchant.proto` en a neuf. Elles
sont identiques au depart et rien ne les empechera de diverger.
"""
import os, re, io, sys, shutil, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
CONTRATS = os.path.join(RACINE, "shared", "contracts")


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def sans_chaines_ni_commentaires(s):
    out = list(s); i, n = 0, len(s)
    while i < n:
        c = s[i]
        if c == '"':
            j = i + 1
            while j < n and s[j] != '"':
                j += 2 if s[j] == "\\" else 1
            j = min(j + 1, n)
            for k in range(i, j):
                if s[k] != "\n": out[k] = " "
            i = j
        elif c == "'":
            j = i + 1
            while j < n and s[j] != "'":
                j += 2 if s[j] == "\\" else 1
            j = min(j + 1, n)
            for k in range(i, j):
                if s[k] != "\n": out[k] = " "
            i = j
        elif s[i:i + 2] == "//":
            j = s.find("\n", i); j = n if j < 0 else j
            for k in range(i, j): out[k] = " "
            i = j
        elif s[i:i + 2] == "/*":
            j = s.find("*/", i); j = n if j < 0 else j + 2
            for k in range(i, j):
                if s[k] != "\n": out[k] = " "
            i = j
        else:
            i += 1
    return "".join(out)


def blocs_de_types(source):
    """Rend [(nom, texte)] pour chaque type de premier niveau, commentaire compris."""
    nu = sans_chaines_ni_commentaires(source)
    resultats = []
    for m in re.finditer(r'\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*'
                         r'(?:class|record|struct|interface|enum)\s+(\w+)', nu):
        debut = source.rfind("\n", 0, m.start())
        debut = 0 if debut < 0 else debut + 1
        lignes = source[:debut].split("\n")
        i = len(lignes) - 1
        while i > 0 and (lignes[i - 1].strip().startswith("///")
                         or lignes[i - 1].strip().startswith("//")
                         or lignes[i - 1].strip().startswith("[")):
            i -= 1
        debut = len("\n".join(lignes[:i]))
        if debut: debut += 1
        ouvrante = nu.find("{", m.end())
        if ouvrante < 0: continue
        prof, j = 0, ouvrante
        while j < len(nu):
            if nu[j] == "{": prof += 1
            elif nu[j] == "}":
                prof -= 1
                if prof == 0: break
            j += 1
        resultats.append((m.group(1), source[debut:j + 1]))
    return resultats


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
    return ".".join(commun) if commun else None


def inventaire():
    """domaine(projet) -> tout ce qu'il faut savoir."""
    infos = {}
    for p in sorted(os.listdir(CONTRATS)):
        if not p.endswith(".Contracts.Grpc"):
            continue
        d = os.path.join(CONTRATS, p)
        csproj = io.open(os.path.join(d, p + ".csproj"), encoding="utf-8").read()
        m = re.search(r'<Protobuf\s+Include="([^"]+)"', csproj)
        proto = m.group(1).replace("\\", "/") if m else None
        chemin_proto = os.path.normpath(os.path.join(d, proto)) if proto else None
        cs_ns = None
        if chemin_proto and os.path.exists(chemin_proto):
            mm = re.search(r'option\s+csharp_namespace\s*=\s*"([^"]+)"',
                           io.open(chemin_proto, encoding="utf-8").read())
            cs_ns = mm.group(1) if mm else None
        sources = [f for f in sorted(os.listdir(d)) if f.endswith(".cs")]
        ns = None
        if sources:
            ns = re.search(r'^namespace\s+([\w\.]+)',
                           io.open(os.path.join(d, sources[0]), encoding="utf-8").read(), re.M).group(1)
        refs = re.findall(r'<ProjectReference\s+Include="([^"]+)"', csproj)
        infos[p] = {
            "dir": d, "proto": chemin_proto, "cs_ns": cs_ns, "ns": ns,
            "sources": [os.path.join(d, f) for f in sources],
            "refs": [r.replace("\\", "/") for r in refs],
            "enregistrements": re.findall(r'public static IServiceCollection Add(\w+)GrpcClient',
                                          "\n".join(io.open(os.path.join(d, f), encoding="utf-8").read()
                                                    for f in sources)) if sources else [],
        }
    return infos


def proprietaires(infos):
    """projet de contrats -> dossier du .Api qui expose le serveur."""
    res = {}
    for base in ("services", "apps"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            if os.sep + "Grpc" + os.sep + "Services" + os.sep not in f:
                continue
            s = io.open(f, encoding="utf-8").read()
            for p, i in infos.items():
                # `using Proto = HBA.Merchants.Grpc.V1;` COMPTE AUTANT QUE LE using NU.
                # Six serveurs sur dix-sept importent leur stub sous un alias ; ne
                # chercher que la forme nue les rendait invisibles, et ce script les
                # aurait declares sans proprietaire.
                if i["cs_ns"] and re.search(
                        r'^using\s+(?:\w+\s*=\s*)?' + re.escape(i["cs_ns"]) + r'\s*;', s, re.M):
                    res[p] = (os.path.dirname(os.path.dirname(os.path.dirname(f))), f)
    return res


def consommateurs(infos):
    """projet de contrats -> [dossier Infrastructure]"""
    par_enregistrement = {}
    for p, i in infos.items():
        for e in i["enregistrements"]:
            par_enregistrement[e] = p
    res = collections.defaultdict(list)
    for base in ("services", "apps"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            if os.path.basename(f) != "DependencyInjection.cs" or os.sep + "Grpc" not in f:
                continue
            if not os.path.dirname(f).endswith(os.path.join("Infrastructure", "Grpc")):
                continue
            s = io.open(f, encoding="utf-8").read()
            infra = os.path.dirname(os.path.dirname(f))
            for m in re.finditer(r'services\.Add(\w+)GrpcClient\(', s):
                p = par_enregistrement.get(m.group(1))
                if p and infra not in res[p]:
                    res[p].append(infra)
    return res


def main():
    infos = inventaire()
    prop = proprietaires(infos)
    cons = consommateurs(infos)
    print(f"{len(infos)} projets de contrats gRPC\n")
    for p in sorted(infos):
        i = infos[p]
        o = os.path.basename(prop[p][0]) if p in prop else "—"
        c = [os.path.basename(x) for x in cons.get(p, [])]
        print(f"  {p:36} proto={'oui' if i['proto'] else 'NON':3} serveur={o:34} {len(c)} consommateurs")
        if not c and p not in prop:
            print(f"      MORT : ni serveur ni consommateur")
    if not SEC:
        print("\nSIMULATION.")
        return infos, prop, cons
    return infos, prop, cons


if __name__ == "__main__":
    main()


# ─────────────────────────────────────────────────────────────────────────────
# APPLICATION
# ─────────────────────────────────────────────────────────────────────────────

PAQUETS_CLIENT = ["Grpc.Net.ClientFactory", "Google.Protobuf"]
PAQUETS_SERVEUR = ["Grpc.AspNetCore", "Google.Protobuf"]

ENTETE = """// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `{origine}` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en {copies} exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════
"""


def internaliser(texte):
    texte = re.sub(r'^public\s+(sealed|static|abstract|partial)\s+',
                   r'internal \1 ', texte, flags=re.M)
    texte = re.sub(r'^public\s+(class|record|struct|interface|enum)\s+',
                   r'internal \1 ', texte, flags=re.M)
    return texte


def rel(depuis_csproj, vers):
    return os.path.relpath(vers, os.path.dirname(depuis_csproj)).replace("/", "\\")


def patcher_csproj(chemin, proto, mode, paquets, refs_projets, retirer):
    s = io.open(chemin, encoding="utf-8").read()
    for r in retirer:
        s = re.sub(r'[ \t]*<ProjectReference\s+Include="[^"]*' + re.escape(r) + r'\.csproj"\s*/>\s*\n', "", s)
    ajouts = []
    chemin_proto = rel(chemin, proto)
    if chemin_proto not in s:
        ajouts.append(
            "  <!-- CE SERVICE COMPILE LUI-MEME LE CONTRAT (lot D). `Access=\"Internal\"` :\n"
            "       voir l'encadre des fichiers de Grpc/. -->\n"
            "  <ItemGroup>\n"
            f'    <Protobuf Include="{chemin_proto}" GrpcServices="{mode}" Access="Internal" />\n'
            "  </ItemGroup>\n")
    manquants = [p for p in paquets if f'"{p}"' not in s]
    bloc_paquets = ""
    if manquants:
        bloc_paquets = "  <ItemGroup>\n" + "".join(
            f'    <PackageReference Include="{p}" />\n' for p in manquants) + "  </ItemGroup>\n"
    if "Grpc.Tools" not in s:
        bloc_paquets += ("  <ItemGroup>\n"
                         '    <PackageReference Include="Grpc.Tools">\n'
                         "      <PrivateAssets>all</PrivateAssets>\n"
                         "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                         "    </PackageReference>\n  </ItemGroup>\n")
    if bloc_paquets:
        ajouts.append(bloc_paquets)
    manquantes = []
    for r in refs_projets:
        nom = os.path.basename(r)
        if nom in s:
            continue
        manquantes.append(rel(chemin, r))
    if manquantes:
        ajouts.append("  <ItemGroup>\n" + "".join(
            f'    <ProjectReference Include="{m}" />\n' for m in sorted(set(manquantes))) + "  </ItemGroup>\n")
    if ajouts:
        s = s.replace("</Project>", "\n" + "\n".join(ajouts) + "\n</Project>")
    io.open(chemin, "w", encoding="utf-8").write(s)


def csproj_de(dossier):
    for f in os.listdir(dossier):
        if f.endswith(".csproj"):
            return os.path.join(dossier, f)
    return None


def appliquer():
    infos = inventaire()
    prop = proprietaires(infos)
    cons = consommateurs(infos)

    declares = set()
    for base in ("services", "apps", "shared"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            for m in re.finditer(r'^namespace\s+([\w\.]+)',
                                 io.open(f, encoding="utf-8", errors="replace").read(), re.M):
                declares.add(m.group(1))

    ecrits = 0
    for p, i in sorted(infos.items()):
        if not i["sources"]:
            continue
        blocs, usings = [], []
        for f in i["sources"]:
            s = io.open(f, encoding="utf-8").read()
            usings += re.findall(r'^using\s+[^\n]+;\s*$', s, re.M)
            blocs += blocs_de_types(s)
        mappings = [b for b in blocs if re.search(r'Mapping|Parsing|Conversion', b[0])]
        clients = [b for b in blocs if b not in mappings]

        ancetres = []
        parts = i["ns"].split(".")
        for k in range(1, len(parts)):
            ns = ".".join(parts[:k])
            if ns in declares:
                ancetres.append(f"using {ns};")
        base_usings = sorted(set(usings + ancetres))
        copies = len(cons.get(p, [])) + (1 if p in prop else 0)

        cibles = []
        if p in prop:
            cibles.append((prop[p][0], "Server", PAQUETS_SERVEUR, True))
        for infra in cons.get(p, []):
            cibles.append((infra, "Client", PAQUETS_CLIENT, False))

        for dossier, mode, paquets, est_serveur in cibles:
            racine_ns = racine_de_namespace(dossier)
            entete = ENTETE.format(origine=i["ns"], copies=copies)

            if mappings:
                os.makedirs(os.path.join(dossier, "Grpc", "Mappers"), exist_ok=True)
                l = os.path.join(dossier, "Grpc", "Mappers", "LISEZMOI.md")
                if os.path.exists(l): os.remove(l)
                contenu = ("\n".join(base_usings) + "\n\n" + entete
                           + f"\nnamespace {racine_ns}.Grpc.Mappers;\n\n"
                           + "\n\n".join(internaliser(t) for _, t in mappings) + "\n")
                io.open(os.path.join(dossier, "Grpc", "Mappers",
                                     p.replace("HBA.", "").replace(".Contracts.Grpc", "") + "Mappers.cs"),
                        "w", encoding="utf-8").write(contenu)
                ecrits += 1

            if not est_serveur and clients:
                os.makedirs(os.path.join(dossier, "Grpc", "Clients"), exist_ok=True)
                l = os.path.join(dossier, "Grpc", "Clients", "LISEZMOI.md")
                if os.path.exists(l): os.remove(l)
                us = base_usings + ([f"using {racine_ns}.Grpc.Mappers;"] if mappings else [])
                contenu = ("\n".join(sorted(set(us))) + "\n\n" + entete
                           + f"\nnamespace {racine_ns}.Grpc.Clients;\n\n"
                           + "\n\n".join(internaliser(t) for _, t in clients) + "\n")
                io.open(os.path.join(dossier, "Grpc", "Clients",
                                     p.replace("HBA.", "").replace(".Contracts.Grpc", "") + "Client.cs"),
                        "w", encoding="utf-8").write(contenu)
                ecrits += 1

            csproj = csproj_de(dossier)
            refs = [os.path.normpath(os.path.join(i["dir"], r)) for r in i["refs"]]
            patcher_csproj(csproj, i["proto"], mode, paquets, refs, [p])

            # le module gRPC du consommateur importait le namespace de l'assemblage dissous
            di = os.path.join(dossier, "Grpc", "DependencyInjection.cs")
            if os.path.exists(di):
                s = io.open(di, encoding="utf-8").read()
                s = s.replace(f"using {i['ns']};", f"using {racine_ns}.Grpc.Clients;")
                io.open(di, "w", encoding="utf-8").write(s)

        # le serveur : plus de using vers l'assemblage dissous, et il devient internal
        if p in prop:
            _, fichier = prop[p]
            racine_ns = racine_de_namespace(prop[p][0])
            s = io.open(fichier, encoding="utf-8").read()
            remplacement = f"using {racine_ns}.Grpc.Mappers;" if mappings else ""
            s = re.sub(r'^using\s+' + re.escape(i["ns"]) + r'\s*;\s*\n',
                       (remplacement + "\n") if remplacement else "", s, flags=re.M)
            s = re.sub(r'^public\s+sealed\s+class', "internal sealed class", s, flags=re.M)
            io.open(fichier, "w", encoding="utf-8").write(s)

    # plus aucune reference aux assemblages dissous, nulle part
    retires = 0
    for base in ("services", "apps", "tests", "tools", "shared"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            for f in fs:
                if not f.endswith(".csproj"):
                    continue
                chemin = os.path.join(d, f)
                s = io.open(chemin, encoding="utf-8").read()
                s2 = re.sub(r'[ \t]*<ProjectReference\s+Include="[^"]*\.Contracts\.Grpc\.csproj"\s*/>\s*\n', "", s)
                if s2 != s:
                    io.open(chemin, "w", encoding="utf-8").write(s2)
                    retires += 1

    # la solution
    sln = os.path.join(RACINE, "HBA.sln")
    s = io.open(sln, encoding="utf-8").read()
    guids = re.findall(r'Project\("\{[^}]+\}"\)\s*=\s*"[^"]*\.Contracts\.Grpc",\s*"[^"]*",\s*"\{([^}]+)\}"', s)
    s = re.sub(r'Project\("\{[^}]+\}"\)\s*=\s*"[^"]*\.Contracts\.Grpc".*?EndProject\s*\n', "", s, flags=re.S)
    for g in guids:
        s = re.sub(r'^\s*\{' + re.escape(g) + r'\}\..*\n', "", s, flags=re.M)
    io.open(sln, "w", encoding="utf-8").write(s)

    # et les projets eux-memes
    supprimes = 0
    for p in sorted(infos):
        shutil.rmtree(infos[p]["dir"])
        supprimes += 1

    print(f"\n{ecrits} fichiers ecrits, {retires} csproj nettoyes, {len(guids)} entrees de solution retirees, "
          f"{supprimes} projets supprimes.")


if __name__ == "__main__" and SEC:
    appliquer()
