using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Ce qu'un appel a rendu, sans lever pour un échec attendu.</summary>
/// <remarks>
/// UN 401 OU UN 503 N'EST PAS EXCEPTIONNEL ICI — C'EST LA VIE NORMALE D'UN
///    CLIENT DE BUREAU.
///
/// Les modéliser en exceptions obligerait chaque vue-modèle à un `try/catch`, et
/// le premier oublié ferait tomber l'application entière sur un service
/// momentanément absent. Le socle du dépôt fait le même choix côté serveur avec
/// `Result&lt;T&gt;` et `ServiceResult&lt;T&gt;`.
/// </remarks>
public sealed record Resultat<T>(bool Reussi, T? Valeur, string? Message)
{
    public static Resultat<T> Ok(T valeur) => new(true, valeur, null);

    public static Resultat<T> Echec(string message) => new(false, default, message);
}

/// <summary>Issue d'une tentative de connexion.</summary>
public enum IssueConnexion
{
    /// <summary>Session ouverte.</summary>
    Ouverte,

    /// <summary>Identifiants acceptés ; il manque le second facteur.</summary>
    CodeExige,

    /// <summary>Refusée.</summary>
    Refusee,
}

/// <summary>Jetons tels que identity-service les rend.</summary>
/// <remarks>
/// DEUX FORMES POUR LA MÊME CHOSE, ET C'EST DANS LE SERVEUR.
///
/// `POST /login` emballe les jetons : `{ mfaRequired, tokens }`. `POST /refresh`
/// et `POST /reauthenticate` les rendent À PLAT. La console vendeur du dépôt
/// porte la même remarque dans `src/lib/bff.ts` — c'est un écart réel, pas une
/// erreur de lecture, et le traiter explicitement des deux côtés évite une
/// session vide et inexplicable.
/// </remarks>
public sealed record JetonsApi(
    [property: JsonPropertyName("accessToken")] string? AccessToken,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken,
    [property: JsonPropertyName("accessTokenExpiresOnUtc")] DateTimeOffset? ExpireLe);

/// <summary>Corps de `POST /api/v1/auth/login`.</summary>
public sealed record ReponseConnexion(
    [property: JsonPropertyName("mfaRequired")] bool MfaRequise,
    [property: JsonPropertyName("tokens")] JetonsApi? Jetons);

/// <summary>Enveloppe du socle HBA pour une réponse NON paginée.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE `meta` EST PRÉSENT, MAIS IL NE PORTE AUCUNE PAGINATION — ET C'EST LE PIÈGE.
///
/// `ApiResults.Ok(x)` appelle `ApiEnvelope.Ok(data)`, qui pose
/// `Meta = meta ?? Meta()` : le corps rendu est donc
/// `{ success, data, meta: { requestId, timestamp } }`. Les champs `page`,
/// `pageSize`, `total` et `hasNext` d'`ApiMeta` sont marqués
/// `JsonIgnoreCondition.WhenWritingNull` et disparaissent du JSON.
///
/// LIRE UN TEL CORPS COMME UN `PageApi` COMPILE ET MENT.
///
/// `MetaPage` déclare `Page`, `PageSize` et `Total` NON nullables : les champs
/// absents deviennent 0. Un écran afficherait « 0 sur 0 » au-dessus d'une liste
/// pleine, et l'on chercherait la panne du côté du service. D'où ce type-ci, qui
/// ne prétend pas lire une pagination qui n'est pas envoyée.
///
/// C'est la forme des listes de référence — marques, catégories — et des
/// réponses d'écriture portant un identifiant. Quatre formes coexistent dans le
/// dépôt et seule la lecture de l'endpoint tranche : `ApiResults.Page` rend
/// `{data, meta}` paginé, `ApiResults.Ok` rend ce qui est décrit ici,
/// `Results.Ok(pagedResult)` rend `{items, total, page, pageSize}` — voir
/// `PageBrute` — et `Results.Ok(liste)` rend le tableau nu.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record EnveloppeApi<T>(
    [property: JsonPropertyName("data")] T? Data);

/// <summary>Enveloppe de réponse des façades BFF.</summary>
public sealed record EnveloppeBff<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("warnings")] IReadOnlyList<AvertissementBff>? Warnings);

/// <summary>Une dépendance dégradée, telle que la passerelle la déclare.</summary>
public sealed record AvertissementBff(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("code")] string Code);

/// <summary>Les files d'attente d'administration.</summary>
public sealed record FilesDAttente(
    [property: JsonPropertyName("files")] IReadOnlyList<FileDAttente> Files);

/// <summary>Une file d'attente.</summary>
/// <param name="Total">
/// <c>null</c> quand le service amont n'a pas répondu — ce qui n'est PAS zéro.
/// </param>
public sealed record FileDAttente(
    [property: JsonPropertyName("cle")] string Cle,
    [property: JsonPropertyName("libelle")] string Libelle,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("approximatif")] bool Approximatif);
