using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Domain.Promotions;

/// <summary>Campagne promotionnelle (§10.16, table <c>promotions</c>).</summary>
public sealed class Promotion : AggregateRoot<Guid>
{
    private readonly List<PromotionRule> _rules = new();

    private Promotion(
        Guid id, string name, PromotionScope scope, PromotionType type, long value,
        DateTime startsAtUtc, DateTime endsAtUtc, long? budget, string currency,
        int sellerFundedShareBps, Guid? ownerSellerId)
        : base(id)
    {
        Name = name;
        Scope = scope;
        Type = type;
        Value = value;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Budget = budget;
        Currency = currency;
        SellerFundedShareBps = sellerFundedShareBps;
        OwnerSellerId = ownerSellerId;
        Status = PromotionStatus.Scheduled;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private Promotion()
    {
        Name = string.Empty;
        Currency = string.Empty;
    }

    public string Name { get; private set; }

    public PromotionScope Scope { get; private set; }

    public PromotionType Type { get; private set; }

    /// <summary>Pourcentage (15 = 15 %) ou montant fixe en unités entières.</summary>
    public long Value { get; private set; }

    public DateTime StartsAtUtc { get; private set; }

    public DateTime EndsAtUtc { get; private set; }

    /// <summary>
    /// Enveloppe totale. Null = pas de plafond global (à n'utiliser qu'en interne).
    /// </summary>
    public long? Budget { get; private set; }

    /// <summary>Part du budget déjà réservée ou engagée.</summary>
    public long BudgetConsumed { get; private set; }

    public string Currency { get; private set; }

    /// <summary>LA PART DE LA REMISE SUPPORTÉE PAR LE VENDEUR, EN POINTS DE BASE (D28).</summary>
    public int SellerFundedShareBps { get; private set; }

    /// <summary>
    /// Le vendeur à qui la campagne APPARTIENT. <c> null</c> = campagne de la
    /// plateforme.
    /// </summary>
    public Guid? OwnerSellerId { get; private set; }

    /// <summary>Lecture de <see cref="SellerFundedShareBps"/>.</summary>
    public PromotionFunder Funder => SellerFundedShareBps switch
    {
        PromotionFunding.PlatformOnly => PromotionFunder.Platform,
        PromotionFunding.SellerOnly => PromotionFunder.Seller,
        _ => PromotionFunder.Shared
    };

    public PromotionStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Budget encore disponible.</summary>
    public long BudgetRemaining => Budget is null ? long.MaxValue : Budget.Value - BudgetConsumed;

    /// <summary>Conditions d'éligibilité (§10.16, table <c>promotion_rules</c>).</summary>
    public IReadOnlyCollection<PromotionRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// LES DEUX DERNIERS PARAMÈTRES ONT UN DÉFAUT, ET IL FAIT PAYER LA PLATEFORME.
    /// </summary>
    public static Result<Promotion> Create(
        string? name, PromotionScope scope, PromotionType type, long value,
        DateTime startsAtUtc, DateTime endsAtUtc, long? budget, string currency = "XOF",
        int sellerFundedShareBps = PromotionFunding.PlatformOnly,
        Guid? ownerSellerId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.name_required", "Le nom de la campagne est obligatoire."));
        }

        if (endsAtUtc <= startsAtUtc)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.window_invalid", "La fin de campagne doit suivre son début."));
        }

