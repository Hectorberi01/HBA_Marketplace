using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>Accueil HBA Food.</summary>
/// <param name="Cuisines">TOUJOURS VIDE — AUCUNE TAXONOMIE DE CUISINE N'EXISTE.</param>
/// <param name="DeliveryOffers">TOUJOURS VIDE — CF. `FoodRestaurantDetailDto.Delivery`.</param>
public sealed record FoodHomeDto(
    PagedResult<FoodRestaurantCardDto> Restaurants,
    FoodActiveOrderDto? ActiveOrder,
    IReadOnlyList<FoodCuisineDto> Cuisines,
    IReadOnlyList<FoodDeliveryOfferDto> DeliveryOffers);

public sealed record FoodCuisineDto(Guid Id, string Name);

public sealed record FoodDeliveryOfferDto(string Label, decimal? Fee);

/// <summary>Carte de la vitrine.</summary>
/// <param name="LogoMediaId">IDENTIFIANT DE MÉDIA **ET** URL — LES DEUX, ET C'EST LE §39.</param>
public sealed record FoodRestaurantCardDto(
    Guid Id,
    string Name,
    string? Description,
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
/// <param name="PopularItems">TOUJOURS VIDE — AUCUN SIGNAL DE POPULARITÉ N'EXISTE.</param>
public sealed record FoodRestaurantDetailDto(
    FoodRestaurantHeaderDto Restaurant,
    FoodRatingDto? Rating,
    FoodDeliveryDto Delivery,
    IReadOnlyList<FoodMenuDto> Menus,
    IReadOnlyList<FoodMenuItemDto> PopularItems);

/// <param name="LogoMediaId">
/// <summary> Cf. <c> FoodRestaurantCardDto</c> : identifiant ET URL
/// héritée.</summary>
/// </param>
/// <param name="AcceptsOrdersNow">
/// <summary> Réponse FERME : lieu ouvert ET au moins un plat commandable.</summary>
/// </param>
public sealed record FoodRestaurantHeaderDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? LogoMediaId,
    string? LogoUrl,
    Guid? CoverMediaId,

    string Phone,
    bool AcceptsOrdersNow,

    string BlockedReason,
    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    string LoadLevel,
    int ExtraWaitMinutes,
    string? SpecialClosureReason,
    IReadOnlyList<FoodServiceHoursDto> ServiceHours);

public sealed record FoodServiceHoursDto(string Day, string OpensAt, string ClosesAt);

/// <summary>TOUJOURS `null` AUJOURD'HUI — CF. `FoodDeliveryDto.NotEvaluated`.</summary>
public sealed record FoodRatingDto(double Average, int Count);

/// <summary>Estimation de livraison.</summary>
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

/// <param name="ImageMediaId">
/// <summary> Cf. <c> FoodRestaurantCardDto</c> : identifiant ET URL
/// héritée.</summary>
/// </param>
public sealed record FoodMenuItemDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ImageMediaId,
    string? ImageUrl,

    decimal BasePrice,
    string Currency,
    bool IsOrderable,
    DateTime? BackAtUtc);
