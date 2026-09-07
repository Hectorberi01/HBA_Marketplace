namespace HBA.Financial.Billing.Domain.Invoices;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);

    Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Une page de factures, tous vendeurs confondus, pour l'administration.</summary>
    Task<(IReadOnlyList<Invoice> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(
            int page, int pageSize, InvoiceStatus? status, Guid? sellerId,
            CancellationToken cancellationToken = default);
}
