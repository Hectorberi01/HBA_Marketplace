using Microsoft.EntityFrameworkCore;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Persistence;

internal sealed class SellerEarningRepository : ISellerEarningRepository
{
    private readonly WalletDbContext _dbContext;

    public SellerEarningRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SellerEarning earning, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AddAsync(earning, cancellationToken);

    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AnyAsync(e => e.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListAccruedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Accrued && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListReleasedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        // On filtre sur la date de LIBÉRATION (ReleasedAtUtc = quand le gain est devenu
        // payable), et NON sur la date de création/confirmation : c'est la période de
        // règlement qui compte. Un gain confirmé avant la période mais livré pendant
        // doit être réglé ; un gain confirmé pendant mais pas encore livré ne doit pas.
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Released
                        && e.ReleasedAtUtc != null
                        && e.ReleasedAtUtc >= startUtc
                        && e.ReleasedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    // SUIVI activé : l'annulation d'un lot mute ces entités (retour à « Released »).
    public async Task<IReadOnlyList<SellerEarning>> ListByBatchAsync(Guid settlementBatchId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SettlementBatchId == settlementBatchId)
            .ToListAsync(cancellationToken);

    // SUIVI activé : l'imputation d'un retrait mute ces entités (passage à « Settled »).
    //
    // LE TRI PORTE SUR TROIS COLONNES, ET LES DEUX DERNIÈRES NE SONT PAS DÉCORATIVES.
    //
    // `ReleasedAtUtc` seul ne départage pas deux gains libérés dans la même
    // transaction — une commande multi-lignes en produit plusieurs à la
    // milliseconde près. PostgreSQL rendrait alors un ordre arbitraire, et deux
    // exécutions du même retrait imputeraient des gains différents. L'identifiant
    // en dernier ressort rend le classement TOTAL, donc reproductible.
    public async Task<IReadOnlyList<SellerEarning>> ListReleasedBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SellerId == sellerId && e.Status == EarningStatus.Released)
            .OrderBy(e => e.ReleasedAtUtc)
            .ThenBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    // SUIVI activé : un retrait refusé ou échoué remet ces gains en « Released ».
    public async Task<IReadOnlyList<SellerEarning>> ListByWithdrawalAsync(Guid withdrawalId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SettledByWithdrawalId == withdrawalId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.OrderId == orderId)
            .ToListAsync(cancellationToken);

    public async Task<SellerStatement> GetSellerStatementAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var lines = await _dbContext.Earnings
            .AsNoTracking()
            .Where(e => e.SellerId == sellerId && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        var currency = lines.Count > 0 ? lines[0].Currency : "XOF";

        // ═════════════════════════════════════════════════════════════════════
        // LE RELEVÉ EST NET DES REPRISES — IL NE L'ÉTAIT PAS.
        //
        // Ces quatre sommes portaient sur les montants D'ORIGINE. Une vente
        // remboursée y figurait donc en entier : le vendeur lisait un chiffre
        // d'affaires et un net à percevoir qui incluaient une marchandise qui lui
        // était revenue, et dont l'argent lui avait déjà été repris au portefeuille.
        // Le relevé annonçait un dû que le lot de reversement ne paierait jamais.
        //
        // CE QUE CE CHOIX COÛTE : LE RELEVÉ NE MONTRE PLUS LES REPRISES, IL LES
        // DÉDUIT.
        //
        // Un vendeur qui compare deux relevés successifs voit un total baisser sans
        // qu'aucune ligne ne dise pourquoi. La lecture juste serait d'exposer le brut
        // d'origine ET le repris côte à côte — mais `SellerStatement` et
        // `SellerStatementSummary` n'ont que quatre montants, et en ajouter traverse
        // les contrats publics et les applications clientes. Ce n'est pas dans ce lot,
        // et c'est la lacune à traiter ensuite.
        //
        // SOMME EN MÉMOIRE, ET C'EST OBLIGATOIRE : `Remaining*` est calculé et
        // n'existe pas en base. La matérialisation (`ToListAsync`) au-dessus n'est
        // donc plus une commodité — la retirer casserait la traduction SQL.
        // ═════════════════════════════════════════════════════════════════════
        return new SellerStatement(
            sellerId,
            lines.Sum(e => e.RemainingGrossAmount),
            lines.Sum(e => e.RemainingCommissionAmount),
            lines.Sum(e => e.RemainingProviderFeeAmount),
            lines.Sum(e => e.RemainingNetAmount),
            currency,
            lines.Count);
    }

