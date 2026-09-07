using System.Text.Json;

namespace HBA.Gateway.Application.Bff;

/// <summary>Un bloc d'une réponse d'agrégation, avec son état propre.</summary>
/// <param name="Key">Clé stable côté client, indépendante du service interne appelé.</param>
/// <param name="Available">Vrai si <paramref name="Data"/> porte une réponse exploitable.</param>
/// <param name="Data">Charge utile du service, absente si indisponible.</param>
public sealed record BffSection(string Key, bool Available, JsonElement? Data)
{
    public static BffSection Ok(string key, JsonElement data) => new(key, true, data);

    /// <summary>AUCUN MOTIF N'EST EXPOSÉ AU CLIENT.</summary>
    public static BffSection Unavailable(string key) => new(key, false, null);
}
