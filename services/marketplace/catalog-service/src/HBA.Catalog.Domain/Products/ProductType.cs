namespace HBA.Catalog.Domain.Products;

/// <summary>
/// Nature du produit (§8 : "productType": "PHYSICAL").
///
/// CE CHAMP DÉCIDE DE CE QUE LES AUTRES SERVICES DOIVENT FAIRE.
///
/// Un produit numérique n'a ni poids, ni dimensions, ni mode de livraison, et
/// Inventory n'a pas de stock à décrémenter pour lui. Le laisser implicite —
/// « tout est physique » — obligerait chaque consommateur à le deviner, et le
/// premier à se tromper serait Delivery, qui chercherait une adresse de retrait
/// pour un fichier.
///
/// C'est une modification critique au sens du §6 : passer de PHYSICAL à DIGITAL
/// change la nature de ce que l'acheteur reçoit.
/// </summary>
public enum ProductType
{
    /// <summary>Bien matériel : stock, poids, dimensions, livraison.</summary>
    Physical = 0,

    /// <summary>Bien immatériel livré par téléchargement ou par lien.</summary>
    Digital = 1,

    /// <summary>Prestation : ni stock ni livraison, mais une exécution.</summary>
    Service = 2
}
