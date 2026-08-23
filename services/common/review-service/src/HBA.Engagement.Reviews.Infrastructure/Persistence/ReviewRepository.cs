using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Infrastructure.Persistence;

internal sealed class ReviewRepository : IReviewRepository
{
    private readonly ReviewsDbContext _dbContext;

    public ReviewRepository(ReviewsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Review review, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AddAsync(review, cancellationToken);

    public async Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> ListByProductAsync(
        Guid productId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// CELLE-CI NE FILTRE PAS SUR `Published` — c'est le carnet du VENDEUR, qui
    /// doit voir ses avis en modération. Ne pas confondre avec l'agrégation de la
    /// note, qui ne compte que le publié.
    /// </remarks>
    public async Task<IReadOnlyList<Review>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// PAS DE FILTRE SUR `Published` ICI, CONTRAIREMENT À `ListByProductAsync`.
    ///
    /// C'est la file de modération : elle existe précisément pour montrer ce qui
    /// n'est PAS publié. Reprendre le filtre de la vitrine la viderait de tout ce
    /// qu'elle doit servir.
    ///
    /// L'index `(SellerId, Status)` posé pour la note vendeur ne sert pas cette
    /// requête, qui n'a pas de vendeur. Sur une table d'avis qui grossit, un index
    /// sur `(Status, CreatedAtUtc)` deviendra nécessaire — ce n'est pas dans ce
    /// lot, et le dire ici vaut mieux que de le découvrir en production.
    /// </remarks>
    public async Task<(IReadOnlyList<Review> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForModerationAsync(int page, int pageSize, ReviewStatus? status, CancellationToken cancellationToken = default)
    {
        var nu = _dbContext.Reviews.AsNoTracking();

        var comptes = await nu
            .GroupBy(r => r.Status)
            .Select(g => new { Statut = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtre = status is { } etat ? nu.Where(r => r.Status == etat) : nu;

        var total = await filtre.CountAsync(cancellationToken);

        // Les plus anciens d'abord : une file de modération se traite par le bas.
        // C'est l'inverse des listes de vitrine, qui montrent le plus récent.
        var elements = await filtre
            .OrderBy(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (elements, total, comptes.ToDictionary(x => x.Statut.ToString(), x => x.Nombre));
    }

    public async Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AnyAsync(
            r => r.BuyerId == buyerId && r.ProductId == productId && r.OrderId == orderId, cancellationToken);

    public async Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var (moyenne, total) = await AgregerAsync(
            r => r.ProductId == productId, cancellationToken);

        return new ProductRating(productId, moyenne, total);
    }

    public async Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var (moyenne, total) = await AgregerAsync(
            r => r.SellerId == sellerId, cancellationToken);

        return new SellerRating(sellerId, moyenne, total);
    }

    /// <summary>
    /// La moyenne et le compte des avis publiés, calculés SUR CINQ LIGNES AU PLUS.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES DEUX MOYENNES CHARGEAIENT TOUS LES AVIS POUR EN FAIRE UNE DIVISION (§12).
    ///
    /// Le commentaire d'alors disait « on agrège en mémoire pour éviter toute
    /// traduction SQL fragile du VO » et « volumétrie modérée ». Le premier point
    /// était juste — le second, non : la note d'un vendeur porte sur TOUS ses
    /// produits, et elle est recalculée à CHAQUE avis publié ou rejeté
    /// (`PublierNoteVendeurAsync`). Un vendeur à succès relisait donc l'intégralité
    /// de ses avis à chaque nouvel avis reçu. Le coût croissait avec sa réussite.
    ///
    /// ET LA NOTE VENDEUR N'EST PAS MISE EN CACHE, contrairement à la note
    /// produit — elle était donc la plus exposée des deux.
    ///
    /// POURQUOI PAS UN `AVG()`, QUE L'AUDIT DEMANDAIT.
    ///
    /// `Rating` est un objet-valeur adossé à un CONVERTISSEUR
    /// (`HasConversion(rating => rating.Value, …)`). EF sait traduire la propriété
    /// elle-même ; il ne sait PAS traduire `r.Rating.Value`, parce que le
    /// convertisseur lui est opaque. Un `Sum(r => r.Rating.Value)` échouerait à la
    /// traduction — c'est exactement la « traduction fragile » que l'ancien
    /// commentaire redoutait, et il avait raison de la redouter.
    ///
    /// D'OÙ LE `GroupBy` SUR LA NOTE, QUI EST TRADUISIBLE ET BORNÉ PAR NATURE.
    ///
    /// `GROUP BY "Rating"` rend une ligne par note distincte. `Rating` va de 1 à 5 :
    /// **la requête ne peut pas rendre plus de cinq lignes**, quel que soit le
    /// nombre d'avis. Ce n'est pas une borne qu'on impose, c'est une borne que le
    /// domaine porte — donc une qu'on ne peut pas oublier de maintenir.
    ///
    /// La moyenne se reconstitue exactement : Σ(note × compte) ÷ Σ(compte).
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task<(double Moyenne, int Total)> AgregerAsync(
        Expression<Func<Review, bool>> perimetre, CancellationToken cancellationToken)
    {
        var repartition = await _dbContext.Reviews
            .AsNoTracking()
            .Where(perimetre)
            .Where(r => r.Status == ReviewStatus.Published)
            .GroupBy(r => r.Rating)
            .Select(g => new { Note = g.Key, Compte = g.Count() })
            .ToListAsync(cancellationToken);

        var total = repartition.Sum(x => x.Compte);

        if (total == 0)
        {
            return (0d, 0);
        }

        var somme = repartition.Sum(x => (long)x.Note.Value * x.Compte);

        return (Math.Round((double)somme / total, 2), total);
    }
}
