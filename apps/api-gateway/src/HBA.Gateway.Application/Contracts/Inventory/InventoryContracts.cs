namespace HBA.Gateway.Application.Contracts.Inventory;

/// <summary>
/// Disponibilité agrégée d'un SKU, toutes localisations confondues.
/// </summary>
/// <remarks>
/// LA CLÉ EST UN SKU, PAS UN IDENTIFIANT DE PRODUIT.
///
/// C'est la contrainte la plus structurante de la fiche produit : un produit à
/// quatre déclinaisons demande QUATRE appels à inventory-service, qui n'expose
/// aucune route de lot par SKU. Cf. <c>ProductDetailHandler</c>, où le nombre
/// d'appels est borné pour cette raison précise.
/// </remarks>
public sealed record StockAvailability(string Sku, int TotalAvailable);
