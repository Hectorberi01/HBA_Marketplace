using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;

namespace HBA.Shared.Infrastructure.Events;

/// <summary>
/// Dispatch in-process d'un event d'intégration vers tous ses handlers
/// enregistrés. C'est l'implémentation « bus en mémoire » d'aujourd'hui ;
/// demain, le même contrat est rempli par un consumer Kafka — sans toucher au
/// code métier des handlers.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'IDEMPOTENCE DE CONSOMMATION EST ICI, ET NON DANS CHAQUE HANDLER.
///
/// 6 HANDLERS SUR 96 SE PROTÉGEAIENT. LES 90 AUTRES ÉTAIENT REJOUABLES.
///
/// Le dispositif existait — `IConsumerInbox`, `EfConsumerInbox`, la table
/// `consumer_inbox` — et six handlers l'appelaient à la main. Les quatre-vingt-dix
/// autres non : Kafka livre AU MOINS UNE FOIS, donc un rééquilibrage de partitions
/// ou une reprise du consumer recréditait un vendeur, réservait une seconde fois du
/// stock, renvoyait une notification et jusqu'à trois e-mails de réinitialisation.
///
/// Le défaut était MASQUÉ par un autre : producteurs et consommateurs ne
/// s'accordent pas sur le nom des topics (ISSUE-001), donc les messages
/// n'arrivaient pas. Il se serait révélé à l'instant précis où l'on aurait corrigé
/// les topics — c'est pourquoi cette correction-ci passe DEVANT.
///
/// POURQUOI LA TRACE EST POSÉE **AVANT** L'APPEL DU HANDLER.
///
/// `MarkProcessedAsync` n'écrit rien : il ajoute la ligne au contexte EF et rend la
/// main (son commentaire le dit — un `SaveChanges` ici créerait deux transactions,
/// et la fenêtre entre les deux est exactement le trou que l'inbox doit fermer).
/// La ligne part donc dans le MÊME `SaveChangesAsync` que l'effet métier du
/// handler. Atomicité obtenue sans que le handler ait à en savoir quoi que ce soit.
///
/// Poser la trace APRÈS aurait paru plus prudent et aurait été plus faible : le
/// handler ayant déjà committé son effet, la trace serait partie dans une seconde
/// transaction, et un incident entre les deux rejouerait l'effet.
///
/// ET SI LE HANDLER LÈVE ?
///
/// L'exception traverse cette méthode : la boucle s'arrête, les handlers suivants
/// ne sont pas appelés, et la portée est libérée sans `SaveChanges`. Ni l'effet ni
/// la trace ne sont committés — le message est rejoué en entier, ce qui est le
/// comportement voulu. C'est aussi ce qui rend le « marquage avant » sûr : aucun
/// handler ultérieur ne peut committer la trace d'un handler qui a échoué, puisqu'
/// aucun handler ultérieur ne s'exécute.
///
/// CE QUI RESTE DÉCOUVERT, ET IL FAUT LE SAVOIR.
///
/// Un handler qui n'écrit RIEN en base — `SendEmailVerificationHandler`,
/// `SendPasswordResetEmailHandler`, un webhook sortant — ne provoque aucun
/// `SaveChanges`. Sa trace reste en attente et n'est jamais committée : au rejeu,
/// il refait son effet. Ces handlers-là doivent enregistrer eux-mêmes quelque
/// chose, ou accepter le doublon. La liste est courte et elle est nommée dans
/// `KAFKA_EVENT_MATRIX.md` §8.2.
///
/// DANS UN HÔTE QUI COMPOSE PLUSIEURS MODULES, ON MARQUE DANS **TOUTES** LES
/// INBOX ENREGISTRÉES.
///
/// `IConsumerInbox` est lié à UN `DbContext`, et `HBA.Financial.Api` compose
/// payments, wallet et billing — trois contextes, trois schémas. Résoudre UNE
/// inbox par `GetService` rendait la DERNIÈRE enregistrée : les handlers des autres
/// modules voyaient leur trace ajoutée à un contexte qu'ils ne sauvegardent pas,
/// donc jamais committée, donc restaient rejouables. Et en silence : l'avertissement
/// « aucune inbox » ne se déclenchait pas, puisqu'il y en avait une.
///
/// Pire, l'arbitrage dépendait de l'ORDRE des installeurs dans `Program.cs`.
/// Déplacer une ligne d'appel déprotégeait un module sans que rien ne change de
/// visible.
///
/// D'où `GetServices` : la trace est inscrite dans CHAQUE contexte enregistré.
/// Celui que le handler sauvegarde la committe ; les autres la laissent tomber avec
/// leur portée. Chaque handler est donc protégé par l'inbox de SON module, sans que
/// le dispatcher ait à savoir de quel module il vient.
///
/// Le coût est une requête de plus par inbox surnuméraire — deux, dans le seul hôte
/// concerné. Le prix d'un handler financier rejoué est sans commune mesure.
///
/// CE QUE CELA NE FERME PAS. Si un handler LÈVE et qu'un autre module sauvegarde
/// son contexte plus tard dans la même portée, la trace en attente de cet autre
/// module partirait avec. L'événement passerait alors pour traité alors qu'il a
/// échoué. En pratique le dispatcher est appelé une fois par message, dans sa
/// propre portée, et une exception la termine — mais c'est une propriété du
/// consommateur, pas de cette classe.
///
/// UN SERVICE SANS INBOX N'EST PAS CASSÉ — IL EST SEULEMENT NON PROTÉGÉ.
///
/// `IConsumerInbox` est résolu en OPTIONNEL. Les services qui ne l'ont pas encore
/// enregistré (ni table `consumer_inbox`, ni configuration EF) continuent de
/// fonctionner comme avant, et un avertissement le dit au premier message. Faire
/// autrement aurait transformé une amélioration en panne de démarrage pour la
/// moitié de la plateforme.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class IntegrationEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntegrationEventDispatcher>? _logger;

    public IntegrationEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<IntegrationEventDispatcher>>();
    }

    public async Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        var handlers = _serviceProvider.GetServices(handlerType).Where(h => h is not null).ToList();

        if (handlers.Count == 0)
        {
            return;
        }

        // `GetServices` ET NON `GetService` — voir l'encadré de cette classe.
        var inboxes = _serviceProvider.GetServices<IConsumerInbox>()
            .Where(i => i is not null)
            .ToList();

        if (inboxes.Count == 0)
        {
            _logger?.LogWarning(
                "Aucun IConsumerInbox enregistré : {Nombre} handler(s) de {Evenement} s'exécutent sans garde "
                + "d'idempotence. Kafka livre au moins une fois — un rejeu refera leur effet.",
                handlers.Count, integrationEvent.GetType().Name);
        }

        var typeEvenement = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name;
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;

        foreach (var handler in handlers)
        {
            var consommateur = NomDuConsommateur(handler!);

            if (inboxes.Count > 0)
            {
                // UNE SEULE INBOX QUI CONNAÎT L'ÉVÉNEMENT SUFFIT À LE SAUTER.
                //
                // La trace n'a été committée que par le module qui a sauvegardé —
                // les autres n'en ont aucune. Exiger l'accord de toutes ferait
                // rejouer chaque handler d'un hôte multi-modules, c'est-à-dire
                // exactement l'inverse du dispositif.
                var dejaTraite = false;

                foreach (var inbox in inboxes)
                {
                    if (await inbox.HasProcessedAsync(integrationEvent.Id, consommateur, cancellationToken))
                    {
                        dejaTraite = true;
                        break;
                    }
                }

                if (dejaTraite)
                {
                    _logger?.LogDebug(
                        "Événement {EventId} déjà traité par {Consommateur} : ignoré.",
                        integrationEvent.Id, consommateur);
                    continue;
                }

                // Ajout aux contextes, sans écriture : la ligne partira avec le
                // `SaveChangesAsync` du handler. Voir l'en-tête de cette classe.
                foreach (var inbox in inboxes)
                {
                    await inbox.MarkProcessedAsync(
                        integrationEvent.Id,
                        consommateur,
                        typeEvenement,
                        // VALAIT `null`, ALORS QUE L'INFORMATION ÉTAIT LÀ.
                        //
                        // La colonne existait, le message reçu la portait, et le
                        // consommateur Kafka installe désormais le contexte ambiant
                        // avant d'appeler ici. Une trace d'inbox sans corrélation ne
                        // sert qu'à répondre « oui, déjà traité » ; avec elle, elle
                        // dit de quel parcours utilisateur il s'agissait.
                        correlationId: string.IsNullOrWhiteSpace(HbaRequestContext.Current.CorrelationId)
                            ? null
                            : HbaRequestContext.Current.CorrelationId,
                        cancellationToken);
                }
            }

            await (Task)method.Invoke(handler, new object[] { integrationEvent, cancellationToken })!;
        }
    }

    /// <summary>
    /// Le nom sous lequel ce handler est inscrit dans `consumer_inbox`.
    ///
    /// IL EST EN BASE, DONC IL DOIT ÊTRE STABLE. Renommer une classe de handler
    /// change ce nom, et tous ses événements passés redeviennent « jamais traités » :
    /// au prochain rejeu, ils refont leur effet. Un renommage de handler est donc un
    /// geste à traiter comme une migration, pas comme du confort d'IDE.
    ///
    /// On prend le nom COMPLET du type — espace de noms compris — plutôt que le nom
    /// court : deux services composés dans le même hôte peuvent avoir un
    /// `PaymentCapturedHandler` chacun, et ils ne doivent pas se faire taire l'un
    /// l'autre.
    ///
    /// Les six handlers qui portaient déjà une constante `ConsumerName` gardent la
    /// leur : leur garde interne s'exécute avant celle-ci et court-circuite. Leurs
    /// traces historiques restent donc valides.
    /// </summary>
    private static string NomDuConsommateur(object handler)
    {
        var nom = handler.GetType().FullName ?? handler.GetType().Name;

        // La colonne fait 120 caractères (ConsumerInboxConfiguration). Un nom plus
        // long serait tronqué par PostgreSQL — ou refusé — et deux handlers dont les
        // 120 premiers caractères coïncident se confondraient. On garde la FIN, qui
        // est la partie discriminante : l'espace de noms se ressemble, pas le nom de
        // classe.
        return nom.Length <= 120 ? nom : nom[^120..];
    }
}
