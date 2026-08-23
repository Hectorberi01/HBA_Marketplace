using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Financial.Billing.Contracts;
using HBA.Financial.Billing.Domain.Invoices;

namespace HBA.Financial.Billing.Application.Invoices;

/// <summary>Récupère une facture par son identifiant.</summary>
public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceSummary>;

/// <summary>Liste les factures d'un vendeur.</summary>
public sealed record ListInvoicesBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<InvoiceSummary>>;

/// <summary>La page des factures, tous vendeurs confondus (administration).</summary>
/// <remarks>
/// TROIS STATUTS SEULEMENT, ET C'EST `Issued` QUI COMPTE.
///
/// `Draft` est une facture en cours de composition — on lui ajoute des lignes ;
/// `Issued` est émise et attend son paiement ; `Paid` est soldée. La seule file
/// qui demande une action est donc `Issued`, et c'est ce que l'écran met en
/// avant. Le filtre reste un paramètre : relire une facture payée est un usage
/// légitime.
/// </remarks>
public sealed record ListInvoicesQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null,
    Guid? SellerId = null) : IQuery<PagedResult<InvoiceSummary>>;

internal static class InvoiceMapper
{
    public static InvoiceSummary ToSummary(Invoice i) => new(
        i.Id.Value, i.SellerId, i.PeriodStartUtc, i.PeriodEndUtc, i.Currency, i.TotalAmount, i.Status.ToString());
}

internal sealed class GetInvoiceQueryHandler : IQueryHandler<GetInvoiceQuery, InvoiceSummary>
{
    private readonly IInvoiceRepository _repository;

    public GetInvoiceQueryHandler(IInvoiceRepository repository) => _repository = repository;

    public async Task<Result<InvoiceSummary>> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoice = await _repository.GetByIdAsync(new InvoiceId(query.InvoiceId), cancellationToken);
        return invoice is null
            ? Error.NotFound("billing.invoice.not_found", "Facture introuvable.")
            : InvoiceMapper.ToSummary(invoice);
    }
}

internal sealed class ListInvoicesQueryHandler : IQueryHandler<ListInvoicesQuery, PagedResult<InvoiceSummary>>
{
    private readonly IInvoiceRepository _repository;

    public ListInvoicesQueryHandler(IInvoiceRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<InvoiceSummary>>> Handle(
        ListInvoicesQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // Un statut illisible est ignoré plutôt que refusé : la liste complète se
        // voit, et le compte par statut rendu avec la page permet de vérifier ce
        // qui a filtré. Même choix que les listes voisines d'identity et de
        // return-refund.
        InvoiceStatus? statut = Enum.TryParse<InvoiceStatus>(query.Status, ignoreCase: true, out var lu)
            ? lu
            : null;

        var (items, total, comptes) = await _repository.ListForAdminAsync(
            page, pageSize, statut, query.SellerId, cancellationToken);

        return new PagedResult<InvoiceSummary>(
            items.Select(InvoiceMapper.ToSummary).ToList(), total, page, pageSize, comptes);
    }
}

internal sealed class ListInvoicesBySellerQueryHandler : IQueryHandler<ListInvoicesBySellerQuery, IReadOnlyList<InvoiceSummary>>
{
    private readonly IInvoiceRepository _repository;

    public ListInvoicesBySellerQueryHandler(IInvoiceRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<InvoiceSummary>>> Handle(ListInvoicesBySellerQuery query, CancellationToken cancellationToken)
    {
        var invoices = await _repository.ListBySellerAsync(query.SellerId, cancellationToken: cancellationToken);
        IReadOnlyList<InvoiceSummary> summaries = invoices.Select(InvoiceMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
