using HBA.Shared.Infrastructure.Persistence;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Delivery.Pricing.Infrastructure.Persistence.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using HBA.Shared.Infrastructure.Events;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Retry;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Processors;
// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.
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

namespace HBA.Delivery.Pricing.Infrastructure.Persistence.Outbox;

/// <summary>
/// Déclare qu'un DbContext porte une table d'outbox. Enregistré sur TOUS les hôtes, même
/// ceux qui ne drainent pas — c'est ce qui permet à la console d'administration (BFF Admin,
/// où <c>OUTBOX_ENABLED=false</c>) de LIRE les lettres mortes des 25 modules sans jamais
/// les traiter.
/// </summary>
public sealed record OutboxContextRegistration(Type DbContextType, string ModuleName);

/// <summary>
/// Enregistrement conditionnel du processeur d'outbox. Les modules sont composés
/// in-process par PLUSIEURS hôtes (API + 4 BFF) : si chacun lançait l'outbox de
/// tous les modules, on multiplierait les connexions Postgres (≈ modules × hôtes)
/// jusqu'à épuisement (« too many clients »).
///
/// <para>
/// Et surtout — la raison la plus grave, qui n'était pas écrite : les 5 processeurs
/// liraient les <b>MÊMES</b> messages et les dispatcheraient <b>5 FOIS</b>. La lecture n'est
/// protégée par aucun <c>SELECT … FOR UPDATE SKIP LOCKED</c>. Chaque gain vendeur serait
/// crédité cinq fois, chaque notification envoyée cinq fois. On ne lance donc l'outbox que
/// là où <c>OUTBOX_ENABLED</c> n'est pas « false » — l'API uniquement ; les BFF la
/// positionnent à « false » dans le compose de production.
/// </para>
///
/// <para>
/// <b>Cela vaut aussi pour la mise à l'échelle horizontale : deux répliques de l'API =
/// double dispatch.</b> Avant de scaler l'API, il faut implémenter le verrou de ligne.
/// C'est une contrainte de déploiement, pas une opinion.
/// </para>
/// </summary>
public static class OutboxRegistration
{
    // LE DRAPEAU EST LU AU SOCLE, PAS ICI.
    //
    // Cette propriete etait une copie de la lecture de `OUTBOX_ENABLED`. Vingt-cinq
    // copies d'une comparaison de chaine, c'est vingt-cinq occasions de la faire
    // autrement — et un service qui draine quand les autres ne drainent pas est un
    // defaut qu'on ne voit qu'en cherchant pourquoi UN service publie encore.
    public static bool Enabled => DrainageDOutbox.Actif;
    public static IServiceCollection AjouterLOutboxLocale(this IServiceCollection services)
    {
        // Politique de réessai PARTAGÉE par les 25 modules : un seul endroit où régler le
        // plafond de tentatives et le backoff, plutôt que vingt-cinq.
        //
        // TryAddSingleton, et non AddSingleton : cette méthode est appelée UNE FOIS PAR
        // MODULE. AddSingleton empilerait 25 descripteurs identiques — sans dommage
        // fonctionnel (le dernier gagne), mais c'est un mensonge dans le conteneur, et le
        // jour où quelqu'un injectera IEnumerable<OutboxRetryPolicy> il en trouvera 25.
        services.TryAddSingleton<OutboxRetryPolicy>();

        // Le registre est peuplé sur TOUS les hôtes, indépendamment d'Enabled : le BFF Admin
        // ne draine pas l'outbox, mais doit pouvoir en lister — et rejouer — les lettres
        // mortes.
        var moduleName = typeof(DeliveryPricingDbContext).Name.Replace("DbContext", string.Empty);
        services.AddSingleton(new OutboxContextRegistration(typeof(DeliveryPricingDbContext), moduleName));

        if (Enabled)
        {
            services.AddHostedService<OutboxProcessor>();

            // La purge suit le MÊME interrupteur que le processeur, et pour la même
            // raison : elle n'a rien à faire sur les hôtes qui ne drainent pas. Un BFF
            // qui effacerait les messages traités d'un module qu'il ne traite pas
            // serait au mieux inutile, au pire concurrent de l'API sur la même table.
            //
            // Elle existe parce que la table n'était JAMAIS purgée — et que son
            // contenu est du JSON en clair, dont certains événements portent le code
            // envoyé par e-mail à l'utilisateur. Voir OutboxPurger.
            services.AddHostedService<OutboxPurger>();
        }

        // PAS DE PURGE D'INBOX : ce service ne consomme rien, il n'a donc
        // pas de table `consumer_inbox`. L'y enregistrer faisait interroger
        // a chaque tick une table qu'aucune migration ne cree.

        return services;
    }
}
