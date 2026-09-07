#!/usr/bin/env python3
"""
L'OUTBOX ET L'INBOX DESCENDENT DANS CHAQUE SERVICE — `shared` n'en garde que les ports.

C'EST LE LOT LE PLUS RISQUE DE LA SERIE, ET IL FAUT LE DIRE AVANT DE LE LIRE.

Ce n'est pas du cache : c'est le chemin qui GARANTIT QU'AUCUN EVENEMENT N'EST
PERDU. La boucle de livraison, sa politique de rejeu et sa file de lettres mortes
existeront desormais en un exemplaire par service. Un defaut corrige a un endroit
devra l'etre vingt-six fois, et rien ne le signalera.

CE QUI DESCEND :
  Persistence/Outbox/  l'entite, sa configuration, le depot, la purge, la
                       politique de rejeu, l'enregistrement
  Persistence/Inbox/   l'entite, sa configuration, le depot — et une PURGE, qui
                       n'existait nulle part : `consumer_inbox` grossissait sans
                       fin, sur une cle lue a chaque message recu
  Messaging/Kafka/Processors/  la boucle de livraison

CE QUI RESTE, ET POURQUOI CE SONT DES PORTS ET NON DES IMPLEMENTATIONS :
  IConsumerInbox        `IntegrationEventDispatcher`, partage, l'appelle avant
                        chaque gestionnaire. Sans ce port, l'idempotence centrale
                        n'existe plus.
  IntegrationEventQueue la file EN MEMOIRE que la couche Application remplit ;
                        elle n'est pas l'outbox, elle est ce que l'outbox draine.
  IMessageDOutbox
  IEntreeDInbox         deux marqueurs vides. `ModuleDbContext` doit exclure ces
                        deux tables du journal d'audit — sans quoi journaliser
                        produit des lignes a journaliser. Filtrer sur un NOM DE
                        CLASSE laisserait un service qui renomme son entite
                        retrouver la boucle infinie, en silence.

CE QU'IL FAUDRA FAIRE SUR TON POSTE : les instantanes de modele referencent
« HBA.Shared.Infrastructure.Outbox.OutboxMessage » et « ...Inbox.ConsumerInboxEntry »
sous forme de chaine. Ils compilent, les migrations s'appliquent toujours par
identifiant — mais modele et instantane divergent jusqu'a un
`dotnet ef migrations add` par service, au diff de schema VIDE.
"""
import os, re, io, sys, shutil

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
SI = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure")


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


def services():
    """(dossier infra, nom court, DbContext) pour chaque service qui a une outbox."""
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) != "src":
                continue
            for x in sorted(dirs):
                if not x.endswith(".Infrastructure"):
                    continue
                infra = os.path.join(d, x)
                # LE CONTEXTE EST TROUVE PAR SA CLASSE, PAS PAR SON ENREGISTREMENT.
                #
                # Premiere version : chercher `AddOutboxProcessor<X>`. Elle marchait
                # une fois — le script REMPLACE cet appel, donc au deuxieme passage
                # il ne trouvait plus que les services non encore traites, et
                # rendait « 6 services » au lieu de 24. Un script rejouable ne doit
                # pas se reconnaitre a ce qu'il a lui-meme efface.
                contexte = None
                for f in fichiers_cs(infra):
                    m = re.search(r'class\s+(\w+)\s*:\s*ModuleDbContext',
                                  io.open(f, encoding="utf-8", errors="replace").read())
                    if m:
                        contexte = m.group(1)
                        break
                court = x.replace("HBA.", "").replace(".Infrastructure", "").replace(".", "")
                yield infra, court, contexte


def degenerique(texte, contexte):
    texte = texte.replace("<TDbContext>", "").replace("TDbContext", contexte)
    texte = re.sub(r'\n\s*where ' + re.escape(contexte) + r' : [^\n]*\n', "\n", texte)
    return texte


