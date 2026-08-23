using HBA.Deliveries.Contracts;
using HBA.Food.Application.Orders;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts;
using HBA.Inventory.Contracts;
using HBA.Ordering.Contracts;

// LE CONTRAT DE L'ÉVÉNEMENT VIENT D'order-service, SON PUBLIEUR.
//
// Il a existé en double — ici via un contrat partagé, et chez son propriétaire.
// L'enveloppe Kafka ne portant que le nom court « order.confirmed », le
// consommateur en résolvait un des deux au hasard de l'ordre de chargement des
// assemblies. Un gestionnaire enregistré sur l'autre n'était jamais appelé,
// sans la moindre erreur.
//
// Le duplicata a été supprimé. La règle qui reste : un fait métier, un contrat,
// un propriétaire — celui qui le publie.
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using MediatR;

using HBA.Food.Domain.Orders;

namespace HBA.Food.Api.Integration;

/// <summary>
/// La référence sous laquelle un ticket de cuisine se reconnaît dans une course.
/// </summary>
/// <remarks>
/// Même mécanisme que `ORDER-` côté marketplace : Delivery transporte une chaîne
/// opaque et la rend telle quelle. Trois préfixes circulent sur le même canal —
/// `SHIP-`, `FOOD-`, `ORDER-` — et chaque consommateur ne relit que le sien.
///
/// LA RÉFÉRENCE EST CELLE DU TICKET, PAS DE LA COMMANDE.
///
/// C'est la clé d'idempotence de Delivery, et c'est ce qui permet au retour de
/// course de retrouver le ticket à clore. Une commande peut porter plusieurs
/// tickets le jour où un client commandera dans deux restaurants.
/// </remarks>
public static class FoodOrderReference
{
    // DÉLÉGUÉ AU SOCLE PARTAGÉ — voir `DeliveryReference`.
    //
    // La référence est celle du TICKET, pas de la commande : c'est la clé
    // d'idempotence de Delivery, et ce qui permet au retour de course de
    // retrouver la cuisine à clore.
    public static string For(Guid foodOrderId) => DeliveryReference.ForFoodOrder(foodOrderId);

    public static Guid? Read(string? reference) => DeliveryReference.ReadFoodOrder(reference);
}

