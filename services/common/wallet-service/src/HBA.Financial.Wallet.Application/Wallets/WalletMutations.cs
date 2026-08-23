using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// Service interne mutualisant les mouvements de portefeuille (vendeur et
/// plateforme) déclenchés par les events de commande, et l'écriture des lignes
/// au grand livre. NE persiste PAS : l'Unit of Work du handler appelant commite.
///
/// Un cache par requête (service « scoped ») évite de recréer deux fois un même
/// portefeuille non encore persisté (ex. commission puis frais de livraison sur
/// la même commande) et l'insertion en double qui en résulterait.
/// </summary>
/// <summary>
/// Une OPÉRATION comptable en cours d'écriture : les mouvements qui, ensemble,
/// forment un seul geste, et dont on vérifiera qu'ils s'équilibrent (§10.13).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉCRITURES SONT RETENUES, PAS ÉCRITES AU FIL DE L'EAU.
///
/// C'est ce qui donne son sens à `CloreAsync` : tant que l'opération n'est pas
/// équilibrée, RIEN n'entre au grand livre. Les écrire au fur et à mesure aurait
/// laissé un grand livre à moitié rempli le jour où l'invariant refuse — c'est-à-
/// dire précisément l'état qu'il existe pour empêcher.
///
/// CE QU'ELLE NE PROTÈGE PAS : les SOLDES, eux, sont mutés immédiatement par
/// les méthodes de `WalletMutations`. Ils ne sont pas persistés pour autant : le
/// gestionnaire appelant n'appelle pas `SaveChangesAsync` quand l'opération est
/// refusée, et la portée meurt avec eux. La garantie tient à cette discipline
/// d'appel, pas à cette classe.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Ouvre une opération comptable. Les mouvements qui reçoivent l'objet rendu
    /// partagent son identifiant et sont retenus jusqu'à <see cref="CloreAsync"/>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// FACULTATIF, ET C'EST UN CHOIX QU'IL FAUT ASSUMER.
    ///
    /// Les méthodes ci-dessous acceptent une opération en dernier paramètre,
    /// optionnel. Sans elle, chaque écriture reste sa propre opération et part
    /// directement au grand livre — le comportement d'avant, à l'identique.
    ///
    /// Rendre le paramètre obligatoire aurait forcé, d'un seul geste et sans
    /// compilateur pour le vérifier, la conversion des quinze sites d'écriture du
    /// module. Le prix de ce choix est réel : un NOUVEAU chemin d'écriture peut
    /// naître sans contrepartie, et l'invariant ne le verra pas. Il est nommé
    /// ici plutôt que découvert plus tard.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public OperationComptable Ouvrir() => new(WalletLedger.NewTransactionId());

    /// <summary>
    /// Inscrit la contrepartie du monde extérieur : l'argent qui entre depuis
    /// l'acheteur, ou qui sort vers l'opérateur. Voir
    /// <see cref="WalletOwnerType.External"/>.
    /// </summary>
    /// <remarks>
    /// LE MONTANT DOIT VENIR D'AILLEURS QUE DE LA SOMME DES AUTRES ÉCRITURES.
    ///
    /// Le passer en recopiant ce qu'on vient de créditer rendrait l'invariant
    /// tautologique : il ne pourrait plus jamais échouer. Aux deux sites
    /// convertis, il vient du BRUT — encaissé ou rendu —, calculé indépendamment
    /// de sa répartition. C'est cette indépendance qui fait le contrôle.
    /// </remarks>
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
    /// livre. En cas d'échec, RIEN n'est versé et l'appelant doit renoncer à son
    /// `SaveChangesAsync`.
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
    /// sinon. C'est le seul point par lequel les mouvements de ce service écrivent.
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

        // ═════════════════════════════════════════════════════════════════════
        // IL MANQUAIT LA MOITIÉ DE CE MOUVEMENT (ISSUE-051).
        //
        // `ReleaseToAvailable` DÉPLACE : il retire de l'en-cours et ajoute au
        // disponible. Le grand livre n'enregistrait que l'arrivée. Conséquence
        // mesurable : la somme des écritures du compte « en-cours » ne redescend
        // jamais, et ne peut donc plus être rapprochée du solde stocké — le seul
        // contrôle qui aurait révélé une dérive était rendu impossible par le
        // grand livre lui-même.
        //
        // Ce n'était pas un choix : rien n'en parle nulle part. Les deux écritures
        // partagent un identifiant d'opération, parce que c'est UN geste.
        // ═════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Sort du solde livraison : part versée au livreur, ou course remboursée.
    ///
    /// SANS LUI, LE SOLDE « FRAIS DE LIVRAISON » N'EST PAS UNE MARGE.
    ///
    /// Il n'enregistrait que les encaissements. La part du coursier — l'essentiel
    /// du montant — n'en sortait jamais, et une commande remboursée le laissait
    /// intact. Voir `PlatformWallet.DebitShipping`.
    /// </summary>
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

    /// <summary>
    /// CONTRE-PASSATION : reprend au vendeur le gain d'une vente remboursée.
    ///
    /// Deux écritures distinctes au grand livre (solde à venir, puis solde principal),
    /// et non une seule globale : c'est la seule façon de pouvoir expliquer plus tard,
    /// ligne à ligne, POURQUOI le solde d'un vendeur a bougé. Un débit unique et opaque
    /// se solde toujours par une discussion qu'on ne peut pas gagner.
    ///
    /// Le portefeuille est CRÉÉ s'il n'existe pas : un vendeur peut avoir été remboursé
    /// avant d'avoir le moindre solde. Refuser le débit dans ce cas ferait disparaître
    /// la dette au lieu de l'enregistrer.
    /// </summary>
    /// <summary>
    /// LA RÉFÉRENCE EST LE REMBOURSEMENT, PAS LA COMMANDE.
    ///
    /// Ces écritures portaient `("order", orderId)`. C'était un défaut : deux retours
    /// distincts sur une même commande produisaient des lignes INDISTINGUABLES au grand
    /// livre — impossible d'y lire lequel avait déjà été contre-passé, ni d'expliquer à
    /// un vendeur pourquoi son solde a bougé deux fois.
    ///
    /// Elles portent désormais `("refund", returnRequestId)`, ce qui les rend uniques
    /// ET sert de registre d'idempotence (voir ExistsForReferenceAsync).
    /// </summary>
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
        //
        // Un remboursement qui déborde de l'en-cours sur le disponible produit deux
        // lignes. Ce sont deux mouvements d'UN SEUL geste : sans identifiant commun,
        // rien ne le dit, et un rapprochement comptable les traite comme deux
        // remboursements partiels sans lien.
        //
        // Le solde résultant est reporté sur chaque écriture : c'est ce qui permet
        // plus tard de comparer la somme des mouvements au solde stocké, et de voir
        // la dérive à la ligne près au lieu de constater un écart global.
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
    /// Comptabilise un remboursement DIRECT versé à un client (coût plateforme) : crédite
    /// le solde « refunds » du portefeuille plateforme et trace l'écriture. Référencé par
    /// l'identifiant du remboursement (idempotence + traçabilité au grand livre).
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

    /// <summary>Contre-passe un remboursement client comptabilisé dont le payout a échoué.</summary>
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

    // ════════════════════════════════════════════════════════════════════════
    // LE PORTEFEUILLE CLIENT (D33).
    //
    // FedaPay n'expose aucune API de remboursement : l'argent est rendu au client
    // SUR SON PORTEFEUILLE, et le virement Mobile Money est une demande distincte.
    // Ces deux constantes sont les types de référence du grand livre pour ce canal.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Type de référence des crédits de remboursement client au grand livre.
    ///
    /// IL EST DISTINCT DE `"customer_refund"`, ET CE N'EST PAS COSMÉTIQUE.
    ///
    /// `"customer_refund"` désigne déjà le COÛT plateforme d'un versement MoMo direct
    /// (`AccrueCustomerRefundAsync`). Réutiliser la même chaîne ferait entrer deux
    /// flux distincts dans le même index unique partiel — c'est exactement ce qui a
    /// fait sauter la contrainte `driver_earning` au premier paiement, voir l'encadré
    /// de `WalletTransactionConfiguration`. Toute nouvelle écriture rattachée au
    /// portefeuille client doit prendre SON propre type de référence.
    /// </summary>
    public const string CustomerRefundCreditReferenceType = "customer_refund_credit";

    /// <summary>
    /// Type de référence des mouvements liés à une demande de virement client
    /// (retenue, puis restitution en cas de refus).
    ///
    /// PAS D'INDEX UNIQUE SUR CE TYPE, ET C'EST VOULU : une même demande produit
    /// DEUX écritures légitimes — le débit à la demande, le crédit au refus. Une
    /// contrainte d'unicité y interdirait le remboursement d'une demande refusée.
    /// </summary>
    public const string CustomerWithdrawalReferenceType = "customer_withdrawal";

    /// <summary>
    /// L'écriture de crédit déjà passée pour cette référence d'idempotence, s'il y
    /// en a une. C'est le REGISTRE de rejeu : voir `ExistsForReferenceAsync`.
    /// </summary>
    public Task<WalletTransaction?> FindCustomerRefundCreditAsync(Guid reference, CancellationToken ct)
        => _ledger.FindByReferenceAsync(CustomerRefundCreditReferenceType, reference, ct);

    /// <summary>
    /// Rend un montant au client sur son portefeuille et l'inscrit au grand livre.
    ///
    /// Le portefeuille est CRÉÉ s'il n'existe pas : un client peut être remboursé
    /// avant d'avoir le moindre solde — c'est même le cas normal, puisque ce
    /// portefeuille ne se remplit que de remboursements.
    ///
    /// LA DEVISE DU PORTEFEUILLE FAIT FOI, ET UN ÉCART EST UN REFUS.
    ///
    /// Ce portefeuille n'a qu'UN solde. Créditer 8 EUR sur un solde en XOF
    /// écrirait 8 dans une colonne qui compte des francs : le client verrait son
    /// solde augmenter de 8 XOF pour un remboursement de 8 EUR, et le grand livre
    /// porterait une écriture en EUR que l'invariant de `WalletLedger` compte à
    /// part — donc un déséquilibre invisible entre le solde stocké et la somme des
    /// mouvements. On refuse plutôt que de convertir : aucun taux de change n'est
    /// disponible ici, et en inventer un serait pire que de refuser.
    ///
    /// NE persiste PAS : l'Unit of Work du gestionnaire appelant commite.
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

        // Le motif métier accompagne l'écriture (« refund », « order_cancelled »… selon
        // l'appelant) ; il est tronqué à la borne de la colonne `Reason` (50) plutôt que
        // de faire échouer un remboursement sur une chaîne trop longue — l'argent rendu
        // compte plus que le libellé, et la référence reste intacte pour retrouver le
        // dossier d'origine.
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

    /// <summary>
    /// Le portefeuille d'un client, créé s'il n'existe pas.
    ///
    /// Le cache par requête évite qu'un même `SaveChanges` en crée deux : deux
    /// remboursements sur le même client dans la même opération liraient tous deux
    /// « absent » sur une entité non encore persistée. C'est le pendant applicatif de
    /// l'index unique sur `CustomerId` — l'index ferme la concurrence entre requêtes,
    /// ce cache ferme la duplication à l'intérieur d'une seule.
    /// </summary>
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
