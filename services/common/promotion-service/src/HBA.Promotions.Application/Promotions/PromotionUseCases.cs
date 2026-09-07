using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>Unité de travail du service promotion.</summary>
public interface IPromotionsUnitOfWork : IUnitOfWork
{
}

// ═══════════════════════════════════════════════════════════════════ Lectures

/// <summary>Vue d'une campagne (§10.16, réponse de `GET /api/v1/merchant/promotions`).</summary>
/// <param name="Funder">
/// <summary>« PLATFORM », « SELLER » ou « SHARED ».</summary>
/// </param>
/// <param name="SellerFundedShareBps">
/// <summary>Part vendeur en points de base. 0 = la plateforme paie tout.</summary>
/// </param>
/// <param name="OwnerSellerId">
/// <summary>Vendeur propriétaire. <c>null</c> = campagne de la plateforme.</summary>
/// </param>
public sealed record PromotionView(
    Guid Id,
    string Name,
    string Scope,
    string Type,
    long Value,
    string Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    long? Budget,
    long BudgetConsumed,
    long? BudgetRemaining,
    string Currency,

    // AJOUTÉS EN FIN D'ENREGISTREMENT, AVEC UN DÉFAUT (D32, appliquée aux contrats
    // de lecture comme aux événements).
    string Funder = "PLATFORM",
    int SellerFundedShareBps = 0,
    Guid? OwnerSellerId = null);

/// <summary>
/// Résultat d'une évaluation de coupon (§10.16, `POST
/// /api/v1/promotions/validate`).
/// </summary>
public sealed record PromotionEvaluation(
    bool Valid,
    Guid? PromotionId,
    long Discount,
    string Currency,
    string Message,
    string? Reason,
    long SellerFundedDiscount = 0,
    long PlatformFundedDiscount = 0,
    Guid? OwnerSellerId = null);

/// <summary>`OwnerSellerId` N'EST PAS UN FILTRE DE CONFORT : C'EST LA MOITIÉ DE LA GARDE.</summary>
public sealed record ListPromotionsQuery(
    PromotionScope? Scope, int Take = 50, Guid? OwnerSellerId = null)
    : IQuery<IReadOnlyList<PromotionView>>;

/// <summary>Une campagne par son identifiant.</summary>
public sealed record GetPromotionQuery(Guid PromotionId) : IQuery<PromotionView>;

internal sealed class GetPromotionQueryHandler : IQueryHandler<GetPromotionQuery, PromotionView>
{
    private readonly IPromotionRepository _promotions;

    public GetPromotionQueryHandler(IPromotionRepository promotions) => _promotions = promotions;

    public async Task<Result<PromotionView>> Handle(
        GetPromotionQuery query, CancellationToken cancellationToken)
    {
        var promotion = await _promotions.GetByIdAsync(query.PromotionId, cancellationToken);

        if (promotion is null)
        {
            return Result.Failure<PromotionView>(Error.NotFound(
                ErrorCodes.NotFound(ServiceCodes.Promotion), "Campagne introuvable."));
        }

        return ListPromotionsQueryHandler.Decrire(promotion);
    }
}

internal sealed class ListPromotionsQueryHandler
    : IQueryHandler<ListPromotionsQuery, IReadOnlyList<PromotionView>>
{
    private readonly IPromotionRepository _promotions;

    public ListPromotionsQueryHandler(IPromotionRepository promotions) => _promotions = promotions;

    public async Task<Result<IReadOnlyList<PromotionView>>> Handle(
        ListPromotionsQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 200);

        var campagnes = await _promotions.ListAsync(
            query.Scope, take, query.OwnerSellerId, cancellationToken);

        return Result.Success<IReadOnlyList<PromotionView>>(campagnes.Select(Decrire).ToList());
    }

    internal static PromotionView Decrire(Promotion p) => new(
        p.Id, p.Name, p.Scope.ToString(), p.Type.ToString(), p.Value, p.Status.ToString(),
        p.StartsAtUtc, p.EndsAtUtc, p.Budget, p.BudgetConsumed,

        // `null` ET NON `long.MaxValue` DANS LE CONTRAT PUBLIC.
        p.Budget is null ? null : p.BudgetRemaining,
        p.Currency,

        // « PLATFORM » / « SELLER » / « SHARED », PAS `Enum.ToString()`.
        PromotionConstantes.Convertir(p.Funder.ToString()),
        p.SellerFundedShareBps,
        p.OwnerSellerId);
}

// ═══════════════════════════════════════════════════════════════════ Création

public sealed record PromotionRuleInput(string RuleType, string RuleJson);

/// <summary>LE FINANCEUR EST UNE DONNÉE DE CRÉATION, PAS UN RÉGLAGE ULTÉRIEUR.</summary>
public sealed record CreatePromotionCommand(
    string? Name,
    PromotionScope Scope,
    PromotionType Type,
    long Value,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    long? Budget,
    string Currency = "XOF",
    IReadOnlyList<PromotionRuleInput>? Rules = null,
    int SellerFundedShareBps = PromotionFunding.PlatformOnly,
    Guid? OwnerSellerId = null) : ICommand<PromotionView>;

internal sealed class CreatePromotionCommandHandler
    : ICommandHandler<CreatePromotionCommand, PromotionView>
{
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public CreatePromotionCommandHandler(IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PromotionView>> Handle(
        CreatePromotionCommand command, CancellationToken cancellationToken)
    {
        var creation = Promotion.Create(
            command.Name, command.Scope, command.Type, command.Value,
            command.StartsAtUtc, command.EndsAtUtc, command.Budget, command.Currency,
            command.SellerFundedShareBps, command.OwnerSellerId);

        if (creation.IsFailure)
        {
            return Result.Failure<PromotionView>(creation.Error);
        }

        var promotion = creation.Value;

        // UNE RÈGLE REFUSÉE REFUSE LA CAMPAGNE ENTIÈRE.
        foreach (var regle in command.Rules ?? Array.Empty<PromotionRuleInput>())
        {
            var ajout = promotion.AddRule(regle.RuleType, regle.RuleJson);

            if (ajout.IsFailure)
            {
                return Result.Failure<PromotionView>(ajout.Error);
            }
        }

        await _promotions.AddAsync(promotion, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ListPromotionsQueryHandler.Decrire(promotion);
    }
}

public sealed record CancelPromotionCommand(Guid PromotionId) : ICommand;

internal sealed class CancelPromotionCommandHandler : ICommandHandler<CancelPromotionCommand>
{
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public CancelPromotionCommandHandler(IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CancelPromotionCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _promotions.GetByIdAsync(command.PromotionId, cancellationToken);

        if (promotion is null)
        {
            return Result.Failure(Error.NotFound(
                ErrorCodes.NotFound(ServiceCodes.Promotion), "Campagne introuvable."));
        }

        promotion.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
