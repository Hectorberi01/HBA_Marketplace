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

    // AJOUTÉS EN FIN D'ENREGISTREMENT, AVEC UN DÉFAUT (D32, appliquée aux
    // contrats de lecture comme aux événements). Un client déjà déployé qui
    // désérialise cette réponse ignore les champs qu'il ne connaît pas ; l'ajouter
    // au MILIEU aurait cassé toute construction positionnelle existante.
    string Funder = "PLATFORM",
    int SellerFundedShareBps = 0,
    Guid? OwnerSellerId = null);

/// <summary>
/// Résultat d'une évaluation de coupon (§10.16, `POST /api/v1/promotions/validate`).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// `Discount` SEUL NE SUFFISAIT PAS, ET C'EST TOUT L'OBJET DE D28.
///
/// L'appelant reçoit un montant total et doit écrire, dans son
/// `PriceBreakdownDto`, un `SellerDiscount` ET un `PlatformDiscount`. Sans la
/// décomposition, il n'a le choix qu'entre inventer (imputer au vendeur, donc
/// prélever sur des gains sans que personne ne l'ait décidé) et renoncer (tout
/// mettre à zéro, ce que fait le producteur d'aujourd'hui).
///
/// LES TROIS CHAMPS SONT AJOUTÉS EN FIN, AVEC UN DÉFAUT (D32).
///
/// `SellerFundedDiscount + PlatformFundedDiscount == Discount` dès que
/// `Valid` est vrai. Le défaut « 0 / 0 » n'est donc jamais rendu par le chemin
/// nominal : il n'existe que pour les refus, où `Discount` vaut lui-même 0.
/// ═════════════════════════════════════════════════════════════════════════════
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

/// <summary>
/// `OwnerSellerId` N'EST PAS UN FILTRE DE CONFORT : C'EST LA MOITIÉ DE LA GARDE.
///
/// La route marchand est ouverte au vendeur depuis D28. Un vendeur ne doit voir
/// que SES campagnes — budgets, valeurs et taux sont des données commerciales.
/// L'endpoint pose cette valeur depuis le jeton, jamais depuis la requête : un
/// paramètre de requête aurait rendu la liste de n'importe quel concurrent en une
/// URL, ce qui est exactement le défaut décrit dans `SellerReturnsEndpoints`.
/// <c>null</c> = vue administrateur, sans filtre.
/// </summary>
public sealed record ListPromotionsQuery(
    PromotionScope? Scope, int Take = 50, Guid? OwnerSellerId = null)
    : IQuery<IReadOnlyList<PromotionView>>;

/// <summary>
/// Une campagne par son identifiant.
///
/// ELLE EXISTE POUR LA GARDE D'APPARTENANCE, PAS POUR UN ÉCRAN.
///
/// `DELETE /api/v1/merchant/promotions/{id}` ne porte pas le vendeur : il est dans
/// la RESSOURCE. Sans cette lecture préalable, il n'y a rien à comparer au jeton —
/// et c'est l'état d'avant, celui qui a valu `RequireAdmin` aux trois routes.
/// </summary>
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
        //
        // `BudgetRemaining` vaut `long.MaxValue` pour une campagne sans plafond —
        // pratique en interne, absurde dans un JSON : un écran afficherait
        // « 9 223 372 036 854 775 807 F restants ».
        p.Budget is null ? null : p.BudgetRemaining,
        p.Currency,

        // « PLATFORM » / « SELLER » / « SHARED », PAS `Enum.ToString()`.
        //
        // Même raison que pour `Scope` et `Type` : le contrat public ne doit pas
        // dépendre de la CASSE choisie dans le code. `PromotionConstantes.Convertir`
        // vit dans le projet de contrat pour cette raison — mais elle traduit une
        // graphie `PascalCase` en `SNAKE_CASE`, et « Platform » n'a pas de bosse :
        // la mise en majuscules suffit et reste stable si l'énumération est
        // renommée en `PlatformFunded`… ce qui produirait « PLATFORM_FUNDED ».
        // On passe donc par `Convertir` pour que ce jour-là le contrat casse
        // VISIBLEMENT plutôt que de dériver en silence.
        PromotionConstantes.Convertir(p.Funder.ToString()),
        p.SellerFundedShareBps,
        p.OwnerSellerId);
}

// ═══════════════════════════════════════════════════════════════════ Création

public sealed record PromotionRuleInput(string RuleType, string RuleJson);

/// <summary>
/// LE FINANCEUR EST UNE DONNÉE DE CRÉATION, PAS UN RÉGLAGE ULTÉRIEUR.
///
/// Il n'existe volontairement AUCUNE commande pour le changer après coup. Une
/// campagne dont le financeur bouge en cours de route produirait deux vérités sur
/// les mêmes ventes : celles d'avant ont été imputées à l'un, celles d'après à
/// l'autre, et aucun relevé ne porte la date du basculement. Changer de financeur,
/// c'est une nouvelle campagne — exactement comme ajouter une condition
/// d'éligibilité (voir `Promotion.AddRule`).
/// </summary>
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
        //
        // Rien n'est enregistré tant que `SaveChangesAsync` n'a pas été appelé :
        // sortir ici ne laisse aucune campagne à moitié construite. Créer la
        // campagne en écartant les règles illisibles aurait produit une promotion
        // MOINS restrictive que demandé, et l'écran l'aurait affichée comme créée.
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
