namespace HBA.FoodOrders.Domain.Orders;

public interface IMealOrderRepository
{
    Task AddAsync(MealOrder order, CancellationToken cancellationToken = default);

    Task<MealOrder?> GetByIdAsync(MealOrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealOrder>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealOrder>> ListByRestaurantAsync(
        Guid restaurantId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ acheté un repas pour de bon (Paid, Confirmed ou
    /// Delivered) ?
    ///
    /// « Pour de bon » exclut `Pending` et `AwaitingPayment` à dessein : un panier
    /// abandonné au moment de payer ne doit pas brûler définitivement la promotion
    /// de bienvenue. Le prix de ce choix est une fenêtre étroite — deux paiements
    /// menés en parallèle pourraient tous deux se croire « premiers ». Le dommage
    /// est borné à UNE remise de trop, là où l'inverse punirait durablement
    /// quelqu'un qui a simplement hésité.
    /// </summary>
    Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// La commande née de CE panier, s'il y en a une.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST L'IDEMPOTENCE DU PASSAGE EN COMMANDE, ET ELLE MANQUAIT.
    ///
    /// `POST /api/orders` n'en avait aucune : un double-clic, un réseau lent
    /// suivi d'un renvoi, ou un rejeu de requête créait DEUX commandes sur le
    /// même panier — donc deux paiements. L'audit du cahier panier/commande l'a
    /// relevé, et rien dans le schéma ne s'y opposait : aucune contrainte
    /// d'unicité sur `CartId`.
    ///
    /// Ici la colonne porte un index unique, et cette lecture rend la commande
    /// déjà créée plutôt qu'une erreur : le second appel retrouve la première
    /// commande et la rend, ce que le client attendait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<MealOrder?> GetByCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
