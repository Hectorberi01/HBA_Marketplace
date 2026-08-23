using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Queries.ListSellers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE LIGNE DE LA FILE D'ADMINISTRATION — ET RIEN DE PLUS.
///
/// CETTE LISTE RENDAIT LE `SellerSummary` COMPLET DE CHAQUE VENDEUR.
///
/// Donc, en une requête : le NUMÉRO MOBILE MONEY de tous les vendeurs de la
/// plateforme, leur RCCM, leur IFU, le téléphone de chaque gérant, et les
/// références de toutes leurs PIÈCES D'IDENTITÉ. Une console d'administration a le
/// droit d'afficher ces choses — sur la fiche qu'un administrateur ouvre
/// délibérément, pas dans un listing qu'un écran charge au réveil.
///
/// Ce qu'un modérateur cherche dans cette file tient dans les colonnes ci-dessous :
/// qui, où en est son dossier, depuis quand. Le reste est à un clic, sur
/// `GET /merchants/{id}`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="KybDocumentCount">Combien de pièces attendent d'être ouvertes — le compte, pas les références.</param>
/// <param name="KybRejectionReason">Le motif du dernier refus, pour que le modérateur relise ce qu'il a écrit.</param>
/// <param name="CreatedOnUtc">C'est l'ancienneté qui ordonne une file d'attente.</param>
public sealed record SellerListItem(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string Status,
    string KybStatus,
    int KybDocumentCount,
    string? KybRejectionReason,
    DateTime CreatedOnUtc);

/// <summary>
/// La file d'administration des vendeurs, paginée et filtrable.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE RENDAIT TOUS LES VENDEURS, SANS PAGINATION NI FILTRE.
///
/// `ISellerRepository.ListAsync` chargeait la table entière avec
/// `.Include(KybDocuments)`, et c'était l'unique entrée de la file de validation
/// KYB. À mille vendeurs, la réponse se compte en mégaoctets — et le modérateur y
/// cherche à l'œil les quatre dossiers en revue.
///
/// `PagedResult` et `ApiResults.Page` existaient depuis le lot 6 du catalogue ;
/// cette route était la seule liste du service à ne pas s'en servir.
///
/// LES FACETTES NE SONT PAS UN ORNEMENT.
///
/// Le comptage par `KybStatus` est ce qui permet à la console d'afficher « 4 en
/// revue » AVANT que le modérateur n'ait filtré. Sans lui, il faudrait parcourir
/// toutes les pages pour savoir s'il y a du travail — ce qui ramènerait la lecture
/// intégrale qu'on vient de retirer.
///
/// ET ELLES SE COMPTENT SUR LA RECHERCHE, PAS SUR LA PAGE.
///
/// Compter la page rendrait « 3 en revue » sur une file qui en contient quarante.
/// Compter tout, en ignorant la recherche, afficherait un total que le filtre ne
/// peut jamais atteindre. Voir le dépôt : les facettes suivent `search`, pas
/// `kybStatus` — sinon la facette du statut sélectionné serait la seule non nulle.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Search">Sur le nom de boutique. Insensible à la casse, sous-chaîne.</param>
/// <param name="KybStatus">`NotStarted`, `InReview`, `Verified`, `Rejected`. La colonne du modérateur.</param>
/// <param name="Status">`Pending`, `Active`, `Suspended`, `Closed`, `PendingReactivation`.</param>
public sealed record ListSellersQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? KybStatus = null,
    string? Status = null) : IQuery<PagedResult<SellerListItem>>;

internal sealed class ListSellersQueryHandler
    : IQueryHandler<ListSellersQuery, PagedResult<SellerListItem>>
{
    private readonly ISellerRepository _sellerRepository;

    public ListSellersQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<PagedResult<SellerListItem>>> Handle(
        ListSellersQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // UN FILTRE ILLISIBLE EST IGNORÉ, PAS REFUSÉ.
        //
        // La console envoie ces valeurs depuis ses propres listes déroulantes : un
        // 400 sur une faute de frappe transformerait une colonne mal nommée en
        // écran blanc. Un filtre inconnu ne restreint rien — le modérateur voit
        // toute la file, et comprend immédiatement que son filtre n'a pas pris.
        KybStatus? kyb = Enum.TryParse<KybStatus>(query.KybStatus, ignoreCase: true, out var k) ? k : null;
        SellerStatus? statut = Enum.TryParse<SellerStatus>(query.Status, ignoreCase: true, out var s) ? s : null;

        var (sellers, total, facettes) = await _sellerRepository.ListPagedAsync(
            page, pageSize, query.Search, kyb, statut, cancellationToken);

        var lignes = sellers
            .Select(v => new SellerListItem(
                v.Id.Value,
                v.UserId,
                v.ShopName,
                v.LogoUrl,
                v.Status.ToString(),
                v.KybStatus.ToString(),
                v.KybDocuments.Count,
                v.KybRejectionReason,
                v.CreatedOnUtc))
            .ToList();

        return Result.Success(new PagedResult<SellerListItem>(lignes, total, page, pageSize, facettes));
    }
}
