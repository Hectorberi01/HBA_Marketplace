using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// Service interne mutualisant les mouvements de portefeuille (vendeur et
/// plateforme) déclenchés par les events de commande, et l'écriture des lignes au
/// grand livre.
/// </summary>
/// <summary>
/// Une OPÉRATION comptable en cours d'écriture : les mouvements qui, ensemble,
/// forment un seul geste, et dont on vérifiera qu'ils s'équilibrent (§10.13).
/// </summary>
public sealed class OperationComptable
{
    private readonly List<WalletTransaction> _ecritures = new();

    internal OperationComptable(Guid id) => Id = id;

    /// <summary>L'identifiant partagé par toutes les écritures de l'opération.</summary>
    public Guid Id { get; }

    internal IReadOnlyCollection<WalletTransaction> Ecritures => _ecritures;

    internal void Inscrire(WalletTransaction ecriture) => _ecritures.Add(ecriture);
}

public sealed class WalletMutations
{
    private readonly ISellerWalletRepository _sellerWallets;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly ICustomerWalletRepository _customerWallets;
    private readonly IWalletTransactionRepository _ledger;

    private readonly Dictionary<Guid, SellerWallet> _sellerCache = new();
    private readonly Dictionary<Guid, CustomerWallet> _customerCache = new();
    private PlatformWallet? _platform;

    public WalletMutations(
        ISellerWalletRepository sellerWallets,
        IPlatformWalletRepository platformWallets,
        ICustomerWalletRepository customerWallets,
        IWalletTransactionRepository ledger)
    {
        _sellerWallets = sellerWallets;
        _platformWallets = platformWallets;
        _customerWallets = customerWallets;
        _ledger = ledger;
    }

    /// <summary>Ouvre une opération comptable.</summary>
    public OperationComptable Ouvrir() => new(WalletLedger.NewTransactionId());

    /// <summary>
    /// Inscrit la contrepartie du monde extérieur : l'argent qui entre depuis
    /// l'acheteur, ou qui sort vers l'opérateur.
    /// </summary>
    public void ContrepartieExterne(
        OperationComptable operation, WalletDirection direction, decimal amount, string currency,
        string reason, string referenceType, Guid referenceId)
    {
        if (amount <= 0m)
        {
            return;
        }

        operation.Inscrire(WalletTransaction.ForExternal(
            direction, amount, currency, reason, referenceType, referenceId, operation.Id));
    }

    /// <summary>
    /// Vérifie l'invariant du §10.13 et, s'il tient, verse les écritures au grand
    /// livre.
    /// </summary>
    public async Task<Result> CloreAsync(OperationComptable operation, CancellationToken ct)
    {
        var equilibre = WalletLedger.EnsureBalanced(operation.Ecritures);
        if (equilibre.IsFailure)
        {
            return equilibre;
        }

        foreach (var ecriture in operation.Ecritures)
        {
            await _ledger.AddAsync(ecriture, ct);
        }

        return Result.Success();
    }

    /// <summary>
    /// Verse une écriture : dans l'opération si elle est ouverte, au grand livre
    /// sinon.
    /// </summary>
    private async Task InscrireAsync(WalletTransaction ecriture, OperationComptable? operation, CancellationToken ct)
    {
        if (operation is null)
        {
            await _ledger.AddAsync(ecriture, ct);
            return;
        }

        operation.Inscrire(ecriture);
    }

    /// <summary>Crédite le solde à venir du vendeur (gain net d'une commande confirmée).</summary>
    public async Task CreditSellerPendingAsync(
        Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateSellerAsync(sellerId, currency, ct);
        wallet.CreditPending(netAmount);
        await InscrireAsync(
            WalletTransaction.ForSeller(
                sellerId, WalletAccount.Pending, WalletDirection.Credit, netAmount, currency,
                "order_confirmed", "order", orderId, operation?.Id, wallet.PendingBalance),
            operation, ct);
    }

    /// <summary>Déplace un montant du solde à venir vers le solde principal (livraison).</summary>
    public async Task ReleaseSellerAsync(
        Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await _sellerWallets.GetBySellerAsync(sellerId, ct);
        if (wallet is null)
        {
            return;
        }

        _sellerCache[sellerId] = wallet;
        wallet.ReleaseToAvailable(netAmount);

        // IL MANQUAIT LA MOITIÉ DE CE MOUVEMENT (ISSUE-051).
        var mouvement = operation?.Id ?? WalletLedger.NewTransactionId();

        await InscrireAsync(
            WalletTransaction.ForSeller(
                sellerId, WalletAccount.Pending, WalletDirection.Debit, netAmount, currency,
                "delivery_release", "order", orderId, mouvement, wallet.PendingBalance),
            operation, ct);

        await InscrireAsync(
            WalletTransaction.ForSeller(
                sellerId, WalletAccount.Available, WalletDirection.Credit, netAmount, currency,
                "delivery_release", "order", orderId, mouvement, wallet.AvailableBalance),
            operation, ct);
    }

