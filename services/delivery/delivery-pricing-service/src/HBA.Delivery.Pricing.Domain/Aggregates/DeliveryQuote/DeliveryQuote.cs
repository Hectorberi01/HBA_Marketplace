using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES MONTANTS SONT DES `long`, ET C'EST L'UNE DES DEUX SEULES ÎLES EN ENTIER
/// DU DÉPÔT (D39). L'autre est `promotions`.
///
/// Le franc CFA n'a pas de sous-unité : l'entier ferme la porte aux arrondis au
/// lieu de les gérer. Partout ailleurs — commandes, paiements, portefeuille,
/// courses — l'argent est en `numeric(18,2)`.
///
/// LA FRONTIÈRE EST FRANCHIE UNE FOIS, ET DANS UN SEUL SENS.
///
/// `ConsumeQuote` rend `int64 total` ; `GrpcDeliveryPricingQuoteValidator` le
/// reçoit en `decimal?`. La conversion est implicite et EXACTE — 1 500 devient
/// 1 500,00 — parce que les deux côtés comptent en FRANCS. Il n'y a nulle part,
/// dans tout le dépôt, de multiplication ni de division par cent : le jour où
/// quelqu'un en écrit une ici, c'est qu'il a supposé des centimes.
///
/// CE QUE CE CHOIX SUPPOSE : que la devise n'a pas de sous-unité. Rien ne le
/// vérifie — `Currency` est un texte libre de trois lettres.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DeliveryQuote(
    Guid Id,
    Guid? SellerId,
    Guid? StoreId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int DistanceMeters,
    int DurationSeconds,
    string? VehicleType,
    string ServiceLevel,
    long Subtotal,
    PriceBreakdown Components,
    long Discount,
    long Total,
    string Currency,
    DateTimeOffset ExpiresAt,
    string PricingVersion,
    string Status)
{
    // ═════════════════════════════════════════════════════════════════════════
    // LE CONSTRUCTEUR QU'EF SAIT LIER — SANS LUI, LE SERVICE NE DÉMARRE PAS.
    //
    // CE DÉFAUT A EMPÊCHÉ delivery-pricing-service DE DÉMARRER, DEPUIS TOUJOURS.
    //
    // EF Core ne peut lier à un paramètre de constructeur que des propriétés
    // MAPPÉES. `Pickup`, `Dropoff` et `Components` sont déclarés `OwnsOne` dans
    // `DeliveryPricingDbContext` : ce sont des NAVIGATIONS vers des types
    // possédés, et EF refuse explicitement de les lier. Le message est net :
    //
    //   No suitable constructor was found for entity type 'DeliveryQuote'.
    //   Cannot bind 'Pickup', 'Dropoff', 'Components' in 'DeliveryQuote(...)'
    //
    // L'ÉCHEC ARRIVE À LA FINALISATION DU MODÈLE, donc au démarrage de l'hôte —
    // avant la moindre requête, et avant même la migration. Constaté le
    // 1er septembre 2026 sur le premier Job de migration de production.
    //
    // POURQUOI AUCUN TEST NE L'A VU. Les 50 tests de `HBA.Delivery.UnitTests`
    // n'instancient jamais le `DbContext` : ils éprouvent le calcul de prix, pas
    // la persistance. Le modèle EF n'était donc finalisé nulle part avant la
    // production. C'est le trou de couverture qu'il faut retenir de ce défaut,
    // plus que le défaut lui-même.
    //
    // CE CONSTRUCTEUR NE PORTE QUE DES SCALAIRES. EF le choisit — il retient
    // celui dont il sait lier le plus de paramètres — puis pose `Pickup`,
    // `Dropoff` et `Components` par leurs navigations, comme il le fait pour
    // tout type possédé. Il est `private` : personne d'autre qu'EF ne doit
    // construire un devis sans lieu ni décomposition de prix.
    //
    // CE QU'IL NE COUVRE PAS. Il ne change ni le schéma, ni les colonnes, ni le
    // constructeur public : le reste du code continue de construire un devis
    // complet, en une fois. Et il ne dispense pas d'un test qui finalise le
    // modèle — sans lui, le prochain type possédé ajouté ici casserait de la
    // même façon, et se découvrirait au même endroit.
    // ═════════════════════════════════════════════════════════════════════════
    private DeliveryQuote(
        Guid id,
        Guid? sellerId,
        Guid? storeId,
        int distanceMeters,
        int durationSeconds,
        string? vehicleType,
        string serviceLevel,
        long subtotal,
        long discount,
        long total,
        string currency,
        DateTimeOffset expiresAt,
        string pricingVersion,
        string status)
        : this(
            id,
            sellerId,
            storeId,
            null!,
            null!,
            distanceMeters,
            durationSeconds,
            vehicleType,
            serviceLevel,
            subtotal,
            null!,
            discount,
            total,
            currency,
            expiresAt,
            pricingVersion,
            status)
    {
    }

    public Guid? ConsumedByDeliveryId { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }

    /// <summary>
    /// D'où viennent <see cref="DistanceMeters"/> et <see cref="DurationSeconds"/> :
    /// <c>CLIENT_PROVIDED</c> ou <c>FALLBACK_HAVERSINE</c>. Voir
    /// <c>SourcesEstimation</c>.
    ///
    /// PROPRIÉTÉ `init` ET NON PARAMÈTRE POSITIONNEL — délibéré. Ajouter un
    /// paramètre au constructeur d'un enregistrement casse tous ses appelants,
    /// y compris les fabriques de test, sans que le compilateur puisse dire
    /// lequel voulait quelle valeur. Une propriété à valeur par défaut se pose
    /// là où on la connaît et ne ment nulle part ailleurs.
    ///
    /// LA VALEUR PAR DÉFAUT EST VIDE, PAS `FALLBACK_HAVERSINE`. Une chaîne vide
    /// se lit « on ne sait pas », ce qui est la vérité pour toute ligne écrite
    /// avant la migration qui a créé la colonne. Écrire `FALLBACK_HAVERSINE` par
    /// défaut affirmerait rétroactivement quelque chose qu'aucune donnée ne
    /// soutient.
    /// </summary>
    public string SourceEstimation { get; init; } = string.Empty;

    /// <summary>
    /// Le facteur de correction urbaine EFFECTIVEMENT appliqué à ce devis-ci.
    ///
    /// Il est persisté et non relu de la configuration, parce que la
    /// configuration change : un devis chiffré à 1,0 doit rester explicable
    /// après qu'on soit passé à 1,3, sans quoi un litige se juge avec le
    /// réglage d'aujourd'hui sur un prix d'hier.
    ///
    /// Vaut 0 pour les lignes antérieures à la migration, et pour une distance
    /// fournie par l'appelant — à laquelle aucun facteur ne s'applique.
    /// </summary>
    public decimal FacteurCorrectionApplique { get; init; }
}
