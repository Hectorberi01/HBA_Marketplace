#!/usr/bin/env python3
"""Porte un service sur la forme `Infrastructure/Messaging/Kafka/`.

Idempotent : relancer sur un service deja porte ne fait rien.
Ne compile rien — la verification reste statique.
"""
import os, re, sys, glob, json

RACINE = os.path.expanduser("~/mnt/HBA")

# ── Le domaine de chaque service, recopie de HbaTopics.DomaineParService ──────
DOMAINE = {
    "seller-service": "merchant", "cart-service": "commerce",
    "payment-service": "financial", "review-service": "engagement",
    "notification-service": "communication", "restaurant-service": "food",
    "identity-service": "identity", "user-service": "user",
    "catalog-service": "catalog", "inventory-service": "inventory",
    "order-service": "order", "delivery-service": "delivery",
    "media-service": "media", "promotion-service": "promotion",
    "food-cart-service": "food-cart", "food-order-service": "food-order",
    "return-refund-service": "return-refund",
    "delivery-pricing-service": "delivery-pricing",
    "driver-service": "driver", "route-service": "route",
    # wallet et billing n'ont pas d'hote a eux : HBA.Financial.Api les compose
    # avec payments, sous SERVICE_NAME=payment-service. Leurs evenements
    # partent donc sur `financial`.
    "wallet-service": "financial", "billing-service": "financial",
}

# Evenements publies ailleurs que par un service de `services/` :
# la passerelle et les squelettes n'en publient pas aujourd'hui.
HORS_SERVICES = {}


def lire(p):
    return open(p, encoding="utf-8", errors="replace").read()


def ecrire(p, t):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "w", encoding="utf-8").write(t)


def sources(service):
    for f in glob.glob(f"{service}/src/**/*.cs", recursive=True):
        if "/obj/" in f or "/bin/" in f:
            continue
        yield f


def inventaire():
    """Rend, pour chaque service : projet Infrastructure, DbContext, consumers, publications."""
    inv = {}
    for s in sorted(glob.glob(f"{RACINE}/services/*/*")):
        if not os.path.isdir(f"{s}/src"):
            continue
        nom = os.path.basename(s)
        infra = [p for p in glob.glob(f"{s}/src/*/*.csproj") if p.endswith(".Infrastructure.csproj")]
        if not infra:
            continue
        infra = infra[0]
        # ═════════════════════════════════════════════════════════════════════
        # LE NOM DU csproj N'EST PAS L'ESPACE DE NOMS, ET SEPT PROJETS LE
        #     PROUVENT.
        #
        # `HBA.Order.Infrastructure` declare `HBA.Orders.Infrastructure` ;
        # `HBA.Food.Restaurant.Infrastructure` declare `HBA.Food.Infrastructure`
        # ; il y en a cinq autres. Deduire l'espace de noms du nom de fichier
        # creait un membre `Order` dans l'espace `HBA` — qui MASQUE alors le type
        # `Order` partout dans l'assembly, avec un CS0118 par fichier de
        # persistance, tres loin du module qu'on venait d'ajouter.
        #
        # La racine se lit dans le code : le plus long prefixe commun aux
        # `namespace` deja declares par le projet.
        # ═════════════════════════════════════════════════════════════════════
        declarations = []
        for f in glob.glob(f"{os.path.dirname(infra)}/**/*.cs", recursive=True):
            if "/obj/" in f or "/bin/" in f or "/Messaging/Kafka/" in f:
                continue
            m = re.search(r"^namespace ([\w.]+);", lire(f), re.M)
            if m:
                declarations.append(m.group(1).split("."))
        ns = os.path.basename(infra)[:-7]
        if declarations:
            commun = []
            for i in range(min(len(d) for d in declarations)):
                niveau = {d[i] for d in declarations}
                if len(niveau) != 1:
                    break
                commun.append(niveau.pop())
            if commun:
                ns = ".".join(commun)
        court = "".join(ns.split(".")[1:-1])
        consumers, publie, ctx = {}, set(), None
        for f in sources(s):
            t = lire(f)
            for m in re.finditer(r"class\s+(\w+)\s*:?\s*[^\n{]*IIntegrationEventHandler<(\w+)>", t):
                consumers.setdefault(f, []).append((m.group(1), m.group(2)))
            for m in re.finditer(r"PublishAsync\(\s*\n?\s*new (\w+IntegrationEvent)", t):
                publie.add(m.group(1))
            # `AddOutboxProcessor<TContext>` apparaît aussi dans les helpers
            # generiques : un parametre de type n'est pas un DbContext.
            # UN APPEL EN COMMENTAIRE N'EST PAS UN APPEL.
            #
            # route-service porte « // services.AddOutboxProcessor<RouteDbContext>(); »
            # dans un commentaire : le service n'a pas de DbContext du tout, il
            # garde ses routes en memoire. Le prendre au mot engendrerait un
            # module qui reference un type inexistant.
            for l in t.splitlines():
                if l.lstrip().startswith(("//", "///", "*")):
                    continue
                m = re.search(r"AddOutboxProcessor<(\w+)>", l)
                if m and ctx is None and m.group(1).endswith("DbContext"):
                    ctx = m.group(1)
        inv[nom] = dict(chemin=s, infra=os.path.dirname(infra), ns=ns, court=court,
                        ctx=ctx, consumers=consumers, publie=sorted(publie))
    return inv


