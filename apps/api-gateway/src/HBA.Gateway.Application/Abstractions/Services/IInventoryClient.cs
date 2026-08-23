using HBA.Gateway.Application.Contracts.Inventory;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>inventory-service</c> — stock et lieux d'expédition.</summary>
public interface IInventoryClient : IServiceClient
{
    /// <summary>
    /// <c>GET /api/inventory/availability/{sku}</c>.
    /// </summary>
    /// <remarks>
    /// PAS DE ROUTE DE LOT — UN APPEL PAR SKU.
    ///
    /// inventory-service n'expose aucun point de terminaison acceptant plusieurs
    /// SKU. Une fiche produit à quatre déclinaisons fait donc quatre appels, en
    /// parallèle et bornés par l'appelant.
    ///
    /// Manque à combler : <c>POST /api/inventory/availability/by-skus</c>. Tant
    /// qu'il n'existe pas, les appelants doivent PLAFONNER le nombre de SKU
    /// interrogés — sans quoi un produit à cinquante déclinaisons déclencherait
    /// cinquante appels sortants pour un seul affichage.
    /// </remarks>
    Task<ServiceResult<StockAvailability>> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken);
}
