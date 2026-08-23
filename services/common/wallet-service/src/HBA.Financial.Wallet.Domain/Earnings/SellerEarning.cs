using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Earnings;

/// <summary>Identité forte d'un gain vendeur.</summary>
public readonly record struct SellerEarningId(Guid Value)
{
    public static SellerEarningId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statut d'un gain dans le cycle de reversement :
/// Accrued (comptabilisé à la confirmation, fonds en escrow) → Released
/// (livraison confirmée, escrow libéré, payable) → Settled (inclus dans un lot
/// de payout). Reversed couvre les retours/remboursements.
/// </summary>
public enum EarningStatus
{
    Accrued = 0,
    Released = 1,
    Settled = 2,
    Reversed = 3
}

/// <summary>
/// Ce qu'une reprise a RÉELLEMENT pu inscrire sur un gain, APRÈS bornage.
///
/// CE N'EST PAS FORCÉMENT CE QUI A ÉTÉ DEMANDÉ, ET L'APPELANT DOIT S'EN SERVIR.
///
/// Une reprise est plafonnée à ce qui reste du gain (voir <see cref="SellerEarning.Reverse"/>).
/// Si l'appelant débitait le portefeuille du montant qu'il a CALCULÉ plutôt que de
/// celui-ci, le grand livre et le gain raconteraient deux histoires différentes dès
/// le premier dépassement — et c'est précisément le dépassement qu'on borne.
/// </summary>
public sealed record EarningReversal(
    decimal GrossAmount, decimal CommissionAmount, decimal ProviderFeeAmount, decimal NetAmount);

/// <summary>
/// Gain vendeur accumulé pour une ligne de commande confirmée. Brut = prix
/// produit payé par l'acheteur pour la ligne ; net = brut − commission
/// plateforme − frais provider. Les réductions financées par la plateforme
/// n'entament pas le revenu vendeur. Sert de grand livre alimenté par les
/// events, batché ensuite en reversements.
/// </summary>
public sealed class SellerEarning : AggregateRoot<SellerEarningId>
{
    private SellerEarning()
    {
    }

    private SellerEarning(
        SellerEarningId id, Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
        : base(id)
    {
        OrderId = orderId;
        OfferId = offerId;
        SellerId = sellerId;
        ProductId = productId;
        GrossAmount = grossAmount;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        NetAmount = grossAmount - commissionAmount - providerFeeAmount;
        Currency = currency;
        Status = EarningStatus.Accrued;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal ProviderFeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public EarningStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public Guid? SettlementBatchId { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RETRAIT QUI A SOLDÉ CE GAIN — L'AUTRE MOITIÉ DU CYCLE, QUI MANQUAIT.
    ///
    /// CE GAIN POUVAIT ÊTRE ENCAISSÉ DEUX FOIS, ET AUCUN DES DEUX CANAUX NE
    /// VOYAIT L'AUTRE.
    ///
    /// Deux chemins mènent l'argent au vendeur : le retrait à la demande, qui
    /// débitait le portefeuille et déclenchait un versement Mobile Money réel, et
    /// le lot de reversement, qui marquait des gains « soldés » sans jamais
    /// toucher au portefeuille. Un gain déjà parti par retrait restait donc
    /// « Released » — donc payable — et le lot suivant le repayait.
    ///
    /// Le colonne était le maillon manquant : `SettlementBatchId` disait quel LOT
    /// avait soldé un gain, et rien ne disait quel RETRAIT l'avait fait.
    ///
    /// DEUX COLONNES ET NON UNE, MALGRÉ LA TENTATION.
    ///
    /// Ranger un identifiant de retrait dans `SettlementBatchId` évitait une
    /// migration — la colonne n'a pas de clé étrangère, rien ne s'y opposait
    /// techniquement. Mais la question « quel lot a soldé ce gain ? » aurait alors
    /// répondu un identifiant qui ne joint sur rien. Un modèle qui ment finit
    /// toujours par produire du code qui se trompe.
    ///
    /// Les deux sont mutuellement exclusives : un gain est soldé par un lot OU par
    /// un retrait, jamais les deux — c'est précisément ce que ce travail garantit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid? SettledByWithdrawalId { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI A DÉJÀ ÉTÉ REPRIS SUR CE GAIN — QUATRE CUMULS, PAS UN DRAPEAU.
    ///
    /// `EarningStatus.Reversed` EXISTAIT DEPUIS L'ORIGINE ET PERSONNE NE LE
    /// POSAIT JAMAIS.
    ///
    /// La contre-passation d'un retour débitait le PORTEFEUILLE du vendeur et
    /// laissait le gain « Released ». Le lot de reversement suivant le ramassait et
    /// le comptait payable : une vente remboursée restait dans le relevé du vendeur
    /// et dans le calcul du lot. On reprenait l'argent d'un côté pour le reverser de
    /// l'autre, et aucun des deux registres n'était faux de son côté.
    ///
    /// DES CUMULS ET NON UN BOOLÉEN, PARCE QU'UN RETOUR EST SOUVENT PARTIEL.
    ///
    /// Un client renvoie un article sur trois. Un drapeau « repris » sortirait toute
    /// la commande du circuit ; une seule colonne « brut repris » ne dirait ni quelle
    /// commission ni quels frais ont été restitués, et il faudrait les recalculer —
    /// c'est exactement la duplication de calcul monétaire que
    /// `ReverseEarningsOnReturnRefundedHandler` dénonce en tête de fichier. Les
    /// quatre montants sont donc INSCRITS tels qu'ils ont été appliqués.
    ///
    /// CE QUE CES CUMULS NE COUVRENT PAS.
    ///
    /// Ils ne portent AUCUNE trace de QUEL retour a repris quoi : c'est une somme,
    /// pas un journal. Le détail vit au grand livre, sous `("refund", returnRequestId)`.
    /// Répondre à « quels retours ont touché ce gain ? » suppose de rapprocher les
    /// deux, et rien ne le fait aujourd'hui.
    ///
    /// L'annulation de commande (`ReverseEarningsOnOrderCancelledHandler`) ne les
    /// alimente PAS non plus : elle débite le portefeuille sans toucher au gain,
    /// exactement comme le faisait le retour avant ce travail. C'est le même défaut,
    /// sur un autre chemin, et il reste ouvert.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal ReversedGrossAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedCommissionAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedProviderFeeAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedNetAmount { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI RESTE DÛ AU VENDEUR SUR CE GAIN — LA SEULE BASE PAYABLE.
    ///
    /// TOUT CE QUI ADDITIONNE UN GAIN DOIT ADDITIONNER CECI, ET NON `NetAmount`.
    ///
    /// `NetAmount` est le montant D'ORIGINE, et il ne bouge pas : un montant appliqué
    /// se relit, il ne se réécrit pas. Mais un lot qui somme `NetAmount` reverse une
    /// vente déjà remboursée — c'est tout l'objet de ce travail. Le net RESTANT est la
    /// différence, et c'est lui que le lot, l'imputation d'un retrait et le relevé
    /// doivent additionner.
    ///
    /// NON PERSISTÉ, ET CE CHOIX A UN PRIX.
    ///
    /// Une colonne dérivée de deux autres finit par diverger d'elles — il suffit d'un
    /// chemin d'écriture qui oublie de la rafraîchir. Elle est donc calculée, et
    /// `SellerEarningConfiguration` l'IGNORE explicitement (sans quoi EF chercherait
    /// une colonne inexistante au démarrage).
    ///
    /// Le prix : ces bornes ne sont PAS traduisibles en SQL. Toute somme qui les
    /// utilise se fait donc EN MÉMOIRE, après matérialisation de la requête. Sur des
    /// volumes où cela deviendrait coûteux, il faudra persister le net restant — et
    /// accepter le risque de divergence qu'on refuse ici.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal RemainingGrossAmount => Math.Max(0m, GrossAmount - ReversedGrossAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingCommissionAmount => Math.Max(0m, CommissionAmount - ReversedCommissionAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingProviderFeeAmount => Math.Max(0m, ProviderFeeAmount - ReversedProviderFeeAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingNetAmount => Math.Max(0m, NetAmount - ReversedNetAmount);

    public static Result<SellerEarning> Create(
        Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("settlement.seller_required", "Le vendeur est obligatoire.");
        }

        if (grossAmount < 0m || commissionAmount < 0m || providerFeeAmount < 0m)
        {
            return Error.Validation("settlement.amount_invalid", "Les montants ne peuvent pas être négatifs.");
        }

        return new SellerEarning(
            SellerEarningId.New(), orderId, offerId, sellerId, productId,
            grossAmount, commissionAmount, providerFeeAmount, currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Rend le gain payable à la livraison confirmée (escrow libéré). Idempotent ;
    /// ne s'applique qu'à un gain comptabilisé (Accrued).
    /// </summary>
    public void Release()
    {
        if (Status != EarningStatus.Accrued)
        {
            return;
        }

        Status = EarningStatus.Released;
        ReleasedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Rattache le gain à un lot de reversement (devient soldé).
    ///
    /// SEUL UN GAIN PAYABLE PEUT ÊTRE SOLDÉ — LA MÉTHODE NE VÉRIFIAIT RIEN.
    ///
    /// Elle écrasait le statut quel qu'il fût. Un gain déjà soldé par un RETRAIT
    /// se voyait donc réattribuer à un lot, et son versement partait une seconde
    /// fois — c'est exactement le double paiement qu'on ferme ici. Un gain encore
    /// « Accrued » (escrow non levé) pouvait de même être reversé avant livraison.
    ///
    /// Renvoie <c>false</c> quand le gain n'était pas payable : l'appelant DOIT
    /// s'en servir pour ne pas construire un versement sur du vent.
    /// </summary>
    public bool MarkSettled(Guid settlementBatchId)
    {
        if (Status != EarningStatus.Released)
        {
            return false;
        }

        Status = EarningStatus.Settled;
        SettlementBatchId = settlementBatchId;
        SettledByWithdrawalId = null;
        return true;
    }

    /// <summary>
    /// Solde le gain par un RETRAIT à la demande : l'argent sort du portefeuille
    /// tout de suite, le gain ne doit donc plus jamais entrer dans un lot.
    ///
    /// Même garde que <see cref="MarkSettled"/>, et pour la même raison : sans
    /// elle, deux retraits concurrents imputeraient le même gain.
    /// </summary>
    public bool MarkSettledByWithdrawal(Guid withdrawalId)
    {
        if (Status != EarningStatus.Released)
        {
            return false;
        }

        Status = EarningStatus.Settled;
        SettledByWithdrawalId = withdrawalId;
        SettlementBatchId = null;
        return true;
    }

    /// <summary>
    /// Détache le gain de son lot ou de son retrait et le rend à nouveau payable
    /// (annulation d'un lot avant tout versement ; retrait refusé ou échoué).
    /// Idempotent : sans effet si le gain n'est pas soldé.
    ///
    /// REMET LES DEUX RATTACHEMENTS À NULL.
    ///
    /// N'en effacer qu'un laisserait un gain « payable » portant encore la trace
    /// du retrait qui l'avait consommé : le prochain remboursement de ce retrait
    /// le libérerait une seconde fois, et le solde recréditerait deux fois.
    /// </summary>
    public void Unsettle()
    {
        if (Status != EarningStatus.Settled)
        {
            return;
        }

        Status = EarningStatus.Released;
        SettlementBatchId = null;
        SettledByWithdrawalId = null;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// REPREND TOUT OU PARTIE DU GAIN, APRÈS UNE VENTE REMBOURSÉE.
    ///
    /// CETTE MÉTHODE N'EXISTAIT PAS, ET C'EST POURQUOI `Reversed` ÉTAIT UN
    /// STATUT MORT.
    ///
    /// Les quatre montants viennent de l'APPELANT et ne sont pas recalculés ici :
    /// c'est lui qui connaît la part remboursée, et la règle du dépôt est qu'un
    /// montant appliqué se relit. Le domaine ne tranche que deux choses — jusqu'où
    /// on peut reprendre, et quand le gain sort du circuit.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE STATUT NE BASCULE QU'À LA REPRISE TOTALE DU BRUT.
    ///
    /// Une reprise partielle laisse le gain dans son statut courant. Le faire passer
    /// « Reversed » dès le premier retour sortirait toute la commande du circuit pour
    /// un article sur trois : le vendeur ne serait jamais payé des deux autres, et
    /// rien n'indiquerait pourquoi.
    ///
    /// Le test porte sur le BRUT et non sur le net : le net peut tomber à zéro alors
    /// que la vente n'est remboursée qu'en partie (commission et frais mangent le
    /// reste après arrondi). Le brut est le seul des quatre à décrire ce qui a
    /// réellement été vendu.
    ///
    /// UNE REPRISE NE PEUT PAS DÉPASSER LE GAIN D'ORIGINE — ELLE EST BORNÉE.
    ///
    /// Deux retours successifs sur la même commande, un `RefundAmount` qui inclut la
    /// livraison, un rejeu qui échapperait au verrou du grand livre : chacun peut
    /// demander plus qu'il ne reste. Sans borne, `ReversedNetAmount` dépasserait
    /// `NetAmount`, le net restant deviendrait négatif, et le vendeur se verrait
    /// reprendre plus qu'il n'a jamais touché.
    ///
    /// Chaque montant est plafonné SÉPARÉMENT à son propre reliquat : le brut peut
    /// rester disponible alors que la commission est déjà entièrement restituée.
    /// L'appelant reçoit ce qui a été RÉELLEMENT inscrit, et doit débiter là-dessus.
    ///
    /// CE QUE LA BORNE NE FAIT PAS : ELLE N'EST PAS UN VERROU D'IDEMPOTENCE.
    ///
    /// Elle empêche le dépassement CUMULÉ, pas le rejeu d'un même remboursement tant
    /// que le total reste sous le plafond. Le rejeu est fermé en amont, au grand
    /// livre (`RefundAlreadyReversedAsync`). Retirer l'un en comptant sur l'autre
    /// rouvrirait la brèche.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>
    /// Les montants effectivement inscrits, éventuellement rabotés ; un échec si le
    /// gain était déjà entièrement repris ou si un montant demandé est négatif.
    /// </returns>
    public Result<EarningReversal> Reverse(
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, decimal netAmount)
    {
        if (grossAmount < 0m || commissionAmount < 0m || providerFeeAmount < 0m || netAmount < 0m)
        {
            return Error.Validation(
                "settlement.earning.reversal_invalid", "Les montants d'une reprise ne peuvent pas être négatifs.");
        }

        if (Status == EarningStatus.Reversed || (GrossAmount > 0m && ReversedGrossAmount >= GrossAmount))
        {
            return Error.Conflict(
                "settlement.earning.already_reversed", "Ce gain a déjà été entièrement repris.");
        }

        var brut = Math.Min(grossAmount, Math.Max(0m, GrossAmount - ReversedGrossAmount));
        var commission = Math.Min(commissionAmount, Math.Max(0m, CommissionAmount - ReversedCommissionAmount));
        var frais = Math.Min(providerFeeAmount, Math.Max(0m, ProviderFeeAmount - ReversedProviderFeeAmount));
        var net = Math.Min(netAmount, Math.Max(0m, NetAmount - ReversedNetAmount));

        ReversedGrossAmount += brut;
        ReversedCommissionAmount += commission;
        ReversedProviderFeeAmount += frais;
        ReversedNetAmount += net;

        if (ReversedGrossAmount >= GrossAmount)
        {
            // Le gain sort du circuit : « Reversed » n'est ni « Released » ni
            // « Settled », donc ni `ListReleasedInPeriodAsync` ni
            // `ListReleasedBySellerAsync` ne le rendent — il ne peut plus entrer
            // dans un lot ni être consommé par un retrait. `Unsettle()` ne le
            // ressuscitera pas non plus : elle ne touche qu'un gain « Settled ».
            //
            // LES DEUX RATTACHEMENTS SONT CONSERVÉS, CONTRAIREMENT À `Unsettle()`.
            //
            // `SettlementBatchId` et `SettledByWithdrawalId` ne sont PAS effacés. Un
            // gain repris après avoir été payé garde ainsi la trace de ce qui l'a
            // payé — c'est le cas le plus délicat à justifier à un vendeur, et le
            // seul où la question « par quoi ce gain est-il sorti ? » se pose
            // vraiment. `Unsettle()` les efface parce qu'elle REND le gain payable ;
            // ici il ne l'est plus jamais, il n'y a donc rien à rouvrir.
            Status = EarningStatus.Reversed;
        }

        return new EarningReversal(brut, commission, frais, net);
    }
}