def carte_evenement_service(inv):
    """Evenement -> l'ENSEMBLE des services qui le publient.

    UN EVENEMENT PEUT AVOIR DEUX PRODUCTEURS, DONC DEUX SUJETS.

    `DriverVerifiedIntegrationEvent` est publie par driver-service ET par
    delivery-service. Ne retenir que le premier trouve — ce que faisait
    `setdefault` — a fait declarer a identity-service un seul des deux sujets :
    son gestionnaire de role restait muet une fois sur deux, sans erreur.
    """
    carte = {e: {s} for e, s in HORS_SERVICES.items()}
    for nom, d in inv.items():
        for e in d["publie"]:
            carte.setdefault(e, set()).add(nom)
    return carte


def sujets_pour(evenements, carte):
    """Les sujets a ecouter, et les evenements dont on ne sait pas d'ou ils viennent."""
    sujets, inconnus = set(), []
    for e in evenements:
        producteurs = carte.get(e)
        if not producteurs:
            inconnus.append(e)
            continue
        for p in producteurs:
            sujets.add(f"service.{DOMAINE.get(p, p.replace('-service', ''))}.v1")
    return sorted(sujets), sorted(inconnus)


def fichiers_de_contrats():
    """Les contrats vivent dans `shared/` ET dans le projet Contracts de chaque service."""
    vus = set()
    for motif in (f"{RACINE}/shared/**/*.cs", f"{RACINE}/services/*/*/src/*Contracts*/**/*.cs"):
        for f in glob.glob(motif, recursive=True):
            if "/obj/" in f or "/bin/" in f or f in vus:
                continue
            vus.add(f)
            yield f


def sans_descripteur():
    """Les evenements d'integration qui ne portent pas `[HbaEvent]`."""
    manquants = set()
    for f in fichiers_de_contrats():
        if "/obj/" in f:
            continue
        for m in re.finditer(r"(\[HbaEvent\([^\)]*\)\]\s*)?public sealed record (\w+IntegrationEvent)\b", lire(f)):
            if not m.group(1):
                manquants.add(m.group(2))
    return manquants


# ═════════════════════════════════════════════════════════════════════════════
# LES GABARITS
# ═════════════════════════════════════════════════════════════════════════════

RENVOI = ("/// La justification complete de cette forme est ecrite une seule fois, dans\n"
          "/// `user-service` — `Messaging/Kafka/DependencyInjection.cs` et\n"
          "/// `Messaging/Kafka/Outbox/OutboxUsers.cs`. Elle n'est pas recopiee ici.\n")


def g_sujets(ns, court, sujets, evenements, inconnus):
    lignes = ",\n".join(f'        "{s}"' for s in sujets)
    bloc_inc = ""
    if not sujets:
        bloc_inc += ("///\n/// CE SERVICE NE CONSOMME AUCUN EVENEMENT : la liste est vide, et c'est exact.\n"
                     "///\n/// CE QUE LA LISTE VIDE NE FAIT PAS ENCORE. Le consommateur partage traite\n"
                     "/// « liste vide » comme « pas de liste » et se rabat sur les vingt sujets de la\n"
                     "/// plateforme. Ce service continue donc a tout recevoir et a tout jeter — sans\n"
                     "/// gestionnaire, l'effet est nul, mais le trafic reste. Fermer cela demande de\n"
                     "/// distinguer les deux cas dans `KafkaIntegrationEventConsumer`, ce qui touche\n"
                     "/// les vingt-trois services a la fois : ce n'est pas fait ici.\n")
    if inconnus:
        bloc_inc = ("///\n/// AUCUN PRODUCTEUR CONNU POUR : " + ", ".join(inconnus) + ".\n"
                    "/// Ces evenements sont consommes par ce service et publies par PERSONNE dans\n"
                    "/// le depot. Le gestionnaire correspondant ne sera donc jamais appele. Aucun\n"
                    "/// sujet n'a ete ajoute pour eux : il n'y a rien a ecouter.\n")
    return f"""using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace {ns}.Messaging.Kafka.Configuration;

/// <summary>
/// LES SUJETS QUE CE SERVICE ECOUTE.
///
/// Ils sont deduits des {len(evenements)} evenement(s) declares dans `Consumers/` et du service
/// qui les publie — le sujet porte le domaine du PRODUCTEUR, jamais celui du
/// consommateur (voir `HbaTopics`).
///
/// SANS CETTE LISTE, `SubscribeTopics` reste vide et le consommateur partage se
/// rabat sur les vingt sujets de la plateforme : le service desserialise tout et
/// jette presque tout.
///
/// UN GESTIONNAIRE DANS `Consumers/` DONT LE SUJET MANQUE ICI NE SERA JAMAIS
/// APPELE, en silence. Aucun compilateur ne relie les deux.
{bloc_inc}/// </summary>
public static class Sujets{court}
{{
    private static readonly string[] Sujets =
    [
{lignes}
    ];

    internal static IServiceCollection AjouterSujets{court}(this IServiceCollection services)
    {{
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }}
}}
"""


