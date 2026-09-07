using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Infrastructure.Configuration;

/// <summary>
/// Adresses internes des dix-neuf microservices joignables depuis la passerelle.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SOURCE UNIQUE DES ADRESSES. NE PAS EN OUVRIR UNE SECONDE.
///
/// Ces valeurs alimentent DEUX consommateurs : les clients HTTP typés du BFF, et
/// les destinations des clusters YARP — ces dernières via
/// <see cref="ReverseProxy.ServiceAddressConfigFilter"/>.
///
/// La section `ReverseProxy:Clusters` ne déclare donc QUE des noms de clusters,
/// jamais d'adresses. Sans cela, `Services__Catalog=...` en variable Docker
/// aurait déplacé le BFF sans déplacer le proxy : la moitié du trafic serait
/// partie vers l'ancienne adresse, et rien n'aurait échoué assez fort pour être
/// remarqué avant la production.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

    // ═════════════════════════════════════════════════════════════════════════
    // AJOUTÉ APRÈS COUP, ET L'OUBLI ÉTAIT EXACTEMENT LE DÉFAUT QUE CE FICHIER
    //    DÉCRIT EN TÊTE.
    //
    // Les routes et le cluster « Promotion » ont été posés dans appsettings.json,
    // l'adresse aussi — mais ni cette propriété ni ServiceKeys n'ont suivi.
    // `Resolve("Promotion")` rendait donc null, le filtre laissait le cluster SANS
    // destination, et toute requête publique vers les promotions serait tombée en
    // 503. La configuration paraissait complète : l'adresse était bien écrite,
    // au bon endroit, sous le bon nom.
    //
    // Rien ne l'aurait signalé sans `RoutingTests.Chaque_cluster_recoit_une_
    // destination_depuis_la_section_Services`, qui est précisément là pour ça.
    // ═════════════════════════════════════════════════════════════════════════
    [Required, Url] public string Promotion { get; init; } = string.Empty;

    // ═════════════════════════════════════════════════════════════════════════
    // TROIS ENDROITS À TENIR D'ACCORD — LE COMMENTAIRE DE `ServiceKeys` LE
    //    DIT DÉJÀ, ET « Promotion » EN A FAIT LES FRAIS.
    //
    // Le panier et la commande de restauration ne sont plus servis par
    // commerce-service ni par order-service. Sans ces deux adresses, les clusters
    // « FoodCart » et « FoodOrder » se chargeraient SANS destination et
    // rendraient 503 sur tout le parcours restaurant — avec une configuration qui
    // aurait l'air complète.
    // ═════════════════════════════════════════════════════════════════════════
    [Required, Url] public string FoodCart { get; init; } = string.Empty;
    [Required, Url] public string FoodOrder { get; init; } = string.Empty;

    // ═════════════════════════════════════════════════════════════════════════
    // TROIS SERVICES ENTIERS INJOIGNABLES DEPUIS INTERNET (lot 7.5).
    //
    // Le compose fournissait DÉJÀ leurs adresses — `SERVICES__RETURNREFUND`,
    // `SERVICES__DRIVERS`, `SERVICES__DELIVERYPRICING` — depuis longtemps. Sans
    // propriété pour les lier, elles étaient ingérées et jetées en silence : la
    // configuration avait l'air complète, et rien ne se plaignait.
    //
    // Ce que cela rendait inatteignable :
    //
    //   • return-refund-service — VINGT ET UNE routes, dont le parcours client
    //     complet (créer un retour, suivre son dossier) et les huit routes
    //     vendeur dont le lot retours a écrit le contrôle d'appartenance. Un
    //     travail d'autorisation soigné, sur des routes que personne ne pouvait
    //     appeler ;
    //   • driver-service — la validation, le refus et la suspension des dossiers
    //     de livreur, écrits au lot 5.2, plus les huit routes `/me` de
    //     l'application livreur ;
    //   • delivery-pricing-service — l'édition de la grille tarifaire.
    //
    // CE N'ÉTAIT PAS UN OUBLI DE CODE : LE CODE EXISTAIT.
    //
    // L'audit notait « aucune route de validation des livreurs ». C'est faux
    // depuis le lot 5.2 : les cinq routes existent, typées, gardées. Le défaut a
    // changé de nature sans changer de symptôme — ce n'est plus un manque de
    // fonctionnalité, c'est un manque de routage.
    // ═════════════════════════════════════════════════════════════════════════
    [Required, Url] public string ReturnRefund { get; init; } = string.Empty;
    [Required, Url] public string Drivers { get; init; } = string.Empty;
    [Required, Url] public string DeliveryPricing { get; init; } = string.Empty;

    /// <summary>
    /// Adresse d'un service par sa clé logique, ou <c>null</c> si la clé est inconnue.
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
        ServiceKeys.ReturnRefund => ReturnRefund,
        ServiceKeys.Drivers => Drivers,
        ServiceKeys.DeliveryPricing => DeliveryPricing,
        _ => null
    };
}

/// <summary>
/// Clés logiques des services. Elles servent à la fois de nom de client HTTP,
/// de valeur `service` dans les sections BFF et d'identifiant de cluster YARP —
/// d'où l'intérêt qu'elles ne soient écrites qu'une fois.
/// </summary>
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
    public const string ReturnRefund = "ReturnRefund";
    public const string Drivers = "Drivers";
    public const string DeliveryPricing = "DeliveryPricing";

    // TROIS ENDROITS À TENIR D'ACCORD POUR UN SEUL SERVICE.
    //
    // La propriété de ServicesOptions, la branche de Resolve, et cette liste. Le
    // cluster « Promotion » n'était présent dans aucun des trois, alors que son
    // adresse figurait bien dans appsettings.json.
    //
    // La liste ci-dessous ne sert qu'au message d'erreur du filtre YARP — c'est
    // donc la moins grave à oublier, et la plus facile à oublier.
    public static readonly IReadOnlyList<string> All =
    [
        Identity, User, Merchant, Catalog, Inventory, Commerce, Order,
        Food, Delivery, Financial, Engagement, Communication, Media, Promotion,
        FoodCart, FoodOrder, ReturnRefund, Drivers, DeliveryPricing
    ];
}
