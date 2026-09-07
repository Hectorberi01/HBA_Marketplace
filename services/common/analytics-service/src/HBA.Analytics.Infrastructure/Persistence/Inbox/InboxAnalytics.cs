using HBA.Analytics.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>
/// L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.
///
/// Kafka livre AU MOINS UNE FOIS. Sans cet enregistrement, un rebalancement de
/// partition ou une remise a zero d'offsets rejoue les evenements deja traites :
/// la table `consumer_inbox` existe dans le schema du service, et personne ne la
/// lit.
///
/// ELLE EST PLUS CRITIQUE ICI QUE PARTOUT AILLEURS. Les trois gestionnaires de
/// ce service INCREMENTENT des compteurs : un rejeu double le chiffre d'affaires
/// d'une journee, et rien dans la ligne de roll-up ne peut s'en apercevoir.
/// Ailleurs, une garde d'etat rattrape parfois l'absence d'inbox — un panier deja
/// cloture, une commande deja confirmee. Ici, aucune.
///
/// LA PURGE EST ENREGISTREE ICI, ET NON DANS L'OUTBOX.
///
/// Dans les autres services, `InboxCleanupService` est branche par
/// `AjouterLOutboxLocale` — un raccourci commode tant que tout service qui
/// consomme publie aussi. Celui-ci ne publie rien : le raccourci l'aurait laisse
/// sans purge, et sa table `consumer_inbox` aurait grossi indefiniment, indexee
/// sur une cle CONSULTEE A CHAQUE MESSAGE RECU.
///
/// CE QU'ELLE NE COUVRE PAS. Elle dedoublonne la CONSOMMATION, pas les effets
/// deja partis : un evenement traite a moitie laisse un etat que rien ne
/// rattrape ici.
/// </summary>
public static class InboxAnalytics
{
    internal static IServiceCollection AjouterInboxAnalytics(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        services.AddHostedService<InboxCleanupService>();
        return services;
    }
}
