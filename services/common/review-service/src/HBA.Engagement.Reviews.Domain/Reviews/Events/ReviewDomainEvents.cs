using HBA.Shared.Domain.Events;

namespace HBA.Engagement.Reviews.Domain.Reviews.Events;

/// <summary>
/// Un avis a été publié.
///
/// `SellerId` A ÉTÉ AJOUTÉ, ET L'AGRÉGAT L'AVAIT DÉJÀ EN MAIN.
///
/// Son absence obligeait chaque consommateur à résoudre `ProductId → SellerId` par
/// un appel gRPC au catalogue — c'est ce que fait encore
/// `ReviewPublishedNotificationHandler`, un aller-retour par avis pour une valeur
/// que le producteur connaissait. Et elle rendait impossible la mise à jour de la
/// note d'un vendeur, faute de savoir de quel vendeur il s'agit.
/// </summary>
public sealed record ReviewPublishedDomainEvent(
    Guid ReviewId, Guid ProductId, Guid SellerId, int Rating) : DomainEvent;

/// <summary>Un avis a été retiré (rejeté) — sa contribution à la note disparaît.</summary>
public sealed record ReviewRejectedDomainEvent(
    Guid ReviewId, Guid ProductId, Guid SellerId) : DomainEvent;
