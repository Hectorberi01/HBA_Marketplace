using HBA.DeliveryPricing.Contracts;
using HBA.Food.Contracts;
using HBA.FoodCarts.Contracts;
using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using OrderAggregate = HBA.FoodOrders.Domain.Orders.MealOrder;

namespace HBA.FoodOrders.Application.Orders.Commands;

/// <summary>Adresse de livraison choisie au paiement (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);

/// <summary>
/// Passe la commande à partir du panier de repas actif.
/// </summary>
/// <param name="DeliveryQuoteId">
/// Le devis de course qui FIXE les frais de livraison.
///
/// IL N'Y A PAS DE `ShippingFee` ICI, ET C'EST DÉLIBÉRÉ.
///
/// `PlaceOrderCommand` en portait un, alimenté par le corps de la requête HTTP :
/// l'acheteur posait `ShippingFee = 0`, se faisait livrer gratuitement, et la
/// plateforme achetait pourtant la course au prix réel. Ajouter un contrôle « le
/// montant doit correspondre au devis » aurait laissé le champ en place, donc la
/// possibilité de l'oublier au prochain appelant. Supprimer le champ supprime le
/// mensonge possible.
///
/// L'IDENTIFIANT, lui, vient bien du client — et c'est souhaitable : il désigne
/// le prix qu'on lui a AFFICHÉ. Le redemander en interne produirait un second
/// devis, calculé sur la grille du moment, qu'il n'a jamais accepté.
/// </param>
public sealed record PlaceMealOrderCommand(
    Guid BuyerId,
    ShippingAddressInput? ShippingAddress = null,
    string? DeliveryQuoteId = null,
    string? CustomerNote = null) : ICommand<Guid>;

internal sealed class PlaceMealOrderCommandHandler : ICommandHandler<PlaceMealOrderCommand, Guid>
{
    /// <summary>
    /// Tolérance de comparaison entre le point de chute FIGÉ dans le devis et
    /// celui de l'adresse enregistrée sur la commande.
    ///
    /// CE N'EST PAS UNE MARGE COMMERCIALE, C'EST UNE MARGE DE CODAGE.
    ///
    /// Le devis et la commande portent la MÊME position, saisie une seule fois —
    /// mais elle voyage en `double` à travers protobuf, PostgreSQL et deux
    /// sérialisations JSON. Une égalité stricte casserait sur le dernier bit de
    /// mantisse et referait échouer des paiements légitimes.
    ///
    /// 0,0005° ≈ 55 m à la latitude de Cotonou : assez large pour absorber un
    /// arrondi, bien trop étroit pour couvrir un changement de quartier — donc
    /// pour laisser passer un devis obtenu sur une adresse voisine et moins chère.
    /// </summary>
    private const double ToleranceDegres = 0.0005;

    /// <summary>
    /// UN REPAS PART EN « EXPRESS », ET LE DEVIS DOIT L'AVOIR ÉTÉ AUSSI.
    ///
    /// Un plat chaud a une durée de vie de quelques dizaines de minutes ; un colis
    /// part en « Standard », moins cher. Sans ce contrôle, un client chiffre son
    /// repas en « Standard », paie le tarif du colis, et la plateforme achète
    /// l'Express — la faille du montant refermée ailleurs se rouvrirait par la
    /// porte du niveau de service, avec un devis par ailleurs irréprochable.
    /// </summary>
    private const string NiveauDeService = "Express";

    private readonly IFoodCartModuleApi _carts;
    private readonly IFoodModuleApi _food;
    /// <summary>
    /// La relecture du devis de course.
    /// </summary>
    /// <remarks>
    /// CHEZ delivery-pricing, ET NON PLUS CHEZ delivery-service.
    ///
    /// Ce champ était un `IDeliveryDispatchApi`, et l'appel
    /// `LookupQuoteAsync` rendait `UNIMPLEMENTED` : `DeliveryApi.LookupQuote`
    /// n'a JAMAIS eu de corps de serveur. delivery-service n'a d'ailleurs plus
    /// de domaine de tarification — le seul magasin de devis vivant est celui de
    /// delivery-pricing, et c'est déjà lui que `CreateDeliveryCommand` consomme.
    /// </remarks>
    private readonly IDeliveryQuoteLookup _devis;
    private readonly IMealOrderRepository _orders;
    private readonly IMealOrderUnitOfWork _unitOfWork;

