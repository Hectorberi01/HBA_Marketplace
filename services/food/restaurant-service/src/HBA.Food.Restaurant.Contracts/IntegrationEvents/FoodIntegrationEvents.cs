using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// HBA A VALIDÉ UN ÉTABLISSEMENT.
///
/// C'EST CET ÉVÉNEMENT QUI ATTRIBUE LE RÔLE « FoodPartner ».
///
/// La distinction est la raison d'être de ce message : `POST /api/food/partner`
/// est ouvert à tout compte authentifié — n'importe qui peut se déclarer
/// restaurateur, et c'est normal, c'est une CANDIDATURE. Donner le rôle à ce
/// moment-là reviendrait à laisser chacun se décerner sa propre habilitation.
///
/// La validation, elle, est la décision d'un administrateur qui a regardé un
/// dossier. C'est le seul instant où quelqu'un chez HBA atteste que des repas
/// préparés là peuvent être vendus.
///
/// Exactement le raisonnement de DriverVerifiedIntegrationEvent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record RestaurantApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }

    /// <summary>Le compte HBA du restaurateur — celui qui reçoit le rôle.</summary>
    public required Guid OwnerUserId { get; init; }

    public required string Name { get; init; }
}

/// <summary>
/// Le dossier a été REFUSÉ par la modération.
///
/// LE MOTIF EST CE QUI REND LE REFUS UTILE. Sans lui, le restaurateur voit un
/// statut changer, resoumet le même dossier, la modération le refuse à nouveau,
/// et les deux s'épuisent. Un refus sans motif n'est pas une décision de
/// modération, c'est une impasse.
/// </summary>
public sealed record RestaurantRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// L'établissement a été SUSPENDU par la plateforme : il quitte la vitrine.
///
/// Le restaurateur doit l'apprendre autrement que par la chute de ses commandes —
/// il perdrait des jours à chercher une panne qui n'existe pas.
/// </summary>
public sealed record RestaurantSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>La suspension est levée : l'établissement revient dans la vitrine.</summary>
public sealed record RestaurantReopenedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
}
