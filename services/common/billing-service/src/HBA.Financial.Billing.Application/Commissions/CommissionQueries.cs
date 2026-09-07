using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Billing.Contracts;
using HBA.Financial.Billing.Domain.Commissions;

namespace HBA.Financial.Billing.Application.Commissions;

/// <summary>Liste les règles de commission (back-office).</summary>
public sealed record ListCommissionRulesQuery : IQuery<IReadOnlyList<CommissionRuleSummary>>;

/// <summary>Calcule la commission applicable à un montant brut (aperçu).</summary>
public sealed record ComputeCommissionQuery(Guid SellerId, Guid CategoryId, decimal GrossAmount, string Currency)
    : IQuery<CommissionResult>;

internal static class CommissionMapper
{
    public static CommissionRuleSummary ToSummary(CommissionRule r) => new(
        r.Id.Value, r.Scope.ToString(), r.TargetId, r.Rate, r.FixedFee, r.Currency, r.MinFee, r.MaxFee, r.EffectiveFromUtc, r.IsActive);
}

internal sealed class ListCommissionRulesQueryHandler : IQueryHandler<ListCommissionRulesQuery, IReadOnlyList<CommissionRuleSummary>>
{
    private readonly ICommissionRuleRepository _repository;

    public ListCommissionRulesQueryHandler(ICommissionRuleRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CommissionRuleSummary>>> Handle(ListCommissionRulesQuery query, CancellationToken cancellationToken)
    {
        var rules = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<CommissionRuleSummary> summaries = rules.Select(CommissionMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

/// <summary>L'APERÇU DÉLÈGUE AU MOTEUR, IL NE LE RÉIMPLÉMENTE PAS.</summary>
internal sealed class ComputeCommissionQueryHandler : IQueryHandler<ComputeCommissionQuery, CommissionResult>
{
    private readonly ICommissionModuleApi _commissions;

    public ComputeCommissionQueryHandler(ICommissionModuleApi commissions) => _commissions = commissions;

    public async Task<Result<CommissionResult>> Handle(ComputeCommissionQuery query, CancellationToken cancellationToken)
    {
        var resultat = await _commissions.ComputeCommissionAsync(
            query.SellerId, query.CategoryId, query.GrossAmount, query.Currency, cancellationToken);

        return resultat;
    }
}