    public async Task<IReadOnlyList<SellerEarning>> ListSellerEarningsAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .AsNoTracking()
            .Where(e => e.SellerId == sellerId && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}

internal sealed class SettlementBatchRepository : ISettlementBatchRepository
{
    private readonly WalletDbContext _dbContext;

    public SettlementBatchRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SettlementBatch batch, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.AddAsync(batch, cancellationToken);

    public async Task<SettlementBatch?> GetByIdAsync(SettlementBatchId id, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.Include(b => b.Payouts).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SettlementBatch>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Batches
            .Include(b => b.Payouts)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Le plafond de lecture des versements d'un vendeur.
    /// </summary>
    /// <remarks>
    /// C'EST UN PLAFOND, PAS UNE PAGINATION. Un vendeur ancien ne verra pas ses
    /// versements les plus vieux. C'est délibérément préférable à l'état d'avant —
    /// où la requête n'avait aucune borne du tout — et délibérément insuffisant :
    /// la vraie réponse est une page, avec un curseur. Elle demande de toucher le
    /// contrat de la route, ce que ce lot ne fait pas.
    /// </remarks>
    private const int PlafondDeVersements = 200;

    /// <summary>
    /// Les versements d'UN vendeur, ordonnés du lot le plus récent au plus ancien.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE LISAIT L'HISTORIQUE DE RÈGLEMENT DE TOUTE LA PLATEFORME (§12).
    ///
    /// Elle chargeait TOUS les lots avec TOUS leurs versements — un lot par période,
    /// un versement par vendeur — puis filtrait EN MÉMOIRE sur `SellerId`. Après un
    /// an de règlements hebdomadaires à cinq mille vendeurs, c'est deux cent
    /// soixante mille lignes remontées pour en rendre cinquante-deux.
    ///
    /// Et le filtre en mémoire rendait la chose invisible à la lecture : la requête
    /// avait l'air de porter sur un vendeur.
    ///
    /// LE TRI VIENT DU LOT, PAS DU VERSEMENT. `Payout` ne porte aucune date de
    /// création — seulement `PaidAtUtc`, nulle tant qu'il n'est pas payé. Trier
    /// dessus mettrait les versements en attente dans un ordre arbitraire, alors
    /// que ce sont précisément ceux que le vendeur regarde. D'où la projection
    /// intermédiaire : elle emporte la date du lot jusqu'au tri.
    ///
    /// ET LE TRI EST APPLIQUÉ PAR LA BASE, LA PROJECTION FINALE EN MÉMOIRE.
    /// Rendre `Select(x => x.Payout)` à SQL après un `Take` produirait un
    /// `SELECT … FROM (… ORDER BY … LIMIT n)` dont la requête externe ne garantit
    /// plus l'ordre. On matérialise la page triée, puis on projette.
    ///
    /// L'index `IX_payouts_SellerId` existe déjà : c'est une lecture ciblée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<IReadOnlyList<Payout>> ListPayoutsBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var page = await _dbContext.Batches
            .AsNoTracking()
            .SelectMany(
                b => b.Payouts.Where(p => p.SellerId == sellerId),
                (b, p) => new { b.CreatedAtUtc, Versement = p })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(PlafondDeVersements)
            .ToListAsync(cancellationToken);

        return page.Select(x => x.Versement).ToList();
    }
}
