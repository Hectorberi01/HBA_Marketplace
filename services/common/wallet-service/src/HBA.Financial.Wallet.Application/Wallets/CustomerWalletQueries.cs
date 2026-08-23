using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>Solde du portefeuille d'un client.</summary>
public sealed record GetCustomerWalletQuery(Guid CustomerId) : IQuery<CustomerWalletView>;

/// <summary>Relevé des mouvements du portefeuille d'un client.</summary>
public sealed record ListCustomerWalletTransactionsQuery(Guid CustomerId, int Take = 50)
    : IQuery<IReadOnlyList<WalletTransactionView>>;

/// <summary>Demandes de virement d'un client (les siennes).</summary>
public sealed record ListCustomerWithdrawalsQuery(Guid CustomerId)
    : IQuery<IReadOnlyList<CustomerWithdrawalView>>;

/// <summary>
/// File d'administration des demandes de virement, par statut.
///
/// SANS CETTE VUE, LES DEMANDES SONT INVISIBLES ET PERSONNE N'EST PAYÉ.
///
/// Rien n'exécute ces virements automatiquement — c'est la décision D33. La file
/// EST le mécanisme : une demande qui n'y figure pas est un client dont les fonds
/// sont retenus et que personne ne verra jamais.
/// </summary>
public sealed record ListCustomerWithdrawalsByStatusQuery(string Status)
    : IQuery<IReadOnlyList<CustomerWithdrawalView>>;

internal static class CustomerWalletMapper
{
    public static CustomerWalletView ToView(CustomerWallet w, decimal pendingWithdrawal)
        => new(w.CustomerId, w.AvailableBalance, w.LifetimeRefunded, pendingWithdrawal, w.Currency);

    public static CustomerWithdrawalView ToView(CustomerWithdrawal w)
        => new(w.Id.Value, w.CustomerId, w.Amount, w.Currency, w.Msisdn, w.Provider,
               w.Status.ToString(), w.ExternalReference, w.AdminNote,
               w.RequestedAtUtc, w.DecidedAtUtc, w.DecidedByUserId);
}

internal sealed class CustomerWalletQueryHandler
    : IQueryHandler<GetCustomerWalletQuery, CustomerWalletView>,
      IQueryHandler<ListCustomerWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>,
      IQueryHandler<ListCustomerWithdrawalsQuery, IReadOnlyList<CustomerWithdrawalView>>,
      IQueryHandler<ListCustomerWithdrawalsByStatusQuery, IReadOnlyList<CustomerWithdrawalView>>
{
    private const int MaxTake = 200;

    private readonly ICustomerWalletRepository _wallets;
    private readonly ICustomerWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;

    public CustomerWalletQueryHandler(
        ICustomerWalletRepository wallets,
        ICustomerWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
    }

    public async Task<Result<CustomerWalletView>> Handle(
        GetCustomerWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetByCustomerAsync(query.CustomerId, cancellationToken);

        // Somme retenue : demandes encore en attente de décision. Les fonds ont déjà
        // quitté le solde disponible ; les omettre ferait « disparaître » l'argent de
        // l'écran du client entre sa demande et la décision de l'administrateur —
        // exactement le défaut corrigé sur le portefeuille vendeur.
        var pendingWithdrawal = (await _withdrawals.ListByCustomerAsync(query.CustomerId, cancellationToken: cancellationToken))
            .Where(w => w.Status == CustomerWithdrawalStatus.Requested)
            .Sum(w => w.Amount);

        // ZÉRO PLUTÔT QUE 404.
        //
        // Le portefeuille naît au PREMIER remboursement. La très grande majorité des
        // clients n'en auront jamais — c'est le cas NORMAL, pas une anomalie. Leur
        // répondre « introuvable » sur un écran « mon portefeuille » leur ferait
        // croire à une panne, et enverrait au support des gens qui n'ont rien à
        // réclamer.
        //
        // La nuance qui rend ce repli légitime : ici l'absence de ligne signifie
        // vraiment « aucun mouvement ». Ce n'est pas un zéro qui masque une donnée
        // manquante, c'est la traduction d'un état connu. (Le solde retenu, lui,
        // reste calculé : il ne peut pas être non nul sans portefeuille, mais s'il
        // l'était, l'afficher est plus honnête que de le taire.)
        return wallet is null
            ? Result.Success(new CustomerWalletView(query.CustomerId, 0m, 0m, pendingWithdrawal, "XOF"))
            : Result.Success(CustomerWalletMapper.ToView(wallet, pendingWithdrawal));
    }

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(
        ListCustomerWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        // Le grand livre est indexé par OwnerId : l'identifiant du client y suffit.
        // Aucun risque de collision avec un vendeur ou un livreur — ce sont des
        // identifiants distincts, portant de surcroît un `OwnerType` différent.
        var transactions = await _ledger.ListByOwnerAsync(
            query.CustomerId, Math.Clamp(query.Take, 1, MaxTake), cancellationToken);

        IReadOnlyList<WalletTransactionView> result = transactions.Select(WalletMapper.ToView).ToList();
        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<CustomerWithdrawalView>>> Handle(
        ListCustomerWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var items = await _withdrawals.ListByCustomerAsync(query.CustomerId, cancellationToken: cancellationToken);
        IReadOnlyList<CustomerWithdrawalView> views = items.Select(CustomerWalletMapper.ToView).ToList();
        return Result.Success(views);
    }

    public async Task<Result<IReadOnlyList<CustomerWithdrawalView>>> Handle(
        ListCustomerWithdrawalsByStatusQuery query, CancellationToken cancellationToken)
    {
        // UN STATUT ILLISIBLE EST REFUSÉ, PAS SILENCIEUSEMENT REMPLACÉ.
        //
        // Retomber sur `Requested` en cas de faute de frappe donnerait à
        // l'administrateur une file qui a l'air correcte et qui répond à une autre
        // question que la sienne : il croirait consulter les virements payés et
        // verrait des demandes en attente. Sur une sortie d'argent, une réponse
        // plausible mais fausse est pire qu'un refus.
        if (!Enum.TryParse<CustomerWithdrawalStatus>(query.Status, ignoreCase: true, out var statut))
        {
            return Result.Failure<IReadOnlyList<CustomerWithdrawalView>>(Error.Validation(
                "wallet.customer_withdrawal.status_invalid",
                "Statut inconnu. Valeurs acceptées : Requested, Paid, Rejected."));
        }

        var items = await _withdrawals.ListByStatusAsync(statut, cancellationToken: cancellationToken);
        IReadOnlyList<CustomerWithdrawalView> views = items.Select(CustomerWalletMapper.ToView).ToList();
        return Result.Success(views);
    }
}
