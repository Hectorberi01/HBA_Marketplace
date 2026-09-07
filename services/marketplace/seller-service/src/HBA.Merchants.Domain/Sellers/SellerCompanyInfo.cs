namespace HBA.Merchants.Domain.Sellers;

/// <summary>
/// Informations société du vendeur, saisies à l'auto-inscription et stockées en
/// jsonb (colonne nullable, null par défaut).
/// </summary>
public sealed record SellerCompanyInfo(
    string? LegalName = null,    // raison sociale
    string? Rccm = null,         // registre du commerce et du crédit mobilier
    string? Ifu = null,          // identifiant fiscal unique
    string? Address = null,
    // Commune de la boutique : CODE d'une des 77 communes (« abomey-calavi »), pas
    // un libellé libre.
    string? Commune = null,
    string? Activity = null,     // secteur / activité déclarée
    string? ManagerName = null,  // gérant / représentant légal
    string? Phone = null);
