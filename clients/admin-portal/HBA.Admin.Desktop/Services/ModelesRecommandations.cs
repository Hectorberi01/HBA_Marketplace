using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une recommandation, telle que `RecommendationSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE RECOMMANDATION NE PORTE QUE DES IDENTIFIANTS.
///
/// Ni nom de produit, ni nom d'utilisateur : le service de recommandation est un
/// read model projeté hors du chemin transactionnel, et il n'a aucun accès au
/// catalogue. L'écran affiche donc des identifiants, et le dit — prétendre
/// afficher des noms demanderait un appel croisé vers le catalogue pour chaque
/// ligne, c'est-à-dire une jointure que cette architecture évite délibérément.
///
/// LA CLÉ FONCTIONNELLE EST (TYPE, CONTEXTE), ET ELLE DÉTERMINE L'ÉCRITURE.
///
/// `UpsertRecommendationCommandHandler` cherche l'existant par utilisateur si le
/// type est `Personalized`, par produit sinon. Écrire deux fois sur la même clé
/// REMPLACE : `Refresh` réécrit la liste entière et le score. Ce n'est pas un
/// ajout, et l'écran doit le dire avant le geste, pas après.
///
/// `Score` EST UN `double` LIBRE. Le domaine ne le borne pas, ne le normalise
/// pas, et ne s'en sert nulle part pour ordonner : aucune lecture ne trie
/// dessus. C'est une métadonnée du moteur qui l'a calculé, pas un rang.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="ContextProductId">
/// Le produit dont c'est la fiche. Nul sur une recommandation personnalisée.
/// </param>
/// <param name="UserId">
/// Le destinataire. Nul sur une recommandation attachée à un produit.
/// </param>
/// <param name="GeneratedAtUtc">
/// L'instant du dernier calcul — posé par `Create` comme par `Refresh`, donc
/// toujours celui de la dernière écriture, jamais celui de la création.
/// </param>
public sealed record RecommandationAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("contextProductId")] Guid? ContextProductId,
    [property: JsonPropertyName("userId")] Guid? UserId,
    [property: JsonPropertyName("recommendedProductIds")] IReadOnlyList<Guid> RecommendedProductIds,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc);
