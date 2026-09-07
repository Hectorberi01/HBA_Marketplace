#!/usr/bin/env python3
"""
LE CACHE DESCEND DANS CHAQUE SERVICE — meme principe que gRPC (lot D).

CE QUI ETAIT PARTAGE : `HBA.Shared.Infrastructure.Caching` portait
`DistributedCacheService` et `NoOpCacheService`, et `AddBuildingBlocksInfrastructure`
les enregistrait POUR LES VINGT-SIX SERVICES A LA FOIS, avec le choix Redis /
memoire.

CE QUI CHANGE : chaque service porte son cache dans
`Caching/Redis/Services/`, et le branche lui-meme par `AjouterCache<Service>()`,
appele depuis son installeur.

CE QUI RESTE PARTAGE, ET POURQUOI : `ICacheService` reste dans
`HBA.Shared.Application.Abstractions`. C'est le PORT, pas l'adaptateur : la
couche Application de cinq services en depend, et la descendre obligerait a
changer ce dont depend le code metier pour un deplacement d'infrastructure. La
structure cible nomme `IDistributedCacheService.cs` dans le service ; c'est le
seul point ou je m'en ecarte, et le voici ecrit.

DEUX PIEGES TRAITES :

- HOTE COMPOSE. `HBA.Financial.Api` monte trois modules, donc appellera trois
  fois `AjouterCache*`. `TryAddSingleton` et une garde sur `IDistributedCache`
  evitent d'enregistrer trois fois la meme chose — le dernier gagnerait, ce qui
  serait sans effet ici mais masquerait un vrai doublon un jour.

- FABRIQUES DE CONCEPTION. Trois `*DbContextFactory` construisent leur contexte
  a la main avec `NoOpCacheService.Instance` pour `dotnet ef`. Elles pointent
  desormais vers la copie locale.
"""
import os, re, io, sys

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
PARTAGE = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure", "Caching")


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
    return ".".join(commun) if commun else None


def projets():
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) == "src":
                for x in sorted(dirs):
                    if x.endswith(".Infrastructure"):
                        yield os.path.join(d, x)


ENTETE = """// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Caching`.
//
// Le cache appartient desormais au service : il vit dans son Infrastructure, et
// c'est son propre module qui le branche. `ICacheService` reste en revanche dans
// `HBA.Shared.Application.Abstractions` — c'est le PORT dont depend la couche
// Application, pas l'adaptateur.
//
// `internal` : rien hors de cet assemblage n'a de raison de nommer cette classe.
// Le conteneur la resout par `ICacheService`.
//
// CE QUE ÇA COUTE : cette implementation existe en vingt-six exemplaires. Elles
// sont identiques aujourd'hui, et rien n'empeche qu'elles divergent — un TTL
// change ici ne changera rien ailleurs.
// ═════════════════════════════════════════════════════════════════════════════
"""


