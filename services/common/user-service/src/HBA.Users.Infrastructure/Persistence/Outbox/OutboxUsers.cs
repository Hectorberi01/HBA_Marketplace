using HBA.Users.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox.Persistence.Outbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox.Messaging.Kafka.Processors;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'OUTBOX DE user-service — LE CÂBLAGE ICI, LE TYPE ET LA TABLE AILLEURS.
///
/// Ce dossier existe parce que l'outbox est le chemin de sortie du service vers
/// Kafka : sans le processeur enregistré ci-dessous, les trois événements listés
/// dans `Producers/EvenementsPublies` s'écrivent en base et n'en sortent jamais.
/// La panne est SILENCIEUSE — la transaction métier réussit, l'appelant reçoit
/// son 200, et le consommateur d'en face attend un message qui ne viendra pas.
///
/// CE QUE CE FICHIER NE CONTIENT PAS, ET NE DOIT PAS CONTENIR.
///
/// Pas de `OutboxMessage`, pas de `OutboxRepository`, pas de `OutboxProcessor`
/// propres au service. Ces trois-là restent dans
/// `HBA.Shared.Infrastructure/Outbox/`, et ce n'est pas de la mutualisation par
/// commodité :
///
///   - `OutboxMessage` est une ENTITÉ EF. Sa table est créée par les migrations
///     de CE service et de dix-sept autres ; une copie locale divergerait de la
///     colonne réelle sans que rien ne le signale.
///   - `ModuleDbContext.SaveChangesAsync` draine la file vers cette entité DANS
///     LA TRANSACTION MÉTIER. Un processeur local qui lirait une autre table ne
///     verrait jamais ce que le DbContext y écrit.
///   - `OutboxRetryPolicy` porte la mise en lettre morte après dix tentatives.
///     Deux politiques divergentes, c'est la panne de cette semaine sous un
///     autre nom.
///
/// LA RÈGLE, POUR LES DIX-HUIT SERVICES SUIVANTS : le module Kafka du service
/// possède SA POLITIQUE (quoi enregistrer, quels sujets, quels gestionnaires) ;
/// le socle partagé possède LE TYPE ET LE PROTOCOLE. Un dossier `Outbox/` qui
/// redéfinirait le message serait un fork déguisé en rangement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class OutboxUsers
{
    internal static IServiceCollection AjouterOutboxUsers(this IServiceCollection services)
    {
        // `AddOutboxProcessor` enregistre le processeur ET le purgeur, et ne fait
        // rien si `Outbox:Enabled` est à faux — voir `OutboxRegistration`.
        services.AjouterLOutboxLocale();
        return services;
    }
}
