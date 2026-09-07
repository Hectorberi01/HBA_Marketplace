namespace HBA.Gateway.Api.Options;

/// <summary>
/// Correspondance entre les politiques d'autorisation de la passerelle et les rôles
/// réellement portés par les jetons.
/// </summary>
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

    /// <summary>Boutiquier OU restaurateur — l'un des deux suffit.</summary>
    public const string PartnerOnly = "PartnerOnly";

    public const string DriverOnly = "DriverOnly";
    public const string CustomerOnly = "CustomerOnly";

    /// <summary>Politiques adossées à des rôles (donc alimentées par la configuration).</summary>
    public static readonly IReadOnlyList<string> RoleBased =
    [
        AdminOnly, StaffOnly, MerchantOnly, RestaurantOnly, PartnerOnly, DriverOnly, CustomerOnly
    ];
}
