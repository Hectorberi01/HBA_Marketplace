using System.Collections.Frozen;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Qui a le droit d'appeler quoi, entre services.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SAVOIR QUI APPELLE NE SERT À RIEN SI TOUT LE MONDE A LE DROIT D'APPELER TOUT.
///
/// <see cref="IdentiteInterne"/> répond à « qui » ; cette table répond à « a-t-il
/// le droit ». Sans elle, une identité vérifiée laisserait user-service appeler
/// `RefundPayment` — authentifié, tracé, et parfaitement illégitime.
///
/// CETTE TABLE EST ENGENDRÉE À PARTIR DU CODE, PAS ÉCRITE À LA MAIN.
///
/// le contrôle `autorisations-grpc` la recalcule depuis le graphe réel des
/// références de projet et des sites d'appel, et ÉCHOUE si elle diverge. Une
/// autorisation ajoutée à la main sans appel correspondant est donc rejetée par
/// le contrôle, comme l'est un appel ajouté sans autorisation. C'est la seule
/// façon d'éviter qu'une liste de deux cent quatre-vingt-neuf lignes ne dérive
/// jusqu'à redevenir « tout le monde peut tout ».
///
/// GRANULARITÉ : LE PAQUET DE CONTRATS, PAS L'APPELANT MÉTIER.
///
/// C'est la limite honnête de cette table, et il faut la connaître avant de s'y
/// fier. Un hôte qui référence `HBA.Merchants.Contracts.Grpc` reçoit TOUTES les
/// méthodes que l'enveloppe de ce paquet appelle — parce que l'enveloppe est UNE
/// classe (`MerchantsGrpcClient`) qui les appelle toutes. `GetSellerPayout`
/// passe ainsi de vingt-quatre appelants possibles à dix, et non à un.
///
/// Descendre plus bas demanderait de rattacher chaque `_client.X(` au membre
/// d'interface qui le contient, puis de suivre l'injection de dépendances
/// jusqu'au gestionnaire consommateur — c'est-à-dire d'écrire un compilateur, la
/// même impasse que celle notée dans `check-grpc-rpc.py`. Ce qui ferme
/// réellement `GetSellerPayout` est de découper l'enveloppe par interface, ce
/// qui est un lot en soi.
///
/// CE QUE LA TABLE FERME QUAND MÊME, AUJOURD'HUI :
///   • `RefundPayment` : vingt-et-un appelants possibles → UN.
///   • `ReleaseCoupon` : vingt-et-un → deux.
///   • `ReleaseReservation` : vingt-et-un → cinq.
///   • deux hôtes de livraison n'ont AUCUN droit d'appel sortant :
///     Driver et Route.
///
/// DEUX CHIFFRES ONT CHANGÉ ICI LE 27 AOÛT, POUR DEUX RAISONS DIFFÉRENTES.
///
/// « Vingt-quatre » est devenu « vingt-trois » : `HBA.Delivery.Dispatch.Api` a été
/// retiré du dépôt. Ce service dupliquait une affectation de livreur que
/// delivery-service fait déjà, avec deux identifiants codés en dur et sans base.
///
/// « Six » était FAUX AVANT CE RETRAIT, et vaut quatre aujourd'hui. Le compte
/// n'avait jamais été recalculé après que Delivery.Core et Delivery.Pricing ont
/// reçu des droits sortants. Un commentaire qui annonce un chiffre de sécurité
/// plus favorable que la réalité est pire qu'un commentaire absent : on ne
/// revérifie pas ce qu'on croit déjà compté.
///
/// LE 28 AOÛT, DEUX HÔTES DE PLUS SONT SORTIS : PROOF ET TRACKING.
///
/// « Vingt-trois » est devenu « vingt-et-un », et « quatre hôtes sans droit
/// sortant » est devenu « deux ». Les deux services retirés tenaient leur état
/// dans des `ConcurrentDictionary` de processus — sans base, sans migration,
/// perdu au redémarrage et non partagé entre réplicas — pendant que
/// delivery-service persistait déjà la preuve (`ProofOfDelivery`, `IssuedPin`,
/// `FailedProofAttempts`) et exposait le suivi (`GetTracking`). Aucun des deux
/// n'avait d'entrée dans `ServicesOptions` du gateway : ils n'étaient
/// joignables par personne, de l'extérieur comme de l'intérieur.
///
/// CE QUE CE RETRAIT NE COUVRE PAS. `HBA.Delivery.Route.Api` reste dans la table
/// avec zéro droit sortant et zéro appelant, comme les deux précédents. Il n'est
/// pas retiré parce que le calcul d'itinéraire, lui, a un remplaçant dégradé qui
/// tourne (`FALLBACK_HAVERSINE`) et une décision distincte à trancher.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class AutorisationsGrpc
{
    /// <summary>
    /// Vrai si <paramref name="appelant"/> peut invoquer <paramref name="methode"/>.
    /// </summary>
    /// <remarks>
    /// UN APPELANT ABSENT DE LA TABLE N'A AUCUN DROIT — IL N'EN A PAS TOUS.
    ///
    /// C'est le sens de `TryGetValue` suivi d'un refus : le défaut est fermé.
    /// L'inverse — « inconnu donc autorisé » — rendrait la table décorative, un
    /// nom d'hôte mal orthographié suffisant à tout rouvrir sans le moindre
    /// symptôme.
    /// </remarks>
    public static bool EstAutorise(string appelant, string methode)
        => _table.TryGetValue(appelant, out var methodes) && methodes.Contains(methode);

    /// <summary>Les hôtes connus de la table, pour les contrôles de démarrage.</summary>
    public static IReadOnlyCollection<string> Appelants => _table.Keys;

    // ENGENDRÉ — voir le contrôle `autorisations-grpc`. Ne pas éditer à la
    // main : le contrôle recalcule cette table et échoue à la moindre divergence.
    private static readonly FrozenDictionary<string, FrozenSet<string>> _table =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["HBA.Catalog.Api"] =
            new[]
            {
                "/hba.catalog.v1.CatalogApi/GetOffer",
                "/hba.catalog.v1.CatalogApi/GetOffers",
                "/hba.catalog.v1.CatalogApi/GetProduct",
                "/hba.catalog.v1.CatalogApi/ListOffersBySku",
                "/hba.catalog.v1.CatalogApi/ListPurchasableOffers",
                "/hba.media.v1.MediaApi/CreateSignedUrl",
                "/hba.media.v1.MediaApi/Get",
                "/hba.media.v1.MediaApi/GetMany",
                "/hba.media.v1.MediaApi/ListByOwner",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Commerce.Api"] =
            new[]
            {
                "/hba.catalog.v1.CatalogApi/GetOffer",
                "/hba.catalog.v1.CatalogApi/GetOffers",
                "/hba.catalog.v1.CatalogApi/GetProduct",
                "/hba.catalog.v1.CatalogApi/ListOffersBySku",
                "/hba.catalog.v1.CatalogApi/ListPurchasableOffers",
                "/hba.commerce.v1.CommerceApi/GetActiveCart",
                "/hba.commerce.v1.CommerceApi/GetCart",
                "/hba.inventory.v1.InventoryApi/ConfirmReservation",
                "/hba.inventory.v1.InventoryApi/GetAvailability",
                "/hba.inventory.v1.InventoryApi/GetLocation",
                "/hba.inventory.v1.InventoryApi/ReleaseReservation",
                "/hba.inventory.v1.InventoryApi/ReserveStock",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
                "/hba.promotion.v1.PromotionApi/CommitCoupon",
                "/hba.promotion.v1.PromotionApi/EvaluatePromotion",
                "/hba.promotion.v1.PromotionApi/ReleaseCoupon",
                "/hba.promotion.v1.PromotionApi/ReserveCoupon",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Communication.Api"] =
            new[]
            {
                "/hba.catalog.v1.CatalogApi/GetOffer",
                "/hba.catalog.v1.CatalogApi/GetOffers",
                "/hba.catalog.v1.CatalogApi/GetProduct",
                "/hba.catalog.v1.CatalogApi/ListOffersBySku",
                "/hba.catalog.v1.CatalogApi/ListPurchasableOffers",
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.foodorder.v1.FoodOrderApi/GetOrder",
                "/hba.foodorder.v1.FoodOrderApi/HasPlacedOrder",
                "/hba.identity.v1.IdentityApi/GetUser",
                "/hba.identity.v1.IdentityApi/GetUserByEmail",
                "/hba.identity.v1.IdentityApi/GetUserRoles",
                "/hba.identity.v1.IdentityApi/RevokeUserSessions",
                "/hba.identity.v1.IdentityApi/ValidateAccessToken",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Delivery.Core.Api"] =
            new[]
            {
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.deliverypricing.v1.DeliveryPricingApi/ConsumeQuote",
                "/hba.deliverypricing.v1.DeliveryPricingApi/LookupQuote",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            // HBA.Delivery.Driver.Api : aucun appel gRPC sortant.
            ["HBA.Delivery.Driver.Api"] = FrozenSet<string>.Empty,

            ["HBA.Delivery.Pricing.Api"] =
            new[]
            {
                "/hba.deliverypricing.v1.DeliveryPricingApi/LookupQuote",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            // HBA.Delivery.Route.Api : aucun appel gRPC sortant.
            ["HBA.Delivery.Route.Api"] = FrozenSet<string>.Empty,

            ["HBA.Engagement.Api"] =
            new[]
            {
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Financial.Api"] =
            new[]
            {
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.food.v1.FoodApi/GetFoodOrder",
                "/hba.food.v1.FoodApi/GetMenuItem",
                "/hba.food.v1.FoodApi/GetRestaurant",
                "/hba.food.v1.FoodApi/GetRestaurantByOwner",
                "/hba.food.v1.FoodApi/GetStaffMembership",
                "/hba.foodorder.v1.FoodOrderApi/GetOrder",
                "/hba.foodorder.v1.FoodOrderApi/HasPlacedOrder",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Food.Cart.Api"] =
            new[]
            {
                "/hba.food.v1.FoodApi/GetFoodOrder",
                "/hba.food.v1.FoodApi/GetMenuItem",
                "/hba.food.v1.FoodApi/GetRestaurant",
                "/hba.food.v1.FoodApi/GetRestaurantByOwner",
                "/hba.food.v1.FoodApi/GetStaffMembership",
                "/hba.foodcart.v1.FoodCartApi/GetActiveCart",
                "/hba.foodcart.v1.FoodCartApi/GetCart",
                "/hba.foodorder.v1.FoodOrderApi/GetOrder",
                "/hba.foodorder.v1.FoodOrderApi/HasPlacedOrder",
                "/hba.promotion.v1.PromotionApi/CommitCoupon",
                "/hba.promotion.v1.PromotionApi/EvaluatePromotion",
                "/hba.promotion.v1.PromotionApi/ReleaseCoupon",
                "/hba.promotion.v1.PromotionApi/ReserveCoupon",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Food.Order.Api"] =
            new[]
            {
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.deliverypricing.v1.DeliveryPricingApi/LookupQuote",
                "/hba.food.v1.FoodApi/GetFoodOrder",
                "/hba.food.v1.FoodApi/GetMenuItem",
                "/hba.food.v1.FoodApi/GetRestaurant",
                "/hba.food.v1.FoodApi/GetRestaurantByOwner",
                "/hba.food.v1.FoodApi/GetStaffMembership",
                "/hba.foodcart.v1.FoodCartApi/GetActiveCart",
                "/hba.foodcart.v1.FoodCartApi/GetCart",
                "/hba.foodorder.v1.FoodOrderApi/GetOrder",
                "/hba.foodorder.v1.FoodOrderApi/HasPlacedOrder",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Food.Restaurant.Api"] =
            new[]
            {
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.food.v1.FoodApi/GetFoodOrder",
                "/hba.food.v1.FoodApi/GetMenuItem",
                "/hba.food.v1.FoodApi/GetRestaurant",
                "/hba.food.v1.FoodApi/GetRestaurantByOwner",
                "/hba.food.v1.FoodApi/GetStaffMembership",
                "/hba.foodorder.v1.FoodOrderApi/GetOrder",
                "/hba.foodorder.v1.FoodOrderApi/HasPlacedOrder",
                "/hba.inventory.v1.InventoryApi/ConfirmReservation",
                "/hba.inventory.v1.InventoryApi/GetAvailability",
                "/hba.inventory.v1.InventoryApi/GetLocation",
                "/hba.inventory.v1.InventoryApi/ReleaseReservation",
                "/hba.inventory.v1.InventoryApi/ReserveStock",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Gateway.Api"] =
            new[]
            {
                "/hba.identity.v1.IdentityApi/GetUser",
                "/hba.identity.v1.IdentityApi/GetUserByEmail",
                "/hba.identity.v1.IdentityApi/GetUserRoles",
                "/hba.identity.v1.IdentityApi/RevokeUserSessions",
                "/hba.identity.v1.IdentityApi/ValidateAccessToken",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Identity.Api"] =
            new[]
            {
                "/hba.identity.v1.IdentityApi/GetUser",
                "/hba.identity.v1.IdentityApi/GetUserByEmail",
                "/hba.identity.v1.IdentityApi/GetUserRoles",
                "/hba.identity.v1.IdentityApi/RevokeUserSessions",
                "/hba.identity.v1.IdentityApi/ValidateAccessToken",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Inventory.Api"] =
            new[]
            {
                "/hba.inventory.v1.InventoryApi/ConfirmReservation",
                "/hba.inventory.v1.InventoryApi/GetAvailability",
                "/hba.inventory.v1.InventoryApi/GetLocation",
                "/hba.inventory.v1.InventoryApi/ReleaseReservation",
                "/hba.inventory.v1.InventoryApi/ReserveStock",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Marketplace.ReturnRefund.Api"] =
            new[]
            {
                "/hba.financial.v1.FinancialApi/RefundPayment",
                "/hba.media.v1.MediaApi/CreateSignedUrl",
                "/hba.media.v1.MediaApi/Get",
                "/hba.media.v1.MediaApi/GetMany",
                "/hba.media.v1.MediaApi/ListByOwner",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Media.Api"] =
            new[]
            {
                "/hba.media.v1.MediaApi/CreateSignedUrl",
                "/hba.media.v1.MediaApi/Get",
                "/hba.media.v1.MediaApi/GetMany",
                "/hba.media.v1.MediaApi/ListByOwner",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Merchants.Api"] =
            new[]
            {
                "/hba.identity.v1.IdentityApi/GetUser",
                "/hba.identity.v1.IdentityApi/GetUserByEmail",
                "/hba.identity.v1.IdentityApi/GetUserRoles",
                "/hba.identity.v1.IdentityApi/RevokeUserSessions",
                "/hba.identity.v1.IdentityApi/ValidateAccessToken",
                "/hba.inventory.v1.InventoryApi/ConfirmReservation",
                "/hba.inventory.v1.InventoryApi/GetAvailability",
                "/hba.inventory.v1.InventoryApi/GetLocation",
                "/hba.inventory.v1.InventoryApi/ReleaseReservation",
                "/hba.inventory.v1.InventoryApi/ReserveStock",
                "/hba.media.v1.MediaApi/CreateSignedUrl",
                "/hba.media.v1.MediaApi/Get",
                "/hba.media.v1.MediaApi/GetMany",
                "/hba.media.v1.MediaApi/ListByOwner",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Order.Api"] =
            new[]
            {
                "/hba.catalog.v1.CatalogApi/GetOffer",
                "/hba.catalog.v1.CatalogApi/GetOffers",
                "/hba.catalog.v1.CatalogApi/GetProduct",
                "/hba.catalog.v1.CatalogApi/ListOffersBySku",
                "/hba.catalog.v1.CatalogApi/ListPurchasableOffers",
                "/hba.commerce.v1.CommerceApi/GetActiveCart",
                "/hba.commerce.v1.CommerceApi/GetCart",
                "/hba.delivery.v1.DeliveryApi/CancelDelivery",
                "/hba.delivery.v1.DeliveryApi/CreateDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDelivery",
                "/hba.delivery.v1.DeliveryApi/GetDeliveryByReference",
                "/hba.delivery.v1.DeliveryApi/GetTracking",
                "/hba.delivery.v1.DeliveryApi/ResolveDriver",
                "/hba.deliverypricing.v1.DeliveryPricingApi/LookupQuote",
                "/hba.food.v1.FoodApi/GetFoodOrder",
                "/hba.food.v1.FoodApi/GetMenuItem",
                "/hba.food.v1.FoodApi/GetRestaurant",
                "/hba.food.v1.FoodApi/GetRestaurantByOwner",
                "/hba.food.v1.FoodApi/GetStaffMembership",
                "/hba.inventory.v1.InventoryApi/ConfirmReservation",
                "/hba.inventory.v1.InventoryApi/GetAvailability",
                "/hba.inventory.v1.InventoryApi/GetLocation",
                "/hba.inventory.v1.InventoryApi/ReleaseReservation",
                "/hba.inventory.v1.InventoryApi/ReserveStock",
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.order.v1.OrderApi/GetOrder",
                "/hba.order.v1.OrderApi/GetOrderReturnContext",
                "/hba.order.v1.OrderApi/GetSellerSalesCount",
                "/hba.order.v1.OrderApi/ListOrdersByBuyer",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Promotions.Api"] =
            new[]
            {
                "/hba.merchant.v1.MerchantApi/CheckMerchantCapability",
                "/hba.merchant.v1.MerchantApi/GetMemberAccess",
                "/hba.merchant.v1.MerchantApi/GetSeller",
                "/hba.merchant.v1.MerchantApi/GetSellerByUser",
                "/hba.merchant.v1.MerchantApi/GetSellerPayout",
                "/hba.merchant.v1.MerchantApi/GetStore",
                "/hba.merchant.v1.MerchantApi/ListSellerStores",
                "/hba.merchant.v1.MerchantApi/ValidateSeller",
                "/hba.promotion.v1.PromotionApi/CommitCoupon",
                "/hba.promotion.v1.PromotionApi/EvaluatePromotion",
                "/hba.promotion.v1.PromotionApi/ReleaseCoupon",
                "/hba.promotion.v1.PromotionApi/ReserveCoupon",
            }
            .ToFrozenSet(StringComparer.Ordinal),

            ["HBA.Users.Api"] =
            new[]
            {
                "/hba.identity.v1.IdentityApi/GetUser",
                "/hba.identity.v1.IdentityApi/GetUserByEmail",
                "/hba.identity.v1.IdentityApi/GetUserRoles",
                "/hba.identity.v1.IdentityApi/RevokeUserSessions",
                "/hba.identity.v1.IdentityApi/ValidateAccessToken",
                "/hba.user.v1.UsersApi/GetProfile",
                "/hba.user.v1.UsersApi/GetProfiles",
            }
            .ToFrozenSet(StringComparer.Ordinal),
        }
        .ToFrozenDictionary(StringComparer.Ordinal);
}
