using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Remboursement DIRECT d'un client, initié par l'admin sur une commande, hors du
/// flux « retour ».
/// </summary>
public sealed class CustomerRefund : AggregateRoot<CustomerRefundId>
{
    private CustomerRefund()
    {
    }

    private CustomerRefund(
        CustomerRefundId id, Guid orderId, Guid buyerId, decimal amount, string currency,
        string reason, string msisdn, string provider, string idempotencyKey)
        : base(id)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        Msisdn = msisdn;
        Provider = provider;
        IdempotencyKey = idempotencyKey;
        Status = CustomerRefundStatus.Processing;
        CreatedAtUtc = DateTime.UtcNow;
        SentToPspAtUtc = CreatedAtUtc;
    }

    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public string Msisdn { get; private set; } = default!;
    public string Provider { get; private set; } = default!;

    /// <summary>
    /// Clé d'idempotence du versement, telle que le client l'a envoyée dans
    /// l'en-tête `Idempotency-Key` (§5).
    /// </summary>
    public string IdempotencyKey { get; private set; } = default!;

    public CustomerRefundStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentToPspAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// En cours de versement au PSP, en attente de confirmation (réconciliation).
    /// </summary>
    public bool IsProcessing => Status == CustomerRefundStatus.Processing;

    /// <summary>`idempotencyKey` est fournie par l'appelant.</summary>
    public static CustomerRefund Create(
        Guid orderId, Guid buyerId, decimal amount, string currency, string reason, string msisdn,
        string provider, string idempotencyKey)
        => new(CustomerRefundId.New(), orderId, buyerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            reason.Trim(), msisdn.Trim(), provider.Trim(), idempotencyKey.Trim());

    /// <summary>
    /// Versement DEMANDÉ au PSP (start accepté) ou issue indéterminée : on garde le
    /// débit plateforme, la réconciliation tranchera.
    /// </summary>
    public void MarkProcessing(string? providerRef, string? note = null)
    {
        Status = CustomerRefundStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = note;
        SentToPspAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>Versement CONFIRMÉ par le PSP (statut « sent »).</summary>
    public void Complete(string? providerRef)
    {
        Status = CustomerRefundStatus.Completed;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Rejet définitif du PSP : le débit plateforme est contre-passé par le
    /// handler.
    /// </summary>
    public void Fail(string reason)
    {
        Status = CustomerRefundStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Total déjà remboursé (non échoué) pour une commande, via ce flux direct.</summary>
public interface ICustomerRefundRepository
{
    Task AddAsync(CustomerRefund refund, CancellationToken cancellationToken = default);

    Task<CustomerRefund?> GetByIdAsync(CustomerRefundId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des remboursements NON échoués (Processing + Completed) d'une
    /// commande.
    /// </summary>
    Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Remboursements clients encore « en cours » (pour la réconciliation).</summary>
    Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(
        int take = 100, CancellationToken cancellationToken = default);
}
