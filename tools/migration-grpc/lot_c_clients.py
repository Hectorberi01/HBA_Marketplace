#!/usr/bin/env python3
"""
LOT C DE LA MIGRATION gRPC — le cablage des clients descend dans Infrastructure.

CE QUE CE SCRIPT FAIT.

Les 51 enregistrements `Add<X>GrpcClient(configuration)` vivaient dans les
`Program.cs` (48) et dans trois installeurs. Ils descendent dans le module gRPC
du service appelant, `Infrastructure/Grpc/DependencyInjection.cs`, derriere un
point d'entree unique `AjouterClientsGrpc<Service>()` — meme forme que
`AjouterMessagerie<Service>()`.

CE QU'IL NE FAIT PAS, ET C'EST DELIBERE.

Il ne DUPLIQUE PAS les adaptateurs ni les mappings : ils restent, pour l'instant,
dans `shared/contracts/HBA.<X>.Contracts.Grpc`. La raison est ecrite dans les
LISEZMOI qu'il pose dans `Grpc/Clients/` et `Grpc/Mappers/` — un adaptateur
implemente l'INTERFACE ENTIERE `I<X>ModuleApi`, donc une copie par consommateur
serait une copie integrale, sans reduction possible.

Les commentaires qui precedent chaque enregistrement voyagent avec lui : dans ce
depot, la raison d'un cablage est dans le bloc qui le precede, et separer les deux
produit deux mensonges d'un coup.
"""
import os, re, io, sys, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv

APPEL = re.compile(
    r'^([ \t]*)(builder\.Services|services)\.Add(\w+)GrpcClient\((builder\.Configuration|configuration)\);[ \t]*$',
    re.M)


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
    while len(commun) > 1 and os.path.isdir(os.path.join(dossier, commun[-1])):
        commun = commun[:-1]
    return ".".join(commun) if commun else None


def bloc_de_commentaire_avant(source, debut_ligne):
    """Remonte le bloc de commentaire contigu au-dessus d'une ligne."""
    lignes = source[:debut_ligne].split("\n")
    i = len(lignes) - 1
    while i > 0 and lignes[i - 1].strip().startswith("//"):
        i -= 1
    return "\n".join(lignes[i:len(lignes) - 1])


