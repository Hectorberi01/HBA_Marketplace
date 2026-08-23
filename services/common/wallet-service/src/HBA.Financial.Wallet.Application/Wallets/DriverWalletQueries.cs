using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>Solde du portefeuille d'un livreur.</summary>
public sealed record GetDriverWalletQuery(Guid DriverId) : IQuery<DriverWalletView>;

/// <summary>Relevé des mouvements du portefeuille d'un livreur.</summary>
public sealed record ListDriverWalletTransactionsQuery(Guid DriverId, int Take = 50)
    : IQuery<IReadOnlyList<WalletTransactionView>>;

internal sealed class DriverWalletQueryHandler
    : IQueryHandler<GetDriverWalletQuery, DriverWalletView>,
      IQueryHandler<ListDriverWalletTransactionsQuery, IReadOnlyList<WalletTransactionView>>
{
    private const int MaxTake = 200;

    private readonly IDriverWalletRepository _wallets;
    private readonly IWalletTransactionRepository _ledger;

    public DriverWalletQueryHandler(IDriverWalletRepository wallets, IWalletTransactionRepository ledger)
    {
        _wallets = wallets;
        _ledger = ledger;
    }

    public async Task<Result<DriverWalletView>> Handle(GetDriverWalletQuery query, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetByDriverAsync(query.DriverId, cancellationToken);

        // ZÉRO PLUTÔT QUE 404.
        //
        // Le portefeuille naît à la première course. Un livreur fraîchement vérifié
        // qui ouvre son écran « mes gains » n'en a donc pas encore — et lui répondre
        // « introuvable » lui ferait croire à une panne le jour même où il commence.
        // Un solde de zéro est la réponse EXACTE à sa question : il n'a rien gagné
        // pour l'instant.
        //
        // La nuance qui rend ce choix légitime : ici l'absence de ligne signifie
        // vraiment « aucun mouvement ». Ce n'est pas un repli qui masque une donnée
        // manquante, c'est la traduction d'un état connu.
        return wallet is null
            ? Result.Success(new DriverWalletView(query.DriverId, 0m, 0m, "XOF"))
            : Result.Success(new DriverWalletView(
                wallet.DriverId, wallet.AvailableBalance, wallet.LifetimeEarned, wallet.Currency));
    }

    public async Task<Result<IReadOnlyList<WalletTransactionView>>> Handle(
        ListDriverWalletTransactionsQuery query, CancellationToken cancellationToken)
    {
        // Le grand livre est indexé par OwnerId : l'identifiant du livreur y suffit,
        // aucun risque de collision avec un vendeur puisque ce sont des identifiants
        // distincts, portant de surcroît un OwnerType différent.
        var transactions = await _ledger.ListByOwnerAsync(
            query.DriverId, Math.Clamp(query.Take, 1, MaxTake), cancellationToken);

        IReadOnlyList<WalletTransactionView> result = transactions.Select(WalletMapper.ToView).ToList();
        return Result.Success(result);
    }
}
