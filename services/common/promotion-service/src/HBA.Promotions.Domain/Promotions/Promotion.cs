using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Domain.Promotions;

/// <summary>
/// Campagne promotionnelle (§10.16, table <c>promotions</c>).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE BUDGET EST LA SEULE CHOSE QUI EMPÊCHE UNE PROMOTION DE COÛTER L'INFINI.
///
/// Une remise sans plafond global est une promesse ouverte : si le code fuite sur
/// un réseau social, la plateforme paie autant de fois qu'il est utilisé. Les
/// plafonds par coupon et par utilisateur ne suffisent pas — mille comptes
/// respectant chacun sa limite épuisent quand même la trésorerie.
///
/// Le budget se consomme donc EN RÉSERVATION, pas au paiement. Réserver au
/// checkout et engager au paiement ferme la fenêtre pendant laquelle mille paniers
/// simultanés pourraient tous se croire dans le budget.
///
/// CONTREPARTIE ASSUMÉE : UN PANIER ABANDONNÉ IMMOBILISE DU BUDGET.
///
/// C'est pourquoi une réservation EXPIRE. Sans expiration, quelques milliers
/// d'abandons suffiraient à éteindre une campagne qui n'a rien coûté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Enveloppe totale. Null = pas de plafond global (à n'utiliser qu'en interne).</summary>
    public long? Budget { get; private set; }

    /// <summary>Part du budget déjà réservée ou engagée.</summary>
    public long BudgetConsumed { get; private set; }

    public string Currency { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PART DE LA REMISE SUPPORTÉE PAR LE VENDEUR, EN POINTS DE BASE (D28).
    ///
    /// 0 = la plateforme paie tout. 10 000 = le vendeur paie tout. Toute valeur
    /// intermédiaire est une remise COFINANCÉE — c'est exactement ce que D28
    /// exigeait de pouvoir exprimer « plus tard sans migration supplémentaire ».
    ///
    /// CE N'EST PAS UN CONFORT DE MODÉLISATION, C'EST LA COLONNE QUI EMPÊCHE
    /// LE PRÉLÈVEMENT SILENCIEUX.
    ///
    /// wallet calcule le gain du vendeur sur `UnitBasePrice - SellerDiscount`.
    /// Sans cette part, le producteur de `PriceBreakdownDto` n'a aucun moyen de
    /// décider ce qu'il écrit dans `SellerDiscount` : il écrit 0 (et la remise
    /// n'est imputée à personne), ou il écrit le total (et le vendeur paie les
    /// campagnes de la plateforme). Les deux sont faux, et aucun des deux ne
    /// laisse de trace.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int SellerFundedShareBps { get; private set; }

    /// <summary>
    /// Le vendeur à qui la campagne APPARTIENT. <c>null</c> = campagne de la
    /// plateforme.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PART NE SUFFIT PAS À FONDER LA GARDE D'APPARTENANCE. IL FAUT UN NOM.
    ///
    /// D28 dit qu'un financeur donne enfin la question à poser — « cette promotion
    /// est-elle la vôtre ? ». Une PART répond « un vendeur paie », jamais
    /// « LEQUEL ». Sans ce champ, `/api/v1/merchant/promotions` resterait fermée à
    /// `RequireAdmin` faute de pouvoir distinguer deux marchands, ce qui est
    /// précisément l'état que l'encadré de `PromotionEndpoints` décrit.
    ///
    /// PAS DE `OwnerType`, ET C'EST DÉLIBÉRÉ.
    ///
    /// `PromotionSummary` (HBA.Pricing.Contracts) porte un couple
    /// `OwnerType` / `OwnerId`. Il n'existe ici qu'un seul type de propriétaire
    /// non-plateforme — le vendeur — et `null` dit déjà « la plateforme ». Une
    /// colonne de discrimination qui ne discrimine rien serait une colonne à
    /// tenir, à indexer et à expliquer, pour aucune question qu'on sache poser.
    ///
    /// PROPRIÉTAIRE ET FINANCEUR SONT DEUX CHOSES, ET LEUR ÉCART EST LÉGITIME.
    ///
    /// Une campagne peut appartenir à un vendeur et être payée par la plateforme
    /// (`SellerFundedShareBps = 0`) : c'est un geste commercial de la place de
    /// marché sur la boutique de quelqu'un. L'inverse est en revanche INTERDIT —
    /// une part vendeur non nulle sans propriétaire désignerait un payeur que
    /// personne ne peut nommer. Voir <see cref="Create"/>.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Guid? OwnerSellerId { get; private set; }

    /// <summary>Lecture de <see cref="SellerFundedShareBps"/>. Non persistée.</summary>
    public PromotionFunder Funder => SellerFundedShareBps switch
    {
        PromotionFunding.PlatformOnly => PromotionFunder.Platform,
        PromotionFunding.SellerOnly => PromotionFunder.Seller,
        _ => PromotionFunder.Shared
    };

    public PromotionStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Budget encore disponible. `long.MaxValue` si la campagne n'a pas de plafond.</summary>
    public long BudgetRemaining => Budget is null ? long.MaxValue : Budget.Value - BudgetConsumed;

    /// <summary>Conditions d'éligibilité (§10.16, table <c>promotion_rules</c>).</summary>
    public IReadOnlyCollection<PromotionRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// LES DEUX DERNIERS PARAMÈTRES ONT UN DÉFAUT, ET IL FAIT PAYER LA
    /// PLATEFORME.
    ///
    /// C'est le même défaut que celui posé aux lignes DÉJÀ EN BASE par la
    /// migration `20260901000100_FinanceurDePromotion`, et pour la même raison :
    /// une campagne dont personne n'a désigné le financeur n'a pas de vendeur à
    /// qui la facturer. Le défaut inverse — part vendeur — prélèverait sur des
    /// marchands qui n'ont rien signé, par un chemin (le calcul des gains) où le
    /// prélèvement ne se voit pas.
    ///
    /// Les appelants qui savent — l'API marchand — passent la part explicitement.
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
        //
        // Refuser ici plutôt que plafonner au calcul : plafonner masquerait une
        // campagne saisie à 150 % qui continuerait d'exister en base, et que
        // quelqu'un « corrigerait » un jour en levant le plafond.
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
        // produit quand il n'a rien à mettre. La traiter comme « aucun
        // propriétaire » évite qu'une campagne se retrouve rattachée à un
        // identifiant que la garde d'appartenance ne pourra jamais faire
        // correspondre à personne — donc invisible et inannulable par son auteur.
        var proprietaire = ownerSellerId is { } candidat && candidat != Guid.Empty ? candidat : (Guid?)null;

        // UN PAYEUR SANS NOM EST REFUSÉ ICI, PAS RATTRAPÉ PLUS TARD.
        //
        // Une part vendeur non nulle sans propriétaire décrit une remise que
        // « le vendeur » paie, sans dire lequel. Le producteur de
        // `PriceBreakdownDto` n'aurait alors le choix qu'entre l'imputer à
        // n'importe quel vendeur du panier — donc au mauvais — et l'ignorer, donc
        // rendre la part décorative. Les deux se découvrent sur un relevé.
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

    /// <summary>
    /// Ajoute une condition d'éligibilité.
    ///
    /// REFUSÉE APRÈS LE DÉMARRAGE DE LA CAMPAGNE.
    ///
    /// Restreindre une campagne déjà active change la règle sous les pieds des
    /// clients : celui qui a rempli son panier pour atteindre un minimum qui
    /// n'existait pas hier ne comprendra pas le refus, et le support non plus.
    /// Une nouvelle condition, c'est une nouvelle campagne.
    /// </summary>
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

    /// <summary>
    /// Dit si la campagne peut s'appliquer à ce contexte, sans rien consommer.
    ///
    /// LE CODE D'ERREUR EST DÉLIBÉRÉMENT PRÉCIS ICI.
    ///
    /// « Ce coupon ne s'applique pas » est vrai pour six raisons différentes, et le
    /// client ne peut réagir qu'à la sienne : ajouter un article s'il manque un
    /// minimum, revenir demain si la campagne n'a pas commencé, renoncer si elle
    /// est épuisée. Un message unique les prive tous du seul renseignement utile.
    /// </summary>
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
        //
        // Un type de règle inconnu refuse ici — voir l'encadré de `PromotionRule`.
        // C'est le seul endroit où une condition d'éligibilité est consultée : la
        // dupliquer dans l'appelant ferait diverger les deux au premier ajout.
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

    /// <summary>
    /// Calcule la remise pour ce contexte, sans rien consommer.
    ///
    /// LA REMISE NE PEUT JAMAIS DÉPASSER CE QU'ELLE RÉDUIT.
    ///
    /// Une remise fixe de 5 000 sur un panier de 3 000 produirait un total négatif —
    /// donc un remboursement à quelqu'un qui n'a rien payé. Le plafonnement se fait
    /// ici, une fois, plutôt que dans chaque appelant.
    /// </summary>
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

    /// <summary>
    /// Répartit une remise accordée entre le vendeur et la plateforme.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST LE SEUL ENDROIT OÙ LA RÉPARTITION SE CALCULE.
    ///
    /// La recopier chez l'appelant — dans le fournisseur de tarification, dans le
    /// report en commande, dans wallet — la ferait diverger au premier partage
    /// cofinancé, et la divergence produirait des `SellerDiscount` différents
    /// selon le chemin emprunté par la même vente. Personne ne trouve cela en
    /// lisant du code : on le trouve en rapprochant deux relevés.
    ///
    /// ARITHMÉTIQUE ENTIÈRE, ET LE RESTE VA À LA PLATEFORME.
    ///
    /// Les montants sont en unités monétaires entières (§2). `1 001 × 5 000 / 10 000`
    /// vaut 500 en division entière, pas 500,5 : l'unité perdue est reprise par la
    /// plateforme, qui reçoit `total - partVendeur`. La somme des deux parts vaut
    /// donc TOUJOURS exactement la remise accordée — aucune unité n'apparaît, aucune
    /// ne disparaît — et le sens de l'arrondi favorise systématiquement le vendeur.
    ///
    /// C'est un choix, pas une fatalité : arrondir au plus proche aurait fait porter
    /// au vendeur un franc de plus une fois sur deux, pour un gain de justesse nul et
    /// une ligne de relevé inexplicable.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// Consomme du budget. Refuse si le reste ne couvre pas la remise, et bascule en
    /// `Exhausted` dès que le budget est atteint.
    ///
    /// ON NE SERT PAS UNE REMISE PARTIELLE.
    ///
    /// Accorder 300 quand il reste 300 sur une remise de 1 000 donnerait au client un
    /// montant qu'il n'a pas demandé, sans qu'aucun écran ne l'explique. Mieux vaut
    /// refuser franchement : la campagne est épuisée.
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
            // Le budget ne couvre plus une remise entière : la campagne s'arrête ici.
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

    /// <summary>
    /// Bascule en « épuisée » et l'annonce, à la TRANSITION seulement.
    ///
    /// SANS CETTE GARDE, CHAQUE CHECKOUT REFUSÉ REPUBLIERAIT L'ALERTE.
    ///
    /// Ce n'est pas un cas limite, c'est le cas courant : une fois le budget
    /// épuisé, TOUTE tentative de réservation suivante rappelle `ConsumeBudget`,
    /// retombe sur la branche « budget insuffisant », et arrive ici. Une campagne
    /// populaire qui vient de s'épuiser reçoit des dizaines d'appels par minute —
    /// et publierait autant d'événements `promotion.exhausted`, noyant la seule
    /// notification qui demande une décision humaine : remettre au budget, ou
    /// laisser mourir.
    ///
    /// UNE RÉOUVERTURE SUIVIE D'UN NOUVEL ÉPUISEMENT RÉ-ANNONCE, ET C'EST VOULU.
    ///
    /// `ReleaseBudget` peut rendre la campagne active — panier abandonné, commande
    /// annulée. Si elle s'épuise de nouveau ensuite, c'est un fait nouveau : le
    /// budget rendu a été reconsommé. Le taire laisserait le marketing sur une
    /// information périmée.
    /// </summary>
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
    ///
    /// LA CAMPAGNE REDEVIENT ACTIVE SI ELLE ÉTAIT ÉPUISÉE.
    ///
    /// Sans cela, un seul panier abandonné au mauvais moment éteindrait
    /// définitivement une campagne dont le budget est intact.
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
