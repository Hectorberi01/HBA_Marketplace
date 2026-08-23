using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Inventory;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Inventory;

/// <inheritdoc cref="IInventoryClient" />
public sealed class InventoryClient : ServiceHttpClient, IInventoryClient
{
    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Inventory;

    /// <remarks>
    /// LE SKU EST ÉCHAPPÉ. NE PAS SUPPRIMER `Uri.EscapeDataString`.
    ///
    /// C'est la SEULE valeur de tout ce fichier qui provienne, indirectement, du
    /// catalogue — donc d'une saisie vendeur. Un SKU contenant « ../ » ou « ? »
    /// réécrirait le chemin appelé sur le service interne : « ../../actuator »
    /// atteindrait une route qu'aucun pare-feu n'expose. Le préfixe fixe ne
    /// protège de rien si le segment variable peut en sortir.
    /// </remarks>
    public Task<ServiceResult<StockAvailability>> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken)
        => GetAsync<StockAvailability>(
            $"/api/inventory/availability/{Uri.EscapeDataString(sku)}", cancellationToken);
}
