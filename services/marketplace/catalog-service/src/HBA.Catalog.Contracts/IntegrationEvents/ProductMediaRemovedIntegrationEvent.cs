using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié quand une image cesse d'être rattachée à un produit — détachée une par
/// une, ou emportée par la suppression du produit.
///
/// CE MESSAGE EST LA SEULE CHOSE QUI DÉSIGNE ENCORE LE FICHIER.
///
/// Au moment où un consommateur le lit, la ligne `product_media` n'existe plus.
/// S'il est perdu, l'image reste dans le stockage sans qu'aucune requête ne
/// puisse la retrouver. C'est la raison pour laquelle il passe par l'outbox, dans
/// la transaction qui supprime la ligne, et non par un appel direct.
///
/// Consommé par un adaptateur du composition root, qui seul connaît à la fois
/// Catalog et le service média.
/// </summary>
public sealed record ProductMediaRemovedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }

    public required Guid MediaId { get; init; }
}
