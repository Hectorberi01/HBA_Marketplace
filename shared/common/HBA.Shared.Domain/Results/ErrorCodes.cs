namespace HBA.Shared.Domain.Results;

/// <summary>
/// Codes d'erreur normalisés du cahier des charges (§5 et §10.x).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI DES CONSTANTES ET NON DES CHAÎNES ÉCRITES SUR PLACE.
///
/// Le code d'erreur est un CONTRAT PUBLIC : les applications mobile et web
/// branchent leur affichage dessus. Une faute de frappe dans un `"VALIDATION_ERORR"`
/// ne casse aucune compilation, ne lève aucun test, et se manifeste chez
/// l'utilisateur par un message générique au lieu du bon — six mois plus tard,
/// dans un cas limite que personne ne rejoue.
///
/// L'audit du 17 août 2026 a compté ZÉRO occurrence de ces cinq codes dans les
/// 1 066 fichiers du dépôt. Ce fichier est donc le point de départ : tant que
/// les services renvoient des codes maison, l'enveloppe d'erreur du §5 n'a
/// aucun contenu stable à transporter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ErrorCodes
{
    /// <summary>400 — Payload invalide ou champ obligatoire absent.</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>422 — Règle métier non satisfaite. La requête est bien formée, l'état ne permet pas.</summary>
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

    /// <summary>
    /// 404 — Ressource principale introuvable, préfixé par le service.
    ///
    /// Le cahier des charges ne définit pas un `NOT_FOUND` unique mais un code par
    /// service : <c>IDENTITY_SERVICE_NOT_FOUND</c>, <c>FOOD_ORDER_SERVICE_NOT_FOUND</c>…
    /// Le préfixe dit au client QUEL service n'a pas trouvé, ce qu'un 404 nu ne dit
    /// pas quand un BFF agrège trois appels gRPC dans une seule réponse.
    ///
    /// Usage : <c>ErrorCodes.NotFound(ServiceCodes.FoodOrder)</c>.
    /// </summary>
    public static string NotFound(string serviceCode) => $"{serviceCode}_SERVICE_NOT_FOUND";
}

/// <summary>
/// Préfixes de service pour <see cref="ErrorCodes.NotFound"/>, un par service du §3.
/// Les valeurs sont celles du cahier des charges, pas celles des dossiers du dépôt :
/// <c>MERCHANT</c> et non <c>SELLER</c>, tant que l'alignement du vocabulaire n'est pas tranché.
/// </summary>
public static class ServiceCodes
{
    public const string Identity = "IDENTITY";
    public const string User = "USER";
    /// <summary>
    /// `SELLER` ET NON `MERCHANT` — ÉCART ASSUMÉ AU CAHIER DES CHARGES.
    ///
    /// Le §3 nomme ce service « Merchant Service ». Le code l'appelle `Seller`
    /// depuis l'origine, sur 83 fichiers, ses protos, ses topics et sa base. La
    /// décision du 17 août 2026 est de conserver `Seller` — un vendeur marketplace
    /// et un restaurant sont le même agrégat — et d'aligner la spec, pas le code.
    ///
    /// Le code d'erreur suit : `SELLER_SERVICE_NOT_FOUND`. Garder `MERCHANT` ici
    /// aurait fait porter au contrat public un mot qui n'existe nulle part ailleurs.
    /// </summary>
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
