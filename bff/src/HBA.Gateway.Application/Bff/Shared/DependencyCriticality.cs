namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Ce qu'il advient d'un écran quand une de ses dépendances tombe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CRITICITÉ EST UNE DÉCISION PRODUIT, PAS UNE PROPRIÉTÉ DU SERVICE.
///
/// Le même service change de niveau selon l'écran : inventory-service est
/// OPTIONNEL sur une liste de produits — un stock manquant n'empêche pas de
/// parcourir — et CRITIQUE sur un panier, où valider une commande sans savoir ce
/// qui reste en rayon promet au client ce qu'on ne pourra pas livrer.
///
/// C'est pourquoi le niveau se déclare au point d'agrégation, jamais sur le
/// client HTTP.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public enum DependencyCriticality
{
    /// <summary>
    /// Sans elle, l'écran n'a pas de sens : la réponse est un échec (503).
    /// </summary>
    Critical,

    /// <summary>
    /// L'écran reste utile, mais amputé : réponse 200 AVEC un avertissement.
    /// </summary>
    /// <remarks>
    /// L'avertissement existe pour que le client puisse afficher « indisponible »
    /// plutôt qu'une valeur vide. Le contre-exemple à ne pas reproduire : rendre
    /// un stock à zéro quand inventory est à terre — le client afficherait
    /// « rupture » sur un produit qui en a trois cents.
    /// </remarks>
    Important,

    /// <summary>
    /// Agrément : le champ vaut <c>null</c>, sans avertissement.
    /// </summary>
    /// <remarks>
    /// Pas d'avertissement parce qu'il n'y a rien à en faire côté client : une
    /// section de recommandations absente se masque, elle ne se signale pas. En
    /// émettre un remplirait le tableau d'avertissements de bruit permanent, et
    /// les vrais n'y seraient plus lus.
    /// </remarks>
    Optional,
}
