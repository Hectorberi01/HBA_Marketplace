namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// La convention de référence qui relie une course à ce qu'elle transporte.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE CONVENTION EXISTE, ET POURQUOI ELLE EST ICI.
///
/// Le moteur logistique ne connaît ni commande, ni expédition, ni repas : c'est
/// ce qui lui permet de servir HBAExpress, HBA Food et des partenaires externes
/// avec le même code, et ce qui le rend vendable à des tiers. Il transporte une
/// chaîne OPAQUE et la rend telle quelle dans ses événements.
///
/// Cette chaîne doit donc être fabriquée et relue de façon identique par tous
/// ceux qui donnent des ordres au transporteur. Elle l'était par DEUX copies —
/// une dans order-service, une dans food-service — et communication-service en
/// aurait ajouté une troisième pour notifier l'acheteur du suivi.
///
/// Le monolithe avait déjà écrit l'avertissement, à propos de ses deux copies à
/// lui : « deux façons de découper la même chaîne finiraient par diverger, et la
/// divergence se manifesterait par des expéditions qui n'avancent plus, sans
/// erreur ». Trois copies valaient mieux ne pas être écrites.
///
/// POURQUOI DANS LE SOCLE PARTAGÉ, ET NON DANS LES CONTRATS DE DELIVERY.
///
/// Y placer cette classe ferait connaître à Delivery les trois natures de ses
/// donneurs d'ordre — exactement l'ignorance qui fait sa valeur.
///
/// Ce n'est pas non plus une règle métier : c'est un format de corrélation entre
/// événements, au même titre qu'un identifiant de trace. Le socle partagé, qui
/// ne doit contenir aucune règle, peut porter une convention de nommage.
///
/// TROIS PRÉFIXES CIRCULENT SUR LE MÊME CANAL, ET C'EST VOULU.
///
/// Tous les consommateurs reçoivent TOUS les événements de course. Sans préfixe
/// distinct, order-service lirait l'identifiant d'un ticket de cuisine comme le
/// sien et enverrait ses commandes sur des GUID qui n'existent pas dans sa base
/// — un échec silencieux, puisqu'une commande introuvable ne lève pas.
///
/// `Read` rend donc `null` pour tout ce qui n'appartient pas à l'appelant, et
/// c'est le cas NORMAL, pas une anomalie.
///
/// TOUT PRÉFIXE POSÉ DOIT ÊTRE RELU QUELQUE PART. RIEN NE LE VÉRIFIE.
///
/// `FOOD-` a été posé par le pont restauration sans que personne ne consomme la
/// fin des courses qu'il désigne : le repas était remis au client, le ticket
/// restait « prêt » à vie, la commande n'atteignait jamais « livrée » — et le
/// restaurateur n'était jamais payé. `ORDER-`, créé dans le même geste et dans
/// ce même fichier, avait son gestionnaire de retour ; pas `FOOD-`.
///
/// L'asymétrie est INVISIBLE par construction : puisque `Read` rend `null` pour
/// ce qui n'est pas à soi, un préfixe que PERSONNE ne relit se comporte
/// exactement comme un préfixe que tout le monde ignore à bon droit. Ni la
/// compilation, ni les tests, ni les journaux ne les distinguent.
///
/// Le jour où un quatrième préfixe est ajouté ici, écrire son gestionnaire de
/// retour fait partie du même changement — sinon ses courses partent et ne
/// reviennent pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class DeliveryReference
{
    /// <summary>Une commande marketplace, sans expédition intermédiaire.</summary>
    public const string OrderPrefix = "ORDER-";

    /// <summary>Un ticket de cuisine — la référence est celle du TICKET, pas de la commande.</summary>
    public const string FoodPrefix = "FOOD-";

    /// <summary>
    /// Une expédition du module Shipping, resté dans le monolithe.
    /// </summary>
    /// <remarks>
    /// Déclaré ici bien qu'aucun service HBA ne le pose : le monolithe en produit
    /// encore, et ces courses transitent par les mêmes sujets. Un service qui
    /// l'ignorerait risquerait de confondre le préfixe avec le sien.
    /// </remarks>
    public const string ShipmentPrefix = "SHIP-";

    public static string ForOrder(Guid orderId) => Build(OrderPrefix, orderId);

    public static string ForFoodOrder(Guid foodOrderId) => Build(FoodPrefix, foodOrderId);

    /// <summary>
    /// Relit une référence pour un préfixe donné. Nulle si elle appartient à
    /// quelqu'un d'autre — ce qui est le cas le plus fréquent.
    /// </summary>
    public static Guid? Read(string? reference, string prefix)
        => reference is not null
           && reference.StartsWith(prefix, StringComparison.Ordinal)
           && Guid.TryParseExact(reference[prefix.Length..], "N", out var parsed)
            ? parsed
            : null;

    public static Guid? ReadOrder(string? reference) => Read(reference, OrderPrefix);

    public static Guid? ReadFoodOrder(string? reference) => Read(reference, FoodPrefix);

    // FORMAT « N » — TRENTE-DEUX CARACTÈRES SANS TIRETS.
    //
    // Le format par défaut d'un Guid en contient ; mélanger les deux ferait
    // qu'une référence écrite d'un côté ne serait pas relue de l'autre. C'est
    // précisément ce que cette classe existe pour empêcher.
    private static string Build(string prefix, Guid id) => $"{prefix}{id:N}";
}