def main():
    sources = {}
    for f in sorted(os.listdir(PARTAGE)):
        if f.endswith(".cs"):
            sources[f] = io.open(os.path.join(PARTAGE, f), encoding="utf-8").read()
    print(f"source : {list(sources)}")

    cibles = list(projets())
    print(f"{len(cibles)} projets d'infrastructure\n")
    if not SEC:
        for c in cibles:
            print("  ", os.path.basename(c))
        print("\nSIMULATION.")
        return

    for infra in cibles:
        racine_ns = racine_de_namespace(infra)
        court = os.path.basename(infra).replace("HBA.", "").replace(".Infrastructure", "").replace(".", "")
        dossier = os.path.join(infra, "Caching", "Redis", "Services")
        os.makedirs(dossier, exist_ok=True)
        for l in (os.path.join(dossier, "LISEZMOI.md"),
                  os.path.join(infra, "Caching", "Redis", "LISEZMOI.md")):
            if os.path.exists(l): os.remove(l)

        for nom, texte in sources.items():
            s = re.sub(r'^namespace\s+[\w\.]+;', f"namespace {racine_ns}.Caching.Redis.Services;", texte, flags=re.M)
            s = re.sub(r'^public\s+(sealed\s+|static\s+)?class', lambda m: "internal " + (m.group(1) or "") + "class",
                       s, flags=re.M)
            s = s.replace("namespace ", ENTETE + "\nnamespace ", 1)
            io.open(os.path.join(dossier, nom), "w", encoding="utf-8").write(s)

        # le module de cache du service
        di = f"""using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using {racine_ns}.Caching.Redis.Services;

namespace {racine_ns}.Caching.Redis;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CACHE DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// Il etait branche par `AddBuildingBlocksInfrastructure`, donc pour les
/// vingt-six services a la fois. Il l'est desormais par le service lui-meme.
///
/// REDIS ABSENT : ON RETOMBE EN MEMOIRE, MAIS BRUYAMMENT. Le repli reste possible
/// — un poste de developpement n'a pas toujours un Redis. Ce qu'on ne refait pas,
/// c'est le repli SILENCIEUX, celui qui se decouvre en production : le message
/// part sur la sortie standard, le conteneur n'etant pas encore construit.
///
/// UN PREFIXE COMMUN, ET NON UN PAR SERVICE. Les services partagent une instance
/// Redis. Le prefixe les isole d'un autre locataire, pas les uns des autres : les
/// cles sont deja nommees par domaine, et deux repliques du MEME service doivent
/// imperativement partager la leur.
///
/// LES GARDES `Try*` NE SONT PAS DU CONFORT. Trois hotes montent plusieurs
/// modules dans un seul processus — `HBA.Financial.Api` en monte trois. Sans
/// elles, `IDistributedCache` serait enregistre trois fois et le dernier
/// gagnerait : sans effet ici, mais ce serait exactement la forme d'un vrai
/// doublon qu'on ne verrait plus.
///
/// CE QUE ÇA NE COUVRE PAS : la chaine de connexion reste lue dans
/// « Redis:ConnectionString », donc dans la configuration du deploiement. Un
/// service ne choisit pas SON Redis — il choisit ce qu'il en fait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{{
    public static IServiceCollection AjouterCache{court}(
        this IServiceCollection services, IConfiguration configuration)
    {{
        if (services.All(d => d.ServiceType != typeof(IDistributedCache)))
        {{
            var redis = configuration["Redis:ConnectionString"];

            if (string.IsNullOrWhiteSpace(redis))
            {{
                Console.WriteLine(
                    "[HBA] Redis absent — cache EN MEMOIRE, par instance. "
                    + "Toute invalidation ne touchera que le processus qui l'a declenchee : "
                    + "les autres repliques serviront des valeurs perimees jusqu'au TTL. "
                    + "Renseignez « Redis:ConnectionString » hors developpement.");

                services.AddDistributedMemoryCache();
            }}
            else
            {{
                services.AddStackExchangeRedisCache(options =>
                {{
                    options.Configuration = redis;
                    options.InstanceName = "hba:";
                }});
            }}
        }}

        // LE LOGGER EST RESOLU EN OPTIONNEL, ET C'EST DELIBERE.
        //
        // Le cache journalise ses pannes (Redis injoignable, invalidation ratee).
        // Exiger un `ILogger` ferait echouer tout conteneur monte sans
        // `AddLogging()` — c'est le cas de plusieurs harnais de tests, qui
        // n'installent qu'un module. Le cache aurait alors casse des tests qui
        // n'ont rien a voir avec lui.
        services.TryAddSingleton<ICacheService>(sp => new DistributedCacheService(
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetService<ILogger<DistributedCacheService>>() ?? NullLogger<DistributedCacheService>.Instance));

        return services;
    }}
}}
"""
        io.open(os.path.join(infra, "Caching", "Redis", "DependencyInjection.cs"), "w", encoding="utf-8").write(di)

        # paquets
        csproj = [os.path.join(infra, f) for f in os.listdir(infra) if f.endswith(".csproj")][0]
        s = io.open(csproj, encoding="utf-8").read()
        manquants = [p for p in ("Microsoft.Extensions.Caching.StackExchangeRedis",
                                 "Microsoft.Extensions.Caching.Memory") if f'"{p}"' not in s]
        if manquants:
            bloc = ("\n  <!-- LE CACHE DE CE SERVICE (Caching/Redis/). Ces paquets venaient\n"
                    "       transitivement de HBA.Shared.Infrastructure ; ils sont explicites\n"
                    "       depuis que l'implementation est ici. -->\n  <ItemGroup>\n"
                    + "".join(f'    <PackageReference Include="{p}" />\n' for p in manquants)
                    + "  </ItemGroup>\n")
            s = s.replace("</Project>", bloc + "\n</Project>")
            io.open(csproj, "w", encoding="utf-8").write(s)

        # l'installeur du module appelle le cache
        for f in os.listdir(infra):
            if not f.endswith("ModuleInstaller.cs"):
                continue
            p = os.path.join(infra, f)
            s = io.open(p, encoding="utf-8").read()
            if f"AjouterCache{court}(" in s:
                continue
            m = re.search(r'(public void Install\(IServiceCollection services, IConfiguration configuration\)\s*\n\s*\{\s*\n)', s)
            if not m:
                print(f"  point d'accroche introuvable : {p}")
                continue
            appel = ("        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le\n"
                     "        // socle pour les vingt-six services a la fois ; il l'est desormais ici.\n"
                     f"        services.AjouterCache{court}(configuration);\n\n")
            s = s[:m.end()] + appel + s[m.end():]
            besoin = f"using {racine_ns}.Caching.Redis;"
            if besoin not in s:
                usings = list(re.finditer(r'^using\s+[^\n]+;\s*$', s, re.M))
                s = s[:usings[-1].end()] + "\n" + besoin + s[usings[-1].end():]
            io.open(p, "w", encoding="utf-8").write(s)

    print("copies et modules ecrits.")


if __name__ == "__main__":
    main()
