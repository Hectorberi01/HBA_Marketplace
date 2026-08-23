using HBA.Merchants.Domain.Members;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>Une ligne du journal, telle que l'écran d'équipe la montre.</summary>
/// <param name="ActorUserId">
/// Le COMPTE, pas le membre. L'écran le rapproche de la liste des membres qu'il a
/// déjà chargée ; refaire ce rapprochement ici obligerait à joindre deux agrégats
/// pour une colonne d'affichage.
/// </param>
public sealed record AuditEntryView(
    long Id,
    string EntityType,
    string EntityId,
    string Operation,
    Guid? ActorUserId,
    string ActorType,
    string? CorrelationId,
    DateTime OccurredOnUtc);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JOURNAL D'ÉQUIPE — LA SEULE ROUTE QUE `AUDIT_VIEW` GARDE.
///
/// ELLE NE LIT QUE LE JOURNAL DE seller-service, ET C'EST ASSUMÉ.
///
/// CE PARAGRAPHE AFFIRMAIT QUATRE JOURNAUX. IL Y EN AVAIT UN.
///
/// Il annonçait « un journal par schéma : catalog, inventory, order et celui-ci »,
/// puis « le plus utile des quatre ». Ni `CatalogDbContext`, ni `InventoryDbContext`,
/// ni `OrderingDbContext` n'ont jamais surchargé `KeepsAuditTrail` : ils héritent de
/// `false`, et aucun des trois schémas n'a de table `audit_entries` — ni migration,
/// ni entrée de snapshot.
///
/// Le mensonge n'est pas décoratif. Il répondait par avance à la question « qui a
/// modifié ce prix ? » en promettant « ce sera une route de catalog-service », donc
/// une simple route à écrire sur une table qui existe. La table n'existait pas. Un
/// lecteur pressé aurait chiffré le lot à une journée et découvert la migration
/// manquante en cours de route.
///
/// C'est le même défaut que les gardes citées et jamais écrites — `ConcurrencyExceptionHandler`,
/// `check-config-and-guards.py` — sauf qu'aucun contrôle ne lit les commentaires.
///
/// L'ÉTAT RÉEL, AU LOT 7.1.
///
/// Le journal est actif sur `sellers`, `food_ordering` et `return_refund`, et sur
/// les contextes que ce lot vient d'allumer. Chacun a sa table dans SON schéma :
/// c'est délibéré, une table partagée ferait dépendre tous les services d'un même
/// verrou d'écriture.
///
/// Cette route-ci ne lit que `sellers`, et c'est assumé : réunir plusieurs journaux
/// demanderait autant d'appels gRPC paginés, dont les curseurs ne s'alignent pas,
/// pour une page qu'aucune clé commune ne permet de trier de façon stable.
///
/// Celui-ci reste le plus utile pour un VENDEUR : il retient qui a invité qui, qui
/// a changé les rôles de qui, qui a repointé le compte de reversement. Ce sont les
/// questions qu'on pose après un incident dans une équipe.
///
/// ELLE NE REND QUE LES GESTES DES MEMBRES DE CE VENDEUR.
///
/// Les lignes d'audit portent un `ActorUserId`, pas un `SellerId` : le journal est
/// une table d'infrastructure, elle ne connaît pas le métier. Le filtre est donc
/// construit ici, depuis la liste des membres. Conséquence à connaître : un geste
/// de la PLATEFORME sur ce dossier — suspension, validation KYB — porte un acteur
/// administrateur qui n'est pas membre, et n'apparaît donc PAS. C'est le bon
/// comportement : ce journal répond de l'équipe du vendeur, pas de la modération,
/// et mêler les deux laisserait croire au vendeur qu'il voit tout.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="MemberUserId">
/// Filtre facultatif sur un membre précis — « qu'a fait Sophie ». C'est le compte
/// et non l'identifiant de membre : c'est ce que la table porte, et traduire ici
/// ferait échouer silencieusement la recherche sur un membre révoqué.
/// </param>
public sealed record ListAuditEntriesQuery(
    Guid SellerId,
    Guid ActorUserId,
    Guid? MemberUserId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize) : IQuery<PagedResult<AuditEntryView>>;

/// <summary>
/// Lecture du journal d'audit du module. L'interface vit ici plutôt qu'au domaine :
/// <c>AuditEntry</c> est une entité d'INFRASTRUCTURE, pas un agrégat métier, et le
/// domaine n'a rien à en savoir.
/// </summary>
public interface IAuditTrailReader
{
    Task<PagedResult<AuditEntryView>> ListAsync(
        IReadOnlyCollection<Guid> actorUserIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

internal sealed class AuditQueryHandler : IQueryHandler<ListAuditEntriesQuery, PagedResult<AuditEntryView>>
{
    private readonly ISellerMemberRepository _members;
    private readonly IAuditTrailReader _journal;
    private readonly MemberAccessResolver _acces;

    public AuditQueryHandler(
        ISellerMemberRepository members, IAuditTrailReader journal, MemberAccessResolver acces)
    {
        _members = members;
        _journal = journal;
        _acces = acces;
    }

    public async Task<Result<PagedResult<AuditEntryView>>> Handle(
        ListAuditEntriesQuery query, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(query.SellerId, query.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<PagedResult<AuditEntryView>>(acteur.Error);
        }

        var habilitation = acteur.Value.Ensure(MerchantPermission.AuditView);
        if (habilitation.IsFailure)
        {
            return Result.Failure<PagedResult<AuditEntryView>>(habilitation.Error);
        }

        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // TOUS LES MEMBRES, RÉVOQUÉS COMPRIS.
        //
        // `ListBySellerAsync` ne filtre pas sur le statut, et c'est exactement ce
        // qu'il faut : le geste qu'on cherche après un incident est souvent celui
        // d'un compte qu'on a révoqué DEPUIS. Ne journaliser que les membres actifs
        // effacerait du journal la personne qu'on soupçonne, au moment précis où
        // l'on cherche à savoir ce qu'elle a fait.
        var membres = await _members.ListBySellerAsync(query.SellerId, cancellationToken);

        var comptes = query.MemberUserId is { } cible

            // LE FILTRE PAR MEMBRE RESTE BORNÉ À L'ÉQUIPE.
            //
            // Passer `cible` directement rendrait cette route lisible sur N'IMPORTE
            // QUEL compte de la plateforme : il suffirait d'y mettre l'identifiant
            // d'un concurrent. On intersecte donc avec l'équipe, et un compte
            // étranger produit une liste vide plutôt qu'un refus — le distinguer
            // dirait à qui tâtonne lesquels sont membres d'un vendeur donné.
            ? membres.Select(m => m.UserId).Where(id => id == cible).ToArray()
            : membres.Select(m => m.UserId).ToArray();

        if (comptes.Length == 0)
        {
            return PagedResult<AuditEntryView>.Empty(page, pageSize);
        }

        return await _journal.ListAsync(
            comptes, query.FromUtc, query.ToUtc, page, pageSize, cancellationToken);
    }
}
