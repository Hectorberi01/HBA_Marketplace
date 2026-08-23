using Microsoft.EntityFrameworkCore;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Infrastructure.Persistence;

internal sealed class SellerWalletRepository : ISellerWalletRepository
{
    private readonly WalletDbContext _dbContext;

    public SellerWalletRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task<SellerWallet?> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerWallets.FirstOrDefaultAsync(w => w.SellerId == sellerId, cancellationToken);

    /// <remarks>
    /// PAS D'`AsNoTracking` — les appelants MUTENT ces portefeuilles. Le poser
    /// ici ferait échouer tous les crédits en silence : ils ne seraient jamais
    /// persistés, et le lot passerait pour un succès.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, SellerWallet>> ListBySellersAsync(
        IReadOnlyCollection<Guid> sellerIds, CancellationToken cancellationToken = default)
    {
        if (sellerIds.Count == 0)
        {
            return new Dictionary<Guid, SellerWallet>();
        }

        var liste = sellerIds.Distinct().ToList();

        return await _dbContext.SellerWallets
            .Where(w => liste.Contains(w.SellerId))
            .ToDictionaryAsync(w => w.SellerId, cancellationToken);
    }

    public async Task AddAsync(SellerWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.SellerWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class DriverWalletRepository : IDriverWalletRepository
{
    private readonly WalletDbContext _dbContext;

    public DriverWalletRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF activé (pas d'AsNoTracking) : le portefeuille est muté juste après
    // — c'est le seul usage de cette lecture.
    public async Task<DriverWallet?> GetByDriverAsync(Guid driverId, CancellationToken cancellationToken = default)
        => await _dbContext.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == driverId, cancellationToken);

    public async Task AddAsync(DriverWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.DriverWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class PlatformWalletRepository : IPlatformWalletRepository
{
    private readonly WalletDbContext _dbContext;

    public PlatformWalletRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task<PlatformWallet?> GetAsync(CancellationToken cancellationToken = default)
        => await _dbContext.PlatformWallets.FirstOrDefaultAsync(w => w.Id == PlatformWallet.SingletonId, cancellationToken);

    public async Task AddAsync(PlatformWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.PlatformWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class WithdrawalRepository : IWithdrawalRepository
{
    private readonly WalletDbContext _dbContext;

    public WithdrawalRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.AddAsync(withdrawal, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.SellerId == sellerId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (validation/refus admin).
    public async Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    /// <remarks>
    /// BORNÉE, PARCE QU'ELLE ALIMENTE UNE BOUCLE D'APPELS INTER-SERVICES (§11-12).
    ///
    /// `WalletQueries` itère sur ce résultat et interroge seller-service pour CHAQUE
    /// demande — jusqu'à deux allers-retours par ligne. Sans borne, une file laissée
    /// une semaine sans traitement produisait autant d'appels que de demandes en
    /// attente, à chaque affichage de l'écran d'administration.
    ///
    /// Borner la file borne donc aussi la boucle : c'est la correction qui compte
    /// le plus des deux.
    /// </remarks>
    public async Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(
        WithdrawalStatus status, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.Status == status)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    public async Task<IReadOnlyList<Withdrawal>> ListProcessingForReconciliationAsync(int take, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .Where(w => w.Status == WithdrawalStatus.Processing)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    // SUIVI activé : le webhook mute l'entité juste après la corrélation.
    public async Task<Withdrawal?> GetByProviderRefAsync(string providerRef, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .FirstOrDefaultAsync(w => w.ProviderRef == providerRef, cancellationToken);
}

internal sealed class CustomerRefundRepository : ICustomerRefundRepository
{
    private readonly WalletDbContext _dbContext;

    public CustomerRefundRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CustomerRefund refund, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.AddAsync(refund, cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (réconciliation).
    public async Task<CustomerRefund?> GetByIdAsync(CustomerRefundId id, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.OrderId == orderId && r.Status != CustomerRefundStatus.Failed)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    /// <remarks>
    /// SA VOISINE `ListProcessingForReconciliationAsync` ÉTAIT DÉJÀ BORNÉE, PAS
    /// ELLE. Deux lectures du même état, à deux lignes d'écart, avec deux
    /// comportements. Celle-ci est censée rendre une file courte — mais « censée »
    /// n'est pas une borne : si l'opérateur Mobile Money bloque, elle rend tout.
    /// </remarks>
    public async Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(
        int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.Status == CustomerRefundStatus.Processing)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);
}

internal sealed class CustomerWalletRepository : ICustomerWalletRepository
{
    private readonly WalletDbContext _dbContext;

    public CustomerWalletRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF activé (pas d'AsNoTracking) : le portefeuille est muté juste après —
    // crédit d'un remboursement, retenue ou restitution d'un virement. C'est le seul
    // usage de cette lecture, y compris pour l'affichage du solde, qui ne mute rien
    // et ne paie donc que le coût du suivi sur une seule ligne.
    public async Task<CustomerWallet?> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWallets.FirstOrDefaultAsync(w => w.CustomerId == customerId, cancellationToken);

    public async Task AddAsync(CustomerWallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWallets.AddAsync(wallet, cancellationToken);
}

internal sealed class CustomerWithdrawalRepository : ICustomerWithdrawalRepository
{
    private readonly WalletDbContext _dbContext;

    public CustomerWithdrawalRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CustomerWithdrawal withdrawal, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWithdrawals.AddAsync(withdrawal, cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (paiement / refus admin).
    public async Task<CustomerWithdrawal?> GetByIdAsync(CustomerWithdrawalId id, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWithdrawals.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CustomerWithdrawal>> ListByCustomerAsync(
        Guid customerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWithdrawals
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .OrderByDescending(w => w.RequestedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    // Les plus anciennes d'abord : la file d'administration se traite dans l'ordre
    // d'arrivée, et ce sont les demandes qui traînent qui coûtent au client.
    public async Task<IReadOnlyList<CustomerWithdrawal>> ListByStatusAsync(
        CustomerWithdrawalStatus status, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerWithdrawals
            .AsNoTracking()
            .Where(w => w.Status == status)
            .OrderBy(w => w.RequestedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);
}

internal sealed class WalletTransactionRepository : IWalletTransactionRepository
{
    private readonly WalletDbContext _dbContext;

    public WalletTransactionRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions.AddAsync(transaction, cancellationToken);

    public async Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Le grand livre a-t-il déjà une écriture pour cette référence ?
    ///
    /// On interroge le CHANGE TRACKER autant que la base (pas d'AsNoTracking, et
    /// `Local` d'abord) : dans une même transaction, les écritures précédentes ne sont
    /// pas encore commitées. Sans cela, deux handlers du même message se croiraient
    /// tous deux « premiers ».
    /// </summary>
    public async Task<bool> ExistsForReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var pending = _dbContext.WalletTransactions.Local
            .Any(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId);

        if (pending)
        {
            return true;
        }

        return await _dbContext.WalletTransactions
            .AnyAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId, cancellationToken);
    }

    /// <summary>
    /// L'écriture déjà passée pour cette référence.
    ///
    /// MÊME PRÉCAUTION QUE `ExistsForReferenceAsync` : on interroge le CHANGE
    /// TRACKER (`Local`) AVANT la base. Dans une même transaction, l'écriture qui
    /// vient d'être ajoutée n'est pas encore commitée — sans cela, deux crédits du
    /// même remboursement dans un même `SaveChanges` se croiraient tous deux
    /// premiers, et l'index unique ferait échouer l'opération ENTIÈRE au lieu de
    /// reconnaître le rejeu.
    ///
    /// Pas d'`AsNoTracking` : l'entité rendue n'est jamais mutée par ce flux, mais
    /// la garder suivie évite qu'EF en matérialise une SECONDE copie d'une ligne
    /// déjà présente dans le tracker — deux instances de la même écriture, dont l'une
    /// porterait un `BalanceAfter` périmé.
    /// </summary>
    public async Task<WalletTransaction?> FindByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var pending = _dbContext.WalletTransactions.Local
            .FirstOrDefault(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId);

        if (pending is not null)
        {
            return pending;
        }

        return await _dbContext.WalletTransactions
            .FirstOrDefaultAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId, cancellationToken);
    }
}