    public PlaceMealOrderCommandHandler(
        IFoodCartModuleApi carts,
        IFoodModuleApi food,
        IDeliveryQuoteLookup devis,
        IMealOrderRepository orders,
        IMealOrderUnitOfWork unitOfWork)
    {
        _carts = carts;
        _food = food;
        _devis = devis;
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(PlaceMealOrderCommand command, CancellationToken cancellationToken)
    {
        var panier = await _carts.GetActiveCartAsync(command.BuyerId, cancellationToken);
        if (panier is null || panier.Lines.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.cart_empty", "Aucun panier de repas à commander."));
        }

        if (panier.RestaurantId is not { } restaurantId || restaurantId == Guid.Empty)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.cart_without_restaurant", "Ce panier ne désigne aucun restaurant."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // IDEMPOTENCE : LE MÊME PANIER NE PRODUIT QU'UNE COMMANDE.
        //
        // `POST /api/orders` n'en avait aucune, et rien dans le schéma ne s'y
        // opposait : aucune contrainte d'unicité sur `CartId`. Un double-clic, un
        // réseau lent suivi d'un renvoi, ou un rejeu de requête créait DEUX
        // commandes sur le même panier — donc deux paiements, et un client à
        // rembourser.
        //
        // On rend la commande déjà créée plutôt qu'une erreur : c'est ce que le
        // client attendait, et cela rend le second appel inoffensif. L'index
        // unique en base ferme la course entre deux requêtes simultanées, que
        // cette lecture seule ne peut pas voir.
        // ═════════════════════════════════════════════════════════════════════
        var deja = await _orders.GetByCartAsync(panier.CartId, cancellationToken);
        if (deja is not null)
        {
            return Result.Success(deja.Id.Value);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE RESTAURANT DOIT ENCORE PRENDRE DES COMMANDES AU MOMENT DE PAYER.
        //
        // Le panier a pu être rempli à 22 h 50 et payé à 23 h 05, après la
        // fermeture. Sans ce contrôle, la commande est encaissée, le ticket part
        // vers une cuisine éteinte, et personne ne le voit avant le lendemain.
        //
        // C'est aussi le seul endroit où l'on peut encore le dire au client d'une
        // façon qui l'aide : « ce restaurant est fermé » vaut mieux que « votre
        // commande a été annulée » vingt minutes plus tard.
        // ═════════════════════════════════════════════════════════════════════
        var restaurant = await _food.GetRestaurantAsync(restaurantId, cancellationToken);
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "food_ordering.restaurant_not_found", "Établissement introuvable."));
        }

