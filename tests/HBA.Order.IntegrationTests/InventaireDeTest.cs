using System.Collections.Concurrent;
using HBA.Inventory.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>Un geste de stock demandé à inventory-service, tel qu'il a été demandé.</summary>
/// <param name="Geste">« reserve », « release » ou « confirm ».</param>
internal sealed record GesteDeStock(string Geste, string Sku, Guid LieuId, Guid CommandeId, int Quantite);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN INVENTAIRE EN MÉMOIRE QUI ENREGISTRE CE QU'ON LUI DEMANDE.
///
/// C'EST LUI QUI PORTE LA PREUVE D'ISSUE-003, ET IL N'Y A PAS D'ALTERNATIVE.
///
/// « Le paiement échoue, le stock reste réservé sans limite de temps » ne laisse
/// AUCUNE trace dans le schéma `ordering` : la libération est un appel SORTANT
/// vers inventory-service. On peut constater dans la base que la commande passe à
/// « Cancelled » — mais une commande annulée dont les réservations restent posées
/// est très exactement l'état de la panne. Le seul observable qui distingue les
/// deux est la liste des appels reçus par ce contrat.
///
/// ET C'EST POURQUOI IL ENREGISTRE PAR LIGNE, AVEC SON SKU.
///
/// L'audit ne demande pas « une libération a eu lieu » mais « une libération par
/// ligne qui réserve du stock ». La différence n'est pas rhétorique : la survente
/// par étranglement est CUMULATIVE, chaque ligne oubliée en retire un peu plus.
/// Un compteur global rendrait vert un service qui n'en libérerait qu'une sur
/// deux.
///
/// SINGLETON : le test lit ce que la requête HTTP et le consommateur Kafka ont
/// écrit, depuis trois portées différentes. D'où `ConcurrentQueue`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class InventaireDeTest : IInventoryModuleApi
{
    private readonly ConcurrentQueue<GesteDeStock> _gestes = new();
    private readonly ConcurrentDictionary<Guid, FulfillmentLocationSummary> _lieux = new();

    /// <summary>Tous les gestes demandés, dans l'ordre.</summary>
    public IReadOnlyList<GesteDeStock> Gestes => _gestes.ToArray();

    /// <summary>Les gestes d'un type donné pour UNE commande.</summary>
    public IReadOnlyList<GesteDeStock> Pour(string geste, Guid commandeId)
        => _gestes.Where(g => g.Geste == geste && g.CommandeId == commandeId).ToArray();

    /// <summary>
    /// Dépose un lieu d'expédition et rend son identifiant.
    /// </summary>
    /// <remarks>
    /// IL DOIT EXISTER, SANS QUOI LA COURSE N'EST PAS DEMANDÉE.
    ///
    /// `CreateDeliveryOnOrderConfirmedHandler` relit le lieu pour en faire le
    /// point de COLLECTE, et LÈVE s'il est introuvable. Un lieu absent ferait donc
    /// échouer un gestionnaire sans rapport avec ce qu'on éprouve, trois fois de
    /// suite, puis abandonner l'événement en Critical.
    /// </remarks>
    public Guid DeposerLieu()
    {
        var id = Guid.NewGuid();

        _lieux[id] = new FulfillmentLocationSummary(
            Id: id,
            Type: "SellerAddress",
            OwnerId: Guid.NewGuid(),
            CommuneCode: "cotonou",
            CommuneName: "Cotonou",
            Quartier: "Akpakpa",
            Landmark: "En face de la pharmacie",
            Line: null,
            CountryCode: "BJ",
            Latitude: 6.36,
            Longitude: 2.42,
            // Numéro à 10 chiffres : `Address.Create` passe par
            // `BeninGeography.NormalizePhone`, qui refuse l'ancien format à 8
            // chiffres d'avant la migration de 2024.
            ContactPhone: "+2290197000002");

        return id;
    }

    public Task<FulfillmentLocationSummary?> GetLocationAsync(
        Guid locationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_lieux.TryGetValue(locationId, out var lieu) ? lieu : null);

    /// <summary>
    /// Réserve toujours. Le refus de réservation a son propre chemin de
    /// compensation dans `PlaceOrderCommandHandler`, et ce n'est PAS celui que
    /// cette suite éprouve : ici la commande doit exister et attendre son paiement.
    /// </summary>
    public Task<bool> TryReserveAsync(
        string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
    {
        _gestes.Enqueue(new GesteDeStock("reserve", sku, locationId, orderId, quantity));
        return Task.FromResult(true);
    }

    /// <summary>La compensation d'ISSUE-003 : c'est cet appel qui manquait.</summary>
    public Task ReleaseReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        _gestes.Enqueue(new GesteDeStock("release", sku, locationId, orderId, Quantite: 0));
        return Task.CompletedTask;
    }

    /// <summary>Le solde de la réservation à la confirmation : la suite d'ISSUE-002.</summary>
    public Task<bool> ConfirmReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        _gestes.Enqueue(new GesteDeStock("confirm", sku, locationId, orderId, Quantite: 0));
        return Task.FromResult(true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LE RESTE LÈVE. order-service ne lit ni la disponibilité ni le stock.
    //
    // Rendre des valeurs neutres ferait passer en silence un futur chemin de code
    // qui se mettrait à les interroger — et une lecture de stock qui répond
    // toujours « disponible » est une commande acceptée sur un stock qui n'existe
    // pas.
    // ═════════════════════════════════════════════════════════════════════════

    public Task<AvailabilitySummary> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("order-service ne lit pas la disponibilité d'un SKU.");

    public Task<bool> IsInStockAsync(
        string sku, int quantity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("order-service ne lit pas le stock.");
}
