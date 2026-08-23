using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUELS GAINS UN RETRAIT CONSOMME-T-IL ? — LE MAILLON QUI N'EXISTAIT PAS.
///
/// UN MÊME GAIN POUVAIT ÊTRE PAYÉ DEUX FOIS, PAR DEUX CANAUX AVEUGLES L'UN À
/// L'AUTRE.
///
/// Le retrait à la demande débitait le portefeuille et envoyait un versement
/// Mobile Money réel, sans jamais toucher aux `SellerEarning`. Le lot de
/// reversement prenait tous les gains « Released » et les marquait « Settled »,
/// sans jamais toucher au portefeuille. Un gain sorti par retrait — argent
/// réellement parti — restait donc payable, et le lot suivant le repayait. Perte
/// sèche, silencieuse, et d'autant plus difficile à voir qu'aucun des deux
/// registres n'était faux de son côté.
///
/// Ce service est la moitié manquante du canal A : il impute un retrait sur des
/// gains précis, et sait les rendre payables si le retrait échoue.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerEarningImputation
{
    private readonly ISellerEarningRepository _earnings;

    public SellerEarningImputation(ISellerEarningRepository earnings) => _earnings = earnings;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RÈGLE D'IMPUTATION : LE PLUS ANCIEN D'ABORD (PEPS / FIFO).
    ///
    /// Trois raisons, dans cet ordre d'importance :
    ///
    ///   1. C'est le seul ordre OBSERVABLE et REPRODUCTIBLE. Le vendeur lit son
    ///      relevé dans l'ordre de ses ventes ; l'administrateur aussi. Toute autre
    ///      règle — le plus gros d'abord, la combinaison qui tombe juste — suppose
    ///      un choix que personne n'a fait, et fait diverger deux exécutions
    ///      identiques.
    ///
    ///   2. Elle BORNE L'ÂGE du plus vieux gain non soldé. Un gain reste
    ///      contre-passable tant qu'un retour peut survenir : plus vite il est
    ///      soldé, plus tôt la fenêtre se referme. Payer les récents d'abord
    ///      laisserait indéfiniment traîner les plus anciens.
    ///
    ///   3. C'est la convention comptable par défaut sur des créances fongibles.
    ///      Quand il faut expliquer une imputation à un vendeur — ou à un
    ///      contrôleur — « vos plus anciennes ventes d'abord » se défend sans
    ///      argumentaire.
    ///
    /// UN MONTANT NE TOMBE PRESQUE JAMAIS JUSTE, ET C'EST LE VRAI PIÈGE.
    ///
    /// Le vendeur retire 50 000 ; ses gains payables valent 18 000, 22 000,
    /// 15 000. Les deux premiers font 40 000 ; le troisième ferait 55 000, donc
    /// dépasserait. On s'arrête AVANT : deux gains sont soldés, et 10 000 francs
    /// du troisième sont sortis du portefeuille sans que son statut bouge.
    ///
    /// On ne coupe PAS le gain en deux. Un gain est l'unité qui porte le brut, la
    /// commission et les frais du prestataire : le scinder obligerait à répartir
    /// ces trois montants, avec les arrondis que cela suppose, sur toute la chaîne
    /// aval (relevés, contre-passation, lots). Une complexité permanente pour un
    /// reliquat temporaire.
    ///
    /// Le reliquat n'est pas perdu pour autant : il est retenu par le RESTE du
    /// dispositif — le lot de reversement plafonne désormais ce qu'il verse au
    /// solde réellement disponible du portefeuille (voir
    /// `RunSettlementCommandHandler`). Le troisième gain sera donc soldé par ce
    /// lot, mais payé 5 000 et non 15 000 : les 10 000 déjà sortis ne repartent
    /// pas. Le total versé reste exact au franc près, et c'est le portefeuille —
    /// non le statut des gains — qui l'assure.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>
    /// Le RELIQUAT : la part du retrait qu'aucun gain entier ne couvre. Nul
    /// lorsque le montant tombe juste. Sert au journal, pas au calcul.
    /// </returns>
    public async Task<decimal> ImputeWithdrawalAsync(
        Guid sellerId, decimal amount, Guid withdrawalId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        var payables = await _earnings.ListReleasedBySellerAsync(sellerId, cancellationToken);

        // LE NET RESTANT, PAS LE NET D'ORIGINE — SUR LES TROIS LIGNES CI-DESSOUS.
        //
        // Un gain partiellement REPRIS (un article sur trois rendu) reste payable,
        // mais amputé. Imputer son `NetAmount` d'origine ferait consommer au retrait
        // plus que ce que le gain vaut encore : le vendeur solderait, avec un
        // retrait, une part de vente qui lui a déjà été reprise — et le lot suivant
        // ne trouverait plus rien à lui verser pour la part réellement due.
        var impute = 0m;
        foreach (var earning in payables)
        {
            if (earning.RemainingNetAmount <= 0m)
            {
                // Un gain nul ne consomme rien mais doit sortir de la file payable :
                // le laisser « Released » le ferait revenir à chaque imputation, et
                // il finirait dans un lot pour un versement de zéro franc.
                earning.MarkSettledByWithdrawal(withdrawalId);
                continue;
            }

            if (impute + earning.RemainingNetAmount > amount)
            {
                // ON S'ARRÊTE, ON NE SAUTE PAS AU SUIVANT.
                //
                // Continuer pour trouver un gain plus petit qui « rentre » romprait
                // l'ordre chronologique, et deux retraits du même montant, sur les
                // mêmes gains, n'imputeraient plus la même chose selon l'ordre
                // d'arrivée. La règle vaut par sa prévisibilité.
                break;
            }

            if (earning.MarkSettledByWithdrawal(withdrawalId))
            {
                impute += earning.RemainingNetAmount;
            }
        }

        return amount - impute;
    }

    /// <summary>
    /// Rend payables les gains qu'un retrait avait consommés : refus admin, rejet
    /// du prestataire, échec confirmé à la réconciliation.
    ///
    /// DOIT ACCOMPAGNER CHAQUE RECRÉDIT DU PORTEFEUILLE, SANS EXCEPTION.
    ///
    /// Les trois chemins de remboursement recréditaient le solde principal. Si
    /// l'un d'eux oubliait de libérer les gains, le vendeur retrouverait son argent
    /// au portefeuille mais ses gains resteraient « Settled » : ils ne
    /// reviendraient dans aucun lot, et le solde ne redescendrait jamais. Le
    /// vendeur serait payé, puis payé encore, sans que rien ne se solde.
    ///
    /// Idempotent par <see cref="SellerEarning.Unsettle"/> : un gain déjà payable
    /// n'est pas touché.
    /// </summary>
    /// <returns>Le nombre de gains rendus payables (journalisation).</returns>
    public async Task<int> ReleaseWithdrawalAsync(Guid withdrawalId, CancellationToken cancellationToken = default)
    {
        var imputes = await _earnings.ListByWithdrawalAsync(withdrawalId, cancellationToken);

        var liberes = 0;
        foreach (var earning in imputes)
        {
            if (earning.Status == EarningStatus.Settled)
            {
                earning.Unsettle();
                liberes++;
            }
        }

        return liberes;
    }
}
