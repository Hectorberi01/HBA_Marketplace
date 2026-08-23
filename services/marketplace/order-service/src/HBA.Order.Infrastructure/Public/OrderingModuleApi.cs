using MediatR;
using HBA.Orders.Application.Orders.Queries;
using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Ordering. Délègue à la
/// requête GetOrder ; renvoie null si absent.
/// </summary>
internal sealed class OrderingModuleApi : IOrderingModuleApi
{
    private readonly ISender _sender;
    private readonly IOrderRepository _orders;
    private readonly ISellerOrderRepository _sellerOrders;

    public OrderingModuleApi(ISender sender, IOrderRepository orders, ISellerOrderRepository sellerOrders)
    {
        _sender = sender;
        _orders = orders;
        _sellerOrders = sellerOrders;
    }

    /// <summary>
    /// Lecture directe au repository, sans passer par MediatR : c'est un simple EXISTS
    /// sur index, appelé à CHAQUE valorisation de panier. Y interposer un pipeline de
    /// requête (validation, logging, transaction) coûterait plus que la requête elle-même.
    /// </summary>
    public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => _orders.HasPurchasedAsync(buyerId, cancellationToken);

    public async Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Ce que return-refund doit savoir pour ouvrir un dossier et plafonner un
    /// remboursement.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX CHAMPS ÉTAIENT CODÉS À ZÉRO EN DUR (ISSUE-014).
    ///
    ///     AlreadyReturnedQuantity: 0,
    ///     AlreadyRefundedAmount: 0m,
    ///
    /// Ce n'était pas un calcul approximatif : c'était une affirmation fausse,
    /// répétée à chaque appel. Return-refund fonde là-dessus DEUX contrôles — la
    /// quantité encore retournable (`DeliveredQuantity − AlreadyReturnedQuantity`,
    /// dans `ReturnItem.Create`) et le plafond de remboursement de la commande
    /// (`CapturedAmount − AlreadyRefundedAmount`, dans `DecideRefundCommandHandler`).
    /// Les deux repartaient de zéro à chaque dossier. Le même exemplaire pouvait
    /// donc être retourné et remboursé autant de fois qu'on ouvrait de demandes,
    /// chacune validée par des garde-fous qui s'exécutaient et n'arrêtaient rien.
    ///
    /// Order-service ne pouvait pas le calculer : il ne possède pas les retours.
    /// C'est `ReturnRefundedIntegrationEvent` qui les lui apprend désormais, et
    /// `RecordReturnSettlementOnRefundHandler` qui les inscrit dans l'agrégat.
    ///
    /// CE QUE CES DEUX CHAMPS COMPTENT EXACTEMENT — ET CE QU'ILS NE COMPTENT PAS.
    ///
    /// Ils comptent les remboursements ABOUTIS : l'argent est parti, la référence
    /// du prestataire existe. Ils ne comptent pas les dossiers ouverts, ni les
    /// remboursements décidés et pas encore versés — order-service ne les voit
    /// pas. Cette fenêtre-là se ferme du côté de return-refund, qui possède ses
    /// propres dossiers en cours ; voir `CreateReturnCommandHandler`.
    ///
    /// ET UN TROISIÈME CHAMP MENTAIT : `SellerOrderId: null` EN DUR (ISSUE-027).
    ///
    /// Ce n'était pas non plus une négligence de calcul : l'agrégat `SellerOrder`
    /// n'existait pas. C'était la trace la plus visible du manque — return-refund
    /// recevait un champ prévu pour désigner la commande VENDEUR, et il était
    /// toujours nul. Il est désormais résolu par le couple (commande, vendeur).
    ///
    /// IL RESTE NUL DANS DEUX CAS, ET AUCUN N'EST UNE ANOMALIE.
    ///
    ///   • les commandes CONFIRMÉES AVANT la migration `CommandeParVendeur` : la
    ///     table est née vide, elles n'ont pas de part. Ce sont précisément les
    ///     seules commandes déjà livrées, donc les seules retournables
    ///     aujourd'hui — la valeur mettra donc un cycle de vente complet à
    ///     devenir majoritairement renseignée ;
    ///   • les commandes de REPAS, qui n'ont pas de vendeur au sens de la
    ///     marketplace. Elles n'arrivent de toute façon pas ici : `GetOrderReturn
    ///     ContextAsync` exige `Delivered` et un paiement, mais rien n'écarte un
    ///     repas — voir la limite du `SellerId` ci-dessous, qui est la même.
    ///
    /// ET IL SUIT `SellerId`, DONC IL HÉRITE DE SON APPROXIMATION.
    ///
    /// Ce contexte ne décrit qu'UN vendeur — celui de la PREMIÈRE ligne — alors
    /// qu'une commande peut en compter deux. Ce raccourci est antérieur à ce lot
    /// et n'est pas corrigé ici : le corriger demande de rendre `OrderReturnContext`
    /// multi-vendeurs, donc de changer un contrat que return-refund consomme.
    /// La part rendue est celle du vendeur annoncé, ce qui garde les deux champs
    /// COHÉRENTS entre eux — c'était la seule chose à ne pas rater.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<OrderReturnContext?> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(new OrderId(orderId), cancellationToken);
        if (order is null || order.Status != OrderStatus.Delivered || order.PaymentId is null)
        {
            return null;
        }

