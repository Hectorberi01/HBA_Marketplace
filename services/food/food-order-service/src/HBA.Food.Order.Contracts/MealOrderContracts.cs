namespace HBA.FoodOrders.Contracts;

/// <summary>Une option figée sur une ligne de commande.</summary>
public sealed record MealOrderLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Une ligne de commande de repas, FIGÉE.
///
/// <paramref name="Name"/> et <paramref name="UnitPrice"/> sont ceux du moment de
/// l'achat, pas ceux de la carte d'aujourd'hui — c'est ce qui permet au
/// restaurateur de retirer un plat sans réécrire l'histoire des commandes.
/// </summary>
public sealed record MealOrderLineSummary(
    Guid LineId,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency,
    string? Notes,
    IReadOnlyList<MealOrderLineOptionSummary> Options);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// OÙ LE REPAS DOIT ÊTRE PORTÉ.
///
/// ÉCRIT PARCE QU'AUCUNE COURSE N'ÉTAIT JAMAIS CRÉÉE POUR UN REPAS.
///
/// `CreateDeliveryOnFoodOrderReadyHandler`, dans restaurant-service, construit la
/// demande de course quand le sac est prêt. Il lisait l'adresse chez
/// order-service — le seul univers de commandes qu'il connaissait. Pour un ticket
/// né d'une `MealOrder`, la commande était introuvable : il levait, les reprises
/// Kafka s'épuisaient, et le repas restait sur le passe sans qu'aucun livreur ne
/// soit cherché.
///
/// `CommuneName` ET NON `CommuneCode`.
///
/// `MealOrder` stocke le CODE — c'est la bonne donnée à persister, elle ne bouge
/// pas. Mais `DeliveryStopRequest` attend le LIBELLÉ, et `BeninGeography` vit
/// dans le socle du domaine : faire résoudre le code par restaurant-service
/// marcherait, et ferait deux endroits où la traduction peut diverger. On rend
/// donc ce que le consommateur utilise.
///
/// CE QUE CE TYPE N'EST PAS : UNE ADRESSE DE CARNET.
///
/// C'est l'adresse FIGÉE de cette commande-là, telle qu'elle a été saisie au
/// moment de commander. Le carnet d'adresses de l'acheteur vit chez user-service
/// et peut changer ensuite ; le livreur, lui, doit aller là où le client
/// attendait le soir de la commande.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MealOrderShippingAddressSummary(
    string? Recipient,
    string? Phone,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude);

/// <summary>Une commande de repas, vue de l'extérieur du service.</summary>
public sealed record MealOrderSummary(
    Guid OrderId,
    Guid BuyerId,
    Guid RestaurantId,
    string Status,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalAmount,
    string Currency,
    string? PromotionCode,
    string? DeliveryQuoteId,
    string? CustomerNote,
    DateTime CreatedOnUtc,
    IReadOnlyList<MealOrderLineSummary> Lines,

    /// <summary>
    /// L'adresse de remise figée à la commande. NULLE tant qu'elle n'a pas été
    /// posée — `PlaceMealOrderCommandHandler` l'exige, mais une commande écrite
    /// avant ce lot peut ne rien porter.
    ///
    /// AJOUT OPTIONNEL EN FIN DE LISTE (D32) : les appelants positionnels
    /// existants continuent de compiler, et un `null` se distingue d'une adresse
    /// vide — « on ne sait pas » n'est pas « nulle part ».
    /// </summary>
    MealOrderShippingAddressSummary? ShippingAddress = null);

/// <summary>
/// API publique du service de commande de repas.
///
/// VOLONTAIREMENT MAIGRE. Ce que l'extérieur a le droit de savoir d'une
/// commande de restaurant se limite à ses rattachements, son état, ses lignes
/// figées — et, depuis le lot 6.4, l'adresse de remise, sans laquelle personne ne
/// pouvait appeler de livreur pour un repas. Le ticket de cuisine — acceptation, postes, minutes de préparation —
/// appartient à restaurant-service et n'a rien à faire ici.
/// </summary>
public interface IMealOrderModuleApi
{
    Task<MealOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il déjà commandé un repas ?
    ///
    /// EXISTE POUR QUE LES PROMOTIONS « PREMIÈRE COMMANDE » FONCTIONNENT.
    ///
    /// Le panier de la marketplace a connu le cas inverse : `IsFirstOrder` était
    /// codé en dur à `false`, et toute une famille de promotions était donc
    /// littéralement inapplicable — le back-office pouvait les créer, l'admin les
    /// activer, aucun acheteur n'en voyait la couleur. Le commentaire est encore
    /// dans `CartPricer`.
    ///
    /// Le coût est un EXISTS sur un index, évalué une fois par panier et non une
    /// fois par ligne : c'est une propriété de l'acheteur, pas du plat.
    /// </summary>
    Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
