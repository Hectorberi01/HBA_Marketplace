using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Remboursement DIRECT d'un client, initié par l'admin sur une commande, hors du
/// flux « retour ». L'argent part par un payout FedaPay Mobile Money vers le numéro
/// du client (FedaPay ne rembourse pas une transaction via l'API) et le coût est
/// débité du portefeuille plateforme.
///
/// Cycle calqué sur les retraits vendeur : on ne clôture en Completed que sur
/// confirmation du PSP ; sur issue indéterminée, on reste Processing (jamais de
/// contre-passation qui autoriserait un second versement).
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
    /// Clé d'idempotence du versement, telle que le client l'a envoyée dans l'en-tête
    /// `Idempotency-Key` (§5). Unique par commande — voir `CustomerRefundConfiguration`.
    ///
    /// ELLE EST OBLIGATOIRE, ET LE DOMAINE N'EN INVENTE PAS.
    ///
    /// Un versement Mobile Money vers un client ne se rattrape pas : une fois parti,
    /// il est parti. La seule chose qui empêche un second envoi sur un appel HTTP
    /// réessayé est cette clé, et il faut donc qu'elle vienne de l'appelant. Un
    /// repli fabriqué ici (à partir du montant et de la commande, par exemple)
    /// paraîtrait protéger et ferait pire : il interdirait un SECOND remboursement
    /// partiel légitime sur la même commande, tout en laissant passer un rejeu dès
    /// que le montant change d'un franc.
    /// </summary>
    public string IdempotencyKey { get; private set; } = default!;

    public CustomerRefundStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentToPspAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>En cours de versement au PSP, en attente de confirmation (réconciliation).</summary>
    public bool IsProcessing => Status == CustomerRefundStatus.Processing;

    /// <summary>
    /// `idempotencyKey` est fournie par l'appelant. C'est au GESTIONNAIRE de refuser
    /// quand elle est absente : lui seul peut rendre une erreur de validation lisible
    /// au client, là où le domaine ne pourrait que lever. Rien n'est fabriqué ici.
    /// </summary>
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

    /// <summary>Rejet définitif du PSP : le débit plateforme est contre-passé par le handler.</summary>
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

    /// <summary>Somme des remboursements NON échoués (Processing + Completed) d'une commande.</summary>
    Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Remboursements clients encore « en cours » (pour la réconciliation).</summary>
    Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(
        int take = 100, CancellationToken cancellationToken = default);
}