        var lines = order.Lines.Select(line => new OrderReturnLineContext(
            OrderItemId: line.Id,
            ProductId: line.ProductId,
            VariantId: null,
            CategoryId: Guid.Empty,
            Sku: line.Sku,
            Name: string.IsNullOrWhiteSpace(line.Sku) ? line.ProductId.ToString() : line.Sku,
            OrderedQuantity: line.Quantity,
            DeliveredQuantity: line.Quantity,
            AlreadyReturnedQuantity: order.ReturnedQuantityFor(line.Id),
            UnitPaidAmount: line.FinalUnitPrice)).ToList();

        var firstSellerId = lines.Count == 0
            ? Guid.Empty
            : order.Lines.First().SellerId;

        // La part de CE vendeur-là, pour que les deux champs désignent le même.
        var sellerOrder = firstSellerId == Guid.Empty
            ? null
            : await _sellerOrders.FindAsync(order.Id.Value, firstSellerId, cancellationToken);

        return new OrderReturnContext(
            OrderId: order.Id.Value,
            CustomerId: order.BuyerId,
            SellerId: firstSellerId,
            StoreId: firstSellerId,
            SellerOrderId: sellerOrder?.Id.Value,
            DeliveredAtUtc: order.CreatedAtUtc,
            PaymentId: order.PaymentId.Value.ToString(),
            Currency: order.Currency,
            CapturedAmount: order.GrandTotal,
            AlreadyRefundedAmount: order.RefundedAmount,
            Lines: lines);
    }

    /// <summary>
    /// Somme des quantités vendues par ce vendeur sur les commandes encaissées
    /// (Confirmed / Delivered).
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE CHARGEAIT TOUT L'HISTORIQUE POUR RENDRE UN ENTIER (§11-12).
    ///
    /// Elle appelait `ListBySellerAsync` — toutes les commandes du vendeur, leurs
    /// lignes, et les options de chaque ligne — puis faisait la somme EN MÉMOIRE.
    /// Le commentaire d'alors disait « c'est une agrégation simple » : elle l'est,
    /// et c'est précisément pour cela qu'elle n'avait rien à faire en mémoire.
    ///
    /// ET L'APPELANT EN FAISAIT UNE BOUCLE. `SellerSalesCountHandler` itère sur
    /// les vendeurs d'une commande confirmée et appelle ceci pour chacun — à chaque
    /// confirmation. Une commande à trois vendeurs relisait donc trois historiques
    /// complets. Le coût croissait avec le succès de chaque vendeur : plus la
    /// plateforme marche, plus elle ralentit.
    ///
    /// La somme est désormais faite par la base. `ListBySellerAsync` reste là pour
    /// les appelants qui ont réellement besoin des commandes.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => _orders.SumSoldQuantityBySellerAsync(sellerId, cancellationToken);
}