def g_garde(ns, court):
    return f"""using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace {ns}.Messaging.Kafka.Configuration;

/// <summary>
/// REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.
///
/// L'outbox, l'inbox et les abonnements ne sont plus enregistres par
/// l'installeur — que le composition root appelle toujours — mais par
/// `AjouterMessagerie{court}()`, qu'il peut oublier. Un oubli ne casse RIEN de
/// visible : le service compile, demarre, sert ses routes, et n'emet ni ne
/// consomme plus rien.
///
/// La garde est enregistree par l'INSTALLEUR : elle doit exister quand ce
/// qu'elle verifie est absent.
///
/// `IHostedService` et non `BackgroundService` : une exception levee dans
/// `StartAsync` arrete l'hote, la meme dans `ExecuteAsync` est avalee.
///
/// CE QU'ELLE NE COUVRE PAS. Elle verifie que le module a ete appele, pas qu'il
/// est complet : un sujet ou un gestionnaire oublie passe sans rien dire.
/// </summary>
internal sealed class GardeDeCablage(IServiceProvider services) : IHostedService
{{
    public Task StartAsync(CancellationToken cancellationToken)
    {{
        if (services.GetService<AbonnementsKafka>() is null)
        {{
            throw new InvalidOperationException(
                "Le module de messagerie de ce service n'est pas enregistre : aucun "
                + "AbonnementsKafka dans le conteneur. Sans lui, ce service ne consomme "
                + "aucun evenement et son outbox n'est jamais videe. Ajouter "
                + "« builder.Services.AjouterMessagerie{court}(); » dans Program.cs.");
        }}

        return Task.CompletedTask;
    }}

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}}
"""


def g_outbox(ns, court, ctx, ns_ctx):
    return f"""using HBA.Shared.Infrastructure.Outbox;
using {ns_ctx};
using Microsoft.Extensions.DependencyInjection;

namespace {ns}.Messaging.Kafka.Outbox;

/// <summary>
/// L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.
///
/// Sans le processeur enregistre ci-dessous, les evenements listes dans
/// `Producers/EvenementsPublies` s'ecrivent en base et n'en sortent jamais. La
/// panne est SILENCIEUSE : la transaction metier reussit, l'appelant recoit son
/// 200, et le consommateur d'en face attend un message qui ne viendra pas.
///
/// CE FICHIER NE CONTIENT NI `OutboxMessage`, NI `OutboxProcessor`, NI
/// `OutboxRetryPolicy`. Ce sont des types du socle partage : `OutboxMessage` est
/// une entite EF dont la table est creee par les migrations de ce service, et
/// `ModuleDbContext.SaveChangesAsync` y ecrit dans la transaction metier. Une
/// copie locale divergerait de la colonne reelle en silence.
///
{RENVOI}/// </summary>
public static class Outbox{court}
{{
    internal static IServiceCollection AjouterOutbox{court}(this IServiceCollection services)
    {{
        services.AddOutboxProcessor<{ctx}>();
        return services;
    }}
}}
"""


def g_inbox(ns, court, ctx, ns_ctx):
    return f"""using HBA.Shared.Infrastructure.Inbox;
using {ns_ctx};
using Microsoft.Extensions.DependencyInjection;

namespace {ns}.Messaging.Kafka.Inbox;

/// <summary>
/// L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.
///
/// Kafka livre AU MOINS UNE FOIS. Sans cet enregistrement, un rebalancement de
/// partition ou une remise a zero d'offsets rejoue les evenements deja traites :
/// la table `consumer_inbox` existe dans le schema du service, et personne ne la
/// lit.
///
/// CE QU'ELLE NE COUVRE PAS. Elle dedoublonne la CONSOMMATION, pas les effets
/// deja partis : un evenement traite a moitie laisse un etat que rien ne
/// rattrape ici.
///
{RENVOI}/// </summary>
public static class Inbox{court}
{{
    internal static IServiceCollection AjouterInbox{court}(this IServiceCollection services)
    {{
        services.AddScoped<IConsumerInbox, EfConsumerInbox<{ctx}>>();
        return services;
    }}
}}
"""


def espaces_de_noms_evenements():
    """Evenement -> espace de noms ou il est declare."""
    carte = {}
    for f in fichiers_de_contrats():
        if "/obj/" in f:
            continue
        t = lire(f)
        m = re.search(r"^namespace ([\w.]+);", t, re.M)
        if not m:
            continue
        for e in re.findall(r"public sealed record (\w+IntegrationEvent)\b", t):
            carte[e] = m.group(1)
    return carte


