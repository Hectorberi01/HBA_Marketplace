namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>
/// Accès aux parts vendeur d'une commande.
/// </summary>
/// <remarks>
/// DÉPÔT SÉPARÉ D'`IOrderRepository`, ET NON UNE NAVIGATION SUR `Order`.
///
/// La tentation était d'accrocher les parts à la commande comme les lignes et
/// les dossiers de retour, et de tout charger d'un coup. Ce serait une erreur
/// d'échelle : `SellerOrder` est un AGRÉGAT, avec son propre verrou optimiste et
/// ses propres transitions. Chargé sous la commande, chaque confirmation de
/// vendeur ferait remonter la commande entière — donc salirait `orders`, donc
/// mettrait deux vendeurs d'une même commande en concurrence sur la MÊME ligne
/// parente. Deux vendeurs qui confirment au même instant se renverraient un 409
/// l'un l'autre, pour deux gestes parfaitement indépendants.
///
/// C'est exactement ce que le test exigé par l'audit vérifie : deux parts, deux
/// états, l'une confirme sans affecter l'autre.
/// </remarks>
public interface ISellerOrderRepository
{
    Task AddRangeAsync(IEnumerable<SellerOrder> sellerOrders, CancellationToken cancellationToken = default);

    /// <summary>
    /// La part de CE vendeur dans CETTE commande, ou <c>null</c>.
    ///
    /// C'est la lecture des cinq routes vendeur : elles désignent la part par le
    /// couple (commande, vendeur) plutôt que par un `SellerOrderId` que le vendeur
    /// n'a aucun moyen de connaître avant d'avoir lu son carnet.
    /// </summary>
    Task<SellerOrder?> FindAsync(Guid orderId, Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Les parts d'une commande, tous vendeurs confondus.</summary>
    Task<IReadOnlyList<SellerOrder>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Le carnet d'un vendeur.</summary>
    /// <summary>
    /// Parts vendeur, de la plus récente à la plus ancienne, dans la limite de
    /// <paramref name="take"/> — qui DOIT être la même que celle passée à
    /// <c>IOrderRepository.ListBySellerAsync</c> quand les deux sont jointes.
    /// </summary>
    Task<IReadOnlyList<SellerOrder>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cette commande a-t-elle DÉJÀ été découpée ?
    ///
    /// C'EST L'IDEMPOTENCE DU DÉCOUPAGE, ET ELLE NE SUFFIT PAS SEULE.
    ///
    /// La confirmation arrive par Kafka, qui livre AU MOINS une fois : deux parts
    /// pour le même (commande, vendeur) doubleraient la vue du vendeur et le
    /// montant de sa part. Cette lecture traite le cas courant — un rejeu
    /// séquentiel. Elle ne voit PAS deux messages traités en parallèle : les deux
    /// répondent « non » avant que l'un ait écrit. Seul l'index unique
    /// `(OrderId, SellerId)` ferme cette course, et il la ferme du bon côté — la
    /// seconde insertion échoue, le message est rejoué, et le second passage
    /// trouve les parts. C'est la même construction que
    /// `order_return_settlements` et que `UnicitePanierParCommande`.
    /// </summary>
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
