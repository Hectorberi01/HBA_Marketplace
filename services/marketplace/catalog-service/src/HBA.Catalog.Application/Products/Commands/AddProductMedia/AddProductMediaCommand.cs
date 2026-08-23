using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.AddProductMedia;

/// <summary>
/// Rattache à un produit un média DÉJÀ DÉPOSÉ dans le service média.
///
/// CETTE COMMANDE PRENAIT UNE URL VENUE DU CLIENT.
///
/// Un vendeur pouvait donc afficher sur sa fiche n'importe quelle adresse du web :
/// une image hébergée ailleurs (que le site sert alors comme sienne, avec le
/// trafic et la responsabilité qui vont avec), un pixel de suivi qui trace chaque
/// visiteur de la fiche, ou simplement une image qu'un tiers peut remplacer après
/// coup — la modération valide un contenu, le vendeur en substitue un autre.
///
/// L'appelant dépose le fichier, obtient un identifiant, et le rattache. C'est
/// TOUT ce qu'il fournit : le handler demande l'adresse au service média, qui est
/// le seul à la connaître pour de bon. La copie de lecture stockée sur
/// `ProductMedia` vient donc de là, jamais de la requête.
/// </summary>
public sealed record AddProductMediaCommand(
    Guid ProductId,
    Guid MediaId,
    string Type = "Image",
    string? AltText = null,
    bool IsPrimary = false) : ICommand<Guid>;