def g_publies(ns, publie, ns_ev, sans_desc):
    connus = [e for e in publie if e not in sans_desc]
    legacy = [e for e in publie if e in sans_desc]
    usings = "\n".join(f"using {u};" for u in sorted({ns_ev[e] for e in publie if e in ns_ev}))
    l_connus = "\n".join(f"        typeof({e})," for e in connus) or "        // aucun"
    l_legacy = "\n".join(f"        typeof({e})," for e in legacy) or "        // aucun"
    return f"""{usings}
using HBA.Shared.Infrastructure.Kafka;

namespace {ns}.Messaging.Kafka.Producers;

/// <summary>
/// CE QUE CE SERVICE PUBLIE — LA MOITIE MANQUANTE DU MODULE.
///
/// `Consumers/` repond a « qu'est-ce que ce service ecoute ». Sans ce fichier,
/// « qu'est-ce qu'il emet » n'avait aucune reponse : il fallait chercher les
/// `PublishAsync` dans toute la couche Application, et on ne trouvait que ceux
/// qui existent — jamais celui qui manque.
///
/// LES PUBLICATIONS RESTENT DANS `Application`, ET IL NE FAUT PAS LES DEPLACER.
/// L'evenement doit etre mis en file LA OU LE FAIT METIER SE PRODUIT, pour que
/// `ModuleDbContext.SaveChangesAsync` le draine vers l'outbox DANS LA MEME
/// TRANSACTION. Ce dossier DECLARE, il ne publie pas.
///
/// CE QUE LA DECLARATION APPORTE. `HbaEventNaming.Describe` rend `null` quand un
/// evenement ne porte pas `[HbaEvent]`. La verification ci-dessous fait echouer
/// le DEMARRAGE plutot que de laisser decouvrir l'oubli a l'autre bout de la
/// plateforme, des semaines plus tard, dans une table qui reste vide.
///
/// CE QU'ELLE NE COUVRE PAS. Elle ne sait pas si un evenement publie quelque part
/// MANQUE a cette liste : rien ne relie un `PublishAsync` perdu dans Application
/// a ce fichier. La liste se tient a la main, et c'est sa faiblesse.
/// </summary>
public static class EvenementsPublies
{{
    /// <summary>Les evenements publies par ce service, descripteur `[HbaEvent]` compris.</summary>
    public static readonly IReadOnlyList<Type> Types =
    [
{l_connus}
    ];

    /// <summary>
    /// LES EVENEMENTS PUBLIES QUI N'ONT PAS ENCORE DE `[HbaEvent]`.
    ///
    /// Ils sont NOMMES ici plutot que passes sous silence. La verification les
    /// ignore volontairement : les faire echouer arreterait un service qui tourne
    /// aujourd'hui en production, pour un defaut qui n'a pas d'effet tant que
    /// `HbaEventNaming` n'est pas branche sur le fil (voir `HbaTopics`, §19.2).
    ///
    /// CE QU'ILS COUTENT DEJA. Leur nom d'evenement et leur sujet tombent sur le
    /// repli de `KafkaEventNaming`. Le jour ou le nommage canonique sera branche,
    /// ces evenements changeront de nom sur le fil — c'est cette liste qu'il
    /// faudra vider AVANT, pas apres.
    /// </summary>
    public static readonly IReadOnlyList<Type> SansDescripteur =
    [
{l_legacy}
    ];

    /// <summary>Refuse le demarrage si un evenement de `Types` n'a pas `[HbaEvent]`.</summary>
    internal static void VerifierLesDescripteurs()
    {{
        var manquants = Types
            .Where(type => HbaEventNaming.Describe(type) is null)
            .Select(type => type.Name)
            .ToArray();

        if (manquants.Length > 0)
        {{
            throw new InvalidOperationException(
                "Evenement(s) publie(s) sans descripteur [HbaEvent] : "
                + string.Join(", ", manquants)
                + ". Le nom et le sujet tomberaient sur un repli, et le consommateur "
                + "d'en face rejetterait le message en silence. Ajouter l'attribut, ou "
                + "inscrire l'evenement dans SansDescripteur en disant pourquoi.");
        }}
    }}
}}
"""


