namespace HBA.Gateway.Application.Contracts.Engagement;

/// <summary>
/// Note agrégée d'un produit.
/// </summary>
/// <remarks>
/// ENGAGEMENT EST ENTIÈREMENT AUTHENTIFIÉ CÔTÉ SERVICE.
///
/// Toutes ses routes passent par <c>MapAuthenticatedGroup</c> — avis,
/// recommandations et liste d'envies compris. Une fiche produit consultée sans
/// session ne peut donc PAS afficher de note : ce n'est pas un choix de la
/// passerelle, c'est l'état du service.
///
/// Conséquence assumée : <c>rating</c> vaut <c>null</c> pour un visiteur anonyme,
/// et l'agrégateur n'émet aucun avertissement — un service qui refuse
/// légitimement n'est pas un service en panne. Si le produit veut des notes
/// publiques, c'est engagement-service qu'il faut ouvrir, pas la passerelle qu'il
/// faut contourner.
/// </remarks>
public sealed record ProductRating(Guid ProductId, double Average, int Count);

public sealed record ProductReview(
    Guid Id,
    Guid ProductId,
    int Rating,
    string Title,
    string Body,
    bool IsVerifiedPurchase,
    DateTime CreatedAtUtc,
    string? SellerReply);

/// <summary>
/// Un jeu de recommandations.
/// </summary>
/// <remarks>
/// LE SERVICE REND UN OBJET, PAS UNE LISTE D'IDENTIFIANTS.
///
/// J'avais d'abord typé ce retour comme `IReadOnlyList&lt;Guid&gt;` — la forme
/// que j'attendais, pas celle que `RecommendationSummary` déclare. La
/// désérialisation aurait rendu une liste vide sans lever la moindre erreur, et
/// la section « recommandés » serait restée obstinément vide en production.
///
/// C'est exactement le défaut que le §51 cherche à prévenir : le contrat se lit,
/// il ne se devine pas.
/// </remarks>
public sealed record RecommendationSet(
    Guid Id,
    string Type,
    Guid? ContextProductId,
    Guid? UserId,
    IReadOnlyList<Guid> RecommendedProductIds,
    double Score,
    DateTime GeneratedAtUtc);
