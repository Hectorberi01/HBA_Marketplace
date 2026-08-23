namespace HBA.Financial.Wallet.Application.Pricing;

/// <summary>
/// Options de tarification du module Settlement (section de configuration
/// « Pricing »). Le prix produit payé par l'acheteur = prix vendeur net +
/// commission plateforme + frais provider, les deux frais étant calculés en
/// fraction du prix vendeur net. Utilisées à la répartition du brut d'une ligne
/// lors de l'accrual (confirmation de commande).
///
/// Liées manuellement dans le WalletModuleInstaller (même pattern que
/// Billing). Placées dans la couche Application car consommées par l'accrual
/// (la dépendance Infrastructure → Application interdit l'inverse).
/// </summary>
public sealed class PricingOptions
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE BARÈME D'AFFICHAGE — CE N'EST PLUS LE TAUX RÉELLEMENT PRÉLEVÉ.
    ///
    /// CETTE VALEUR ÉTAIT LA SEULE QUE L'ARGENT LISAIT, ET C'ÉTAIT LE DÉFAUT.
    ///
    /// L'accrual multipliait le brut par ce taux fixe. Un admin créait une règle
    /// « vendeur X à 5 % » dans le moteur de commission, la voyait dans son écran
    /// d'administration — et la plateforme prélevait quand même 10 %.
    /// `ICommissionModuleApi` n'avait aucun appelant hors de Billing.
    ///
    /// Ce que ce taux fait ENCORE, et pourquoi on le garde :
    ///
    ///   • il a construit le prix acheteur (`net × (1 + commission + provider)`,
    ///     côté Products) ; c'est donc le seul diviseur qui permette de retrouver
    ///     le prix vendeur net à partir du brut ;
    ///   • il alimente le DÉFAUT du moteur (voir <c>BillingOptions</c>), appliqué
    ///     quand aucune règle ne s'applique — c'est-à-dire, en pratique, presque
    ///     toujours.
    ///
    /// UNE SEULE VALEUR CONFIGURÉE, ET C'EST <c>Pricing:PlatformCommissionRate</c>.
    ///
    /// Ce champ et <c>BillingOptions.DefaultCommissionRate</c> ne sont pas deux
    /// réglages : les deux installeurs les lisent tous deux depuis
    /// <c>PlatformPricing</c>, qui refuse de démarrer si l'ancienne clé
    /// <c>Billing:DefaultCommissionRate</c> traîne encore. C'est cette clé-là qui a
    /// été retenue parce qu'elle est celle DÉJÀ DÉPLOYÉE, et parce qu'elle est en
    /// TAUX : basculer sur l'autre unité aurait relu « 0.1 » comme 0,1 %.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal PlatformCommissionRate { get; set; } = 0.10m;

    /// <summary>
    /// Taux des frais provider, en fraction du prix vendeur net (0.05 = 5 %).
    /// </summary>
    public decimal ProviderFeeRate { get; set; } = 0.05m;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// COMMISSION SUR LA RESTAURATION, DISTINCTE DE CELLE DE LA MARCHANDISE.
    ///
    /// RÉUTILISER `PlatformCommissionRate` AURAIT ÉTÉ UN CHOIX PAR DÉFAUT, PAS
    /// UNE DÉCISION.
    ///
    /// Les deux métiers n'ont ni la même structure de coûts ni les mêmes usages :
    /// une place de marché généraliste prélève quelques pour cent sur un produit
    /// qu'elle n'a ni stocké ni transporté, tandis que la livraison de repas
    /// supporte la course, le délai, la casse et la fraîcheur — et se pratique
    /// couramment bien plus haut.
    ///
    /// Un seul réglage aurait forcé à trancher entre deux métiers à chaque
    /// ajustement, et le premier qui aurait bougé aurait cassé l'autre en
    /// silence.
    ///
    /// La valeur par défaut est délibérément posée au même niveau que celle de la
    /// marchandise : elle ne prétend PAS être le bon taux, elle évite seulement
    /// qu'une installation non configurée prélève un montant surprenant. Le taux
    /// réel est une décision commerciale.
    ///
    /// ET C'EST POURQUOI LA RESTAURATION NE PASSE PAS PAR LE MOTEUR DE RÈGLES.
    ///
    /// La marchandise, elle, l'interroge désormais. Les portées du moteur sont
    /// Global / Catégorie / Vendeur, et sa « catégorie » est une catégorie de
    /// CATALOGUE, qu'un plat n'a pas. Y ranger la restauration imposerait un
    /// identifiant inventé — et une règle GLOBALE capterait les repas au passage,
    /// remettant les deux métiers sur un seul réglage, ce que ce champ existe
    /// justement pour éviter. Voir AccrueEarningsOnOrderConfirmedHandler.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal FoodCommissionRate { get; set; } = 0.10m;
}
