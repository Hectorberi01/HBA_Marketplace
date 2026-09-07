using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Application.Messaging;

/// <summary>
/// Que faire du <see cref="Result"/> d'une étape de saga déclenchée par un message.
/// </summary>
public static class SagaOutcome
{
    /// <summary>
    /// Inspecte le résultat d'une étape de saga : journalise et acquitte si l'état
    /// s'y oppose, lève si la cause paraît passagère.
    /// </summary>
    /// <param name="etape">
    /// Ce que l'étape devait accomplir, à la troisième personne et en clair — «
    /// confirmer le paiement de la commande », « créditer le gain du livreur ».
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