def g_di(ns, court, ns_ev, handlers, a_publie, a_inbox, a_outbox):
    """handlers : liste de (classe, evenement)."""
    # UN `using` VERS UN DOSSIER QUI N'EXISTE PAS NE COMPILE PAS.
    #
    # `Consumers` etait importe sans condition. Pour les neuf services qui ne
    # consomment rien, le dossier n'existe pas — un dossier vide se lit comme une
    # promesse tenue ailleurs — et l'espace de noms non plus : CS0234.
    us = {f"{ns}.Messaging.Kafka.Configuration", "Microsoft.Extensions.DependencyInjection",
          "HBA.Shared.IntegrationEvents"}
    if handlers:
        us.add(f"{ns}.Messaging.Kafka.Consumers")
    if a_publie:
        us.add(f"{ns}.Messaging.Kafka.Producers")
    if a_inbox:
        us.add(f"{ns}.Messaging.Kafka.Inbox")
    if a_outbox:
        us.add(f"{ns}.Messaging.Kafka.Outbox")
    us |= {ns_ev[t[1]] for t in handlers if t[1] in ns_ev}
    usings = "\n".join(f"using {u};" for u in sorted(us))

    corps = []
    if a_publie:
        corps.append("        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici\n"
                     "        // coute un demarrage, le decouvrir en face coute des semaines.\n"
                     "        EvenementsPublies.VerifierLesDescripteurs();\n")
    corps.append(f"        services.AjouterSujets{court}();")
    if a_outbox:
        corps.append(f"        services.AjouterOutbox{court}();")
    if a_inbox:
        corps.append(f"        services.AjouterInbox{court}();")
    if handlers:
        for classe, ev, commentaire in handlers:
            corps.append("")
            for l in commentaire:
                corps.append(f"        {l}")
            corps.append(f"        services.AddScoped<\n            IIntegrationEventHandler<{ev}>,\n            {classe}>();")
    corps = "\n".join(corps)

    absents = []
    if not a_publie:
        absents.append("/// `Producers/` est absent : ce service ne publie aucun evenement d'integration.")
    if not a_inbox:
        absents.append("/// `Inbox/` est absent : ce service ne consomme aucun evenement.")
    absents.append("/// `Serialization/` est absent : ce service n'a pas de convertisseur propre et")
    absents.append("/// utilise celui de `HBA.Shared.Infrastructure.Kafka`. Un dossier vide se lirait")
    absents.append("/// comme une promesse tenue ailleurs.")
    absents.append("/// `Interceptors/` est absent : la correlation et le `traceparent` sont deja")
    absents.append("/// portes par l'enveloppe partagee. Un intercepteur local serait une SECONDE")
    absents.append("/// implementation du meme contrat.")
    absents = "\n".join(absents)

    return f"""{usings}

namespace {ns}.Messaging.Kafka;

/// <summary>
/// LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// Les consommateurs de la plateforme vivaient dans SIX conventions differentes
/// selon le service. Chercher « qui ecoute quoi » supposait de connaître la
/// convention du service qu'on ouvrait.
///
/// LA LIGNE DE PARTAGE : CE DOSSIER PORTE LA POLITIQUE DU SERVICE, LE SOCLE
/// PARTAGE PORTE LE TYPE ET LE PROTOCOLE. `Outbox/` et `Inbox/` contiennent le
/// geste d'enregistrement, PAS une copie de `OutboxMessage` ni de
/// `ConsumerInboxEntry` — ce sont des entites EF dont les tables sont creees par
/// les migrations de ce service.
///
/// CE QUI N'EST PAS ICI :
{absents}
///
/// L'IDEMPOTENCE reste dans l'installeur quand elle y est : elle sert aussi les
/// routes HTTP annotees `AllowIdempotency()`.
///
{RENVOI}/// </summary>
public static class DependencyInjection
{{
    /// <summary>
    /// Branche toute la messagerie du service. Appelee par `Program.cs` ; son
    /// absence est detectee au demarrage par `GardeDeCablage`.
    /// </summary>
    public static IServiceCollection AjouterMessagerie{court}(this IServiceCollection services)
    {{
{corps}

        return services;
    }}
}}
"""


# ═════════════════════════════════════════════════════════════════════════════
# L'APPLICATION
# ═════════════════════════════════════════════════════════════════════════════

RE_REG = re.compile(
    r"[ \t]*(?:builder\.Services|services)\.AddScoped<\s*\n?\s*"
    r"IIntegrationEventHandler<\w+>\s*,\s*\n?\s*[\w.]+\s*>\(\);[ \t]*\n")

NOTE_REG = ("        // Les gestionnaires d'evenements sont enregistres par le module de\n"
            "        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.\n")

RE_UNE = re.compile(r"(?:builder\.Services|services)\.AddScoped<\s*IIntegrationEventHandler<(\w+)>\s*,\s*([\w.]+)\s*>\(\);")


def enregistrements(chemin_hote):
    """Les enregistrements de gestionnaires d'un fichier hote, AVEC leur commentaire.

    LE COMMENTAIRE VOYAGE AVEC LA LIGNE QU'IL EXPLIQUE.

    Ce depot met la raison d'un enregistrement dans le commentaire qui le
    precede — « SANS CES DEUX LIGNES, SUSPENDRE UN VENDEUR NE RETIRE RIEN
    (ISSUE-025) ». Deplacer la ligne en laissant le commentaire derriere
    produirait deux mensonges d'un coup : un commentaire qui explique du code
    absent, et du code sans sa raison.
    """
    lignes = lire(chemin_hote).splitlines(keepends=True)
    trouves, a_supprimer = [], set()
    i = 0
    while i < len(lignes):
        # L'enregistrement doit COMMENCER a cette ligne : sans ce test, une
        # fenetre de trois lignes qui commence au milieu d'un commentaire trouve
        # quand meme l'appel, et on amputerait le commentaire de ses premieres
        # lignes en le croyant complet.
        if not lignes[i].lstrip().startswith(("services.AddScoped<", "builder.Services.AddScoped<")):
            i += 1
            continue
        m = RE_UNE.search("".join(lignes[i:i + 3]))
        if not m:
            i += 1
            continue
        # Combien de lignes l'enregistrement occupe-t-il vraiment ?
        for n in (1, 2, 3):
            if RE_UNE.search("".join(lignes[i:i + n])):
                break
        fin = i + n
        # Le commentaire contigu au-dessus, s'il n'appartient pas deja au precedent.
        haut = i
        while haut - 1 >= 0 and lignes[haut - 1].strip().startswith("//") and (haut - 1) not in a_supprimer:
            haut -= 1
        commentaire = [lignes[k].strip() for k in range(haut, i)]
        trouves.append((m.group(2).split(".")[-1], m.group(1), commentaire))
        a_supprimer.update(range(haut, fin))
        i = fin
    return trouves, a_supprimer


