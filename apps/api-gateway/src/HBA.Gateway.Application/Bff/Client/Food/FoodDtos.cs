using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>
/// Accueil HBA Food.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN PRODUIT MARKETPLACE ICI, ET AUCUN NE DOIT Y ENTRER (§8, §45).
///
/// Symétrique exact de <c>ExpressHomeDto</c> : la frontière entre les deux
/// univers est portée par le TYPE. Un champ « produits recommandés » ajouté
/// « pour enrichir » ferait entrer HBAExpress dans l'accueil restauration.
///
/// « nearbyRestaurants » ET « popularRestaurants » NE SONT PAS ICI.
///
/// Le cahier des charges (§8) les prévoit tous les deux. food-service n'expose
/// NI recherche géographique, NI signal de popularité — ni note, ni volume de
/// commandes agrégé. Les nommer ainsi en servant simplement la première page de
/// la vitrine serait un mensonge de champ : le client afficherait « près de chez
/// vous » sur une liste triée par ordre alphabétique.
///
/// Un seul champ, honnête : <c>Restaurants</c>, la vitrine paginée. Les deux
/// sections reviendront quand les endpoints existeront.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record FoodHomeDto(
    PagedResult<FoodRestaurantCardDto> Restaurants,
    FoodActiveOrderDto? ActiveOrder,

    /// <summary>
    /// TOUJOURS VIDE — AUCUNE TAXONOMIE DE CUISINE N'EXISTE.
    ///
    /// « Pizza », « africain », « grillades » : food-service ne porte aucune
    /// catégorie d'établissement. Le champ existe pour que le contrat client soit
    /// stable le jour où elle arrivera ; il n'est alimenté par rien.
    ///
    /// N'émet PAS d'avertissement : une absence permanente n'est pas une
    /// dégradation, et le tableau d'avertissements ne doit signaler que ce qui
    /// peut se rétablir.
    /// </summary>
    IReadOnlyList<FoodCuisineDto> Cuisines,

    /// <summary>
    /// TOUJOURS VIDE — CF. `FoodRestaurantDetailDto.Delivery`.
    ///
    /// Une « offre de livraison » suppose un moteur tarifaire. Le §19 interdit
    /// explicitement d'en inventer un.
    /// </summary>
    IReadOnlyList<FoodDeliveryOfferDto> DeliveryOffers);

public sealed record FoodCuisineDto(Guid Id, string Name);

public sealed record FoodDeliveryOfferDto(string Label, decimal? Fee);

/// <summary>
/// Carte de la vitrine.
/// </summary>
/// <remarks>
/// `IsOpenNow`, PAS `AcceptsOrdersNow` — LE NOM PORTE LA PROMESSE.
///
/// La liste ne vérifie que le LIEU. La disponibilité réelle de la carte est
/// confirmée sur la fiche. Renommer ce champ ferait traverser la ville à un
/// client pour découvrir que tout est épuisé.
/// </remarks>
public sealed record FoodRestaurantCardDto(
    Guid Id,
    string Name,
    string? Description,
    /// <summary>
    /// IDENTIFIANT DE MÉDIA **ET** URL — LES DEUX, ET C'EST LE §39.
    ///
    /// <c>LogoUrl</c> n'est renseignée que pour les établissements d'avant la
    /// bascule vers media-service, qui portent encore une URL en dur. Pour les
    /// autres elle vaut <c>null</c>, et c'est <c>LogoMediaId</c> qui permet au
    /// client de demander la variante voulue.
    ///
    /// La passerelle NE RÉSOUT PAS les URL ici : media-service n'expose que
    /// <c>GET /api/v1/media/{id}</c>, un appel par média. Une page de vingt
    /// restaurants coûterait vingt appels de plus pour des vignettes — le N+1 du
    /// §43, sur l'écran d'entrée.
    ///
    /// Manque à combler : <c>POST /api/v1/media/urls</c> acceptant un lot
    /// d'identifiants.
    /// </summary>
    Guid? LogoMediaId,
    string? LogoUrl,

    bool IsOpenNow,
    string ClosedReason,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    string LoadLevel,
    int ExtraWaitMinutes,
    string? SpecialClosureReason);

public sealed record FoodActiveOrderDto(Guid Id, string Status, decimal GrandTotal, string Currency);

/// <summary>Fiche d'un restaurant (§9).</summary>
public sealed record FoodRestaurantDetailDto(
    FoodRestaurantHeaderDto Restaurant,
    FoodRatingDto? Rating,
    FoodDeliveryDto Delivery,
    IReadOnlyList<FoodMenuDto> Menus,

    /// <summary>
    /// TOUJOURS VIDE — AUCUN SIGNAL DE POPULARITÉ N'EXISTE.
    ///
    /// « Plats populaires » suppose de compter les ventes par plat. Ni Food ni
    /// Engagement ne le font. Les remplacer par « les quatre premiers de la
    /// carte » serait un mensonge de champ : le client croirait à un classement.
    /// </summary>
    IReadOnlyList<FoodMenuItemDto> PopularItems);

public sealed record FoodRestaurantHeaderDto(
    Guid Id,
    string Name,
    string? Description,

    /// <summary>Cf. <c>FoodRestaurantCardDto</c> : identifiant ET URL héritée.</summary>
    Guid? LogoMediaId,
    string? LogoUrl,
    Guid? CoverMediaId,

    string Phone,

    /// <summary>Réponse FERME : lieu ouvert ET au moins un plat commandable.</summary>
    bool AcceptsOrdersNow,

    string BlockedReason,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    string LoadLevel,
    int ExtraWaitMinutes,
    string? SpecialClosureReason,
    IReadOnlyList<FoodServiceHoursDto> ServiceHours);

public sealed record FoodServiceHoursDto(string Day, string OpensAt, string ClosesAt);

/// <summary>
/// TOUJOURS `null` AUJOURD'HUI — CF. `FoodDeliveryDto.NotEvaluated`.
/// </summary>
public sealed record FoodRatingDto(double Average, int Count);

/// <summary>
/// Estimation de livraison.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN TARIF N'EST CALCULÉ, ET AUCUN NE DOIT L'ÊTRE (§9, §19).
///
/// Votre cahier l'écrit deux fois : « Ne retourne pas de faux tarif Delivery » et
/// « Ne jamais inventer prix/km, zone fee, vehicle fee, urgency fee ».
///
/// Un devis dépend de l'adresse de destination — que la fiche restaurant ne
/// connaît pas — et d'un moteur tarifaire dont l'existence n'est pas établie.
/// <c>Available = false</c> avec <c>Fee = null</c> dit honnêtement « pas
/// calculé ». Le client masque le bloc au lieu d'afficher un montant qu'il
/// faudrait démentir au paiement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record FoodDeliveryDto(bool Available, decimal? Fee, int? EtaMinutes)
{
    public static FoodDeliveryDto NotEvaluated => new(false, null, null);
}

public sealed record FoodMenuDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsServedNow,
    string? ServedFrom,
    string? ServedUntil,
    IReadOnlyList<FoodMenuSectionDto> Sections);

public sealed record FoodMenuSectionDto(
    Guid Id, string Name, string? Description, IReadOnlyList<FoodMenuItemDto> Items);

public sealed record FoodMenuItemDto(
    Guid Id,
    string Name,
    string? Description,

    /// <summary>Cf. <c>FoodRestaurantCardDto</c> : identifiant ET URL héritée.</summary>
    Guid? ImageMediaId,
    string? ImageUrl,

    decimal BasePrice,
    string Currency,
    bool IsOrderable,
    DateTime? BackAtUtc);
