using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Application.Batches;

/// <summary>Récupère un lot de reversement.</summary>
public sealed record GetSettlementBatchQuery(Guid BatchId) : IQuery<SettlementBatchSummary>;

/// <summary>Liste les lots de reversement.</summary>
public sealed record ListSettlementBatchesQuery : IQuery<IReadOnlyList<SettlementBatchSummary>>;

/// <summary>Relevé d'un vendeur sur une période (ventes, commissions, net).</summary>
public sealed record GetSellerStatementQuery(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc) : IQuery<SellerStatementSummary>;

/// <summary>Lignes détaillées du relevé d'un vendeur sur une période (un gain = une ligne), triées par date.</summary>
public sealed record GetSellerStatementLinesQuery(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc) : IQuery<IReadOnlyList<SellerStatementLine>>;

internal static class SettlementMapper
{
    public static SettlementBatchSummary ToSummary(SettlementBatch b) => new(
        b.Id.Value, b.PeriodStartUtc, b.PeriodEndUtc, b.Currency, b.TotalNet, b.Status.ToString(), b.CreatedAtUtc,
        b.Payouts.Select(p => new PayoutSummary(
            p.Id, p.SellerId, p.GrossAmount, p.CommissionAmount, p.NetAmount, p.Currency, p.Status.ToString(), p.ProviderRef, p.PaidAtUtc)).ToList());
}

internal sealed class GetSettlementBatchQueryHandler : IQueryHandler<GetSettlementBatchQuery, SettlementBatchSummary>
{
    private readonly ISettlementBatchRepository _repository;

    public GetSettlementBatchQueryHandler(ISettlementBatchRepository repository) => _repository = repository;

    public async Task<Result<SettlementBatchSummary>> Handle(GetSettlementBatchQuery query, CancellationToken cancellationToken)
    {
        var batch = await _repository.GetByIdAsync(new SettlementBatchId(query.BatchId), cancellationToken);
        return batch is null
            ? Error.NotFound("settlement.batch.not_found", "Lot introuvable.")
            : SettlementMapper.ToSummary(batch);
    }
}

internal sealed class ListSettlementBatchesQueryHandler : IQueryHandler<ListSettlementBatchesQuery, IReadOnlyList<SettlementBatchSummary>>
{
    private readonly ISettlementBatchRepository _repository;

    public ListSettlementBatchesQueryHandler(ISettlementBatchRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SettlementBatchSummary>>> Handle(ListSettlementBatchesQuery query, CancellationToken cancellationToken)
    {
        var batches = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<SettlementBatchSummary> summaries = batches.Select(SettlementMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetSellerStatementQueryHandler : IQueryHandler<GetSellerStatementQuery, SellerStatementSummary>
{
    private readonly ISellerEarningRepository _repository;

    public GetSellerStatementQueryHandler(ISellerEarningRepository repository) => _repository = repository;

    public async Task<Result<SellerStatementSummary>> Handle(GetSellerStatementQuery query, CancellationToken cancellationToken)
    {
        var s = await _repository.GetSellerStatementAsync(query.SellerId, query.PeriodStartUtc, query.PeriodEndUtc, cancellationToken);
        return new SellerStatementSummary(
            s.SellerId, s.GrossSales, s.Commissions, s.ProviderFees, s.NetPayout, s.Currency, s.LineCount);
    }
}

internal sealed class GetSellerStatementLinesQueryHandler : IQueryHandler<GetSellerStatementLinesQuery, IReadOnlyList<SellerStatementLine>>
{
    private readonly ISellerEarningRepository _repository;

    public GetSellerStatementLinesQueryHandler(ISellerEarningRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<SellerStatementLine>>> Handle(GetSellerStatementLinesQuery query, CancellationToken cancellationToken)
    {
        var earnings = await _repository.ListSellerEarningsAsync(query.SellerId, query.PeriodStartUtc, query.PeriodEndUtc, cancellationToken);

        // LES LIGNES SUIVENT LE RÉSUMÉ : MONTANTS NETS DES REPRISES.
        //
        // Le résumé (`GetSellerStatementAsync`) déduit désormais ce qui a été repris
        // sur une vente remboursée. Laisser ici les montants d'origine ferait un
        // relevé dont les lignes ne totalisent pas le total — le pire des deux
        // mondes : le vendeur ne saurait ni ce qu'il a vendu, ni ce qui lui est dû,
        // et conclurait à une erreur de calcul.
        //
        // La trace de la reprise n'est pas perdue pour autant : le STATUT de la ligne
        // passe « Reversed » dès que la vente est entièrement rendue. Une reprise
        // PARTIELLE, elle, ne se lit nulle part sur cette ligne — les montants
        // baissent, le statut ne bouge pas. C'est la même lacune de contrat que sur le
        // résumé, et elle se refermera au même endroit.
        IReadOnlyList<SellerStatementLine> lines = earnings
            .Select(e => new SellerStatementLine(
                e.Id.Value, e.OrderId, e.CreatedAtUtc, e.RemainingGrossAmount, e.RemainingCommissionAmount,
                e.RemainingProviderFeeAmount, e.RemainingNetAmount, e.Currency, e.Status.ToString()))
            .ToList();
        return Result.Success(lines);
    }
}