def porter(nom, inv, carte, ns_ev, sans_desc, appliquer):
    d = inv[nom]
    infra, ns, court, ctx = d["infra"], d["ns"], d["court"], d["ctx"]
    dossier = f"{infra}/Messaging/Kafka"
    journal = []

    if os.path.exists(f"{dossier}/DependencyInjection.cs"):
        return [f"{nom} : deja porte, rien a faire"], [], []

    # L'ORDRE ET LES COMMENTAIRES VIENNENT DU FICHIER HOTE, PAS DU SCAN.
    # Le scan des classes trouve les gestionnaires ; seul le fichier qui les
    # enregistre porte leur ordre et la raison de chacun.
    handlers = []
    for f in sorted(hotes(d)):
        if "/Messaging/Kafka/" in f:
            continue
        if not (re.search(r"Module\w*Installer\.cs$", f) or f.endswith("Program.cs")):
            continue
        t, _ = enregistrements(f)
        handlers.extend(t)
    connus = {c for c, _, _ in handlers}
    for l in d["consumers"].values():
        for c, e in l:
            if c not in connus:
                handlers.append((c, e, ["// ENREGISTREMENT INTROUVABLE DANS LE COMPOSITION ROOT.",
                                        "// Ce gestionnaire existe et n'etait enregistre nulle part : il n'a",
                                        "// jamais ete appele. Il l'est desormais."]))
                connus.add(c)
    evenements = sorted({e for _, e, _ in handlers})
    sujets, inconnus = sujets_pour(evenements, carte)
    a_inbox = bool(handlers)
    a_outbox = ctx is not None
    a_publie = bool(d["publie"])

    # L'espace de noms du DbContext, pour le using d'Outbox/Inbox.
    ns_ctx = None
    if ctx:
        for f in glob.glob(f"{infra}/**/*.cs", recursive=True):
            if "/obj/" in f:
                continue
            t = lire(f)
            if re.search(rf"class {ctx}\b", t):
                m = re.search(r"^namespace ([\w.]+);", t, re.M)
                if m:
                    ns_ctx = m.group(1)
                    break
    if ctx and ns_ctx is None:
        return [], [f"{nom} : DbContext {ctx} introuvable dans {infra}"], []

    ecritures, deplacements = {}, []

    # 1. Les fichiers de gestionnaires descendent dans Consumers/.
    for f in d["consumers"]:
        if "/Messaging/Kafka/Consumers/" in f:
            continue
        cible = f"{dossier}/Consumers/{os.path.basename(f)}"
        deplacements.append((f, cible))

    # 2. Les fichiers engendres.
    ecritures[f"{dossier}/Configuration/Sujets{court}.cs"] = g_sujets(ns, court, sujets, evenements, inconnus)
    ecritures[f"{dossier}/Configuration/GardeDeCablage.cs"] = g_garde(ns, court)
    if a_outbox:
        ecritures[f"{dossier}/Outbox/Outbox{court}.cs"] = g_outbox(ns, court, ctx, ns_ctx)
    if a_inbox:
        ecritures[f"{dossier}/Inbox/Inbox{court}.cs"] = g_inbox(ns, court, ctx, ns_ctx)
    if a_publie:
        ecritures[f"{dossier}/Producers/EvenementsPublies.cs"] = g_publies(ns, d["publie"], ns_ev, sans_desc)
    ecritures[f"{dossier}/DependencyInjection.cs"] = g_di(ns, court, ns_ev, handlers, a_publie, a_inbox, a_outbox)

    journal.append(f"{nom} : {len(deplacements)} fichier(s) deplace(s), {len(ecritures)} engendre(s), "
                   f"{len(handlers)} gestionnaire(s), {len(sujets)} sujet(s)")
    if inconnus:
        journal.append(f"   AUCUN PRODUCTEUR : {', '.join(inconnus)}")

    if not appliquer:
        return journal, [], []

    os.makedirs(f"{dossier}/Consumers", exist_ok=True)
    paires = []
    for src, cible in deplacements:
        m = re.search(r"^namespace ([\w.]+);", lire(src), re.M)
        if m:
            paires.append((m.group(1), f"{ns}.Messaging.Kafka.Consumers"))
        os.system(f'cd "{RACINE}" && git mv "{os.path.relpath(src, RACINE)}" "{os.path.relpath(cible, RACINE)}"')
        t = lire(cible)
        t = re.sub(r"^namespace [\w.]+;", f"namespace {ns}.Messaging.Kafka.Consumers;", t, count=1, flags=re.M)
        # LE FICHIER DIT OU IL VIT, ET IL VENAIT DE DEMENAGER UNE PREMIERE FOIS.
        #
        # Plusieurs de ces fichiers portent en tete « CE FICHIER A DEMENAGE DE
        # `Application` VERS `Infrastructure/Integration` ». Laisser la phrase
        # apres un second deplacement en ferait un panneau qui indique une piece
        # vide — le genre de commentaire qui coute une demi-heure a qui le croit.
        vieux = os.path.relpath(os.path.dirname(src), os.path.dirname(infra))
        vieux = "/".join(vieux.split("/")[0].split(".")[-1:] + vieux.split("/")[1:])
        t = t.replace(f"`{vieux}`", "`Infrastructure/Messaging/Kafka/Consumers`")
        ecrire(cible, t)
    for p, t in ecritures.items():
        ecrire(p, t)
    return journal, [], paires


