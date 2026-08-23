namespace HBA.Orders.Domain.Orders;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'UNE LIGNE DE COMMANDE REPRÉSENTE.
///
/// CE DISCRIMINANT DÉCIDE DE CE QUI SE PASSE APRÈS LE PAIEMENT.
///
/// Une ligne <see cref="Goods"/> réserve du stock, puis part en colis vers
/// Shipping. Une ligne <see cref="Food"/> ne réserve rien — un plat n'existe pas
/// dans Inventory — et part en cuisine, où elle doit être acceptée avant d'être
/// préparée puis remise à un livreur.
///
/// SON ABSENCE NE PRODUISAIT AUCUNE ERREUR, ET C'ÉTAIT LE PROBLÈME.
///
/// Avant lui, un panier de plats produisait une commande dont chaque ligne avait
/// un SKU vide. `IInventoryModuleApi.TryReserveAsync` répond VRAI pour un SKU sans
/// enregistrement de stock — comportement voulu pour les articles non suivis — si
/// bien que la réservation « réussissait ». La commande partait au paiement, puis
/// à l'expédition comme un colis. Le client était débité et personne ne cuisinait.
///
/// Un défaut qui ne lève pas d'exception ne se découvre qu'en production.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum OrderLineKind
{
    /// <summary>Une offre marketplace : stock réservé, expédition.</summary>
    Goods = 0,

    /// <summary>Un plat de restaurant : préparation en cuisine, pas de stock.</summary>
    Food = 1
}
