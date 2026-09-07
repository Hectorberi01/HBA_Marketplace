using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Infrastructure.Configuration;

/// <summary>Adresses internes des vingt microservices joignables depuis la passerelle.</summary>
public sealed class ServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url] public string Identity { get; init; } = string.Empty;
    [Required, Url] public string User { get; init; } = string.Empty;
    [Required, Url] public string Merchant { get; init; } = string.Empty;
    [Required, Url] public string Catalog { get; init; } = string.Empty;
    [Required, Url] public string Inventory { get; init; } = string.Empty;
    [Required, Url] public string Commerce { get; init; } = string.Empty;
    [Required, Url] public string Order { get; init; } = string.Empty;
    [Required, Url] public string Food { get; init; } = string.Empty;
    [Required, Url] public string Delivery { get; init; } = string.Empty;
    [Required, Url] public string Financial { get; init; } = string.Empty;
    [Required, Url] public string Engagement { get; init; } = string.Empty;
    [Required, Url] public string Communication { get; init; } = string.Empty;
    [Required, Url] public string Media { get; init; } = string.Empty;

    // AJOUTÉ APRÈS COUP, ET L'OUBLI ÉTAIT EXACTEMENT LE DÉFAUT QUE CE FICHIER
    // DÉCRIT EN TÊTE.
    [Required, Url] public string Promotion { get; init; } = string.Empty;

    // TROIS ENDROITS À TENIR D'ACCORD — LE COMMENTAIRE DE `ServiceKeys` LE DIT
    // DÉJÀ, ET « Promotion » EN A FAIT LES FRAIS.
    [Required, Url] public string FoodCart { get; init; } = string.Empty;
    [Required, Url] public string FoodOrder { get; init; } = string.Empty;

    // TROIS SERVICES ENTIERS INJOIGNABLES DEPUIS INTERNET (lot 7.5).
    [Required, Url] public string Analytics { get; init; } = string.Empty;

    [Required, Url] public string ReturnRefund { get; init; } = string.Empty;
    [Required, Url] public string Drivers { get; init; } = string.Empty;
    [Required, Url] public string DeliveryPricing { get; init; } = string.Empty;

    /// <summary>
    /// Adresse d'un service par sa clé logique, ou <c> null</c> si la clé est
    /// inconnue.
    /// </summary>
    public string? Resolve(string serviceKey) => serviceKey switch
    {
        ServiceKeys.Identity => Identity,
        ServiceKeys.User => User,
        ServiceKeys.Merchant => Merchant,
        ServiceKeys.Catalog => Catalog,
        ServiceKeys.Inventory => Inventory,
        ServiceKeys.Commerce => Commerce,
        ServiceKeys.Order => Order,
        ServiceKeys.Food => Food,
        ServiceKeys.Delivery => Delivery,
        ServiceKeys.Financial => Financial,
        ServiceKeys.Engagement => Engagement,
        ServiceKeys.Communication => Communication,
        ServiceKeys.Media => Media,
        ServiceKeys.Promotion => Promotion,
        ServiceKeys.FoodCart => FoodCart,
        ServiceKeys.FoodOrder => FoodOrder,
        ServiceKeys.Analytics => Analytics,
        ServiceKeys.ReturnRefund => ReturnRefund,
        ServiceKeys.Drivers => Drivers,
        ServiceKeys.DeliveryPricing => DeliveryPricing,
        _ => null
    };
}

/// <summary>Clés logiques des services.</summary>
public static class ServiceKeys
{
    public const string Identity = "Identity";
    public const string User = "User";
    public const string Merchant = "Merchant";
    public const string Catalog = "Catalog";
    public const string Inventory = "Inventory";
    public const string Commerce = "Commerce";
    public const string Order = "Order";
    public const string Food = "Food";
    public const string Delivery = "Delivery";
    public const string Financial = "Financial";
    public const string Engagement = "Engagement";
    public const string Communication = "Communication";
    public const string Media = "Media";
    public const string Promotion = "Promotion";
    public const string FoodCart = "FoodCart";
    public const string FoodOrder = "FoodOrder";
    public const string Analytics = "Analytics";
    public const string ReturnRefund = "ReturnRefund";
    public const string Drivers = "Drivers";
    public const string DeliveryPricing = "DeliveryPricing";

    // TROIS ENDROITS À TENIR D'ACCORD POUR UN SEUL SERVICE.
    public static readonly IReadOnlyList<string> All =
    [
        Identity, User, Merchant, Catalog, Inventory, Commerce, Order,
        Food, Delivery, Financial, Engagement, Communication, Media, Promotion,
        FoodCart, FoodOrder, ReturnRefund, Drivers, DeliveryPricing, Analytics
    ];
}