def reparer_usings(paires, appliquer):
    """Repare les references a l'espace de noms qu'un fichier vient de quitter.

    UN `using` QUI POINTE VERS UN ESPACE DE NOMS VIDE NE COMPILE PAS, ET UN TEST
    QUI REFERENCE LA CLASSE DEPLACEE NON PLUS.

    Le deplacement des gestionnaires change leur espace de noms. Tout ce qui les
    nommait — l'installeur, les tests unitaires, les commentaires qui citent le
    chemin — pointe alors vers du vide. Le compilateur le dira pour le code ; il
    ne dira RIEN pour les commentaires, qui resteraient a mentir indefiniment.
    """
    journal = []
    for ancien, nouveau in sorted(set(paires)):
        if ancien == nouveau:
            continue
        # L'ancien espace de noms est-il encore declare quelque part ?
        reste = False
        for f in glob.glob(f"{RACINE}/services/**/*.cs", recursive=True):
            if "/obj/" in f or "/bin/" in f:
                continue
            if re.search(rf"^namespace {re.escape(ancien)};", lire(f), re.M):
                reste = True
                break
        for f in (glob.glob(f"{RACINE}/services/**/*.cs", recursive=True)
                  + glob.glob(f"{RACINE}/tests/**/*.cs", recursive=True)
                  + glob.glob(f"{RACINE}/bff/**/*.cs", recursive=True)):
            if "/obj/" in f or "/bin/" in f:
                continue
            t0 = lire(f)
            # La forme abregee compte autant que la complete : sans ce test, un
            # commentaire qui ecrit `Catalog.Infrastructure.Integration.X` sans le
            # prefixe `HBA.` passerait a travers et resterait faux.
            if ancien not in t0 and ancien.removeprefix("HBA.") not in t0:
                continue
            t = t0
            if not reste:
                if f"using {nouveau};" in t:
                    t = t.replace(f"using {ancien};\n", "")
                else:
                    t = t.replace(f"using {ancien};", f"using {nouveau};")
            elif (f"using {ancien};" in t and f"using {nouveau};" not in t
                  and f.startswith(nouveau.split(".Messaging.")[0].replace(".", "/")[:0] or "")
                  and os.path.basename(os.path.dirname(f.split("/Messaging/")[0])) or True):
                # AJOUTER LE NOUVEAU `using` N'EST LEGITIME QUE DANS L'ASSEMBLY QUI
                #     PORTE LE MODULE.
                #
                # Quand l'ancien espace de noms survit — un fichier non deplace y
                # reste — la reparation ajoutait le nouveau `using` PARTOUT ou
                # l'ancien apparaissait. Dans `Application`, cela cree une
                # dependance vers `Infrastructure` : l'inverse du sens des
                # references, donc CS0234.
                projet = nouveau.split(".Messaging.")[0]
                if f"/{projet}/" in f:
                    t = t.replace(f"using {ancien};", f"using {ancien};\nusing {nouveau};")
            # Les mentions en commentaire, qui ne compilent pas mais qui mentent.
            # La forme abregee — sans le prefixe `HBA.` — est aussi frequente dans
            # ce depot que la forme complete, et tout aussi fausse apres coup.
            t = re.sub(rf"\b{re.escape(ancien)}\.", f"{nouveau}.", t)
            court_a, court_n = ancien.removeprefix("HBA."), nouveau.removeprefix("HBA.")
            t = re.sub(rf"\b{re.escape(court_a)}\.", f"{court_n}.", t)
            if t != t0:
                journal.append(f"   using -> {os.path.relpath(f, RACINE)}")
                if appliquer:
                    ecrire(f, t)
    return journal


def hotes(d):
    """Les fichiers du composition root de ce service — Program.cs compris.

    TROIS SERVICES N'ONT PAS DE Program.cs A EUX.

    `HBA.Financial.Api` compose payments, wallet et billing dans un seul
    processus ; `HBA.Engagement.Api` compose reviews, recommendations et
    wishlist. Le Program.cs de wallet-service n'existe pas : le sien est dans le
    dossier de payment-service. Chercher l'appel a poser dans le seul dossier du
    service laisserait ces modules enregistres et jamais branches — et la garde
    de cablage arreterait l'hote au demarrage.
    """
    fichiers = list(sources(d["chemin"]))
    if not any(f.endswith("Program.cs") for f in fichiers):
        cible = os.path.basename(glob.glob(f"{d['infra']}/*.csproj")[0])
        for f in glob.glob(f"{RACINE}/services/*/*/src/*/Program.cs"):
            proj = glob.glob(f"{os.path.dirname(f)}/*.csproj")
            if proj and cible in lire(proj[0]):
                fichiers.append(f)
    return fichiers