    /// <summary>Crédite le solde commission de la plateforme.</summary>
    public async Task CreditPlatformCommissionAsync(
        decimal amount, string currency, Guid orderId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditCommission(amount);
        await InscrireAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Commission, WalletDirection.Credit, amount, currency,
                "commission", "order", orderId, operation?.Id),
            operation, ct);
    }

    /// <summary>Crédite le solde frais provider de la plateforme.</summary>
    public async Task CreditPlatformProviderFeeAsync(
        decimal amount, string currency, Guid orderId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditProviderFee(amount);
        await InscrireAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Provider, WalletDirection.Credit, amount, currency,
                "provider_fee", "order", orderId, operation?.Id),
            operation, ct);
    }

    /// <summary>Crédite le solde frais de livraison de la plateforme.</summary>
    public async Task CreditPlatformShippingAsync(
        decimal amount, string currency, Guid orderId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditShipping(amount);
        await InscrireAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Shipping, WalletDirection.Credit, amount, currency,
                "shipping_fee", "order", orderId, operation?.Id),
            operation, ct);
    }

    /// <summary>Sort du solde livraison : part versée au livreur, ou course remboursée.</summary>
    public async Task DebitPlatformShippingAsync(
        decimal amount, string currency, string reason, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.DebitShipping(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Shipping, WalletDirection.Debit, amount, currency, reason, referenceType, referenceId), ct);
    }

    /// <summary>CONTRE-PASSATION : reprend au vendeur le gain d'une vente remboursée.</summary>
    /// <summary>LA RÉFÉRENCE EST LE REMBOURSEMENT, PAS LA COMMANDE.</summary>
    public const string RefundReferenceType = "refund";

    public async Task<bool> RefundAlreadyReversedAsync(Guid returnRequestId, CancellationToken ct)
        => await _ledger.ExistsForReferenceAsync(RefundReferenceType, returnRequestId, ct);

    public async Task DebitSellerForRefundAsync(
        Guid sellerId, decimal netAmount, string currency, Guid returnRequestId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateSellerAsync(sellerId, currency, ct);
        var (fromPending, fromAvailable) = wallet.DebitForRefund(netAmount);

        // UN SEUL IDENTIFIANT D'OPÉRATION POUR LES DEUX ÉCRITURES.
        var operationId = operation?.Id ?? WalletLedger.NewTransactionId();

        if (fromPending > 0m)
        {
            await InscrireAsync(
                WalletTransaction.ForSeller(
                    sellerId, WalletAccount.Pending, WalletDirection.Debit, fromPending, currency,
                    "refund_reversal", RefundReferenceType, returnRequestId,
                    operationId, wallet.PendingBalance),
                operation, ct);
        }

        if (fromAvailable > 0m)
        {
            await InscrireAsync(
                WalletTransaction.ForSeller(
                    sellerId, WalletAccount.Available, WalletDirection.Debit, fromAvailable, currency,
                    "refund_reversal", RefundReferenceType, returnRequestId,
                    operationId, wallet.AvailableBalance),
                operation, ct);
        }
    }

    /// <summary>Restitue la commission de la plateforme sur une vente remboursée.</summary>
    public async Task DebitPlatformCommissionAsync(
        decimal amount, string currency, Guid returnRequestId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.DebitCommission(amount);
        await InscrireAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Commission, WalletDirection.Debit, amount, currency,
                "refund_reversal", RefundReferenceType, returnRequestId, operation?.Id),
            operation, ct);
    }

    /// <summary>Restitue les frais provider de la plateforme sur une vente remboursée.</summary>
    public async Task DebitPlatformProviderFeeAsync(
        decimal amount, string currency, Guid returnRequestId, CancellationToken ct,
        OperationComptable? operation = null)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.DebitProviderFee(amount);
        await InscrireAsync(
            WalletTransaction.ForPlatform(
                WalletAccount.Provider, WalletDirection.Debit, amount, currency,
                "refund_reversal", RefundReferenceType, returnRequestId, operation?.Id),
            operation, ct);
    }

    /// <summary>
    /// Comptabilise un remboursement DIRECT versé à un client (coût plateforme) :
    /// crédite le solde « refunds » du portefeuille plateforme et trace l'écriture.
    /// </summary>
    public async Task AccrueCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.AccrueRefund(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Refunds, WalletDirection.Credit, amount, currency, "customer_refund", "customer_refund", refundId), ct);
    }

    /// <summary>
    /// Contre-passe un remboursement client comptabilisé dont le payout a échoué.
    /// </summary>
    public async Task ReverseCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.ReverseRefund(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Refunds, WalletDirection.Debit, amount, currency, "customer_refund_reversal", "customer_refund", refundId), ct);
    }

    // LE PORTEFEUILLE CLIENT (D33).

    /// <summary>Type de référence des crédits de remboursement client au grand livre.</summary>
    public const string CustomerRefundCreditReferenceType = "customer_refund_credit";

    /// <summary>
    /// Type de référence des mouvements liés à une demande de virement client
    /// (retenue, puis restitution en cas de refus).
    /// </summary>
    public const string CustomerWithdrawalReferenceType = "customer_withdrawal";

    /// <summary>
    /// L'écriture de crédit déjà passée pour cette référence d'idempotence, s'il y
    /// en a une.
    /// </summary>
    public Task<WalletTransaction?> FindCustomerRefundCreditAsync(Guid reference, CancellationToken ct)
        => _ledger.FindByReferenceAsync(CustomerRefundCreditReferenceType, reference, ct);

    /// <summary>
    /// Rend un montant au client sur son portefeuille et l'inscrit au grand livre.
    /// </summary>
    public async Task<Result<WalletTransaction>> CreditCustomerRefundAsync(
        Guid customerId, decimal amount, string currency, string reason, Guid reference, CancellationToken ct)
    {
        var wallet = await GetOrCreateCustomerAsync(customerId, currency, ct);
        var devise = NormalizeCurrency(currency);

        if (!string.Equals(wallet.Currency, devise, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<WalletTransaction>(Error.Conflict(
                "wallet.customer.currency_mismatch",
                $"Le portefeuille de ce client est en {wallet.Currency} ; un remboursement en {devise} ne peut pas y être crédité."));
        }

        var credit = wallet.CreditRefund(amount);
        if (credit.IsFailure)
        {
            return Result.Failure<WalletTransaction>(credit.Error);
        }

        // Le motif métier accompagne l'écriture (« refund », « order_cancelled »…
        // selon l'appelant) ; il est tronqué à la borne de la colonne `Reason` (50)
        // plutôt que de faire échouer un remboursement sur une chaîne trop longue —
        // l'argent rendu compte plus que le libellé, et la référence reste intacte
        // pour retrouver le dossier d'origine.
        var motif = string.IsNullOrWhiteSpace(reason) ? "customer_refund_credit" : reason.Trim();
        if (motif.Length > 50)
        {
            motif = motif[..50];
        }

        var ecriture = WalletTransaction.ForCustomer(
            customerId, WalletDirection.Credit, amount, wallet.Currency, motif,
            CustomerRefundCreditReferenceType, reference,
            WalletLedger.NewTransactionId(), wallet.AvailableBalance);

        await _ledger.AddAsync(ecriture, ct);
        return ecriture;
    }

    /// <summary>Le portefeuille d'un client, créé s'il n'existe pas.</summary>
    public async Task<CustomerWallet> GetOrCreateCustomerAsync(Guid customerId, string currency, CancellationToken ct)
    {
        if (_customerCache.TryGetValue(customerId, out var cached))
        {
            return cached;
        }

        var wallet = await _customerWallets.GetByCustomerAsync(customerId, ct);
        if (wallet is null)
        {
            wallet = CustomerWallet.Create(customerId, currency);
            await _customerWallets.AddAsync(wallet, ct);
        }

        _customerCache[customerId] = wallet;
        return wallet;
    }

    private static string NormalizeCurrency(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();

    private async Task<SellerWallet> GetOrCreateSellerAsync(Guid sellerId, string currency, CancellationToken ct)
    {
        if (_sellerCache.TryGetValue(sellerId, out var cached))
        {
            return cached;
        }

        var wallet = await _sellerWallets.GetBySellerAsync(sellerId, ct);
        if (wallet is null)
        {
            wallet = SellerWallet.Create(sellerId, currency);
            await _sellerWallets.AddAsync(wallet, ct);
        }

        _sellerCache[sellerId] = wallet;
        return wallet;
    }

    private async Task<PlatformWallet> GetOrCreatePlatformAsync(string currency, CancellationToken ct)
    {
        if (_platform is not null)
        {
            return _platform;
        }

        var wallet = await _platformWallets.GetAsync(ct);
        if (wallet is null)
        {
            wallet = PlatformWallet.Create(currency);
            await _platformWallets.AddAsync(wallet, ct);
        }

        _platform = wallet;
        return wallet;
    }
}
