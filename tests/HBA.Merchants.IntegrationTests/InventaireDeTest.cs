using System.Collections.Concurrent;
using HBA.Inventory.Contracts;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN INVENTAIRE EN MÉMOIRE, PILOTABLE — MÊME ARBITRAGE QUE `MediaDeTest`.
///
/// La règle à éprouver est le REFUS : un lieu d'expédition qui n'appartient pas au
/// vendeur, ou qui n'existe pas. Un faux qui dirait toujours oui rendrait vert un
/// service ayant reperdu son contrôle — c'est exactement l'état dans lequel il
/// était, avec le commentaire « voir la route du BFF Vendeur » pour toute garde.
///
/// SINGLETON : le test dépose le lieu, la requête le lit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class InventaireDeTest : IInventoryModuleApi
{
    private readonly ConcurrentDictionary<Guid, FulfillmentLocationSummary> _lieux = new();

    /// <summary>
    /// Dépose un lieu d'expédition et rend son identifiant.
    /// </summary>
    /// <param name="ownerId">
    /// Le vendeur propriétaire. <c>null</c> pour un entrepôt PLATEFORME (FBP) —
    /// c'est ainsi que le domaine le représente, et c'est le cas qui doit être
    /// refusé : le laisser passer rendrait la garde inopérante.
    /// </param>
    public Guid Deposer(Guid? ownerId, string type = "SellerAddress")
    {
        var id = Guid.NewGuid();

        _lieux[id] = new FulfillmentLocationSummary(
            Id: id,
            Type: type,
            OwnerId: ownerId,
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

    // ═════════════════════════════════════════════════════════════════════════
    // LE RESTE LÈVE. seller-service ne touche ni au stock ni aux réservations.
    //
    // Rendre des valeurs neutres ferait passer en silence un futur chemin de code
    // qui se mettrait à réserver du stock depuis ce service — et une réservation
    // qui échoue silencieusement, c'est une commande acceptée sur un stock qui
    // n'existe pas.
    // ═════════════════════════════════════════════════════════════════════════

    public Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne lit pas la disponibilité d'un SKU.");

    public Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne lit pas le stock.");

    public Task<bool> TryReserveAsync(
        string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne réserve pas de stock.");

    public Task ReleaseReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne libère pas de réservation.");

    public Task<bool> ConfirmReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne confirme pas de réservation.");
}
