namespace HBA.Orders.Domain.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commandes d'un acheteur, de la plus récente à la plus ancienne, dans la
    /// limite de <paramref name="take"/>. Voir <see cref="ListBySellerAsync"/> pour
    /// ce que cette borne coûte et pourquoi elle est préférable à son absence.
    /// </summary>
    Task<IReadOnlyList<Order>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ acheté pour de bon ? (statut Paid, Confirmed ou Delivered)
    ///
    /// « Pour de bon » exclut Pending et AwaitingPayment à dessein : un panier abandonné au
    /// moment de payer ne doit PAS brûler définitivement la promo de bienvenue de
    /// l'acheteur. Le prix de ce choix est une fenêtre étroite — deux checkouts menés en
    /// parallèle par la même personne pourraient tous deux se croire « premiers » et
    /// obtenir la remise de bienvenue. Le dommage est borné (UNE remise de trop), là où
    /// l'inverse punirait durablement un utilisateur honnête qui a hésité à payer.
    /// </summary>
    Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// La commande née de CE panier, s'il y en a une.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST L'IDEMPOTENCE DU PASSAGE EN COMMANDE, ET ELLE N'EXISTAIT PAS.
    ///
    /// `POST /api/orders` n'en avait aucune, et rien dans le schéma ne s'y
    /// opposait : `CartId` n'avait ni contrainte d'unicité, ni même un index. Un
    /// double-clic, un réseau lent suivi d'un renvoi, ou un rejeu de requête
    /// créait DEUX commandes sur le même panier — et l'acheteur se retrouvait
    /// avec deux paiements à réclamer.
    ///
    /// La fenêtre n'est pas étroite : entre l'entrée dans le gestionnaire et la
    /// clôture du panier il y a une lecture gRPC du panier, une relecture de
    /// devis chez delivery-service et une boucle de réservation de stock. La
    /// clôture, elle, passe par Kafka — donc plus tard encore.
    ///
    /// On rend la commande déjà créée plutôt qu'une erreur : c'est ce que
    /// l'appelant attendait, et cela rend le second appel inoffensif.
    ///
    /// CETTE LECTURE NE SUFFIT PAS SEULE.
    ///
    /// Elle ne voit pas deux requêtes SIMULTANÉES : les deux lisent « aucune
    /// commande » avant que l'une ait écrit. Seul l'index unique posé par la
    /// migration `UnicitePanierParCommande` ferme cette course, et il la ferme du
    /// bon côté — la seconde insertion échoue, au lieu d'encaisser deux fois.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<Order?> GetByCartAsync(Guid cartId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commandes comportant au moins une ligne vendue par ce vendeur, de la plus
    /// récente à la plus ancienne, dans la limite de <paramref name="take"/>.
    /// </summary>
    /// <remarks>
    /// CETTE BORNE CHANGE CE QUE VOIENT LES CLIENTS, ET IL FAUT LE SAVOIR (§12).
    ///
    /// L'historique d'un vendeur n'avait AUCUNE limite : le carnet remontait
    /// entier, avec les lignes et les options de chaque commande. Un vendeur à
    /// succès payait sa réussite à chaque ouverture de son écran de travail.
    ///
    /// Un vendeur qui a plus de `take` commandes ne verra désormais que les plus
    /// récentes. **C'est une régression fonctionnelle assumée**, et la vraie
    /// réponse est une pagination de la route — `ListPagedAsync` existe déjà comme
    /// modèle côté console d'administration. Elle change le contrat HTTP, donc elle
    /// se décide avec les clients web et mobile, pas ici.
    ///
    /// Entre « tronqué et visible » et « illimité et lent », le premier est
    /// réparable.
    /// </remarks>
    Task<IReadOnlyList<Order>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des quantités vendues par ce vendeur sur les commandes encaissées.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ELLE EXISTE PARCE QUE CE COMPTEUR SE CALCULAIT EN CHARGEANT TOUT (§11-12).
    ///
    /// `GetSellerSalesCountAsync` appelait `ListBySellerAsync` — donc TOUTES les
    /// commandes du vendeur, avec leurs lignes et les options de chaque ligne —
    /// pour n'en tirer qu'une somme d'entiers. Le tout dans une boucle sur les
    /// vendeurs d'une commande, déclenchée à CHAQUE CONFIRMATION.
    ///
    /// Un vendeur à dix mille ventes faisait donc remonter dix mille agrégats en
    /// mémoire pour rendre un nombre — et une commande à trois vendeurs le faisait
    /// trois fois. Le coût croissait avec le succès du vendeur, ce qui est la pire
    /// forme : plus la plateforme marche, plus elle ralentit.
    ///
    /// Cette signature rend l'agrégation à la BASE, qui sait la faire sans rien
    /// matérialiser.
    ///
    /// ELLE NE REMPLACE PAS `ListBySellerAsync`, qui reste nécessaire là où
    /// l'appelant a réellement besoin des commandes. Elle lui retire seulement le
    /// seul appelant qui n'en avait pas besoin.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<int> SumSoldQuantityBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les commandes de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de commandes pour la console admin : filtre par statut, recherche par
    /// identifiant (commande ou acheteur, GUID exact), tri par date décroissante.
    /// Renvoie le total filtré + la répartition par statut (avant filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);
}
