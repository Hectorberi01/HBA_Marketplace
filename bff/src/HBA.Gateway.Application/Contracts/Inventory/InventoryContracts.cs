namespace HBA.Gateway.Application.Contracts.Inventory;

/// <summary>Disponibilité agrégée d'un SKU, toutes localisations confondues.</summary>
public sealed record StockAvailability(string Sku, int TotalAvailable);
