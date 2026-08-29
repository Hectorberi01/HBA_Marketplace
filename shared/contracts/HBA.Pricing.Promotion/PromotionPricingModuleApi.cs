using HBA.Pricing.Contracts;
using HBA.Promotions.Contracts;
using Microsoft.Extensions.Logging;

// DÉPLACÉ DEPUIS `HBA.Commerce.Infrastructure.Public` LE 29 AOÛT 2026.
//
// Il n'a jamais dépendu de quoi que ce soit de `cart-service` : deux contrats et
// un logger. Il vivait là par accident d'écriture, et cet accident a coûté à
// `food-cart-service` de démarrer sans aucune promotion — donc de refuser tout
// coupon en silence.
namespace HBA.Pricing.Promotion;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE FOURNISSEUR DE TARIFICATION, BRANCHÉ SUR promotion-service (ISSUE-033, D28).
///
/// CE QUI ÉTAIT CASSÉ, ET L'AMPLEUR EN ÉTAIT TOTALE.
///
/// `NeutralPricingModuleApi` était la SEULE implémentation d'`IPricingModuleApi`
/// du dépôt, enregistrée sans garde d'environnement dans les deux services de
/// panier. Elle rendait `SellerDiscount: 0, PlatformDiscount: 0,
/// FinalAmount = BaseAmount` et refusait TOUT coupon. Conséquence : aucune
/// campagne commerciale n'était possible, et promotion-service — complet, avec son
/// domaine, ses règles d'éligibilité, ses coupons, ses budgets et son API gRPC —
/// n'était appelé par PERSONNE.
///
/// ET IL NE SUFFISAIT PAS DE LE BRANCHER.
///
/// `PriceBreakdownDto` porte `SellerDiscount` ET `PlatformDiscount`, et wallet
/// calcule le gain du vendeur sur `UnitBasePrice - SellerDiscount`. Un fournisseur
/// qui recevrait un montant total sans savoir qui le finance n'aurait le choix
/// qu'entre deux erreurs : tout imputer au vendeur (donc prélever sur des
/// marchands qui n'ont rien signé, par un chemin invisible) ou tout mettre à zéro
/// (donc ne rien imputer du tout). C'est la raison d'être de D28, et c'est
/// pourquoi ISSUE-052 se traite AVANT ISSUE-033.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN COUPON EST PAR PANIER, CE CONTRAT EST PAR LIGNE.
///
/// C'est le vrai problème de ce fichier. `CalculatePriceAsync` est appelée une fois
/// PAR LIGNE. Évaluer le coupon contre le sous-total de la ligne accorderait une
/// remise « 1 000 F sur la commande » autant de fois qu'il y a de lignes.
///
/// D'où la forme retenue : le coupon est évalué contre le sous-total du PANIER
/// (`PriceRequest.CartSubtotal`), une fois par ligne — l'évaluation est en LECTURE
/// PURE, la rejouer ne consomme rien — puis chaque ligne reçoit sa QUOTE-PART, au
/// prorata de son sous-total. La somme des quote-parts ne dépasse jamais la remise
/// accordée : la division entière tronque, donc l'acheteur reçoit au pire quelques
/// francs de moins que la valeur faciale du coupon, jamais un franc de plus. Le
/// sens du dépassement est choisi, pas subi.
///
/// LE COÛT ASSUMÉ : N ÉVALUATIONS POUR UN PANIER DE N LIGNES.
///
/// Ce n'est pas la forme la plus économe — un seul appel par panier le serait —
/// mais elle ne demande AUCUN changement à `IPricingModuleApi`, dont l'unité est
/// la ligne, et donc aucun changement chez ses autres appelants. Le jour où le
/// coût comptera, c'est une mémoïsation par portée de requête qu'il faudra, pas
/// une réécriture du contrat.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR NE PAIE JAMAIS LA CAMPAGNE D'UN AUTRE.
///
/// La part financée par un vendeur n'est imputée à `SellerDiscount` que sur les
/// lignes DE CE VENDEUR. Sur les autres, elle retombe sur la plateforme, et le
/// repli est journalisé.
///
/// C'est un pis-aller, et il faut savoir pourquoi : `Promotion` n'a AUCUN ciblage
/// par vendeur — son périmètre est GLOBAL / MARKETPLACE / FOOD, et la seule
/// condition qu'elle sache évaluer est un montant minimum. Rien n'empêche donc
/// aujourd'hui une campagne financée par le vendeur A de s'appliquer à un panier
/// qui contient des articles de B. Le choix fait ici — la plateforme absorbe — est
/// le seul qui ne facture personne à tort. La vraie correction est un type de
/// règle « vendeur » dans `PromotionRuleTypes`, et elle n'est pas dans ce lot.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX PANNES, DEUX TRAITEMENTS OPPOSÉS, ET LA DIFFÉRENCE VAUT DE L'ARGENT.
///
/// promotion-service INJOIGNABLE sur ce chemin : on vend quand même, au plein
/// tarif, et on le DIT (journal `Warning`). L'évaluation est en lecture pure et
/// n'engage rien ; refuser de valoriser un panier parce qu'un service de remise
/// ne répond pas transformerait une panne de promotion en panne de vente. Le coût
/// est borné et visible : quelques paniers affichés sans remise, une ligne de
/// journal par ligne de panier.
///
/// Une RETENUE (`IPromotionModuleApi.ReserveAsync`) dont on perdrait la trace est
/// l'inverse : le budget est DÉJÀ débité côté promotion, et sans l'identifiant de
/// retenue plus personne ne peut ni l'engager ni la libérer. L'enveloppe est
/// perdue jusqu'à l'expiration, et l'expiration ne rend le budget que depuis le
/// lot ISSUE-053. C'est pourquoi `PromotionGrpcClient` n'avale AUCUNE
/// `RpcException` : ce qui remonte en exception est une vraie panne et doit
/// remonter. La retenue ne passe pas par ce fichier — elle appartient au checkout,
/// donc à order-service — et elle ne doit jamais recevoir le traitement clément
/// appliqué ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PromotionPricingModuleApi : IPricingModuleApi
{
    /// <summary>
    /// L'univers de ce service de panier.
    ///
    /// « MARKETPLACE » ET NON « GLOBAL ».
    ///
    /// `Promotion.EnsureApplicable` laisse passer une campagne GLOBALE quel que
    /// soit le contexte, et une campagne ciblée seulement si l'univers correspond.
    /// Annoncer « GLOBAL » écarterait donc toutes les campagnes MARKETPLACE — le
    /// contraire de ce qu'on veut — alors qu'annoncer « MARKETPLACE » laisse passer
    /// les deux.
    ///
    /// CE QUE CELA FERME : LES LIGNES FOOD DE CE PANIER.
    ///
    /// `Cart` est bi-nature (voir `CartLineSummary.Kind`) et peut porter des lignes
    /// de restauration. Elles ne recevront pas les campagnes FOOD, qui sont servies
    /// par food-cart-service. C'est une limite connue et non un oubli :
    /// `PriceRequest` ne transporte pas la nature de la ligne, et l'y ajouter pour
    /// évaluer deux campagnes par panier demanderait de décider laquelle gagne
    /// quand les deux s'appliquent — une question de commerce, pas de code.
    /// </summary>
    private const string Univers = "MARKETPLACE";

    /// <summary>
    /// Sous-total de sonde pour `ValidateCouponAsync` quand l'appelant ne connaît
    /// pas le panier.
    ///
    /// UN MILLIARD, ET C'EST UN CHIFFRE CHOISI, PAS UN `long.MaxValue`.
    ///
    /// `Promotion.ComputeDiscount` calcule `Subtotal * Value / 100` pour une remise
    /// en pourcentage : `long.MaxValue` déborderait silencieusement et rendrait un
    /// montant négatif. Un milliard de francs CFA dépasse tout panier réel et tout
    /// seuil `MINIMUM_SUBTOTAL` plausible, sans approcher la limite du type.
    ///
    /// La sonde ne MENT pas, mais elle répond à une question plus étroite : « ce
    /// code désigne-t-il une campagne vivante ? ». Une condition de montant minimum
    /// ne sera découverte qu'au calcul du panier. C'est pourquoi le lot D28 ajoute
    /// `cartSubtotal` à la signature : dès que l'appelant le connaît, la sonde ne
    /// sert plus.
    /// </summary>
    private const long SousTotalDeSonde = 1_000_000_000L;

    private readonly IPromotionModuleApi _promotions;
    private readonly ILogger<PromotionPricingModuleApi> _logger;

    public PromotionPricingModuleApi(
        IPromotionModuleApi promotions, ILogger<PromotionPricingModuleApi> logger)
    {
        _promotions = promotions;
        _logger = logger;
    }

    public async Task<PriceBreakdownDto> CalculatePriceAsync(
        PriceRequest request, CancellationToken cancellationToken = default)
    {
        var prixDeBase = SansRemise(request);

        // AUCUN CODE = AUCUNE REMISE, ET C'EST UNE LIMITE À CONNAÎTRE.
        //
        // `Promotion` ne s'applique que par COUPON : il n'existe ni ciblage par
        // produit, ni par catégorie, ni par vendeur, ni règle « première commande ».
        // `PriceRequest.ProductId`, `CategoryId`, `SellerId` et `IsFirstOrder` sont
        // donc reçus et NON EXPLOITÉS. Ce n'est pas un oubli : le vocabulaire
        // correspondant n'existe pas dans le domaine de promotion, et l'inventer ici
        // le ferait diverger du service qui fait autorité.
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return prixDeBase;
        }

        var sousTotalLigne = EnUnitesEntieres(request.Subtotal);

        // `CartSubtotal` à zéro veut dire « l'appelant ne sait pas ». On retombe
        // alors sur la ligne : exact pour une remise proportionnelle, généreux pour
        // une remise en montant fixe (elle serait accordée par ligne). Le repli est
        // JOURNALISÉ, parce qu'il coûte de l'argent et qu'il ne doit pas s'installer.
        var sousTotalPanier = EnUnitesEntieres(request.CartSubtotal);

        if (sousTotalPanier <= 0)
        {
            sousTotalPanier = sousTotalLigne;

            _logger.LogWarning(
                "Tarification : sous-total de panier absent pour la ligne {ProductId}. "
                + "Le coupon est evalue sur la seule ligne — une remise en montant fixe serait "
                + "accordee autant de fois qu'il y a de lignes. Verifier l'appelant de CalculatePriceAsync.",
                request.ProductId);
        }

        if (sousTotalLigne <= 0 || sousTotalPanier <= 0)
        {
            return prixDeBase;
        }

        var evaluation = await EvaluerAsync(
            request.Code!, request.BuyerId, sousTotalPanier, request.Currency, cancellationToken);

        // TROIS CONDITIONS À PLAT PLUTÔT QU'UN MOTIF NÉGATIF QUI CAPTURE.
        //
        // `evaluation is not { Valid: true } verdict` se compile, mais `verdict`
        // n'est affecté que lorsque le motif MATCHE — c'est-à-dire dans la branche
        // de sortie — et serait donc inutilisable dans le second membre du `||`.
        //
        // Écrire les trois conditions rend surtout visible la TROISIÈME : une
        // évaluation valide dont la remise est nulle. Ce n'est pas un cas
        // théorique — c'est ce que rend une campagne `FREE_DELIVERY` évaluée sans
        // frais de port, et le panier n'en connaît jamais.
        if (evaluation is null || !evaluation.Valid || evaluation.Discount <= 0)
        {
            // UN COUPON REFUSÉ N'EST PAS JOURNALISÉ ICI.
            //
            // Saisir un code périmé est un usage ordinaire du champ, et une ligne
            // de journal PAR LIGNE DE PANIER et par rafraîchissement d'écran
            // noierait celles qui comptent. Le motif du refus est déjà rendu au
            // client par `ApplyCouponCommandHandler`, qui interroge
            // `ValidateCouponAsync`.
            return prixDeBase;
        }

        // ── La quote-part de cette ligne dans la remise du panier ─────────────
        //
        // `decimal` POUR L'INTERMÉDIAIRE, `long` POUR LE RÉSULTAT.
        //
        // `Discount * sousTotalLigne` peut dépasser `long` sur des montants
        // extrêmes ; `decimal` porte 28 chiffres significatifs et ne déborde pas.
        // Le plancher est explicite : la somme des quote-parts reste INFÉRIEURE OU
        // ÉGALE à la remise accordée. Arrondir au plus proche ferait, sur un panier
        // à beaucoup de lignes, dépenser plus que l'enveloppe consommée côté
        // promotion — un écart entre ce que la campagne a débité et ce que
        // l'acheteur a reçu, que rien ne rapprocherait.
        var remiseLigne = (long)Math.Floor(
            (decimal)evaluation.Discount * sousTotalLigne / sousTotalPanier);

        if (remiseLigne <= 0)
        {
            return prixDeBase;
        }

        var partVendeur = PartVendeur(request, evaluation, remiseLigne);
        var partPlateforme = remiseLigne - partVendeur;

        // ── Du montant de LIGNE au montant UNITAIRE ───────────────────────────
        //
        // ═════════════════════════════════════════════════════════════════════
        // LA FRONTIÈRE `long` (unités entières, §2) ↔ `decimal` EST ICI, ET
        // C'EST LE SEUL ENDROIT OÙ ELLE EST TRAVERSÉE.
        //
        // Tout le calcul de remise se fait en `long` : le franc CFA n'a pas de
        // sous-unité, et un arrondi sur une remise en pourcentage répété un
        // million de fois n'est plus une erreur d'arrondi. `PriceBreakdownDto`,
        // lui, est en `decimal` et par UNITÉ.
        //
        // La division par la quantité produit donc un nombre qui n'est PAS un
        // montant monétaire : une remise de ligne de 1 000 F sur 3 unités vaut
        // 333,333… par unité. C'est assumé, parce que `CartPricer` remultiplie
        // immédiatement par la MÊME quantité : le total de ligne est ce qui
        // compte, et il est juste.
        //
        // CE QU'UN CONSOMMATEUR NE DOIT PAS FAIRE.
        //
        // Arrondir `SellerDiscount` ou `PlatformDiscount` à l'unité PUIS
        // remultiplier par la quantité : 333 × 3 = 999, et il manque un franc que
        // personne ne retrouve. La valeur monétaire vraie est le TOTAL DE LIGNE.
        //
        // CE QUE `decimal` NE GARANTIT PAS TOUT À FAIT.
        //
        // `(1000m / 3m) × 3m` vaut 999,999…9 et non 1 000 : `decimal` a 28 chiffres
        // significatifs, pas l'exactitude sur les rationnels. L'écart est de
        // l'ordre de 1e-25 franc par ligne. Il est sans conséquence tant que
        // personne ne compare deux totaux avec `==` — et c'est la raison pour
        // laquelle les montants qui font foi restent, partout ailleurs, des
        // entiers.
        // ═════════════════════════════════════════════════════════════════════
        var quantite = request.Quantity > 0 ? request.Quantity : 1;

        var remiseVendeurUnitaire = (decimal)partVendeur / quantite;
        var remisePlateformeUnitaire = (decimal)partPlateforme / quantite;

        // Le prix final ne peut pas devenir négatif : le domaine plafonne déjà la
        // remise au sous-total, mais un `CartSubtotal` incohérent avec les
        // `Subtotal` de ligne — panier modifié entre deux appels — le pourrait.
        var prixFinal = Math.Max(
            0m, request.BaseAmount - remiseVendeurUnitaire - remisePlateformeUnitaire);

        return new PriceBreakdownDto(
            BaseAmount: request.BaseAmount,
            SellerDiscount: remiseVendeurUnitaire,
            PlatformDiscount: remisePlateformeUnitaire,
            FinalAmount: prixFinal,
            Currency: request.Currency);
    }

    public async Task<CouponValidation> ValidateCouponAsync(
        string code, Guid buyerId, decimal cartSubtotal = 0m,
        CancellationToken cancellationToken = default)
    {
        var sousTotal = EnUnitesEntieres(cartSubtotal);

        if (sousTotal <= 0)
        {
            sousTotal = SousTotalDeSonde;
        }

        var evaluation = await EvaluerAsync(code, buyerId, sousTotal, "XOF", cancellationToken);

        // SERVICE INJOIGNABLE : ON REFUSE D'ATTACHER, ON N'ACCEPTE PAS.
        //
        // C'est l'inverse du choix fait dans `CalculatePriceAsync`, et les deux
        // sont justes. Là-bas, valoriser sans remise laisse la vente se faire ;
        // ici, accepter un code qu'on n'a pas pu valider l'attacherait au panier et
        // l'acheteur découvrirait au checkout qu'il n'a jamais rien donné — c'est
        // très exactement le parcours que cette méthode existe pour éviter.
        //
        // Le motif rendu dit « momentanément » : il est réessayable, et l'écran
        // peut le proposer.
        if (evaluation is null)
        {
            return CouponValidation.Invalid(
                "promotions.unavailable",
                "Le service de promotion est momentanément indisponible. Réessayez dans un instant.");
        }

        return evaluation.Valid
            ? CouponValidation.Valid()
            : CouponValidation.Invalid(
                evaluation.Reason ?? "promotions.coupon.not_applicable",
                evaluation.Message);
    }

    /// <summary>
    /// Interroge promotion-service, ou rend <c>null</c> si le service n'a pas
    /// répondu.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ON ATTRAPE `Exception` ET NON `RpcException`, DÉLIBÉRÉMENT.
    ///
    /// Ce fichier dépend d'`IPromotionModuleApi`, pas de gRPC. C'est ce qui permet
    /// de rebrancher une implémentation en processus par une seule ligne
    /// d'enregistrement — et c'est exactement ce que l'encadré de
    /// `PromotionGrpcClient` décrit. Nommer `RpcException` ici y ferait entrer le
    /// transport, et l'attraper ne couvrirait de toute façon pas les autres pannes
    /// possibles (résolution DNS, délai d'attente du canal, sérialisation).
    ///
    /// L'ANNULATION RESSORT, ELLE. Un `OperationCanceledException` déclenché par
    /// le jeton n'est pas une panne du service : c'est le client qui est parti.
    /// L'avaler ferait continuer un travail que personne n'attend, et ferait passer
    /// un départ de client pour une indisponibilité de promotion dans les journaux.
    ///
    /// AUCUN REFUS MÉTIER NE PASSE PAR ICI. `EvaluateAsync` rend
    /// `valid: false` — une RÉPONSE — pour un code périmé, hors périmètre ou
    /// épuisé. Ce qui arrive en exception est une vraie panne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task<PromotionEvaluationResult?> EvaluerAsync(
        string code, Guid buyerId, long subtotal, string currency, CancellationToken cancellationToken)
    {
        try
        {
            return await _promotions.EvaluateAsync(
                code,
                new PromotionEvaluationContext(
                    Univers,
                    subtotal,

                    // FRAIS DE LIVRAISON À ZÉRO, ET UNE CAMPAGNE EN PÂTIT.
                    //
                    // Le panier ne connaît pas le frais de port : il est calculé
                    // plus tard, par delivery-pricing, sur une adresse que le
                    // panier n'a pas encore. Une campagne `FREE_DELIVERY` évaluée
                    // ici rend donc une remise nulle et sera refusée avec
                    // `promotions.no_discount`. C'est une limite connue : la
                    // livraison offerte ne peut pas s'appliquer au niveau du
                    // panier, elle appartient au checkout.
                    0,
                    string.IsNullOrWhiteSpace(currency) ? "XOF" : currency,
                    buyerId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Tarification : promotion-service injoignable. Le panier est valorise SANS REMISE "
                + "et la vente se poursuit. Aucun budget n'a ete engage — l'evaluation est en lecture pure.");

            return null;
        }
    }

    /// <summary>
    /// La part de la remise de ligne imputable au VENDEUR de cette ligne.
    ///
    /// UN VENDEUR NE PAIE QUE SA PROPRE CAMPAGNE. Voir l'encadré de la classe.
    /// </summary>
    private long PartVendeur(PriceRequest request, PromotionEvaluationResult verdict, long remiseLigne)
    {
        if (verdict.SellerFundedDiscount <= 0)
        {
            return 0;
        }

        if (verdict.OwnerSellerId is not { } financeur || financeur != request.SellerId)
        {
            _logger.LogWarning(
                "Tarification : campagne {PromotionId} financee par le vendeur {Financeur}, "
                + "appliquee a une ligne du vendeur {Vendeur}. La part vendeur ({Part}) est imputee a la "
                + "PLATEFORME pour ne facturer personne a tort. `Promotion` n'a pas de ciblage par vendeur.",
                verdict.PromotionId, verdict.OwnerSellerId, request.SellerId, verdict.SellerFundedDiscount);

            return 0;
        }

        // Même règle d'arrondi que `Promotion.SplitDiscount` : le plancher pour le
        // vendeur, le reste pour la plateforme. La refaire dans l'autre sens ferait
        // porter au vendeur un franc de plus une fois sur deux, sur une ligne de
        // relevé que rien n'expliquerait.
        return (long)Math.Floor(
            (decimal)remiseLigne * verdict.SellerFundedDiscount / verdict.Discount);
    }

    /// <summary>
    /// Le prix de base, sans aucune remise — la réponse à « aucun coupon », « coupon
    /// refusé » et « promotion-service injoignable ».
    /// </summary>
    private static PriceBreakdownDto SansRemise(PriceRequest request)
        => new(
            BaseAmount: request.BaseAmount,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalAmount: request.BaseAmount,
            Currency: request.Currency);

    /// <summary>
    /// `decimal` → unités monétaires entières (§2).
    ///
    /// ARRONDI AU PLUS PROCHE, ET NON TRONCATURE.
    ///
    /// Le franc CFA n'a pas de sous-unité : un montant fractionnaire venu du panier
    /// est déjà une anomalie, pas une précision à préserver. Tronquer 4 999,99 en
    /// 4 999 ferait échouer une condition « panier d'au moins 5 000 » pour un
    /// centime qui n'existe pas dans la devise. L'écart introduit est d'au plus une
    /// unité sur la BASE d'évaluation, donc au plus une unité sur la remise.
    /// </summary>
    private static long EnUnitesEntieres(decimal montant)
        => montant <= 0m ? 0L : (long)Math.Round(montant, MidpointRounding.AwayFromZero);
}