def main():
    # 1. ou est definie chaque extension Add<X>GrpcClient
    definition = {}
    for f in fichiers_cs(os.path.join(RACINE, "shared", "contracts")):
        s = io.open(f, encoding="utf-8", errors="replace").read()
        ns = re.search(r'^namespace\s+([\w\.]+)', s, re.M)
        for m in re.finditer(r'public static IServiceCollection Add(\w+)GrpcClient', s):
            definition[m.group(1)] = (ns.group(1) if ns else None,
                                      os.path.basename(os.path.dirname(f)))

    # 2. les sites d'appel, groupes par service appelant
    sites = collections.defaultdict(list)
    for base in ("services", "bff"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            s = io.open(f, encoding="utf-8", errors="replace").read()
            if "GrpcClient(" not in s:
                continue
            for m in APPEL.finditer(s):
                parts = os.path.relpath(f, RACINE).split(os.sep)
                service = os.sep.join(parts[:3]) if parts[0] == "services" else os.sep.join(parts[:2])
                sites[service].append((f, m.group(3)))

    plan = []
    for service, appels in sorted(sites.items()):
        src = os.path.join(RACINE, service, "src")
        infras = [d for d in os.listdir(src)
                  if d.endswith(".Infrastructure") and os.path.isdir(os.path.join(src, d))]
        if not infras:
            print(f"  AUCUNE Infrastructure pour {service} — ignore")
            continue
        # PLUSIEURS INFRASTRUCTURES DANS UN HOTE COMPOSE : on prend celle qui porte
        # deja le module Kafka ET le plus de code. C'est la ou vivent les
        # gestionnaires qui consomment ces interfaces.
        def poids(d):
            n = sum(1 for _ in fichiers_cs(os.path.join(src, d)))
            kafka = os.path.isdir(os.path.join(src, d, "Messaging", "Kafka"))
            return (kafka, n)
        infra = max(infras, key=poids)
        domaines = sorted({d for _, d in appels})
        plan.append((service, os.path.join(src, infra), domaines,
                     sorted({f for f, _ in appels})))

    for service, infra, domaines, fichiers in plan:
        print(f"  {service:44} -> {os.path.basename(infra):42} {len(domaines)} clients : {', '.join(domaines)}")

    if not SEC:
        print("\nSIMULATION. Relancer avec --ecrire pour appliquer.")
        return

    for service, infra, domaines, fichiers in plan:
        racine_ns = racine_de_namespace(infra)
        court = os.path.basename(infra).replace("HBA.", "").replace(".Infrastructure", "").replace(".", "")
        methode = "AjouterClientsGrpc" + court

        # ---- extraire les appels et leurs commentaires, les retirer des sources
        morceaux = []
        # UN SEUL POINT D'APPEL, MEME QUAND LES ENREGISTREMENTS ETAIENT DISPERSES.
        #
        # return-refund en avait deux dans `Program.cs` et deux dans son installeur.
        # Ecrire l'appel unique dans les DEUX fichiers enregistrerait les clients
        # deux fois : `AddGrpcClient` poserait deux fois la meme configuration de
        # `HttpClient`, et le dernier `AddScoped` gagnerait — en silence. On choisit
        # le composition root quand il existe, l'installeur sinon.
        hote = next((f for f in fichiers if os.path.basename(f) == "Program.cs"), fichiers[0])
        for f in fichiers:
            s = io.open(f, encoding="utf-8").read()
            nouveaux = []
            decalage = 0
            premier = None
            for m in list(APPEL.finditer(s)):
                debut_ligne = m.start()
                commentaire = bloc_de_commentaire_avant(s, debut_ligne)
                bloc_debut = debut_ligne - (len(commentaire) + 1 if commentaire else 0)
                morceaux.append((m.group(3), commentaire))
                nouveaux.append((bloc_debut, m.end()))
            if not nouveaux:
                continue
            if premier is None:
                premier = nouveaux[0][0]
            # remplacement : le premier bloc devient l'appel unique, les autres partent
            indent = "        " if os.path.basename(f) != "Program.cs" else ""
            recepteur = "services" if os.path.basename(f) != "Program.cs" else "builder.Services"
            config = "configuration" if os.path.basename(f) != "Program.cs" else "builder.Configuration"
            remplacement = (
                f"{indent}// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).\n"
                f"{indent}//\n"
                f"{indent}// Ils etaient enregistres ici, un par un, chacun precede de la raison\n"
                f"{indent}// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers\n"
                f"{indent}// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait\n"
                f"{indent}// produit deux mensonges : un commentaire sans code, du code sans raison.\n"
                f"{indent}{recepteur}.{methode}({config});"
            )
            out, curseur, ecrit = [], 0, False
            for a, b in nouveaux:
                out.append(s[curseur:a])
                if not ecrit and f == hote:
                    out.append(remplacement)
                    ecrit = True
                curseur = b
            out.append(s[curseur:])
            s2 = "".join(out)
            besoin = f"using {racine_ns}.Grpc;"
            if f == hote and besoin not in s2:
                usings = list(re.finditer(r'^using\s+[^\n]+;\s*$', s2, re.M))
                if usings:
                    s2 = s2[:usings[-1].end()] + "\n" + besoin + s2[usings[-1].end():]
                else:
                    s2 = besoin + "\n" + s2
            io.open(f, "w", encoding="utf-8").write(s2)

        # ---- le module gRPC du service
        vus, ordonnes = set(), []
        for dom, com in morceaux:
            if dom not in vus:
                vus.add(dom)
                ordonnes.append((dom, com))

        usings = sorted({definition[d][0] for d, _ in ordonnes if d in definition and definition[d][0]})
        usings += ["Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection"]

        corps = []
        for dom, com in ordonnes:
            if com.strip():
                corps.append("\n".join("        " + l.strip() if l.strip() else "" for l in com.split("\n")))
            corps.append(f"        services.Add{dom}GrpcClient(configuration);\n")

        contenu = "\n".join(f"using {u};" for u in sorted(set(usings))) + "\n\n"
        contenu += f"""namespace {racine_ns}.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les {len(ordonnes)} clients de ce service étaient
/// enregistrés dans `Program.cs`, entre le câblage HTTP et celui de la base.
/// Savoir « qui ce service appelle » supposait de lire un fichier de cent lignes
/// où trois préoccupations se mélangeaient. C'est désormais une liste, ici.
///
/// LA LIGNE DE PARTAGE, LA MÊME QUE POUR KAFKA : CE DOSSIER PORTE LA POLITIQUE DU
///     SERVICE, LE SOCLE PARTAGÉ PORTE LE TYPE ET LE PROTOCOLE.
///
///   Configuration/  ce que CE service appelle, et avec quelle échéance
///   Clients/        les adaptateurs — voir le LISEZMOI, ils ne sont pas encore ici
///   Mappers/        les traductions — même chose
///
/// CE QUE CE MODULE NE COUVRE PAS.
///
/// Il ne garantit pas d'être appelé. Contrairement à Kafka, où un module oublié
/// produit un SILENCE — le service démarre et n'écoute rien —, un client gRPC
/// oublié fait échouer la résolution de `I&lt;X&gt;ModuleApi` au démarrage, donc
/// bruyamment. C'est pour cette raison qu'il n'y a pas de `GardeDeCablage` ici :
/// elle vérifierait ce que le conteneur vérifie déjà.
///
/// LE CAS QUI ÉCHAPPE À CE RAISONNEMENT est l'hôte composé, où le même
/// `I&lt;X&gt;ModuleApi` peut être fourni à la fois par le module local du domaine
/// et par un client gRPC : le dernier enregistré gagne, en silence. Aucun service
/// n'est dans ce cas aujourd'hui — vérifié — et c'est la garde qui reste à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{{
    /// <summary>
    /// Branche les clients gRPC du service. Appelée par le composition root.
    /// </summary>
    public static IServiceCollection {methode}(
        this IServiceCollection services, IConfiguration configuration)
    {{
        services.AjouterLesDestinationsGrpc();

{chr(10).join(corps)}
        return services;
    }}
}}
"""
        os.makedirs(os.path.join(infra, "Grpc", "Configuration"), exist_ok=True)
        io.open(os.path.join(infra, "Grpc", "DependencyInjection.cs"), "w", encoding="utf-8").write(contenu)

        # ---- Configuration/DestinationsGrpc.cs : l'endroit des échéances
        listes = "\n".join(f"    ///   • {d}" for d, _ in ordonnes)
        conf = f"""using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.DependencyInjection;

namespace {racine_ns}.Grpc.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CE SERVICE APPELLE, ET AVEC QUELLE ÉCHÉANCE.
///
/// Services appelés :
{listes}
///
/// L'ÉCHÉANCE PAR DÉFAUT EST DE CINQ SECONDES, POSÉE CENTRALEMENT par
/// `InternalCallClientInterceptor` sur tout appel qui n'en porte pas. Elle reste
/// centrale parce que c'est elle qui rend l'oubli impossible : un canal gRPC
/// n'a AUCUN délai par défaut, contrairement à `HttpClient`.
///
/// CE FICHIER EST LE POINT DE SURCHARGE, ET IL EST VIDE — VOLONTAIREMENT.
///
/// Cinq secondes ne veulent pas dire la même chose pour un devis demandé pendant
/// qu'un acheteur attend sa page de paiement et pour un import de catalogue. Mais
/// choisir 800 ms plutôt que 5 s demande des MESURES que personne n'a prises :
/// poser des valeurs inventées ici ferait échouer des appels sains et la panne
/// serait imputée au service appelé.
///
/// Quand la mesure existera, la surcharge s'écrit ainsi, et nulle part ailleurs :
///
///     services.SurchargerLEcheanceGrpc("MerchantApi", TimeSpan.FromMilliseconds(800));
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DestinationsGrpc
{{
    public static IServiceCollection AjouterLesDestinationsGrpc(this IServiceCollection services)
        => services;
}}
"""
        io.open(os.path.join(infra, "Grpc", "Configuration", "DestinationsGrpc.cs"), "w", encoding="utf-8").write(conf)

        # ---- LISEZMOI des dossiers encore vides
        for dossier, texte in (
            ("Clients", "les adaptateurs `I<X>ModuleApi` -> stub gRPC"),
            ("Mappers", "les traductions proto <-> enregistrements de contrats"),
        ):
            os.makedirs(os.path.join(infra, "Grpc", dossier), exist_ok=True)
            io.open(os.path.join(infra, "Grpc", dossier, "LISEZMOI.md"), "w", encoding="utf-8").write(
                f"""# `{dossier}/` — vide aujourd'hui, et voici pourquoi

Ce dossier accueillera {texte} de ce service.

**Aujourd'hui, ils vivent dans `shared/contracts/HBA.<Domaine>.Contracts.Grpc`,
en un seul exemplaire par domaine**, partagé par tous les appelants.

## Ce qui bloque leur descente ici

Un adaptateur implémente `I<Domaine>ModuleApi` **en entier** — l'interface l'exige.
Une copie par service consommateur serait donc une copie INTÉGRALE, sans la
réduction qui la rendrait défendable : `merchant.proto` a neuf consommateurs, et
les neuf devraient porter les mêmes treize traductions.

Deux domaines sur quinze échappent à cette règle, parce qu'ils exposent DEUX
interfaces séparées :

- `HBA.Merchants` — `ISellerModuleApi` et `IMerchantAccessApi`
- `HBA.Deliveries` — `IDeliveryModuleApi` et `IDeliveryDispatchApi`

Là, un consommateur qui n'a besoin que d'une des deux peut n'en porter qu'une.
Partout ailleurs, réduire supposerait de découper `I<X>ModuleApi` par appelant —
un vrai travail de conception, pas un déplacement de fichiers.

## Ce qui est décidé, et ce qui ne l'est pas

Décidé : le CÂBLAGE descend ici — `Grpc/DependencyInjection.cs` et
`Grpc/Configuration/`. C'est ce qui rend la liste des dépendances d'un service
lisible en un fichier.

Pas décidé : la duplication des adaptateurs. Elle coûte environ 12 500 lignes
copiées contre 2 700 aujourd'hui, et elle ne peut pas être réduite comme prévu.
""")

        # ---- references de projet : le module a besoin des contrats gRPC
        csproj = [os.path.join(infra, f) for f in os.listdir(infra) if f.endswith(".csproj")]
        if csproj:
            p = csproj[0]
            s = io.open(p, encoding="utf-8").read()
            manquantes = []
            for dom, _ in ordonnes:
                if dom not in definition:
                    continue
                projet = definition[dom][1]
                if projet + ".csproj" in s:
                    continue
                rel = os.path.relpath(os.path.join(RACINE, "shared", "contracts", projet, projet + ".csproj"),
                                      infra).replace("/", "\\")
                manquantes.append(rel)
            if manquantes:
                bloc = ("\n  <!-- LES CLIENTS gRPC DE CE SERVICE (lot C). Les stubs et les adaptateurs\n"
                        "       vivent encore dans les assemblages de contrats ; le CABLAGE, lui, est\n"
                        "       ici, dans Grpc/. -->\n  <ItemGroup>\n"
                        + "\n".join(f'    <ProjectReference Include="{r}" />' for r in sorted(set(manquantes)))
                        + "\n  </ItemGroup>\n")
                s = s.replace("</Project>", bloc + "\n</Project>")
                io.open(p, "w", encoding="utf-8").write(s)

        print(f"  module ecrit : {os.path.relpath(infra, RACINE)}/Grpc  ({methode}, {len(ordonnes)} clients)")


if __name__ == "__main__":
    main()
