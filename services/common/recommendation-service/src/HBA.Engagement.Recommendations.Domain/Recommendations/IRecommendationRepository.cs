namespace HBA.Engagement.Recommendations.Domain.Recommendations;

public interface IRecommendationRepository
{
    Task AddAsync(Recommendation recommendation, CancellationToken cancellationToken = default);

    /// <summary>Recommandation existante pour une clé (contexte produit) — pour upsert.</summary>
    Task<Recommendation?> GetByProductAsync(RecommendationType type, Guid contextProductId, CancellationToken cancellationToken = default);

    /// <summary>Recommandation personnalisée existante d'un utilisateur — pour upsert.</summary>
    Task<Recommendation?> GetByUserAsync(RecommendationType type, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Une page de recommandations, tous contextes confondus (administration).</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES TROIS SEULES LECTURES ÉTAIENT ADRESSÉES, DONC AVEUGLES.
    ///
    /// `GetByProductAsync` exige un produit, `GetByUserAsync` exige un
    /// utilisateur. Rien ne répondait à « qu'est-ce qui est mis en avant en ce
    /// moment » — alors que l'écriture, elle, existe et persiste. On pouvait
    /// écrire la page d'accueil sans jamais la relire.
    ///
    /// LE COMPTE PAR TYPE EST CALCULÉ AVANT LE FILTRE, comme pour la modération
    /// des avis : les onglets gardent leurs nombres quand on filtre sur l'un
    /// d'eux.
    ///
    /// CE QUE CETTE MÉTHODE NE FAIT PAS : elle ne résout aucun nom de produit.
    /// Une recommandation ne porte que des identifiants, et le service n'a
    /// aucun accès au catalogue. L'écran affiche donc des identifiants — dire
    /// autre chose demanderait un appel croisé que ce read model n'a pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<(IReadOnlyList<Recommendation> Items, int Total, IReadOnlyDictionary<string, int> TypeCounts)>
        ListAsync(int page, int pageSize, RecommendationType? type, CancellationToken cancellationToken = default);
}