ENTETE = """// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `{origine}`.
//
// L'outbox et l'inbox appartiennent au service : leurs tables sont creees par SES
// migrations. `shared` n'en garde que les ports — `IConsumerInbox`, la file en
// memoire, et deux marqueurs vides sans lesquels le journal d'audit se
// journaliserait lui-meme.
//
// CE QUE CETTE COPIE COUTE, ET IL FAUT LE SAVOIR : c'est le chemin qui garantit
// qu'aucun evenement n'est perdu. Il existe maintenant en un exemplaire par
// service. Un defaut corrige ici ne l'est nulle part ailleurs.
// ═════════════════════════════════════════════════════════════════════════════
"""


def main():
    liste = list(services())
    print(f"{len(liste)} projets ; contexte d'outbox trouve pour "
          f"{sum(1 for _, _, c in liste if c)} :")
    for infra, court, contexte in liste:
        print(f"   {os.path.basename(infra):46} {court:28} {contexte or '— aucune outbox'}")
    if not SEC:
        print("\nSIMULATION.")
        return
    print("\n(application faite par la seconde passe)")


if __name__ == "__main__":
    main()


COPIES = [
    # (source, destination relative, origine affichee)
    ("Outbox/OutboxMessage.cs",      "Persistence/Outbox/OutboxMessage.cs"),
    ("Outbox/OutboxConfiguration.cs", "Persistence/Outbox/OutboxMessageConfiguration.cs"),
    ("Outbox/IOutboxDbContext.cs",   "Persistence/Outbox/OutboxRepository.cs"),
    ("Outbox/OutboxPurger.cs",       "Persistence/Outbox/OutboxCleanupService.cs"),
    ("Outbox/OutboxRegistration.cs", "Persistence/Outbox/OutboxRegistration.cs"),
    ("Outbox/OutboxIntegrationEventPublisher.cs", "Persistence/Outbox/OutboxIntegrationEventPublisher.cs"),
    ("Outbox/OutboxRetryPolicy.cs",  "Messaging/Kafka/Retry/OutboxRetryPolicy.cs"),
    ("Outbox/OutboxProcessor.cs",    "Messaging/Kafka/Processors/OutboxProcessor.cs"),
    ("Inbox/ConsumerInboxEntry.cs",  "Persistence/Inbox/InboxMessage.cs"),
    ("Inbox/ConsumerInboxConfiguration.cs", "Persistence/Inbox/InboxMessageConfiguration.cs"),
    ("Inbox/EfConsumerInbox.cs",     "Persistence/Inbox/InboxRepository.cs"),
]

NS_PAR_DOSSIER = {
    "Persistence/Outbox": "Persistence.Outbox",
    "Persistence/Inbox": "Persistence.Inbox",
    "Messaging/Kafka/Retry": "Messaging.Kafka.Retry",
    "Messaging/Kafka/Processors": "Messaging.Kafka.Processors",
}



def ajouter_using(texte, besoin):
    """Insere un `using` meme dans un fichier qui n'en a aucun."""
    if re.search(r'^' + re.escape(besoin) + r'\s*$', texte, re.M):
        return texte
    usings = list(re.finditer(r'^using\s+[^\n]+;\s*$', texte, re.M))
    if usings:
        return texte[:usings[-1].end()] + "\n" + besoin + texte[usings[-1].end():]
    return besoin + "\n" + texte


