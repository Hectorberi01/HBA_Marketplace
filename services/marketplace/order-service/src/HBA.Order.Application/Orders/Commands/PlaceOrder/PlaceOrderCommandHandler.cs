using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Contracts;
using HBA.DeliveryPricing.Contracts;
using HBA.Inventory.Contracts;
using HBA.Products.Contracts;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;
using Microsoft.Extensions.Logging;
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Application.Orders.Commands.PlaceOrder;

/// <summary>
/// Orchestrateur du Saga de commande. Lit le panier valorisé (Cart), fige les
/// prix dans une commande, réserve le stock ligne par ligne (Inventory) et place
/// la commande en attente de paiement. En cas d'échec de réservation, il libère
/// (compensation) ce qui avait déjà été réservé et marque la commande en échec.
/// </summary>
internal sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    /// <summary>
    /// Tolérance de comparaison entre le point de chute FIGÉ dans le devis et
    /// celui de l'adresse enregistrée sur la commande.
    ///
    /// ELLE N'EST PAS UNE MARGE COMMERCIALE, C'EST UNE MARGE DE CODAGE.
    ///
    /// Le devis et la commande portent la MÊME position, saisie une seule fois par
    /// l'acheteur — mais elle voyage en `double` à travers protobuf, PostgreSQL et
    /// deux sérialisations JSON. Une égalité stricte casserait sur le dernier bit
    /// de mantisse, et referait échouer des checkouts parfaitement légitimes.
    ///
    /// 0,0005° ≈ 55 m à la latitude de Cotonou : assez large pour absorber un
    /// arrondi, bien trop étroit pour couvrir un changement de quartier — donc
    /// pour laisser passer un devis obtenu sur une adresse voisine et moins chère.
    /// </summary>
    private const double ToleranceDegres = 0.0005;

    private readonly ICartModuleApi _cartModuleApi;
    private readonly IProductsModuleApi _catalogue;
    private readonly IInventoryModuleApi _inventoryModuleApi;
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
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly ILogger<PlaceOrderCommandHandler> _logger;

    public PlaceOrderCommandHandler(
        ICartModuleApi cartModuleApi,
        IProductsModuleApi catalogue,
        IInventoryModuleApi inventoryModuleApi,
        IDeliveryQuoteLookup devis,
        IOrderRepository orderRepository,
        IOrderingUnitOfWork unitOfWork,
        ILogger<PlaceOrderCommandHandler> logger)
    {
        _cartModuleApi = cartModuleApi;
        _catalogue = catalogue;
        _inventoryModuleApi = inventoryModuleApi;
        _devis = devis;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var cart = await _cartModuleApi.GetActiveCartAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict("ordering.cart_empty", "Aucun panier valorisé à commander."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // IDEMPOTENCE : LE MÊME PANIER NE PRODUIT QU'UNE COMMANDE.
        //
        // Cette route n'en avait aucune, et rien dans le schéma ne s'y opposait :
        // `CartId` n'avait ni contrainte d'unicité ni même un index. Un
        // double-clic, un réseau lent suivi d'un renvoi, ou un rejeu de requête
        // créait DEUX commandes sur le même panier — donc deux réservations de
        // stock, et deux paiements à réclamer.
        //
        // On rend la commande déjà créée plutôt qu'une erreur : c'est ce que
        // l'appelant attendait, et cela rend le second appel inoffensif.
        //
        // ON LIT AVANT TOUT LE RESTE, ET C'EST LE POINT.
        //
        // Placée plus bas, la vérification laisserait passer la relecture du
        // devis — qui n'écrit pas, mais coûte un aller-retour — et surtout la
        // boucle de réservation : le second appel poserait des réservations de
        // stock sur une commande qu'il s'apprête à ne pas créer, et personne ne
        // les libérerait.
        //
        // ELLE NE SUFFIT PAS SEULE — voir `IOrderRepository.GetByCartAsync`.
        // Deux requêtes simultanées lisent toutes deux « aucune commande ». C'est
        // l'index unique posé par `UnicitePanierParCommande` qui ferme la course.
        // ═════════════════════════════════════════════════════════════════════
        var deja = await _orderRepository.GetByCartAsync(cart.CartId, cancellationToken);
        if (deja is not null)
        {
            _logger.LogInformation(
                "Panier {CartId} déjà passé en commande {OrderId} : la requête est rejouée, "
                + "aucune seconde commande n'est créée.",
                cart.CartId, deja.Id.Value);

            return deja.Id.Value;
        }

        // ═════════════════════════════════════════════════════════════════════
        // LA NATURE VOYAGE AVEC LA LIGNE, ET UNE NATURE INCONNUE EST UN REFUS.
        //
        // La déduire ici — « pas de SKU, donc un plat » — serait une supposition
        // de plus à maintenir. Le panier sait ce qu'il porte ; il le dit.
        //
        // ET ON NE SE REPLIE PAS SUR « Goods » EN CAS D'ÉCHEC DE LECTURE.
        //
        // Un repli muet est exactement le défaut que cette nature existe pour
        // corriger : une troisième nature, ou une casse différente, deviendrait
        // silencieusement de la marchandise — réservation sur un SKU vide,
        // expédition d'un colis, personne en cuisine. Mieux vaut un checkout qui
        // refuse et se remarque.
        // ═════════════════════════════════════════════════════════════════════
        var naturesInconnues = cart.Lines
            .Select(l => l.Kind)
            .Where(k => !Enum.TryParse<OrderLineKind>(k, ignoreCase: false, out _))
            .Distinct()
            .ToList();

        if (naturesInconnues.Count > 0)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "ordering.unknown_line_kind",
                $"Nature de ligne inconnue : {string.Join(", ", naturesInconnues)}."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE CATALOGUE EST RELU ICI, ET IL NE L'ÉTAIT NULLE PART (ISSUE-048).
        //
        // Le prix, le statut « publié » et l'achetabilité d'une offre n'étaient
        // JAMAIS revérifiés entre l'ajout au panier et le paiement. Tout ce qui
        // finit sur la ligne de commande venait du panier, et le panier fige
        // `UnitBaseAmount` À L'AJOUT — une seule fois, jamais rafraîchi : `Cart.AddItem`
        // sur une ligne existante appelle `IncreaseQuantity` et IGNORE le prix reçu.
        //
        // LA DÉRIVE N'A AUCUNE BORNE.
        //
        // Rien ne fait expirer un panier : `CartStatus` n'a pas d'état « périmé »,
        // et cart-service n'a aucun service d'arrière-plan. Un panier rempli en
        // janvier se paie en juin au prix de janvier. Le cache du panier valorisé
        // (2 minutes) rafraîchit les REMISES, jamais le prix de base — son
        // commentaire dit « les prix sont recalculés à chaque lecture », ce qui est
        // vrai des remises et faux du prix.
        //
        // ET LES CONTRÔLES FAITS À L'AJOUT NE SONT PAS REFAITS.
        //
        // `AddItemToCartCommandHandler` vérifie `offer.IsPurchasable` et
        // `product.IsVisible`. Une offre dépubliée, suspendue pour contrefaçon, ou
        // dont le vendeur a été écarté restait donc parfaitement commandable depuis
        // un panier ouvert avant la sanction.
        //
        // ON REFUSE, ON NE REVALORISE PAS.
        //
        // Réaligner silencieusement sur le prix courant ferait payer À LA HAUSSE
        // un montant que l'acheteur n'a jamais vu — et à la baisse, cela
        // produirait un total différent de celui affiché à l'écran juste avant.
        // Un checkout qui refuse en disant pourquoi est réparable par l'acheteur ;
        // un débit surprise ne l'est pas.
        //
        // LES LIGNES DE REPAS SONT EXCLUES : elles n'ont pas d'offre catalogue.
        // Leur prix est déjà relu par food-cart-service, qui interroge la carte à
        // chaque ajout.
        // ═════════════════════════════════════════════════════════════════════
        var lignesCatalogue = cart.Lines
            .Where(l => Enum.Parse<OrderLineKind>(l.Kind) == OrderLineKind.Goods)
            .ToList();

        if (lignesCatalogue.Count > 0)
        {
            var offres = await _catalogue.GetOffersAsync(
                lignesCatalogue.Select(l => l.OfferId).Distinct().ToList(), cancellationToken);

            foreach (var ligne in lignesCatalogue)
            {
                if (!offres.TryGetValue(ligne.OfferId, out var offre))
                {
                    // Offre archivée, produit supprimé, ou catalogue qui ne la
                    // connaît plus. On nomme le SKU : c'est ce que l'acheteur voit
                    // dans son panier, l'identifiant d'offre ne lui dit rien.
                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.offer_unavailable",
                        $"L'article « {ligne.Sku} » n'est plus proposé à la vente."));
                }

                if (!offre.IsPurchasable)
                {
                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.offer_not_purchasable",
                        $"L'article « {ligne.Sku} » n'est plus disponible à la commande."));
                }

                // `EffectivePrice` — prix promotionnel s'il court, prix courant
                // sinon. C'est exactement ce que le panier a figé à l'ajout, donc
                // la comparaison porte sur la même grandeur.
                if (offre.EffectivePrice != ligne.UnitBaseAmount)
                {
                    _logger.LogInformation(
                        "Checkout refusé pour l'acheteur {BuyerId} : le prix de l'offre {OfferId} "
                        + "({Sku}) est passé de {Ancien} à {Nouveau} {Devise} depuis l'ajout au panier.",
                        command.BuyerId, ligne.OfferId, ligne.Sku,
                        ligne.UnitBaseAmount, offre.EffectivePrice, offre.Currency);

                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.price_changed",
                        $"Le prix de « {ligne.Sku} » a changé depuis son ajout au panier. "
                        + "Vérifiez votre panier avant de commander."));
                }
            }
        }

        var drafts = cart.Lines.Select(l => new OrderLineDraft(
            l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
            l.UnitBaseAmount, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice,
            Enum.Parse<OrderLineKind>(l.Kind),
            l.RestaurantId,
            l.MenuItemId,
            l.Notes,
            l.Options?.Select(o => new OrderLineOptionDraft(o.OptionGroupId, o.OptionId)).ToList()));

        // Le code promo du panier est FIGÉ dans la commande. C'est indispensable : le
        // panier est clôturé juste après, et le coupon n'est décompté qu'à la
        // CONFIRMATION (paiement encaissé). Sans ce report, Pricing recevrait un
        // « commande confirmée » sans savoir quel coupon consommer — et le coupon
        // resterait éternellement disponible. C'est exactement le bug d'origine.
        var orderResult = OrderAggregate.Create(command.BuyerId, cart.CartId, cart.Currency, drafts, cart.PromotionCode);
        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        var order = orderResult.Value;

        // ─────────────────────────────────────────────────────────────────────────────
        // L'ADRESSE DE LIVRAISON EST UN INVARIANT DE LA COMMANDE, PAS UNE VÉRIFICATION
        // D'INTERFACE.
        //
        // Ce contrôle vivait dans un `if` du BFF Mobile, et il n'y suffisait pas :
        // `PayRequest.AddressId` vaut `null` par défaut, si bien qu'omettre simplement le
        // champ passait à côté du garde-fou et créait une commande SANS adresse — payée.
        // Deux autres surfaces (l'API historique et le BFF Web) appellent d'ailleurs cette
        // commande sans jamais fournir d'adresse.
        //
        // Un invariant qui n'est vrai que sur l'un des trois chemins d'écriture n'est pas
        // un invariant. Il est donc ici, là où toutes les commandes passent.
        //
        // Ce qu'on exige : la COMMUNE et le POINT DE REPÈRE. Pas la rue — au Bénin elle
        // est souvent inexistante, et l'exiger reviendrait à faire inventer une adresse.
        // Le repère, lui, est ce que le coursier utilise vraiment.
        // ─────────────────────────────────────────────────────────────────────────────
        if (command.ShippingAddress is not { } addr
            || string.IsNullOrWhiteSpace(addr.CommuneCode)
            || string.IsNullOrWhiteSpace(addr.Landmark))
        {
            return Result.Failure<Guid>(Error.Validation(
                "ordering.shipping_address_required",
                "Une adresse de livraison complète est obligatoire : commune et point de repère."));
        }

        // POUR UN REPAS, LA COMMUNE ET LE REPÈRE NE SUFFISENT PAS.
        //
        // Ils suffisent à un colis : le livreur a la journée pour trouver, et il
        // appelle au besoin. Un plat chaud, non — la course est calculée à la
        // distance RÉELLE, et sans coordonnées aucun devis n'est possible. Sans ce
        // contrôle, la commande est payée, le repas cuisiné, et la course refusée
        // au moment où le sac est prêt : le pire instant pour découvrir qu'il
        // manquait une donnée saisie vingt minutes plus tôt.
        //
        // LA NATURE SE LIT SUR LA COMMANDE, PAS SUR LE PANIER.
        //
        // `cart.Kind` transite par le cache et se dérive de la première ligne.
        // `order.Kind` est la valeur que tout le reste du circuit utilise —
        // l'événement de confirmation, le refus, l'adaptateur vers la cuisine.
        // Deux sources pour la même question finissent par diverger ; celle qui
        // décide doit servir de référence.
        if (order.Kind == OrderLineKind.Food
            && (command.ShippingAddress is not { } food
                || food.Latitude is null
                || food.Longitude is null
                || string.IsNullOrWhiteSpace(food.Phone)))
        {
            return Result.Failure<Guid>(Error.Validation(
                "ordering.food_address_incomplete",
                "Une commande de repas exige une position sur la carte et un téléphone joignable."));
        }

        order.SetShippingAddress(
            addr.Label, addr.Recipient, addr.Phone,
            addr.CommuneCode, addr.Quartier, addr.Landmark, addr.Line1, addr.CountryCode,
            addr.Latitude, addr.Longitude);

        // ═════════════════════════════════════════════════════════════════════
        // LE SERVEUR FIXE LES FRAIS DE LIVRAISON. L'ACHETEUR NE FAIT QUE DÉSIGNER
        // LE DEVIS QU'IL A VU.
        //
        // CE QUI CASSAIT : L'ACHETEUR POSAIT `ShippingFee = 0` DANS LE CORPS DE
        //    SA REQUÊTE ET SE FAISAIT LIVRER AUX FRAIS DE LA PLATEFORME.
        //
        // `PlaceOrderRequest` portait un `ShippingFee`, recopié tel quel dans la
        // commande puis dans le total encaissé. La course, elle, était bel et bien
        // achetée à delivery-service au prix réel — à la confirmation pour un
        // colis, quand le sac est prêt pour un repas. La plateforme réglait donc
        // deux mille francs de course sur une commande qui en avait encaissé
        // zéro, une fois par commande, sans qu'aucune alerte ne se déclenche : le
        // montant était « celui qu'on avait demandé ».
        //
        // Le seul garde-fou existant comparait `ShippingFee` à zéro pour les
        // repas. Il ne protégeait rien : poser 1 franc le franchissait.
        //
        // LE MÉCANISME OPPOSABLE EXISTAIT DÉJÀ, ENTIER, ET N'AVAIT JAMAIS ÉTÉ
        //    APPELÉ.
        //
        // `DeliveryQuote` est persisté, horodaté, à usage unique, et refuse déjà
        // l'expiration comme le double emploi. `IDeliveryDispatchApi` promettait
        // noir sur blanc : « l'identifiant rendu se fige ensuite dans la commande :
        // c'est ce qui garantit que le montant affiché est celui qui sera
        // facturé. » L'identifiant CIRCULAIT effectivement jusqu'à la création de
        // course — ce qui donnait au dispositif toutes les apparences d'être
        // branché. Seule la lecture du MONTANT manquait, et rien dans le code ne
        // la réclamait. C'est le genre d'oubli qui se reproduit : le chaînon
        // manquant n'est pas celui qu'on voit passer.
        //
        // ON RELIT LE DEVIS, ON N'EN REDEMANDE PAS UN.
        //
        // Les devis SONT persistés — d'où le choix de la relecture par
        // identifiant, `LookupQuoteAsync`, ajoutée au contrat gRPC où elle
        // manquait. Redemander un devis ici (`RequestQuoteAsync`, qui ÉCRIT) aurait
        // produit un SECOND prix, calculé sur la grille de l'instant : on aurait
        // facturé à l'acheteur un montant qu'il n'a jamais vu ni accepté. La
        // relecture est la seule opération qui satisfasse les deux exigences à la
        // fois — le serveur impose le prix, ET c'est le prix affiché.
        // ═════════════════════════════════════════════════════════════════════
        var frais = await ResoudreFraisDeLivraisonAsync(order, addr, command, cancellationToken);
        if (frais.IsFailure)
        {
            return Result.Failure<Guid>(frais.Error);
        }

        order.SetShippingFee(frais.Value.Montant, frais.Value.QuoteId);

        await _orderRepository.AddAsync(order, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // ÉTAPE SAGA : RÉSERVATION DU STOCK, AVEC COMPENSATION EN CAS D'ÉCHEC.
        //
        // LES LIGNES DE REPAS SONT ÉCARTÉES, ET IL LE FAUT ABSOLUMENT.
        //
        // Un plat n'existe pas dans Inventory. Le lui soumettre ne produisait
        // pourtant AUCUNE erreur : `TryReserveAsync` répond VRAI pour un SKU sans
        // enregistrement de stock — comportement voulu pour les articles non
        // suivis. La réservation « réussissait » donc, la commande partait au
        // paiement puis à l'expédition comme un colis, et personne ne cuisinait.
        //
        // Ce filtre n'est pas une optimisation : c'est ce qui empêche un plat
        // d'emprunter la chaîne de la marchandise sans que rien ne proteste.
        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // ON RÉSERVE PAR (SKU, EMPLACEMENT), PAS PAR LIGNE DE COMMANDE
        //    (ISSUE-075).
        //
        // DEUX LIGNES DE PANIER PEUVENT PORTER LE MÊME ARTICLE AU MÊME ENDROIT.
        // C'est banal : l'acheteur ajoute le même produit deux fois, ou deux
        // offres du même SKU se ramènent au même emplacement d'expédition. La
        // boucle ligne à ligne appelait alors `TryReserveAsync` DEUX fois pour le
        // même couple (article, commande) — et posait deux réservations
        // concurrentes du même stock, pour une seule commande.
        //
        // CE N'EST PAS UNE ADAPTATION À LA NOUVELLE CONTRAINTE, C'EST LA
        //    CORRECTION D'UNE ERREUR QUI EXISTAIT AVANT ELLE.
        //
        // « une réservation par article et par commande » est la sémantique que
        // tout le reste suppose : `ReleaseReservationAsync` et
        // `ConfirmReservationAsync` travaillent par (SKU, emplacement, commande) et
        // n'ont aucun moyen de désigner « la deuxième ligne ». Avec deux
        // réservations, un `Release` en compensation en relâchait deux, et
        // `reserved` en portait deux entrées identiques.
        //
        // Depuis `Reserve()` idempotent côté inventory, la boucle non regroupée
        // serait devenue franchement FAUSSE : le second appel POSERAIT la quantité
        // de la seconde ligne à la place de celle de la première, et la commande
        // partirait avec la moitié de son stock réservé. Et l'index unique partiel
        // `ux_stock_reservations_active_order` refuse de toute façon la seconde
        // ligne.
        //
        // On somme donc les quantités par couple, et on réserve UNE fois le total.
        //
        // LE REGROUPEMENT NE TOUCHE PAS LES LIGNES DE LA COMMANDE. Elles restent
        // distinctes — prix, remises et retours se raisonnent ligne par ligne.
        // Seul l'APPEL au stock est regroupé.
        // ═════════════════════════════════════════════════════════════════════
        var aReserver = order.Lines
            .Where(l => l.RequiresStockReservation)
            .GroupBy(l => (l.Sku, l.ShipFromLocationId))
            .Select(g => (Sku: g.Key.Sku, LocationId: g.Key.ShipFromLocationId, Quantity: g.Sum(l => l.Quantity)))
            .ToList();

        var reserved = new List<(string Sku, Guid LocationId)>();
        foreach (var demande in aReserver)
        {
            var ok = await _inventoryModuleApi.TryReserveAsync(
                demande.Sku, demande.LocationId, order.Id.Value, demande.Quantity, cancellationToken);

            if (ok)
            {
                reserved.Add((demande.Sku, demande.LocationId));
                continue;
            }

            // Compensation : on libère les réservations déjà obtenues.
            foreach (var r in reserved)
            {
                await _inventoryModuleApi.ReleaseReservationAsync(r.Sku, r.LocationId, order.Id.Value, cancellationToken);
            }

            order.Fail($"Stock indisponible pour le SKU {demande.Sku}.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<Guid>(Error.Conflict("ordering.out_of_stock", $"Stock insuffisant pour {demande.Sku}."));
        }

        order.MarkAwaitingPayment();

        // ═════════════════════════════════════════════════════════════════════
        // SI LA PERSISTANCE ÉCHOUE ICI, LE STOCK EST PERDU SANS VENTE (ISSUE-032).
        //
        // La boucle ci-dessus compense déjà un REFUS de stock : elle relâche ce
        // qu'elle a obtenu et sort. Elle ne compensait pas l'autre échec, celui
        // qui vient après — `SaveChangesAsync` qui lève. Or à cet instant les
        // réservations sont TOUTES prises chez Inventory, et la commande n'existe
        // nulle part.
        //
        // Le résultat est du stock immobilisé pour une commande qui n'a jamais
        // existé. Invisible : aucune commande ne le porte, donc aucune annulation
        // ne le libérera, et aucun balayeur d'expiration n'existe encore
        // (ISSUE-031). C'est une perte sèche et définitive, par incident de base.
        //
        // ON RELÂCHE PUIS ON RELAIE L'EXCEPTION, ON NE L'AVALE PAS.
        //
        // Rendre un `Result.Failure` ferait passer un incident d'infrastructure
        // pour un refus métier : l'appelant n'aurait aucune raison de réessayer, et
        // la panne se lirait comme « commande refusée ». L'exception poursuit sa
        // route ; ce qui change, c'est qu'elle ne laisse plus de stock derrière
        // elle.
        //
        // UN RELÂCHEMENT QUI ÉCHOUE NE DOIT PAS MASQUER LA CAUSE.
        //
        // Inventory peut être injoignable au moment même où la base l'est. Chaque
        // libération est donc isolée : son échec est journalisé — avec le SKU, la
        // seule prise pour rattraper à la main — et n'empêche ni les suivantes ni
        // la remontée de l'exception d'origine.
        // ═════════════════════════════════════════════════════════════════════
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Commande {OrderId} : la persistance a échoué APRÈS {Count} réservation(s) de stock. "
                + "Libération en compensation ; sans elle, ce stock resterait immobilisé pour une "
                + "commande qui n'existe pas.",
                order.Id.Value, reserved.Count);

            foreach (var r in reserved)
            {
                try
                {
                    await _inventoryModuleApi.ReleaseReservationAsync(
                        r.Sku, r.LocationId, order.Id.Value, CancellationToken.None);
                }
                catch (Exception echecLiberation)
                {
                    _logger.LogCritical(
                        echecLiberation,
                        "Commande {OrderId} : la libération du SKU {Sku} sur l'emplacement {LocationId} a "
                        + "ÉCHOUÉ. Ce stock reste réservé pour une commande inexistante — libération "
                        + "manuelle requise.",
                        order.Id.Value, r.Sku, r.LocationId);
                }
            }

            throw;
        }

        return order.Id.Value;
    }

    /// <summary>Les frais retenus, et le devis qui les fonde.</summary>
    private readonly record struct FraisDeLivraison(decimal Montant, string? QuoteId);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DÉTERMINE LES FRAIS DE LIVRAISON — SANS JAMAIS CROIRE L'ACHETEUR.
    ///
    /// CINQ REFUS, ET AUCUN NE DOIT DEVENIR UN REPLI SILENCIEUX SUR ZÉRO.
    ///
    /// C'est la tentation permanente de ce genre de code : « devis illisible, tant
    /// pis, on met zéro et on laisse passer la commande ». Zéro, ici, N'EST PAS un
    /// défaut prudent — c'est très exactement la faille qu'on referme : l'acheteur
    /// est livré, la plateforme paie la course. Un devis absent, expiré, déjà
    /// consommé, étranger ou établi pour une autre adresse fait donc ÉCHOUER le
    /// checkout, avec un message que l'acheteur peut comprendre et suivre.
    ///
    /// Échouer avant l'encaissement coûte un panier ; laisser passer coûte une
    /// course, à chaque commande, et personne ne le voit passer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<Result<FraisDeLivraison>> ResoudreFraisDeLivraisonAsync(
        OrderAggregate order,
        ShippingAddressInput addr,
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeliveryQuoteId))
        {
            // ═════════════════════════════════════════════════════════════════
            // UN REPAS SANS DEVIS EST REFUSÉ. UNE MARCHANDISE SANS DEVIS PART
            //    À ZÉRO FRANC, ET C'EST UN MANQUE ASSUMÉ, PAS UN TARIF.
            //
            // Pour un repas, la course EST achetée au prix réel quand le sac est
            // prêt. Sans devis, il n'existe aucun montant opposable à encaisser :
            // laisser passer reviendrait à offrir la livraison. Le refus dit à la
            // surface appelante qu'elle doit faire chiffrer la course avant de
            // présenter un total — pas que la restauration lui est interdite.
            //
            // Pour la marchandise, aucune surface de chiffrage n'existe encore
            // côté serveur : il n'y a ni grille de forfaits, ni route de devis
            // appelée au checkout. Refuser fermerait la vente de colis en entier.
            // On enregistre donc zéro — en le JOURNALISANT, parce que c'est un
            // trou de recette connu et pas une décision commerciale.
            //
            // Zéro décidé par le serveur reste strictement meilleur que le nombre
            // que l'acheteur déclarait : il n'y a plus de champ à falsifier, la
            // perte est constante, visible, et mesurable dans les journaux.
            // ═════════════════════════════════════════════════════════════════
            if (order.Kind == OrderLineKind.Food)
            {
                return Result.Failure<FraisDeLivraison>(Error.Validation(
                    "ordering.delivery_quote_required",
                    "Les frais de livraison d'un repas doivent être chiffrés par un devis "
                    + "avant le paiement. Demandez un devis de course, puis reprenez le "
                    + "paiement avec son identifiant."));
            }

            _logger.LogWarning(
                "Commande marchandise de l'acheteur {BuyerId} enregistrée SANS devis de course : "
                + "frais de livraison à zéro. La course sera achetée au prix réel et la "
                + "plateforme en supportera le coût.",
                command.BuyerId);

            return new FraisDeLivraison(0m, null);
        }

        var devis = await _devis.LookupQuoteAsync(command.DeliveryQuoteId, cancellationToken);

        // INTROUVABLE OU ILLISIBLE : MÊME REFUS, MÊME GESTE ATTENDU.
        //
        // « qt_ » recopié de travers, devis purgé, identifiant inventé de toutes
        // pièces pour obtenir un prix : dans tous les cas il n'y a aucun montant
        // opposable, et l'acheteur doit repasser par un devis.
        if (devis is null)
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_not_found",
                "Ce devis de livraison est introuvable. Demandez un nouveau prix "
                + "avant de valider la commande."));
        }

        // CONSOMMÉ AVANT EXPIRÉ : L'ORDRE DES DEUX CONTRÔLES CHANGE LE MESSAGE.
        //
        // Un devis déjà dépensé finit toujours par expirer. Tester l'expiration
        // d'abord dirait « redemandez un prix » à qui rejoue un devis — en
        // masquant le vrai problème, qui est qu'une course existe déjà pour ce
        // devis. Le domaine fait la même distinction dans `DeliveryQuote.Consume` ;
        // la perdre ici la rendrait inutile.
        if (devis.IsConsumed)
        {
            return Result.Failure<FraisDeLivraison>(Error.Conflict(
                "ordering.delivery_quote_used",
                "Ce devis de livraison a déjà servi à une course. Demandez un nouveau prix."));
        }

        if (devis.IsExpired)
        {
            return Result.Failure<FraisDeLivraison>(Error.Conflict(
                "ordering.delivery_quote_expired",
                $"Ce devis de livraison a expiré le {devis.ExpiresAtUtc:u}. "
                + "Demandez un nouveau prix avant de valider la commande."));
        }

        // UN DEVIS DE PARTENAIRE N'EST PAS UN DEVIS D'ACHETEUR.
        //
        // Les devis de l'API publique portent un `PartnerId` et relèvent d'une
        // grille et de conditions négociées, souvent plus basses. Sans ce refus,
        // un acheteur qui a mis la main sur un identifiant de partenaire s'en
        // servirait pour payer son propre colis au tarif de gros.
        // `CreateDeliveryCommand` refuse déjà ce croisement à la consommation —
        // mais bien plus tard, une fois la commande encaissée.
        if (devis.PartnerId is not null)
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_foreign",
                "Ce devis de livraison n'a pas été établi pour cette commande."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE DEVIS DOIT AVOIR ÉTÉ ÉTABLI POUR CETTE ADRESSE-CI.
        //
        // Sans ce contrôle, un devis parfaitement valide reste opposable pour
        // n'importe quelle destination : on chiffre une course de deux rues,
        // puis on présente ce devis pour une livraison à l'autre bout de la
        // ville. Le montant est « réel » — il n'est simplement pas celui de ce
        // trajet-là, et l'écart est payé par la plateforme.
        //
        // ON NE CONTRÔLE QUE SI L'ADRESSE PORTE UNE POSITION.
        //
        // Un repas en porte toujours une — elle est exigée quelques lignes plus
        // haut. Une adresse de colis, non : au Bénin on se repère à la commune et
        // au point de repère, et exiger un point sur la carte pour tout colis
        // fermerait la vente de marchandise à qui ne sait pas s'y placer.
        //
        // ET L'IDENTITÉ DE L'ACHETEUR, ELLE, N'EST PAS VÉRIFIABLE ICI.
        //
        // `DeliveryQuote` n'enregistre aucun acheteur — seulement un partenaire
        // éventuel. Deux acheteurs livrés au même point pourraient donc se prêter
        // un devis. Le coût de l'abus est nul (même trajet, même prix) et le devis
        // reste à usage unique ; c'est un manque connu, à combler le jour où le
        // devis portera son demandeur.
        // ═════════════════════════════════════════════════════════════════════
        if (addr.Latitude is { } lat && addr.Longitude is { } lon
            && (Math.Abs(devis.DropoffLatitude - lat) > ToleranceDegres
                || Math.Abs(devis.DropoffLongitude - lon) > ToleranceDegres))
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_address_mismatch",
                "Ce devis de livraison a été établi pour une autre adresse. "
                + "Demandez un nouveau prix pour l'adresse choisie."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE NIVEAU DE SERVICE DU DEVIS DOIT ÊTRE CELUI QUI SERA ACHETÉ.
        //
        // Un repas part en « Express » — plat chaud, durée de vie de quelques
        // dizaines de minutes — et un colis en « Standard », moins cher. Les deux
        // gestionnaires en aval le codent en dur :
        // `FoodOrderBridgeHandlers` crée la course « Express »,
        // `CreateDeliveryOnOrderConfirmedHandler` la crée « Standard ».
        //
        // Sans ce contrôle, un acheteur chiffre son repas en « Standard », paie le
        // tarif du colis, et la plateforme achète l'Express : la faille refermée
        // plus haut se rouvrirait par la porte du niveau de service, avec un devis
        // par ailleurs irréprochable.
        // ═════════════════════════════════════════════════════════════════════
        var typeAttendu = order.Kind == OrderLineKind.Food ? "Express" : "Standard";

        if (!string.Equals(devis.DeliveryType, typeAttendu, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_wrong_service",
                $"Ce devis de livraison a été établi pour un service « {devis.DeliveryType} », "
                + $"alors que cette commande sera livrée en « {typeAttendu} ». "
                + "Demandez un nouveau prix."));
        }

        // C'EST LE MONTANT DU DEVIS QUI ENTRE DANS LA COMMANDE — PAS UN MONTANT
        // COMPARÉ AU DEVIS.
        //
        // Comparer aurait supposé qu'un montant nous soit soumis, donc qu'un champ
        // falsifiable subsiste dans la requête. En employant directement
        // `devis.Total`, l'écart entre « ce qui est affiché » et « ce qui est
        // facturé » n'est plus une invariante à vérifier : il n'a plus d'endroit
        // où naître.
        //
        // Et c'est bien ce même identifiant qui repart vers delivery-service à la
        // création de course — voir `CreateDeliveryOnOrderConfirmedHandler` pour
        // la marchandise, `FoodOrderBridgeHandlers` pour le repas. L'acheteur est
        // donc facturé exactement ce que la plateforme paiera.
        return new FraisDeLivraison(devis.Total, devis.QuoteId);
    }
}
