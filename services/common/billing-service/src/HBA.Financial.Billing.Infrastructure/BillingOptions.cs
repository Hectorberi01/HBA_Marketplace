namespace HBA.Financial.Billing.Infrastructure;

/// <summary>
/// Options du module Billing (section de configuration « Billing »).
/// </summary>
public sealed class BillingOptions
{
    /// <summary>
    /// Taux de commission plateforme par défaut, en fraction (0.10 = 10 %).
    /// Appliqué à CHAQUE vente lorsqu'aucune règle de commission spécifique
    /// (Global / Catégorie / Vendeur) n'est définie. Évite le prélèvement nul
    /// silencieux : par défaut la plateforme prélève toujours ce taux fixe.
    ///
    /// CE N'EST PAS UN SECOND RÉGLAGE, MAIS UNE COPIE DÉRIVÉE.
    ///
    /// Il n'existe AUCUNE clé « Billing:DefaultCommissionRate » : sa présence en
    /// configuration fait échouer le démarrage (voir <c>PlatformPricing</c>).
    /// Cette valeur est recopiée de <c>Pricing:PlatformCommissionRate</c> par
    /// <c>BillingModuleInstaller</c> — la même que <c>PricingOptions</c> reçoit.
    /// Deux défauts configurables auraient pu diverger ; celui-ci ne le peut pas.
    ///
    /// CE DÉFAUT PORTE MAINTENANT L'ARGENT, PAS SEULEMENT UN APERÇU.
    ///
    /// Depuis que la comptabilisation des gains interroge le moteur, c'est cette
    /// valeur qui s'applique à toute vente sans règle — c'est-à-dire à presque
    /// toutes. La mettre à 0 ne « désactive » pas le moteur : elle offre la
    /// commission de chaque vente.
    /// </summary>
    public decimal DefaultCommissionRate { get; set; } = 0.10m;
}
