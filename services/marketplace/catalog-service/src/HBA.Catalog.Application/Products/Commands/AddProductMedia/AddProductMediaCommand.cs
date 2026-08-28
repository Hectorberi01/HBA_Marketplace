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
/// <param name="RequestedByUserId">
/// Le compte qui rattache. Comparé au DÉPOSANT du média — voir l'encadré du
/// gestionnaire. `Guid.Empty` = appelant inconnu, et le rattachement est refusé.
///
/// Paramètre optionnel en fin d'enregistrement pour ne pas casser les appelants
/// existants à la compilation. Le défaut est le cas FERMÉ : un appelant qui
/// oublie de le passer se voit refuser, il n'obtient pas un contrôle désactivé.
///
/// BALISE `param` SUR LA DÉCLARATION, ET NON `summary` DANS LA LISTE.
///
/// La seconde forme est répandue dans ce dépôt — 424 occurrences dans 22
/// fichiers — et le compilateur émet CS1587 sur chacune : « le commentaire XML
/// n'est pas placé dans un élément valide du langage ». Un commentaire posé
/// devant un paramètre d'enregistrement positionnel n'est pas rattaché à ce
/// paramètre : il n'est rattaché à rien, et ne sort dans aucune documentation.
///
/// Cette forme-ci produit le même texte, au bon endroit, sans avertissement.
/// C'est aussi celle qu'emploie `AddKybDocumentCommand`, écrite le même jour :
/// deux fichiers du même lot ne doivent pas sortir dans deux styles.
/// </param>
public sealed record AddProductMediaCommand(
    Guid ProductId,
    Guid MediaId,
    string Type = "Image",
    string? AltText = null,
    bool IsPrimary = false,
    Guid RequestedByUserId = default) : ICommand<Guid>;