def appliquer():
    liste = [(i, c, x) for i, c, x in services() if x]
    for infra, court, contexte in liste:
        racine = racine_de_namespace(infra)
        ns_outbox = f"{racine}.Persistence.Outbox"
        ns_inbox = f"{racine}.Persistence.Inbox"

        for source, destination in COPIES:
            texte = io.open(os.path.join(SI, source), encoding="utf-8").read()
            origine = "HBA.Shared.Infrastructure." + os.path.dirname(source)
            dossier = os.path.dirname(destination)
            ns = f"{racine}.{NS_PAR_DOSSIER[dossier]}"

            texte = degenerique(texte, contexte)
            texte = re.sub(r'^namespace\s+[\w\.]+;',
                           ENTETE.format(origine=origine) + f"\nnamespace {ns};", texte, flags=re.M)
            # tout ce qui vient des deux dossiers dissous se resout localement
            texte = re.sub(r'^using\s+HBA\.Shared\.Infrastructure\.(Outbox|Inbox)\s*;\s*\n', "", texte, flags=re.M)
            for besoin in (ns_outbox, ns_inbox, f"{racine}.Persistence.DbContext"):
                if besoin != ns and f"using {besoin};" not in texte:
                    texte = f"using {besoin};\n" + texte
            texte = "using HBA.Shared.Infrastructure.Persistence;\n" + texte

            if destination.endswith("OutboxMessage.cs"):
                texte = texte.replace("public sealed class OutboxMessage",
                                      "internal sealed class OutboxMessage : IMessageDOutbox")
            if destination.endswith("InboxMessage.cs"):
                texte = texte.replace("public sealed class ConsumerInboxEntry",
                                      "internal sealed class ConsumerInboxEntry : IEntreeDInbox")

            chemin = os.path.join(infra, destination.replace("/", os.sep))
            os.makedirs(os.path.dirname(chemin), exist_ok=True)
            l = os.path.join(os.path.dirname(chemin), "LISEZMOI.md")
            if os.path.exists(l):
                os.remove(l)
            io.open(chemin, "w", encoding="utf-8").write(texte)

        # ── la purge de l'inbox, qui n'existait nulle part
        io.open(os.path.join(infra, "Persistence", "Inbox", "InboxCleanupService.cs"),
                "w", encoding="utf-8").write(f"""using {racine}.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace {ns_inbox};

/// <summary>
/// Purge les traces d'idempotence de l'inbox.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE SERVICE N'EXISTAIT NULLE PART, ET C'EST UN VRAI MANQUE.
///
/// `OutboxPurger` existait. `IdempotencyPurger` existait. RIEN ne purgeait
/// `consumer_inbox` : chaque evenement traite y laissait une ligne, definitivement,
/// dans les quinze services qui consomment.
///
/// Ce n'est pas urgent — quelques centaines de milliers de lignes par an — mais
/// c'est une table qui ne cesse jamais de grossir, indexee sur une cle CONSULTEE A
/// CHAQUE MESSAGE RECU. Elle finira par couter sur le chemin chaud, et ce jour-la
/// personne ne cherchera la.
///
/// LA RETENTION EST LONGUE, ET C'EST DELIBERE. Une trace d'inbox est ce qui
/// empeche un evenement d'etre retraite. La supprimer trop tot ne casse rien
/// tant que le message ne revient pas — et quand il revient, l'effet metier est
/// rejoue sans que rien ne le signale. Trente jours couvre largement une
/// remise a zero d'offsets, qui est le seul cas ou un message ancien revient.
///
/// CE QUE ÇA NE COUVRE PAS : un rejeu deliberement plus ancien que la retention.
/// Avant une remise a zero d'offsets au-dela de trente jours, il faut savoir que
/// les gestionnaires non idempotents par eux-memes refont leur effet.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class InboxCleanupService : BackgroundService
{{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _fabrique;
    private readonly ILogger<InboxCleanupService> _journal;

    public InboxCleanupService(IServiceScopeFactory fabrique, ILogger<InboxCleanupService> journal)
    {{
        _fabrique = fabrique;
        _journal = journal;
    }}

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {{
        while (!stoppingToken.IsCancellationRequested)
        {{
            try
            {{
                using var portee = _fabrique.CreateScope();
                var contexte = portee.ServiceProvider.GetRequiredService<{contexte}>();

                var limite = DateTime.UtcNow - Retention;

                var supprimees = await contexte.Set<ConsumerInboxEntry>()
                    .Where(entree => entree.ProcessedOnUtc < limite)
                    .ExecuteDeleteAsync(stoppingToken);

                if (supprimees > 0)
                {{
                    _journal.LogInformation(
                        "Inbox : {{Supprimees}} traces de plus de {{Jours}} jours purgees.",
                        supprimees, Retention.TotalDays);
                }}
            }}
            catch (Exception exception) when (exception is not OperationCanceledException)
            {{
                // UNE PURGE QUI ECHOUE NE DOIT PAS ARRETER L'HOTE. La table grossit
                // un tour de plus ; l'alternative — laisser l'exception remonter —
                // arreterait un service qui sert parfaitement ses requetes.
                _journal.LogError(exception, "Inbox : la purge a echoue, elle sera retentee.");
            }}

            try
            {{
                await Task.Delay(Intervalle, stoppingToken);
            }}
            catch (OperationCanceledException)
            {{
                return;
            }}
        }}
    }}
}}
""")

        # ── le contexte du service porte desormais sa table d'outbox
        chemin_ctx = None
        for f in fichiers_cs(infra):
            if re.search(r'class\s+' + contexte + r'\s*:\s*ModuleDbContext', io.open(f, encoding="utf-8").read()):
                chemin_ctx = f
                break
        if chemin_ctx:
            s = io.open(chemin_ctx, encoding="utf-8").read()
            if "AjouterAuOutbox" not in s:
                m = re.search(r'(class\s+' + contexte + r'\s*:\s*ModuleDbContext[^\n]*\n\{\n)', s)
                if m:
                    ajout = f"""    // ═════════════════════════════════════════════════════════════════════════
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    //
    // Le socle draine la file d'evenements et exclut ces deux tables du journal
    // d'audit ; il ne connait plus ni l'une ni l'autre. Ces trois membres sont ce
    // qu'il appelle, et ils repondent avec les entites de `Persistence/`.
    // ═════════════════════════════════════════════════════════════════════════
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {{
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
    }}

    protected override void AjouterAuOutbox(
        string type, string contenu, DateTime survenuLeUtc, string? traceParent, string? correlation)
        => OutboxMessages.Add(new OutboxMessage
        {{
            Type = type,
            Content = contenu,
            OccurredOnUtc = survenuLeUtc,
            TraceParent = traceParent,
            CorrelationId = correlation
        }});

"""
                    s = s[:m.end()] + ajout + s[m.end():]
                    s = s.replace(f"class {contexte} : ModuleDbContext",
                                  f"class {contexte} : ModuleDbContext, IOutboxDbContext", 1)
                    for besoin in (f"using {ns_outbox};", f"using {ns_inbox};",
                                   "using Microsoft.EntityFrameworkCore;"):
                        s = ajouter_using(s, besoin)
                    io.open(chemin_ctx, "w", encoding="utf-8").write(s)

        # ── le cablage pointe vers les types locaux
        for f in fichiers_cs(infra):
            s = io.open(f, encoding="utf-8").read()
            avant = s
            s = s.replace(f"services.AddOutboxProcessor<{contexte}>();", "services.AjouterLOutboxLocale();")
            s = s.replace(f"EfConsumerInbox<{contexte}>", "EfConsumerInbox")
            s = re.sub(r'^using\s+HBA\.Shared\.Infrastructure\.(Outbox|Inbox)\s*;\s*\n', "", s, flags=re.M)
            if s != avant:
                for besoin in (f"using {ns_outbox};", f"using {ns_inbox};"):
                    s = ajouter_using(s, besoin)
                io.open(f, "w", encoding="utf-8").write(s)

        # l'enregistrement local prend le nom qu'on vient d'appeler, et enregistre la purge d'inbox
        p = os.path.join(infra, "Persistence", "Outbox", "OutboxRegistration.cs")
        s = io.open(p, encoding="utf-8").read()
        s = s.replace("public static IServiceCollection AddOutboxProcessor(this IServiceCollection services)",
                      "public static IServiceCollection AjouterLOutboxLocale(this IServiceCollection services)")
        s = s.replace("        return services;",
                      "        // LA PURGE DE L'INBOX, QUI N'EXISTAIT NULLE PART.\n"
                      "        services.AddHostedService<InboxCleanupService>();\n\n"
                      "        return services;", 1)
        io.open(p, "w", encoding="utf-8").write(s)

    print(f"{len(liste)} services pourvus.")


if __name__ == "__main__" and SEC:
    appliquer()
