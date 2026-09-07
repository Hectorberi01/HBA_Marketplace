namespace HBA.Gateway.Api.Options;

/// <summary>
/// Correspondance entre les politiques d'autorisation de la passerelle et les
/// rôles réellement portés par les jetons.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES NOMS PUBLICS ET LES RÔLES ÉMIS NE COÏNCIDENT PAS. C'EST VOULU.
///
/// Les rôles relevés dans le code d'émission existant sont : Admin, Moderator,
/// Seller, Driver, Dispatcher, FoodPartner, et Buyer par défaut à l'inscription
/// (`ApiAuthorization.cs`, `RegisterUserCommandHandler.cs`).
///
/// La plateforme parle, elle, de « marchand » et de « restaurant ». Deux
/// divergences en découlent, et aucune n'est une erreur :
///   • MerchantOnly   → rôle « Seller »
///   • RestaurantOnly → rôle « FoodPartner »
///   • CustomerOnly   → rôle « Buyer »
///
/// Les codifier en dur aurait imposé un renommage coordonné de la passerelle et
/// d'identity-service le jour où les rôles bougeront. En configuration, un seul
/// des deux change — et l'autre continue de fonctionner pendant la transition.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GatewayAuthorizationOptions
{
    public const string SectionName = "Authorization";

    /// <summary>Politique → rôles acceptés (un seul suffit).</summary>
    public Dictionary<string, string[]> Roles { get; init; } = new();
}

/// <summary>Noms des politiques, pour éviter les chaînes libres dans le code.</summary>
public static class GatewayPolicies
{
    public const string Authenticated = "Authenticated";
    public const string AdminOnly = "AdminOnly";
    public const string StaffOnly = "StaffOnly";
    public const string MerchantOnly = "MerchantOnly";
    public const string RestaurantOnly = "RestaurantOnly";

    /// <summary>
    /// Boutiquier OU restaurateur — l'un des deux suffit.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ELLE EXISTE POUR UNE SEULE ROUTE, ET CETTE ROUTE EST LA PORTE D'ENTRÉE.
    ///
    /// `GET /api/v1/bff/merchant/activities` rend les activités du compte —
    /// boutiques ET restaurants — et c'est le PREMIER appel de l'application
    /// partenaire après la connexion : le sélecteur d'activité en dérive, et tous
    /// les écrans contextuels y prennent leur `storeId` ou leur `restaurantId`.
    ///
    /// Elle portait `MerchantOnly`, c'est-à-dire le seul rôle `Seller`. Un compte
    /// PUREMENT restaurateur — `FoodPartner` sans `Seller` — recevait donc 403 sur
    /// la seule route qui lui aurait dit qu'il a un restaurant. Il ne franchissait
    /// jamais l'écran de sélection, et l'application n'avait rien à lui montrer
    /// d'autre qu'un message d'erreur brut.
    ///
    /// Le handler, lui, n'a jamais eu ce défaut : `GetMerchantActivitiesHandler`
    /// interroge food-service ET merchant-service, et scope tout sur le porteur du
    /// jeton. C'était la politique du CONTRÔLEUR qui fermait la porte, pas la
    /// donnée.
    ///
    /// POURQUOI PAS SIMPLEMENT `Authenticated` : un acheteur recevrait alors
    /// 200 avec une liste vide, et l'application partenaire s'ouvrirait sur un
    /// compte qui n'a rien à y faire. Le refus doit rester explicite.
    ///
    /// ELLE NE S'APPLIQUE QU'À `activities`. Le tableau de bord d'une boutique
    /// reste `MerchantOnly`, et le BFF restaurant reste `RestaurantOnly` : élargir
    /// tout le contrôleur rendrait les ventes d'une boutique lisibles à un
    /// restaurateur.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public const string PartnerOnly = "PartnerOnly";

    public const string DriverOnly = "DriverOnly";
    public const string CustomerOnly = "CustomerOnly";

    /// <summary>Politiques adossées à des rôles (donc alimentées par la configuration).</summary>
    public static readonly IReadOnlyList<string> RoleBased =
    [
        AdminOnly, StaffOnly, MerchantOnly, RestaurantOnly, PartnerOnly, DriverOnly, CustomerOnly
    ];
}
