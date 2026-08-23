using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Application.Messaging;

/// <summary>
/// Que faire du <see cref="Result"/> d'une étape de saga déclenchée par un
/// message.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FICHIER EXISTE PARCE QUE DEUX GESTIONNAIRES JETAIENT LEUR RÉSULTAT.
///
/// `ConfirmOrderOnPaymentCapturedHandler` et `CancelOrderOnPaymentFailedHandler`
/// s'écrivaient ainsi :
///
///     public Task HandleAsync(PaymentCapturedIntegrationEvent e, CancellationToken ct)
///         => _sender.Send(new ConfirmOrderPaymentCommand(e.OrderId), ct);
///
/// Le `Result` n'était ni inspecté, ni journalisé, ni levé. Si la commande
/// refusait de passer « payée » — introuvable, déjà annulée, stock insuffisant —
/// le message Kafka était ACQUITTÉ quand même : paiement encaissé, commande
/// jamais confirmée, aucune trace nulle part. C'est la panne la plus coûteuse
/// qui soit, parce qu'elle est silencieuse : personne ne la découvre avant que
/// l'acheteur ne réclame.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA RÈGLE, ET POURQUOI ELLE N'EST PAS « TOUJOURS LEVER »
///
/// Lever fait rejouer le message par l'outbox. C'est le bon réflexe quand la
/// cause est passagère — base indisponible, service voisin en redémarrage,
/// verrou concurrent : le prochain essai passera.
///
/// C'est le mauvais réflexe quand la cause est un ÉTAT. Une commande déjà
/// annulée ne redeviendra pas confirmable, et rejouer indéfiniment un message
/// qui ne peut pas aboutir sature l'outbox tout en noyant les vraies pannes.
///
/// D'où le tri par <see cref="ErrorType"/> :
///
///   NotFound, Conflict, Validation, Forbidden, Unauthorized
///       → l'état s'y oppose. On journalise en ERREUR — il faut une main
///         humaine — et on acquitte. Rejouer ne servirait à rien.
///
///   Failure (et tout ce qui n'est pas nommé ci-dessus)
///       → cause inconnue, donc supposée passagère. On LÈVE, et l'outbox
///         rejoue.
///
/// Le tri vaut ce que vaut le typage des erreurs à la source. Un handler qui
/// renvoie `Error.Failure` pour un état incompatible sera rejoué en boucle ; un
/// qui renvoie `Error.NotFound` pour une base absente sera abandonné à tort. Le
/// jour où l'un des deux arrive, c'est l'appel à `Error.*` qu'il faut corriger,
/// pas ce fichier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class SagaOutcome
{
    /// <summary>
    /// Inspecte le résultat d'une étape de saga : journalise et acquitte si
    /// l'état s'y oppose, lève si la cause paraît passagère.
    /// </summary>
    /// <param name="etape">
    /// Ce que l'étape devait accomplir, à la troisième personne et en clair —
    /// « confirmer le paiement de la commande », « créditer le gain du livreur ».
    /// Le texte finit dans le journal d'exploitation : il est lu par quelqu'un
    /// qui ne connaît pas ce code.
    /// </param>
    public static void Exiger(
        Result resultat,
        ILogger logger,
        string etape,
        params object?[] contexte)
    {
        if (resultat.IsSuccess)
        {
            return;
        }

        var erreur = resultat.Error;

        var etatIncompatible = erreur.Type
            is ErrorType.NotFound
            or ErrorType.Conflict
            or ErrorType.Validation
            or ErrorType.Forbidden
            or ErrorType.Unauthorized;

        if (etatIncompatible)
        {
            // NIVEAU ERREUR, PAS AVERTISSEMENT.
            //
            // Une étape de saga abandonnée laisse un client débité sans
            // commande, ou un vendeur non réglé. Ce n'est pas un incident de
            // routine : cela doit sortir dans les alertes.
            logger.LogError(
                "Étape de saga ABANDONNÉE — {Etape} a échoué en {Code} : {Message}. "
                + "L'état ne permet pas de rejouer ; intervention humaine requise. Contexte : {@Contexte}",
                etape, erreur.Code, erreur.Message, contexte);

            return;
        }

        // Cause inconnue : on laisse le message revenir.
        throw new InvalidOperationException(
            $"Étape de saga en échec — {etape} : {erreur.Code} — {erreur.Message}. "
            + "Cause supposée passagère, le message sera rejoué.");
    }
}