def patcher_hote(nom, inv, appliquer):
    """Retire du composition root ce qui vient de descendre dans le module."""
    d = inv[nom]
    infra, ns, court, ctx = d["infra"], d["ns"], d["court"], d["ctx"]
    journal = []

    for f in hotes(d):
        if "/Messaging/Kafka/" in f:
            continue
        t0 = lire(f)
        t = t0
        est_installeur = bool(re.search(r"Module\w*Installer\.cs$", f))
        est_program = f.endswith("Program.cs")
        if not (est_installeur or est_program):
            continue

        trouves, a_supprimer = enregistrements(f)
        n_reg = len(trouves)
        if n_reg:
            lignes = t.splitlines(keepends=True)
            premier = min(a_supprimer)
            garde = [l for k, l in enumerate(lignes) if k not in a_supprimer]
            garde.insert(premier - sum(1 for k in a_supprimer if k < premier) if False else
                         len([1 for k in range(premier) if k not in a_supprimer]), NOTE_REG)
            t = "".join(garde)

        n_out = n_in = 0
        if est_installeur and ctx:
            avant = t
            t = re.sub(rf"[ \t]*services\.AddOutboxProcessor<{ctx}>\(\);[ \t]*\n", "", t)
            n_out = int(t != avant)
            avant = t
            t = re.sub(rf"[ \t]*services\.AddScoped<IConsumerInbox,\s*EfConsumerInbox<{ctx}>>\(\);[ \t]*\n", "", t)
            n_in = int(t != avant)

            if (n_out or n_in) and "AddHostedService<GardeDeCablage>" not in t:
                garde = (
                    "        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc\n"
                    "        // hors de cet installeur : elles sont desormais enregistrees par\n"
                    f"        // `AjouterMessagerie{court}()`, que le composition root peut oublier.\n"
                    "        // Un oubli ne casserait rien de visible — le service demarre et n'emet\n"
                    "        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister\n"
                    "        // quand ce qu'elle verifie est absent.\n"
                    "        services.AddHostedService<GardeDeCablage>();\n\n")
                m = re.search(r"\n([ \t]*)services\.Add", t)
                if m:
                    t = t[:m.start() + 1] + garde + t[m.start() + 1:]

            if f"using {ns}.Messaging.Kafka.Configuration;" not in t:
                t = re.sub(r"^(using [^\n]+;\n)", rf"\1using {ns}.Messaging.Kafka.Configuration;\n", t, count=1, flags=re.M)

        if est_program and f"AjouterMessagerie{court}()" not in t:
            appel = (
                "// ═════════════════════════════════════════════════════════════════════════\n"
                "// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.\n"
                "//\n"
                "// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service\n"
                "// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par\n"
                "// l'installeur, refuse le demarrage dans ce cas.\n"
                "// ═════════════════════════════════════════════════════════════════════════\n"
                f"builder.Services.AjouterMessagerie{court}();\n")
            m = re.search(r"^var app = builder\.Build\(\);", t, re.M)
            if m:
                t = t[:m.start()] + appel + "\n" + t[m.start():]
            if f"using {ns}.Messaging.Kafka;" not in t:
                t = re.sub(r"^(using [^\n]+;\n)", rf"\1using {ns}.Messaging.Kafka;\n", t, count=1, flags=re.M)

        # Un `using` devenu inutile est retire, mais seulement s'il l'est vraiment.
        for u, motif in ((f"using HBA.Shared.Infrastructure.Outbox;\n", r"\bOutbox\w*<"),
                         (f"using HBA.Shared.Infrastructure.Inbox;\n", r"\b(IConsumerInbox|EfConsumerInbox|ConsumerInbox\w*)\b")):
            if u in t and not re.search(motif, t.replace(u, "")):
                t = t.replace(u, "")

        if t != t0:
            journal.append(f"   {os.path.relpath(f, d['chemin'])} : {n_reg} enregistrement(s), outbox={n_out}, inbox={n_in}")
            if appliquer:
                ecrire(f, t)

    # Le csproj de l'Infrastructure a besoin de Hosting.Abstractions pour la garde.
    proj = glob.glob(f"{infra}/*.csproj")[0]
    t = lire(proj)
    if "Microsoft.Extensions.Hosting.Abstractions" not in t:
        ajout = ('    <!-- Requis par `Messaging/Kafka/Configuration/GardeDeCablage.cs` (IHostedService).\n'
                 '         Le paquet arrivait de facon transitive ; une dependance transitive\n'
                 '         disparaît sans prevenir quand le projet amont change. Declaree, donc. -->\n'
                 '    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />\n')
        m = list(re.finditer(r"[ \t]*<PackageReference [^\n]*\n", t))
        if m:
            t = t[:m[-1].end()] + ajout + t[m[-1].end():]
        else:
            t = t.replace("</Project>", f"  <ItemGroup>\n{ajout}  </ItemGroup>\n\n</Project>")
        journal.append(f"   {os.path.basename(proj)} : + Hosting.Abstractions")
        if appliquer:
            ecrire(proj, t)

    return journal


def main():
    appliquer = "--appliquer" in sys.argv
    cibles = [a for a in sys.argv[1:] if not a.startswith("--")]
    inv = inventaire()
    carte = carte_evenement_service(inv)
    ns_ev = espaces_de_noms_evenements()
    sd = sans_descripteur()
    for nom in (cibles or sorted(inv)):
        if nom not in inv:
            print(f"{nom} : inconnu"); continue
        j, err, paires = porter(nom, inv, carte, ns_ev, sd, appliquer)
        for l in j: print(l)
        for l in err: print("ERREUR", l)
        if not err and "deja porte" not in (j[0] if j else ""):
            for l in patcher_hote(nom, inv, appliquer): print(l)
            for l in reparer_usings(paires, appliquer): print(l)


if __name__ == "__main__":
    main()
