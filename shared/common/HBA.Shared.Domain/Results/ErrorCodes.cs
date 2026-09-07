namespace HBA.Shared.Domain.Results;

/// <summary>Codes d'erreur normalisés du cahier des charges (§5 et §10.x).</summary>
public static class ErrorCodes
{
    /// <summary>400 — Payload invalide ou champ obligatoire absent.</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>422 — Règle métier non satisfaite.</summary>
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";

    /// <summary>409 — État incompatible, version concurrente ou conflit d'idempotence.</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>503 — Dépendance gRPC/Kafka/provider indisponible.</summary>
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";

    /// <summary>401 — Non authentifié. Hors tableau du §10 mais présent au §5.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>403 — Interdit. Hors tableau du §10 mais présent au §5.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>429 — Quota dépassé.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>500 — Erreur interne non qualifiée.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>404 — Ressource principale introuvable, préfixé par le service.</summary>
    public static string NotFound(string serviceCode) => $"{serviceCode}_SERVICE_NOT_FOUND";
}

/// <summary>
/// Préfixes de service pour <see cref="ErrorCodes.NotFound"/> , un par service du
/// §3.
/// </summary>
public static class ServiceCodes
{
    public const string Identity = "IDENTITY";
    public const string User = "USER";
    /// <summary>`SELLER` ET NON `MERCHANT` — ÉCART ASSUMÉ AU CAHIER DES CHARGES.</summary>
    public const string Seller = "SELLER";
    public const string Catalog = "CATALOG";
    public const string Inventory = "INVENTORY";
    public const string MarketplaceCart = "MARKETPLACE_CART";
    public const string MarketplaceOrder = "MARKETPLACE_ORDER";
    public const string Restaurant = "RESTAURANT";
    public const string Menu = "MENU";
    public const string FoodCart = "FOOD_CART";
    public const string FoodOrder = "FOOD_ORDER";
    public const string Payment = "PAYMENT";
    public const string WalletAndSettlement = "WALLET_AND_SETTLEMENT";
    public const string Delivery = "DELIVERY";
    public const string Notification = "NOTIFICATION";
    public const string Promotion = "PROMOTION";
}