/// <summary>
/// Commande de repas payée → ticket de cuisine ouvert.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST L'ENTRÉE DU PARCOURS FOOD, ET ELLE N'EXISTAIT PAS.
///
/// Sans ce gestionnaire, un client peut commander un repas, être débité — et
/// AUCUNE cuisine n'est servie. Le restaurateur ne voit rien arriver, le ticket
/// n'existe pas, et les sept événements `FoodOrder*` que l'audit signalait sans
/// consommateur n'étaient même jamais publiés : il n'y avait rien à publier.
///
/// Le monolithe l'avait, dans sa composition root. Le module Food est parti dans
/// son service et le pont est resté.
///
/// ON N'AGIT QUE SUR `Kind == "Food"`.
///
/// `OrderConfirmed` est publié pour TOUTES les commandes. Ouvrir un ticket pour
/// une commande de marchandise créerait une cuisine fantôme sur un restaurant
/// nul. Le discriminant est porté par l'événement, il n'est pas déduit.
///
/// ON LÈVE SI LE RESTAURANT MANQUE.
///
/// Une commande de repas confirmée sans restaurant est une incohérence grave :
/// le client est débité et personne ne peut le servir. La reprise bornée du
/// consommateur Kafka rejouera trois fois, puis journalisera en Critical. C'est
/// ce qu'on veut d'un argent encaissé sans contrepartie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReceiveFoodOrderOnOrderConfirmedHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IOrderingModuleApi _ordering;
    private readonly ILogger<ReceiveFoodOrderOnOrderConfirmedHandler> _logger;

    public ReceiveFoodOrderOnOrderConfirmedHandler(
        ISender sender,
        IOrderingModuleApi ordering,
        ILogger<ReceiveFoodOrderOnOrderConfirmedHandler> logger)
    {
        _sender = sender;
        _ordering = ordering;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(integrationEvent.Kind, "Food", StringComparison.Ordinal))
        {
            return;
        }

        if (integrationEvent.RestaurantId is not { } restaurantId)
        {
            _logger.LogError(
                "Commande {OrderId} confirmée en restauration SANS restaurant. Le client est "
                + "débité et aucune cuisine ne peut être servie.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} : restauration sans restaurant.");
        }

        // ON RELIT LA COMMANDE, ON NE L'ÉLARGIT PAS.
        //
        // `OrderConfirmed` ne transporte pas les lignes — et ne doit pas. Un
        // événement d'intégration élargi pour le confort d'un consommateur
        // devient un engagement envers tous les autres. La lecture est un appel
        // gRPC de plus sur une opération rare.
        var commande = await _ordering.GetOrderAsync(integrationEvent.OrderId, cancellationToken);

        if (commande is null)
        {
            _logger.LogError(
                "Commande {OrderId} introuvable à la confirmation. Aucun ticket de cuisine ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException($"Commande {integrationEvent.OrderId} introuvable.");
        }

        var lignes = commande.Lines
            .Where(l => string.Equals(l.Kind, "Food", StringComparison.Ordinal))
            .Select(l => new FoodOrderLineInput(
                l.MenuItemId,
                l.Quantity,
                l.Options?.Select(o => o.OptionId).ToList() ?? [],
                l.Notes))
            .ToList();

        if (lignes.Count == 0)
        {
            // CE CAS ÉTAIT INVISIBLE JUSQU'À AUJOURD'HUI.
            //
            // La conversion gRPC des commandes forçait `Kind` à « Goods » sur
            // TOUTE ligne : le filtre ci-dessus n'aurait jamais rien trouvé, et
            // le ticket serait né vide. Le message le dit maintenant plutôt que
            // de créer une cuisine sans plats.
            _logger.LogError(
                "Commande {OrderId} déclarée « Food » mais sans aucune ligne de repas. "
                + "Aucun ticket ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} : aucune ligne de restauration.");
        }

        var resultat = await _sender.Send(
            new ReceiveFoodOrderCommand(
                // L'identifiant vient d'order-service : le ticket devra donc y
                // retourner pour l'adresse de livraison et pour l'arbitrage.
                FoodOrderOrigin.Marketplace,
                integrationEvent.OrderId,
                restaurantId,
                lignes,
                CustomerNote: null),
            cancellationToken);

        if (resultat.IsFailure)
        {
            _logger.LogError(
                "Ticket de cuisine NON ouvert pour la commande {OrderId} — {Code} : {Message}.",
                integrationEvent.OrderId, resultat.Error.Code, resultat.Error.Message);

            throw new InvalidOperationException(
                $"Ticket impossible pour la commande {integrationEvent.OrderId} : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Ticket de cuisine {FoodOrderId} ouvert pour la commande {OrderId} ({Lignes} ligne(s)).",
            resultat.Value, integrationEvent.OrderId, lignes.Count);
    }
}

/// <summary>
/// Repas prêt → un livreur est cherché.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST FOOD QUI APPELLE DELIVERY, ET NON L'INVERSE.
///
/// Delivery ne connaît ni commande, ni repas, ni restaurant — c'est le principe
/// qui le rend vendable à des tiers. Lui faire consommer `FoodOrderReadyForPickup`
/// l'obligerait à référencer les contrats de Food. Le sens de la dépendance est
/// donc : le donneur d'ordre connaît le transporteur.
///
/// TYPE « EXPRESS », ET C'EST LA SEULE DIFFÉRENCE DE FOND AVEC UN COLIS.
///
/// Un plat chaud a une durée de vie de quelques dizaines de minutes. Le traiter
/// en standard le ferait arriver froid.
///
/// LE DEVIS DÉJÀ PAYÉ PASSE AVANT UN DEVIS NEUF.
///
/// Les frais ont été chiffrés au checkout, à la distance réelle, et le client
/// les a réglés. En redemander un ici — vingt à quarante minutes plus tard — en
/// produirait un SECOND, qui peut différer : grille éditée, zone redécoupée. La
/// plateforme achèterait une course à un prix que personne n'a payé, et l'écart
/// ne serait mesuré nulle part.
///
/// ON RELANCE EN CAS DE DONNÉE MANQUANTE — CONTRAIREMENT AUX COLIS.
///
/// Un colis attend sur une étagère ; le rattrapage du lendemain ne coûte rien.
/// Un repas est prêt, il refroidit, et le client l'attend. Lever donne une
/// fenêtre de reprise, puis un journal Critical qui, lui, se voit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <summary>
/// Ce que la CRÉATION DE COURSE a besoin de savoir de la commande commerciale,
/// quel que soit l'univers dont elle vient.
/// </summary>
// PUBLICS tous les deux : ils apparaissent dans la signature du constructeur de
// `CreateDeliveryOnFoodOrderReadyHandler`, qui est public parce que le conteneur
// l'instancie. Un type moins accessible que la méthode qui l'accepte est une
// erreur de compilation (CS0051), pas un détail de style.
public sealed record CommandeALivrer(
    string? Recipient,
    string? Phone,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude,
    decimal Subtotal,
    string? DeliveryQuoteId);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LIRE LA COMMANDE D'UN TICKET, DANS L'UNIVERS QUI LA PORTE.