        if (value <= 0)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.value_invalid", "La valeur de la remise doit être positive."));
        }

        // Une remise de plus de 100 % rendrait de l'argent à l'acheteur.
        if (type == PromotionType.Percent && value > 100)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.percent_above_hundred",
                "Une remise en pourcentage ne peut pas dépasser 100."));
        }

        if (budget is <= 0)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.budget_invalid", "Un budget défini doit être positif."));
        }

        if (sellerFundedShareBps is < PromotionFunding.PlatformOnly or > PromotionFunding.SellerOnly)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.funding_share_invalid",
                "La part financée par le vendeur doit tenir entre 0 et 10 000 points de base."));
        }

        // `Guid.Empty` n'est pas un vendeur : c'est la valeur qu'un appelant
        // produit quand il n'a rien à mettre.
        var proprietaire = ownerSellerId is { } candidat && candidat != Guid.Empty ? candidat : (Guid?)null;

        // UN PAYEUR SANS NOM EST REFUSÉ ICI, PAS RATTRAPÉ PLUS TARD.
        if (sellerFundedShareBps > PromotionFunding.PlatformOnly && proprietaire is null)
        {
            return Result.Failure<Promotion>(Error.Validation(
                "promotions.funding_owner_required",
                "Une remise financée par un vendeur doit désigner ce vendeur."));
        }

        var promotion = new Promotion(
            Guid.NewGuid(), name.Trim(), scope, type, value, startsAtUtc, endsAtUtc, budget,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            sellerFundedShareBps, proprietaire);

        promotion.Raise(new PromotionCreatedDomainEvent(
            promotion.Id, promotion.Name, scope.ToString(), type.ToString(), value,
            startsAtUtc, endsAtUtc, budget, promotion.Currency,
            sellerFundedShareBps, proprietaire));

        return promotion;
    }

    /// <summary>Ajoute une condition d'éligibilité.</summary>
    public Result AddRule(string? ruleType, string? ruleJson)
    {
        if (Status is not (PromotionStatus.Draft or PromotionStatus.Scheduled))
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.rule.campaign_started",
                "Une campagne démarrée ne peut plus recevoir de nouvelle condition."));
        }

        var regle = PromotionRule.Create(Id, ruleType, ruleJson);

        if (regle.IsFailure)
        {
            return Result.Failure(regle.Error);
        }

        _rules.Add(regle.Value);
        return Result.Success();
    }

    /// <summary>Dit si la campagne peut s'appliquer à ce contexte, sans rien consommer.</summary>
    public Result EnsureApplicable(PromotionContext context, DateTime nowUtc)
    {
        if (Status is PromotionStatus.Cancelled or PromotionStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.not_available", "Cette promotion n'est pas disponible."));
        }

        if (Status == PromotionStatus.Exhausted)
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.exhausted", "Le budget de cette promotion est épuisé."));
        }

        if (nowUtc < StartsAtUtc)
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.not_started", "Cette promotion n'a pas encore commencé."));
        }

        if (nowUtc > EndsAtUtc)
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.expired", "Cette promotion est terminée."));
        }

        // Global s'applique partout ; sinon l'univers doit correspondre exactement.
        if (Scope != PromotionScope.Global && Scope != context.Scope)
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.scope_mismatch", "Cette promotion ne s'applique pas à ce panier."));
        }

        if (!string.Equals(Currency, context.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.BusinessRule(
                "promotions.currency_mismatch", "Cette promotion ne s'applique pas à cette devise."));
        }

        // TOUTES LES CONDITIONS, ET LA PREMIÈRE QUI ÉCHOUE DÉCIDE.
        foreach (var regle in _rules)
        {
            var verdict = regle.Evaluate(context);

            if (verdict.IsFailure)
            {
                return verdict;
            }
        }

        return Result.Success();
    }

    /// <summary>Calcule la remise pour ce contexte, sans rien consommer.</summary>
    public PromotionDiscount ComputeDiscount(PromotionContext context)
        => Type switch
        {
            PromotionType.Percent => new PromotionDiscount(
                Math.Min(context.Subtotal, context.Subtotal * Value / 100), 0),

            PromotionType.Fixed => new PromotionDiscount(
                Math.Min(context.Subtotal, Value), 0),

            PromotionType.FreeDelivery => new PromotionDiscount(0, context.DeliveryFee),

            _ => PromotionDiscount.None
        };

    /// <summary>Répartit une remise accordée entre le vendeur et la plateforme.</summary>
    public FundedDiscount SplitDiscount(long discountAmount)
    {
        if (discountAmount <= 0)
        {
            return FundedDiscount.None;
        }

        var partVendeur = discountAmount * SellerFundedShareBps / PromotionFunding.TotalBasisPoints;

        return new FundedDiscount(partVendeur, discountAmount - partVendeur);
    }

    /// <summary>
    /// Consomme du budget. Refuse si le reste ne couvre pas la remise, et bascule
    /// en `Exhausted` dès que le budget est atteint.
    /// </summary>
    public Result ConsumeBudget(long amount)
    {
        if (amount <= 0)
        {
            return Result.Failure(Error.Validation(
                "promotions.consume_invalid", "Le montant consommé doit être positif."));
        }

        if (Budget is not null && BudgetRemaining < amount)
        {
            // Le budget ne couvre plus une remise entière : la campagne s'arrête
            // ici.
            Epuiser();

            return Result.Failure(Error.BusinessRule(
                "promotions.exhausted", "Le budget de cette promotion est épuisé."));
        }

        BudgetConsumed += amount;

        if (Budget is not null && BudgetConsumed >= Budget.Value)
        {
            Epuiser();
        }
        else if (Status == PromotionStatus.Scheduled)
        {
            Status = PromotionStatus.Active;
        }

        return Result.Success();
    }

    /// <summary>Bascule en « épuisée » et l'annonce, à la TRANSITION seulement.</summary>
    private void Epuiser()
    {
        if (Status == PromotionStatus.Exhausted)
        {
            return;
        }

        Status = PromotionStatus.Exhausted;
        Raise(new PromotionExhaustedDomainEvent(Id, Name, BudgetConsumed));
    }

    /// <summary>
    /// Rend du budget quand une réservation expire ou qu'une commande est annulée.
    /// </summary>
    public void ReleaseBudget(long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        BudgetConsumed = Math.Max(0, BudgetConsumed - amount);

        if (Status == PromotionStatus.Exhausted && BudgetRemaining > 0)
        {
            Status = PromotionStatus.Active;
        }
    }

    public void Cancel() => Status = PromotionStatus.Cancelled;
}
