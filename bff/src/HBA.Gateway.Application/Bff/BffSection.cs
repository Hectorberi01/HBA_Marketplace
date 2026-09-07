using System.Text.Json;

namespace HBA.Gateway.Application.Bff;

/// <summary>
/// Un bloc d'une réponse d'agrégation, avec son état propre.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SECTION EN ÉCHEC N'EST PAS UNE RÉPONSE EN ÉCHEC.
///
/// C'est tout l'intérêt d'un BFF : si le service d'avis ne répond pas, l'accueil
/// doit s'afficher sans les avis, pas rendre une erreur. L'application cliente
/// lit `available` section par section et masque ce qui manque.
///
/// Le contre-exemple à ne pas reproduire : renvoyer un tableau vide quand le
/// service est tombé. Le client ne peut alors plus distinguer « il n'y a aucun
/// avis » de « les avis sont indisponibles », et affiche « Aucun avis » sur un
/// produit qui en a trois cents.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Key">Clé stable côté client, indépendante du service interne appelé.</param>
/// <param name="Available">Vrai si <paramref name="Data"/> porte une réponse exploitable.</param>
/// <param name="Data">Charge utile du service, absente si indisponible.</param>
public sealed record BffSection(string Key, bool Available, JsonElement? Data)
{
    public static BffSection Ok(string key, JsonElement data) => new(key, true, data);

    /// <summary>
    /// AUCUN MOTIF N'EST EXPOSÉ AU CLIENT.
    ///
    /// « catalog-service : connexion refusée » nomme un hôte interne et signale
    /// à un attaquant quel composant est à terre. Le motif part dans les journaux,
    /// corrélé ; le client n'apprend que l'indisponibilité.
    /// </summary>
    public static BffSection Unavailable(string key) => new(key, false, null);
}