///
/// ÉCRIT PARCE QU'AUCUNE COURSE N'ÉTAIT JAMAIS CRÉÉE POUR UN REPAS DU NOUVEAU
/// PARCOURS.
///
/// `CreateDeliveryOnFoodOrderReadyHandler` appelait `_ordering.GetOrderAsync` —
/// order-service — sans se demander d'où venait le ticket. Pour un ticket né
/// d'une `MealOrder`, l'identifiant n'existe pas là-bas : « commande
/// introuvable », levée, reprises Kafka épuisées. Le repas était prêt et personne
/// ne cherchait de livreur. Le symptôme est muet côté client : la commande reste
/// « confirmée » pour toujours.
///
/// MÊME FORME QUE `IPayableOrderReader` CÔTÉ PAIEMENT, ET C'EST VOULU.
///
/// Le paiement avait exactement le même défaut, et il a été refermé de la même
/// façon au lot 6.1 : on n'abstrait QUE la lecture — ce qui diffère réellement
/// entre les deux univers — et jamais le geste métier, qui est identique. Deux
/// handlers de création de course auraient dupliqué la construction de la
/// demande, le repli sans devis et la politique de preuve.
///
/// ON NE CHERCHE JAMAIS DANS LES DEUX À LA SUITE.
///
/// L'origine vient du ticket, qui la porte en base depuis la migration
/// `OrigineDuTicketDeCuisine`. Un repli d'un univers sur l'autre rendrait la
/// bonne réponse presque toujours et enverrait un jour un livreur à l'adresse de
/// quelqu'un d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LecteurDeCommandeALivrer
{
    private readonly IOrderingModuleApi _marketplace;
    private readonly IMealOrderModuleApi _repas;

    public LecteurDeCommandeALivrer(IOrderingModuleApi marketplace, IMealOrderModuleApi repas)
    {
        _marketplace = marketplace;
        _repas = repas;
    }

    public async Task<CommandeALivrer?> LireAsync(
        string origine, Guid orderId, CancellationToken cancellationToken)
    {
        if (string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            var repas = await _repas.GetOrderAsync(orderId, cancellationToken);
            if (repas is null)
            {
                return null;
            }

            var adresse = repas.ShippingAddress;

            return new CommandeALivrer(
                adresse?.Recipient, adresse?.Phone, adresse?.CommuneName, adresse?.Quartier,
                adresse?.Landmark, adresse?.Line1, adresse?.Latitude, adresse?.Longitude,
                repas.Subtotal, repas.DeliveryQuoteId);
        }

        // TOUT CE QUI N'EST PAS EXPLICITEMENT « Food » EST TRAITÉ COMME
        // MARKETPLACE, ET C'EST COHÉRENT AVEC LE DÉFAUT DU CONTRAT.
        //
        // `FoodOrderReadyForPickupIntegrationEvent.OrderOrigin` vaut « Marketplace »
        // quand il est absent — un message écrit avant ce lot, encore en file. Les
        // traiter comme des repas les enverrait chercher une commande dans un
        // service qui ne les connaît pas : exactement le défaut qu'on referme,
        // retourné.
        var commande = await _marketplace.GetOrderAsync(orderId, cancellationToken);
        if (commande is null)
        {
            return null;
        }

        var expedition = commande.ShippingAddress;

        return new CommandeALivrer(
            expedition?.Recipient, expedition?.Phone, expedition?.CommuneName, expedition?.Quartier,
            expedition?.Landmark, expedition?.Line1, expedition?.Latitude, expedition?.Longitude,
            commande.Subtotal, commande.DeliveryQuoteId);
    }
}

