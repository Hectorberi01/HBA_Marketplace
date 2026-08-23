using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HBA.Shared.Infrastructure.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE BARÈME DE LA PLATEFORME — UNE SEULE DÉFINITION, POUR TOUT LE MONDE.
///
/// IL Y EN AVAIT TROIS, SUR TROIS CLÉS, DANS DEUX UNITÉS.
///
///   • <c>Products:CommissionPercent</c>      — un POURCENTAGE (10), servant à
///     calculer le PRIX ACHETEUR ;
///   • <c>Pricing:PlatformCommissionRate</c>  — un TAUX (0.1), servant à calculer
///     le GAIN VENDEUR ;
///   • <c>Billing:DefaultCommissionRate</c>   — un TAUX, pour ses propres règles.
///
/// Les trois valaient 10 %, mais par coïncidence : la première n'était même pas
/// dans appsettings.json et tombait sur son défaut interne.
///
/// POURQUOI C'ÉTAIT DANGEREUX
///
/// L'arithmétique ne se referme que si les deux premiers sont ÉGAUX. Products
/// calcule « prix acheteur = net × (1 + commission + frais) » ; Wallet reconstruit
/// le net en divisant par ce même facteur. Passer la commission à 12 % demandait
/// donc d'éditer deux clés distinctes, dans deux unités distinctes.
///
/// En oublier une : l'acheteur débité d'une marge de 12 % pendant que le vendeur
/// est débité de 10 %. L'écart n'apparaît nulle part — chaque module reste
/// cohérent avec lui-même, ils sont simplement en désaccord. Rien n'échoue.
///
/// UNE VALEUR NE PEUT PAS DIVERGER D'ELLE-MÊME. C'est tout l'objet de ce type.
///
/// L'UNITÉ EST LE TAUX, PAS LE POURCENTAGE.
///
/// C'est celle des clés DÉJÀ DÉPLOYÉES. Basculer <c>Pricing:*</c> en pourcentage
/// aurait fait relire « 0.1 » comme 0,1 % au lieu de 10 % — une division par cent
/// de toutes les commissions, sur une simple lecture de configuration.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PlatformPricing : IPlatformPricing
{
    public const string CommissionKey = "Pricing:PlatformCommissionRate";
    public const string ProviderFeeKey = "Pricing:ProviderFeeRate";

    /// <summary>
    /// Commission sur la restauration. Distincte de celle de la marchandise :
    /// deux métiers, deux structures de coûts, deux réglages.
    /// </summary>
    public const string FoodCommissionKey = "Pricing:FoodCommissionRate";

    /// <summary>
    /// Clés ABANDONNÉES. Leur présence fait échouer le démarrage.
    ///
    /// LES IGNORER SILENCIEUSEMENT SERAIT LE PIRE DES COMPORTEMENTS.
    ///
    /// Un environnement où <c>Products:CommissionPercent</c> vaut encore 12
    /// basculerait sans bruit sur la valeur de <c>Pricing:*</c> — et personne ne
    /// saurait, avant le rapprochement comptable, que la marge a changé. Mieux
    /// vaut un démarrage refusé, qui se lit dans les journaux en dix secondes.
    /// </summary>
    private static readonly (string Key, string Replacement)[] LegacyKeys =
    [
        ("Products:CommissionPercent", CommissionKey),
        ("Products:ProviderFeePercent", ProviderFeeKey),
        ("Billing:DefaultCommissionRate", CommissionKey)
    ];

    private const decimal DefaultCommissionRate = 0.10m;
    private const decimal DefaultProviderFeeRate = 0.05m;

    // MÊME DÉFAUT QUE LA MARCHANDISE, ET CE N'EST PAS UNE RECOMMANDATION.
    //
    // Il évite seulement qu'une installation non configurée prélève un montant
    // surprenant. Le taux réel de la restauration est une décision commerciale,
    // qui se pose dans `Pricing:FoodCommissionRate`.
    private const decimal DefaultFoodCommissionRate = 0.10m;

    public PlatformPricing(IConfiguration configuration)
    {
        RejectLegacyKeys(configuration);

        CommissionRate = Read(configuration, CommissionKey, DefaultCommissionRate);
        ProviderFeeRate = Read(configuration, ProviderFeeKey, DefaultProviderFeeRate);
        FoodCommissionRate = Read(configuration, FoodCommissionKey, DefaultFoodCommissionRate);
    }

    /// <summary>Commission plateforme, en fraction du prix vendeur net (0.10 = 10 %).</summary>
    public decimal CommissionRate { get; }

    /// <summary>Frais prestataire de paiement, en fraction du prix vendeur net.</summary>
    public decimal ProviderFeeRate { get; }

    /// <summary>Commission sur la restauration, en fraction du prix restaurant net.</summary>
    public decimal FoodCommissionRate { get; }

    private static void RejectLegacyKeys(IConfiguration configuration)
    {
        foreach (var (key, replacement) in LegacyKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"« {key} » n'est plus lu : le barème de la plateforme a une source unique. "
                    + $"Reportez sa valeur dans « {replacement} » — attention, en TAUX (0.12 pour 12 %), "
                    + "et non en pourcentage — puis retirez l'ancienne clé.");
            }
        }
    }

    private static decimal Read(IConfiguration configuration, string key, decimal fallback)
    {
        var raw = configuration[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        // Culture INVARIANTE, explicitement : s'appuyer sur un réglage global pour
        // lire un taux de commission serait fragile.
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var rate))
        {
            throw new InvalidOperationException(
                $"« {key} » vaut « {raw} », qui n'est pas un nombre. Attendu : un taux, par exemple « 0.1 » pour 10 %.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ON REFUSE DE DÉMARRER SUR UNE VALEUR ABERRANTE.
        //
        // Reprise de la validation qui n'existait que côté Products — elle
        // protège désormais aussi Wallet et Billing, qui n'en avaient aucune :
        // leur lecture acceptait n'importe quel nombre, silencieusement.
        //
        // Le cas qui coûte cher est le taux saisi en POURCENTAGE dans une clé qui
        // attend un taux : « 10 » au lieu de « 0.1 » multiplierait par cent la
        // commission. Le plafond à 0,5 l'attrape, et le message le dit.
        //
        // Une plage refusée à l'amorçage coûte un redémarrage ; le même réglage
        // accepté coûte une campagne de remboursement.
        // ═════════════════════════════════════════════════════════════════════
        if (rate is < 0m or > 0.5m)
        {
            throw new InvalidOperationException(
                $"« {key} » vaut {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}. "
                + "Attendu : un TAUX entre 0 et 0.5 (0.1 pour 10 %). "
                + "Une valeur comme « 10 » est un pourcentage — elle multiplierait la commission par cent, "
                + "sur chaque article du catalogue.");
        }

        return rate;
    }
}
