using HBA.Gateway.Application.Bff;

namespace HBA.Gateway.Application.DTOs;

/// <summary>
/// Réponse d'un écran d'accueil agrégé.
/// </summary>
/// <param name="Surface">
/// « express » ou « food ». Présent DÉLIBÉRÉMENT dans le corps : les deux
/// univers ne doivent jamais être confondus côté client, et une réponse qui
/// s'identifie elle-même rend l'erreur visible immédiatement plutôt qu'au
/// moment où un produit s'affiche dans un menu de restaurant.
/// </param>
/// <param name="CorrelationId">
/// Renvoyé dans le corps en plus de l'en-tête : c'est ce que l'utilisateur peut
/// lire à l'écran et communiquer au support.
/// </param>
/// <param name="Sections">Blocs demandés, dans l'ordre de la configuration.</param>
public sealed record BffHomeResponse(
    string Surface,
    string CorrelationId,
    IReadOnlyList<BffSection> Sections);
