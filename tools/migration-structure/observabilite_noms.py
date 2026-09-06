# -*- coding: utf-8 -*-
"""
REPARATION DU LOT OBSERVABILITE — DEUX DEFAUTS, UNE SEULE CAUSE.

DEFAUT 1 — LES NOMS DE SONDES SE COLLISIONNENT DANS LES HOTES COMPOSES.
    `AjouterObservabilite<X>` enregistrait `AddCheck<SondeDuCache>("cache")`.
    Trois hotes montent plusieurs modules dans un seul processus :
      HBA.Financial.Api      payments + billing + wallet
      HBA.Engagement.Api     reviews  + recommendations + wishlist
      HBA.Communication.Api  messaging + notifications
    `DefaultHealthCheckService.ValidateRegistrations` refuse deux sondes de meme
    nom, et il le fait a la RESOLUTION du service — donc au premier
    `MapHealthChecks`, pas a l'enregistrement. D'ou 26 tests qui tombent sur
    `Duplicate health checks were registered with the name(s): cache, kafka`.

    LA GARDE EXISTAIT DEJA, DANS LE LOT CACHE, ET N'A PAS ETE REPORTEE ICI.
    Le module de cache protege ses enregistrements par `TryAdd` en nommant les
    trois hotes composes. Les NOMS de sondes n'ont pas d'equivalent `TryAdd` :
    la seule reponse est de les rendre uniques par module.

    ET C'EST LA LECTURE JUSTE, pas seulement le contournement : dans un hote
    compose, chaque module a SON cache et SON courtier. Un nom partage cacherait
    la panne d'un module derriere la sante d'un autre.

DEFAUT 2 — LE CORPS DE LA METHODE EST EMIS DEUX FOIS DANS 15 FICHIERS.
    Le correctif qui a ajoute les metriques neutres remplacait le marqueur
    `services.AddHealthChecks()`, present DEUX fois dans les fichiers qui ont un
    client gRPC. Resultat : metriques et `AddSingleton<SondeDeKafka>` en double.
    Sans consequence a l'execution — mais le fichier ne se lit plus.

CE QUE CE SCRIPT NE COUVRE PAS :
    - la sonde `database`, posee une seule fois par `AddHbaService` avec le
      `TDbContext` de l'hote : elle reste nommee `database` et ne collisionne pas.
    - `delivery-pricing-service`, qui pose ses propres `MapHealthChecks` sans
      passer par `AddHbaService`.
"""
import io, os, re, sys

RACINE = os.path.expanduser("~/mnt/HBA")


def kebab(court):
    return re.sub(r"(?<!^)(?=[A-Z])", "-", court).lower()


CORPS = """        // LES METRIQUES NEUTRES DE CE SERVICE.
        //
        // `TryAdd` : un service qui compte vraiment quelque chose enregistre SON
        // implementation avant d'appeler ce module, et elle gagne. Sans `TryAdd`,
        // le neutre l'ecraserait — et un compteur muet ne se voit qu'en cherchant
        // un chiffre qui n'arrive jamais.
        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // SINGLETON, ET C'EST LA CONDITION POUR QUE L'ENCADRE DE LA SONDE SOIT VRAI.
        //
        // `AddCheck<T>` resout par `GetServiceOrCreateInstance` : sans cet
        // enregistrement, une NOUVELLE sonde serait construite a chaque appel de
        // l'orchestrateur — donc un `IAdminClient` et une connexion au courtier
        // toutes les quelques secondes. La sonde couterait alors plus que ce
        // qu'elle mesure.
        services.AddSingleton<SondeDeKafka>();

        // LES NOMS PORTENT LE MODULE, ET CE N'EST PAS DECORATIF.
        //
        // Trois hotes montent plusieurs modules dans un seul processus
        // (`HBA.Financial.Api` en monte trois). Deux sondes nommees `cache` dans
        // le meme conteneur, et `DefaultHealthCheckService` refuse de se
        // construire — au premier `MapHealthChecks`, pas a l'enregistrement.
        //
        // Le suffixe n'est pas qu'un evitement de collision : dans un hote
        // compose, chaque module a SON cache et SON courtier. Un nom partage
        // cacherait la panne d'un module derriere la sante d'un autre.
        //
        // Le verdict de l'orchestrateur ne change pas : `/health/ready` agrege
        // sur le TAG `ready`, jamais sur le nom.
        services.AddHealthChecks()
            .AddCheck<SondeDuCache>("cache-{slug}", tags: ["ready"])
            .AddCheck<SondeDeKafka>("kafka-{slug}", tags: ["ready"]);
{grpc}
        return services;
    }}
}}
"""

GRPC = """
        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre. Une sonde de
        // disponibilite qui tombe avec un voisin transforme une panne en N pannes.
        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc-{slug}", tags: ["dependencies"]);
"""


def main():
    fichiers = []
    for base, _, noms in os.walk(RACINE):
        if os.sep + "Observability" in base and os.path.basename(base) == "Observability":
            c = os.path.join(base, "DependencyInjection.cs")
            if os.path.exists(c):
                fichiers.append(c)

    touches = 0
    for chemin in sorted(fichiers):
        s = io.open(chemin, encoding="utf-8").read()
        m = re.search(r"public static IServiceCollection AjouterObservabilite(\w+)\(", s)
        if not m:
            print("IGNORE (pas de methode) : " + chemin)
            continue
        court = m.group(1)
        slug = kebab(court)
        # LA PRESENCE SE LIT SUR LA CLASSE, PAS SUR SON NOM DANS LE TEXTE.
        # `"SondeDesDestinationsGrpc" in s` matchait la ligne de documentation
        # du tag `dependencies`, presente dans les 26 fichiers — donc 11
        # services sans client gRPC recevaient un AddCheck sur une classe
        # absente. Ici on demande au disque si la sonde EXISTE.
        sondes = os.path.join(os.path.dirname(chemin), "HealthChecks")
        a_du_grpc = os.path.isdir(sondes) and any(
            "class SondeDesDestinationsGrpc" in io.open(
                os.path.join(sondes, f), encoding="utf-8").read()
            for f in os.listdir(sondes) if f.endswith(".cs"))

        # ON REECRIT LE CORPS ENTIER plutot que de rustiner : les deux defauts
        # viennent d'un remplacement de texte par marqueur, et un troisieme
        # remplacement par marqueur serait la meme erreur une fois de plus.
        tete = s[:s.index("(\n        this IServiceCollection services, IConfiguration configuration)\n    {\n")]
        tete += "(\n        this IServiceCollection services, IConfiguration configuration)\n    {\n"

        if not a_du_grpc:
            tete = tete.replace(
                "///   dependencies  — les voisins. Jamais rouge : voir "
                "`SondeDesDestinationsGrpc`.\n", "")
        neuf = tete + CORPS.format(
            slug=slug, grpc=(GRPC.format(slug=slug) if a_du_grpc else ""))

        if neuf != s:
            io.open(chemin, "w", encoding="utf-8").write(neuf)
            touches += 1
            print("REECRIT %-24s %s" % (slug, os.path.relpath(chemin, RACINE)))

    print("\n%d fichiers reecrits sur %d." % (touches, len(fichiers)))


main()
