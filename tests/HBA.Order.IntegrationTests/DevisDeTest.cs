using HBA.DeliveryPricing.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RELECTURE DE DEVIS, EN MÉMOIRE — ET ELLE LÈVE.
///
/// C'EST LE POINT LE PLUS DÉLICAT DE CETTE SUITE DE DOUBLES.
///
/// `PlaceOrderCommandHandler` ne relit un devis QUE si la requête en désigne un ;
/// aucun test de cette suite n'en désigne, la commande part donc à zéro franc de
/// frais — un trou de recette connu, journalisé en avertissement par le service
/// lui-même, et sans rapport avec ISSUE-002 / ISSUE-003.
///
/// Rendre ici un devis complaisant serait bien pire que lever : le contrôle qu'on
/// court-circuiterait est EXACTEMENT celui qui empêche un acheteur de fixer ses
/// propres frais de livraison. Un faux qui dit toujours oui rendrait VERT un
/// service ayant reperdu cette garde.
///
/// SI UN JOUR UN TEST DE CETTE SUITE PASSE UN `deliveryQuoteId`, il devra venir
/// écrire ici ce que delivery-pricing est censé répondre — et se demander
/// pourquoi. C'est le but de la levée : rendre le silence impossible.
///
/// POURQUOI CE DOUBLE EXISTE ALORS QU'IL NE FAIT QUE LEVER.
///
/// `IDeliveryQuoteLookup` est résolu à la CONSTRUCTION du handler. Sans
/// enregistrement, c'est l'injection qui échoue — au premier test, avec un
/// message qui parle de conteneur et non de devis. La levée, elle, dit exactement
/// ce qui manque.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DevisDeTest : IDeliveryQuoteLookup
{
    public Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Aucun test de cette suite ne présente de devis au checkout. "
            + "En rendre un ici court-circuiterait la garde qui empêche l'acheteur "
            + "de fixer ses propres frais de livraison.");
}