public sealed class CreateDeliveryOnFoodOrderReadyHandler
    : IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly IFoodModuleApi _food;
    private readonly LecteurDeCommandeALivrer _commandes;
    private readonly IInventoryModuleApi _inventory;
    private readonly ILogger<CreateDeliveryOnFoodOrderReadyHandler> _logger;

    public CreateDeliveryOnFoodOrderReadyHandler(
        IDeliveryDispatchApi dispatch,
        IFoodModuleApi food,
        LecteurDeCommandeALivrer commandes,
        IInventoryModuleApi inventory,
        ILogger<CreateDeliveryOnFoodOrderReadyHandler> logger)
    {
        _dispatch = dispatch;
        _food = food;
        _commandes = commandes;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderReadyForPickupIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var manquants = new List<string>();

        var restaurant = await _food.GetRestaurantAsync(e.RestaurantId, cancellationToken);

        // L'UNIVERS D'ABORD. Cette ligne interrogeait order-service en dur, et
        // un ticket né d'une `MealOrder` n'y existe pas : voir
        // `LecteurDeCommandeALivrer`.
        var commande = await _commandes.LireAsync(e.OrderOrigin, e.OrderId, cancellationToken);

        if (restaurant is null)
        {
            manquants.Add("restaurant introuvable");
        }

        if (commande is null)
        {
            manquants.Add($"commande introuvable dans l'univers « {e.OrderOrigin} »");
        }

        var lieu = restaurant?.FulfillmentLocationId is { } locationId
            ? await _inventory.GetLocationAsync(locationId, cancellationToken)
            : null;

        if (lieu is null)
        {
            manquants.Add("lieu de collecte du restaurant");
        }

        // ON EXIGE LE REPÈRE ET LA POSITION, PAS « une adresse non nulle ».
        //
        // Une adresse peut être présente et inexploitable : sans repère, aucun
        // livreur ne trouve la porte au Bénin ; sans coordonnées, aucune distance
        // donc aucun prix. `DeliveryStop.Create` refuserait plus loin, mais le
        // refus arriverait sous la forme d'une course rejetée, sans dire ce qui
        // manquait.
        if (commande is not null && string.IsNullOrWhiteSpace(commande.Landmark))
        {
            manquants.Add("point de repère de l'adresse de livraison");
        }

        if (commande is not null && (commande.Latitude is null || commande.Longitude is null))
        {
            manquants.Add("position de l'adresse de livraison");
        }

        if (manquants.Count > 0)
        {
            _logger.LogError(
                "Course NON créée pour le ticket {FoodOrderId} (commande {OrderId} de l'univers "
                + "{Origine}, restaurant {RestaurantId}). Données manquantes : {Manquants}. Le repas "
                + "est prêt et refroidit sans qu'aucun livreur ne soit cherché.",
                e.FoodOrderId, e.OrderId, e.OrderOrigin, e.RestaurantId, string.Join(", ", manquants));

            throw new InvalidOperationException(
                $"Course impossible pour le ticket {e.FoodOrderId} : {string.Join(", ", manquants)}");
        }

        var demande = new CreateDeliveryRequest(
            Reference: FoodOrderReference.For(e.FoodOrderId),

            // « HbaFood », PAS « HbaExpress ».
            //
            // Classer les repas parmi les colis marchands fausserait toute
            // statistique d'exploitation, tout filtre de supervision, et tout
            // tarif par source le jour où la restauration en aura un propre.
            Source: "HbaFood",
            Type: "Express",
            Pickup: new DeliveryStopRequest(
                ContactName: restaurant!.Name,
                Phone: lieu!.ContactPhone,
                Commune: lieu.CommuneName,
                Quartier: lieu.Quartier,
                Landmark: lieu.Landmark,
                Instructions: null,
                Latitude: lieu.Latitude,
                Longitude: lieu.Longitude),
            Dropoff: new DeliveryStopRequest(
                ContactName: commande!.Recipient,
                Phone: commande.Phone,
                Commune: commande.CommuneName,
                Quartier: commande.Quartier,
                Landmark: commande.Landmark,
                Instructions: commande.Line1,
                Latitude: commande.Latitude,
                Longitude: commande.Longitude),
            Package: new DeliveryPackageRequest(
                Description: $"Repas — {restaurant.Name}",
                WeightKg: null,
                IsFragile: false,
                IsPerishable: true),

            // ═════════════════════════════════════════════════════════════════
            // CE QUE VALENT LES MARCHANDISES — POUR QUE LA COURSE EXIGE UNE
            // PREUVE (ISSUE-057).
            //
            // AVANT, CE HANDLER NE DISAIT RIEN, ET LE CONTRAT VALAIT « None »
            // PAR DÉFAUT : toute course née ici était clôturable sans code, sans
            // photo, sans signature. `RequiredProof` était persisté, projeté vers
            // l'application livreur, et n'avait JAMAIS été renseigné par
            // personne.
            //
            // `Subtotal` ET NON `GrandTotal` : c'est la valeur de ce que le
            // livreur porte. `GrandTotal` inclut les frais de livraison, qui ne
            // sont pas dans le colis — les compter gonflerait la valeur déclarée
            // et ferait basculer en code des courses qui n'en ont pas besoin.
            //
            // C'est delivery-service qui décide de la preuve à partir de là
            // (`ProofPolicy`). Ce service DÉCRIT, il ne conclut pas.
            //
            // UN REPAS PASSE PRESQUE TOUJOURS SOUS LE SEUIL, et c'est le bon
            // résultat : la preuve retenue sera une photo, que le livreur prend
            // seul. Exiger un code pour un plat à 3 000 FCFA ferait échouer des
            // remises pour un client qui n'a pas son téléphone en main.
            DeclaredValue: commande.Subtotal,

            // TOUJOURS `false` ICI. Le ticket est réglé au moment de la
            // commande — c'est ce que suppose `DeliveryQuoteId`, un devis DÉJÀ
            // PAYÉ. Même réserve qu'ailleurs : si la restauration ouvre un jour
            // le paiement à la livraison, cette ligne devra suivre, sans quoi le
            // livreur encaissera sans qu'aucune preuve ne le lie à la remise.
            IsCashOnDelivery: false,
            QuoteId: commande.DeliveryQuoteId);

        var course = await _dispatch.CreateAsync(demande, cancellationToken);

        if (course.Succeeded)
        {
            _logger.LogInformation(
                "Course {DeliveryId} créée pour le ticket {FoodOrderId}.",
                course.DeliveryId, e.FoodOrderId);

            return;
        }

        // ON N'ABANDONNE PAS SUR UN DEVIS PÉRIMÉ : ON RETENTE SANS LUI.
        //
        // Un devis expire. Laisser un repas sans livreur pour cette raison serait
        // absurde. L'écart de prix qui en résulte est un moindre mal — et il est
        // journalisé, donc mesurable.
        if (!string.IsNullOrWhiteSpace(commande.DeliveryQuoteId))
        {
            _logger.LogWarning(
                "Le devis payé {QuoteId} du ticket {FoodOrderId} a été refusé ({Motif}). "
                + "Nouvelle tentative sans devis — le prix acheté peut différer de celui payé.",
                commande.DeliveryQuoteId, e.FoodOrderId, course.Reason);

            var secondEssai = await _dispatch.CreateAsync(
                demande with { QuoteId = null }, cancellationToken);

            if (secondEssai.Succeeded)
            {
                _logger.LogInformation(
                    "Course {DeliveryId} créée pour le ticket {FoodOrderId}, hors devis payé.",
                    secondEssai.DeliveryId, e.FoodOrderId);

                return;
            }

            course = secondEssai;
        }

        _logger.LogError(
            "Course NON créée pour le ticket {FoodOrderId} — {Motif}. Le repas est prêt et "
            + "refroidit.",
            e.FoodOrderId, course.Reason);

        throw new InvalidOperationException(
            $"Course refusée pour le ticket {e.FoodOrderId} : {course.Reason}");
    }
}