        if (!restaurant.AcceptsOrdersNow)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.restaurant_closed",
                "Ce restaurant ne prend pas de commande en ce moment."));
        }

        // LE MINIMUM DE COMMANDE SE VÉRIFIE ICI AUSSI, PAS SEULEMENT EN CUISINE.
        //
        // `FoodOrderCommands` le contrôle à la réception du ticket — donc APRÈS le
        // paiement. Le client était débité, puis sa commande refusée pour un
        // montant qu'il aurait pu compléter d'un plat.
        if (restaurant.MinimumOrderAmount is { } minimum && panier.GrandTotal < minimum)
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.below_minimum",
                $"Ce restaurant demande une commande d'au moins {minimum:0.##} {panier.Currency}."));
        }

        var brouillons = panier.Lines.Select(l => new MealOrderLineDraft(
            l.MenuItemId,
            l.Name,
            l.Quantity,
            l.UnitBaseAmount,
            l.SellerDiscount,
            l.PlatformDiscount,
            l.FinalUnitPrice,
            l.Notes,
            l.Options.Select(o => new MealOrderLineOptionDraft(o.OptionGroupId, o.OptionId)).ToList()));

        var creation = OrderAggregate.Create(
            command.BuyerId,
            restaurantId,
            panier.CartId,
            panier.Currency,
            brouillons,
            panier.PromotionCode,
            command.CustomerNote);

        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        var commande = creation.Value;

        // ═════════════════════════════════════════════════════════════════════
        // L'ADRESSE EST UN INVARIANT DE LA COMMANDE, PAS UNE VÉRIFICATION
        //    D'INTERFACE.
        //
        // Le contrôle équivalent vivait dans un `if` du BFF Mobile, et il n'y
        // suffisait pas : le champ valait `null` par défaut, si bien qu'omettre
        // simplement l'adresse passait à côté du garde-fou et créait une commande
        // SANS adresse — payée. Un invariant vrai sur un seul des trois chemins
        // d'écriture n'est pas un invariant.
        //
        // ET POUR UN REPAS, LA COMMUNE ET LE REPÈRE NE SUFFISENT PAS.
        //
        // Ils suffisent à un colis : le livreur a la journée pour trouver, et il
        // appelle au besoin. Un plat chaud, non — la course se calcule à la
        // distance RÉELLE. Sans coordonnées, la commande est payée, le repas
        // cuisiné, et la course refusée au moment où le sac est prêt : le pire
        // instant pour découvrir qu'il manquait une donnée saisie vingt minutes
        // plus tôt.
        // ═════════════════════════════════════════════════════════════════════
        if (command.ShippingAddress is not { } adresse
            || string.IsNullOrWhiteSpace(adresse.CommuneCode)
            || string.IsNullOrWhiteSpace(adresse.Landmark))
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.shipping_address_required",
                "Une adresse de livraison complète est obligatoire : commune et point de repère."));
        }

        if (adresse.Latitude is null
            || adresse.Longitude is null
            || string.IsNullOrWhiteSpace(adresse.Phone))
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.address_incomplete",
                "Une commande de repas exige une position sur la carte et un téléphone joignable."));
        }

        commande.SetShippingAddress(
            adresse.Label, adresse.Recipient, adresse.Phone,
            adresse.CommuneCode, adresse.Quartier, adresse.Landmark, adresse.Line1, adresse.CountryCode,
            adresse.Latitude, adresse.Longitude);

        var frais = await ResoudreFraisAsync(adresse, command.DeliveryQuoteId, cancellationToken);
        if (frais.IsFailure)
        {
            return Result.Failure<Guid>(frais.Error);
        }

        commande.SetShippingFee(frais.Value.Montant, frais.Value.QuoteId);

        await _orders.AddAsync(commande, cancellationToken);

        // AUCUNE RÉSERVATION DE STOCK, ET IL N'Y A RIEN À RÉSERVER.
        //
        // `PlaceOrderCommandHandler` boucle ici sur Inventory. Un plat n'y existe
        // pas — et le lui soumettre ne produisait AUCUNE erreur : `TryReserveAsync`
        // répond VRAI pour un SKU sans enregistrement de stock. La réservation
        // « réussissait », la commande partait au paiement puis à l'expédition
        // comme un colis, et personne ne cuisinait. C'est ce silence que la
        // séparation supprime : il n'y a plus de code de réservation à contourner.
        commande.MarkAwaitingPayment();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return commande.Id.Value;
    }

    /// <summary>Les frais retenus, et le devis qui les fonde.</summary>
    private readonly record struct FraisDeCourse(decimal Montant, string QuoteId);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DÉTERMINE LES FRAIS DE COURSE — SANS JAMAIS CROIRE LE CLIENT.
    ///
    /// SIX REFUS, ET AUCUN NE DOIT DEVENIR UN REPLI SILENCIEUX SUR ZÉRO.
    ///
    /// C'est la tentation permanente de ce genre de code : « devis illisible, tant
    /// pis, on met zéro et on laisse passer ». Zéro n'est PAS un défaut prudent —
    /// c'est exactement la faille qu'on referme : le client est livré, la
    /// plateforme paie la course. Échouer avant l'encaissement coûte un panier ;
    /// laisser passer coûte une course, à chaque commande, sans que personne ne le
    /// voie.
    ///
    /// ON RELIT LE DEVIS, ON N'EN REDEMANDE PAS UN.
    ///
    /// `RequestQuoteAsync` ÉCRIT et rendrait un SECOND prix, calculé sur la grille
    /// de l'instant : on facturerait un montant que le client n'a jamais vu ni
    /// accepté. La relecture est la seule opération qui satisfasse les deux
    /// exigences à la fois — le serveur impose le prix, ET c'est le prix affiché.
    ///
    /// ET LE DEVIS EST OBLIGATOIRE, LÀ OÙ LA MARCHANDISE S'EN PASSAIT.
    ///
    /// `ResoudreFraisDeLivraisonAsync` tolérait l'absence de devis pour un colis :
    /// frais à zéro, journalisés comme un trou de recette assumé, faute de grille
    /// de forfaits côté serveur. Pour un repas la course EST achetée au prix réel
    /// quand le sac est prêt : sans devis il n'existe aucun montant opposable, et
    /// laisser passer reviendrait à offrir la livraison.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<Result<FraisDeCourse>> ResoudreFraisAsync(
        ShippingAddressInput adresse, string? quoteId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(quoteId))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_required",
                "Les frais de livraison d'un repas doivent être chiffrés par un devis avant "
                + "le paiement. Demandez un devis de course, puis reprenez le paiement avec "
                + "son identifiant."));
        }

        var devis = await _devis.LookupQuoteAsync(quoteId, cancellationToken);

        // INTROUVABLE OU ILLISIBLE : MÊME REFUS, MÊME GESTE ATTENDU.
        //
        // Identifiant recopié de travers, devis purgé, ou inventé de toutes pièces
        // pour obtenir un prix : dans tous les cas il n'y a aucun montant
        // opposable, et le client doit repasser par un devis.
        if (devis is null)
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_not_found",
                "Ce devis de livraison est introuvable. Demandez un nouveau prix avant de payer."));
        }

        // CONSOMMÉ AVANT EXPIRÉ : L'ORDRE DES DEUX CONTRÔLES CHANGE LE MESSAGE.
        //
        // Un devis déjà dépensé finit toujours par expirer. Tester l'expiration
        // d'abord dirait « redemandez un prix » à qui rejoue un devis, en masquant
        // le vrai problème : une course existe déjà pour ce devis.
        if (devis.IsConsumed)
        {
            return Result.Failure<FraisDeCourse>(Error.Conflict(
                "food_ordering.delivery_quote_used",
                "Ce devis de livraison a déjà servi à une course. Demandez un nouveau prix."));
        }

        if (devis.IsExpired)
        {
            return Result.Failure<FraisDeCourse>(Error.Conflict(
                "food_ordering.delivery_quote_expired",
                $"Ce devis de livraison a expiré le {devis.ExpiresAtUtc:u}. "
                + "Demandez un nouveau prix avant de payer."));
        }

        // UN DEVIS DE PARTENAIRE N'EST PAS UN DEVIS DE CLIENT.
        //
        // Les devis de l'API publique relèvent d'une grille négociée, souvent plus
        // basse. Sans ce refus, un client qui a mis la main sur un identifiant de
        // partenaire s'en servirait pour payer sa course au tarif de gros.
        if (devis.PartnerId is not null)
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_foreign",
                "Ce devis de livraison n'a pas été établi pour cette commande."));
        }

        // LE DEVIS DOIT AVOIR ÉTÉ ÉTABLI POUR CETTE ADRESSE-CI.
        //
        // Sans ce contrôle, un devis valide reste opposable pour n'importe quelle
        // destination : on chiffre une course de deux rues, puis on la présente
        // pour une livraison à l'autre bout de la ville. Le montant est « réel » —
        // il n'est simplement pas celui de ce trajet-là, et l'écart est payé par la
        // plateforme.
        //
        // La position est toujours présente ici : elle est exigée quelques lignes
        // plus haut. Ce contrôle n'a donc pas la clause conditionnelle que son
        // équivalent marketplace devait porter pour les colis sans coordonnées.
        if (adresse.Latitude is { } lat && adresse.Longitude is { } lon
            && (Math.Abs(devis.DropoffLatitude - lat) > ToleranceDegres
                || Math.Abs(devis.DropoffLongitude - lon) > ToleranceDegres))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_address_mismatch",
                "Ce devis de livraison a été établi pour une autre adresse. "
                + "Demandez un nouveau prix pour l'adresse choisie."));
        }

        if (!string.Equals(devis.DeliveryType, NiveauDeService, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_wrong_service",
                $"Ce devis de livraison a été établi pour un service « {devis.DeliveryType} », "
                + $"alors qu'un repas est livré en « {NiveauDeService} ». Demandez un nouveau prix."));
        }

        // C'EST LE MONTANT DU DEVIS QUI ENTRE DANS LA COMMANDE — PAS UN MONTANT
        // COMPARÉ AU DEVIS.
        //
        // Comparer aurait supposé qu'un montant nous soit soumis, donc qu'un champ
        // falsifiable subsiste. En employant directement `devis.Total`, l'écart
        // entre « ce qui est affiché » et « ce qui est facturé » n'a plus d'endroit
        // où naître.
        return new FraisDeCourse(devis.Total, devis.QuoteId);
    }
}
