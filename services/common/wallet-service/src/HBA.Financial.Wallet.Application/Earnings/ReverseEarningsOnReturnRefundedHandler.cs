using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// Contre-passe les gains d'une vente REMBOURSÉE — argent réellement versé.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE HANDLER N'EXISTAIT PAS, ET SON ABSENCE COÛTAIT DE L'ARGENT À CHAQUE RETOUR.
///
/// `ReturnRefundedIntegrationEvent` était publié depuis le début — et n'avait AUCUN
/// consommateur. Résultat : un article revenait, le client était (censé être)
/// remboursé, et le vendeur gardait malgré tout son gain. La plateforme payait donc
/// DEUX FOIS : le remboursement au client, et la vente au vendeur.
///
/// La marchandise rentre, l'argent sort deux fois. Chaque retour était une perte
/// sèche, invisible dans les comptes.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// <para>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MONTANT APPLIQUÉ SE RELIT — IL NE SE RECALCULE PAS.
///
/// Ce fichier RECALCULAIT la répartition à partir des taux courants, avec la
/// formule de la MARCHANDISE : `brut × PlatformCommissionRate / (1 + comm + prov)`.
/// Elle était appliquée à tout remboursement, sans regarder la nature de la
/// commande. Or la restauration ne prélève pas ainsi : `AccrueEarningsOnOrderConfirmed`
/// y applique `brut × FoodCommissionRate`, SANS la division — le prix de carte n'est
/// pas une majoration d'un prix net, la commission se sert DEDANS.
///
/// Conséquence concrète : on rendait au restaurateur un net calculé avec le taux
/// marchandise ramené au prix net (≈ 8,7 % pour un réglage à 10 %), alors qu'on lui
/// avait prélevé le taux restauration PLEIN. Le prélèvement et sa restitution ne se
/// compensaient pas. Sur un plat à 10 000 francs, la plateforme rendait au
/// restaurateur quelques centaines de francs de trop ou de trop peu — jamais le
/// compte, jamais une erreur visible, et un écart qui ne se découvre qu'au
/// rapprochement comptable.
///
/// La cause n'est pas « la mauvaise formule a été choisie » : c'est qu'il y avait
/// DEUX formules pour un seul chiffre. Toute duplication d'un calcul monétaire finit
/// par diverger, parce que l'une des copies évolue sans l'autre — c'est arrivé ici
/// le jour où la restauration a reçu son propre taux.
///
/// La règle, qui vaut bien au-delà de ce fichier : dès qu'un montant a été ÉCRIT
/// quelque part, toute opération inverse le RELIT. `SellerEarning` porte
/// `GrossAmount`, `CommissionAmount`, `NetAmount` et `ProviderFeeAmount` : ce sont
/// les montants réellement appliqués, arrondis au moment de l'écriture. Les
/// recalculer, c'est réinventer un chiffre qu'on possède — et se rendre vulnérable
/// à tout changement de taux survenu entre-temps.
///
/// Ce handler ne contient donc PLUS AUCUNE multiplication par un taux, et n'a plus
/// besoin de `PricingOptions`. C'est le seul état dans lequel il ne peut pas
/// diverger de l'accrual.
/// ═════════════════════════════════════════════════════════════════════════════
/// </para>
/// </summary>
public sealed class ReverseEarningsOnReturnRefundedHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly ISellerEarningRepository _earnings;
    private readonly WalletMutations _wallets;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<ReverseEarningsOnReturnRefundedHandler> _logger;

    public ReverseEarningsOnReturnRefundedHandler(
        ISellerEarningRepository earnings,
        WalletMutations wallets,
        IWalletUnitOfWork unitOfWork,
        ILogger<ReverseEarningsOnReturnRefundedHandler> logger)
    {
        _earnings = earnings;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.RefundAmount <= 0m)
        {
            return;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
        //
        // L'outbox livre AT-LEAST-ONCE. Le dispatcher rejoue TOUS les handlers d'un
        // message dès que l'un d'eux échoue — y compris ceux qui avaient réussi.
        //
        // Sans ce garde, un simple rejeu débitait le vendeur une SECONDE fois et
        // restituait la commission deux fois. Une perte comptable réelle, silencieuse,
        // et survenant dans le code même censé fiabiliser les remboursements.
        //
        // Les voisins se protègent depuis toujours (AccrueEarnings → ExistsForOrder,
        // Loyalty → HasEarnedForOrder, Analytics → ProcessedOrder). Celui-ci ne le
        // faisait pas : c'était un oubli, pas un choix.
        //
        // Le grand livre sert de registre : s'il porte déjà une écriture pour ce
        // remboursement, la contre-passation a déjà eu lieu.
        // ─────────────────────────────────────────────────────────────────────────
        if (await _wallets.RefundAlreadyReversedAsync(e.ReturnRequestId, cancellationToken))
        {
            _logger.LogInformation(
                "Remboursement {ReturnRequestId} : contre-passation DÉJÀ effectuée — rejeu ignoré.",
                e.ReturnRequestId);
            return;
        }

        var gains = await RetrouverGainsAsync(e, cancellationToken);

        if (gains.Count == 0)
        {
            // ON JOURNALISE EN ERREUR ET ON N'ÉCRIT RIEN — PAS DE REPLI SUR UN CALCUL.
            //
            // Retomber sur une formule serait exactement le défaut qu'on ferme : un
            // chemin discret qui produit un chiffre plausible mais faux, et qui ne se
            // voit jamais. Trois causes possibles : commande antérieure à la tenue du
            // grand livre des gains, gain purgé, ou remboursement portant un vendeur
            // qui n'a rien perçu sur cette commande.
            //
            // On ne relance PAS non plus : contrairement à un dossier de reversement
            // qu'on peut rattacher en quelques minutes, un gain absent ne réapparaîtra
            // pas, et rejouer indéfiniment noierait l'outbox. Le message est consommé,
            // la trace reste, et l'écriture se passe à la main.
            _logger.LogError(
                "Remboursement {ReturnRequestId} (commande {OrderId}, vendeur {SellerId}) : AUCUN gain d'origine "
                + "retrouvé au grand livre. Rien n'est contre-passé — {RefundAmount} {Currency} sont sortis sans "
                + "reprise côté vendeur ni restitution de commission. Écriture manuelle requise.",
                e.ReturnRequestId, e.OrderId, e.SellerId, e.RefundAmount, e.Currency);

            return;
        }

        // LE BRUT D'ORIGINE, ET NON LE BRUT RESTANT — MALGRÉ LES REPRISES.
        //
        // C'est le dénominateur du prorata, et `RefundAmount` est exprimé par rapport
        // à la vente ENTIÈRE : le module Returns ne sait rien des reprises déjà
        // passées ici. Retrancher le brut déjà repris gonflerait la part à chaque
        // retour successif — un deuxième retour de 30 % serait compté 43 % du reste.
        //
        // Ce n'est donc PAS ici qu'on empêche le cumul de dépasser la vente : c'est
        // `SellerEarning.Reverse` qui borne, gain par gain.
        var brutOrigine = gains.Sum(g => g.GrossAmount);

        if (brutOrigine <= 0m)
        {
            _logger.LogError(
                "Remboursement {ReturnRequestId} : les gains de la commande {OrderId} ont un brut nul — "
                + "rien à contre-passer au prorata.",
                e.ReturnRequestId, e.OrderId);

            return;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // REMBOURSEMENT PARTIEL : PRORATA DU GAIN ENREGISTRÉ, ET RIEN D'AUTRE.
        //
        // Un retour peut ne porter que sur une partie de ce qui a été vendu. Le
        // choix : appliquer au gain d'origine le rapport `remboursé / brut d'origine`.
        //
        // Pourquoi c'est le seul choix défendable — l'événement ne dit PAS quelle
        // ligne revient, seulement un montant. Toute autre règle (imputer d'abord la
        // ligne la plus chère, reconstituer les articles depuis le montant) devinerait
        // une information que personne n'a transmise, et deux remboursements du même
        // montant sur la même commande ne se contre-passeraient pas pareil.
        //
        // Le prorata a une propriété que rien d'autre n'a : rendre 100 % rend
        // EXACTEMENT ce qui avait été prélevé. `part = 1` ⇒ les montants relus sont
        // repris tels quels, sans arrondi supplémentaire. Le cas normal — le retour
        // total — est donc exact au franc près par construction, et non par chance.
        //
        // LE RAPPORT EST PLAFONNÉ À 1.
        //
        // `RefundAmount` peut dépasser le brut des gains : il inclut parfois les frais
        // de livraison, qui ne sont PAS du revenu vendeur (voir le compte « livraison »
        // de la plateforme). Sans ce plafond, on reprendrait au vendeur plus qu'il
        // n'avait touché — le geste commercial de la plateforme serait payé par lui.
        // ═════════════════════════════════════════════════════════════════════════
        var part = Math.Min(1m, e.RefundAmount / brutOrigine);

        var commissionTotale = 0m;
        var fraisTotaux = 0m;
        var netTotal = 0m;
        var reprisEntierement = 0;

        foreach (var gain in gains)
        {
            // Le prorata s'applique gain par gain, et non au total : deux gains de la
            // même commande peuvent porter des taux différents (accrual à des dates
            // différentes, marchandise et restauration). Répartir un total recomposé
            // les fondrait en un taux moyen qui n'a jamais été appliqué à personne.
            var brutRendu = Math.Round(gain.GrossAmount * part);
            var commission = Math.Round(gain.CommissionAmount * part);
            var frais = Math.Round(gain.ProviderFeeAmount * part);

            // Le net vendeur est le RESTE, et non un troisième arrondi : c'est ce qui
            // garantit brut = commission + provider + net, au franc près. Trois arrondis
            // indépendants ne se rejoignent pas, et l'écart s'accumule silencieusement.
            // Même règle qu'à l'accrual, où le net est déjà `brut − commission − frais`.
            var net = Math.Max(0m, brutRendu - commission - frais);

            // ═════════════════════════════════════════════════════════════════════
            // ON INSCRIT LA REPRISE SUR LE GAIN AVANT DE TOUCHER AUX SOLDES.
            //
            // Ce handler ne débitait que le PORTEFEUILLE et laissait le gain
            // « Released ». Le lot de reversement suivant le ramassait et le comptait
            // payable : la plateforme reprenait le net d'une main et le reversait de
            // l'autre. `EarningStatus.Reversed` existait dans l'énumération et rien ne
            // le posait jamais — c'était la moitié manquante de la contre-passation.
            //
            // L'ORDRE N'EST PAS ARBITRAIRE : LE GAIN TRANCHE, LES SOLDES SUIVENT.
            //
            // `Reverse` BORNE la reprise à ce qui reste du gain, et rend ce qu'elle a
            // réellement pu inscrire. Débiter d'abord `net` puis inscrire le raboté
            // ferait diverger le grand livre du gain dès le premier dépassement — et le
            // dépassement est exactement ce qu'on borne ici (deux retours partiels
            // successifs qui, cumulés, excèdent la vente).
            //
            // Les trois débits portent donc les montants RENDUS par le domaine, pas
            // ceux qu'on venait de calculer.
            // ═════════════════════════════════════════════════════════════════════
            var reprise = gain.Reverse(brutRendu, commission, frais, net);

            if (reprise.IsFailure)
            {
                // ON SAUTE LE GAIN, ON N'ÉCRIT RIEN, ET ON NE FAIT PAS ÉCHOUER LE
                // MESSAGE.
                //
                // Le seul refus possible ici est « déjà entièrement repris » : la vente
                // a déjà été rendue en totalité par un retour antérieur. Débiter
                // malgré tout reprendrait au vendeur de l'argent qu'il n'a pas touché.
                // Relancer indéfiniment ne changerait rien non plus — le gain ne
                // redeviendra jamais reprenable.
                //
                // Reste un cas à connaître : si TOUS les gains sont dans cet état,
                // aucune écriture ne part, et le verrou de rejeu (qui est le grand
                // livre) n'est donc PAS posé pour ce remboursement. Un rejeu
                // repassera ici et refusera à nouveau, sans rien débiter — c'est sûr,
                // mais bruyant.
                _logger.LogWarning(
                    "Remboursement {ReturnRequestId} : le gain {EarningId} (commande {OrderId}) refuse la reprise "
                    + "({Code}) — {Net} {Currency} NE sont pas repris au vendeur {SellerId}.",
                    e.ReturnRequestId, gain.Id.Value, e.OrderId, reprise.Error.Code, net, gain.Currency, gain.SellerId);

                continue;
            }

            var applique = reprise.Value;

            if (gain.Status == EarningStatus.Reversed)
            {
                reprisEntierement++;
            }

            // CES TROIS DÉBITS NE SONT PAS SOUS L'INVARIANT COMPTABLE (§10.13).
            //
            // La confirmation de commande l'est depuis ISSUE-051 ; cette
            // contre-passation NE L'EST PAS, et ce n'est pas un oubli. Elle en a la
            // forme — brut rendu d'un côté, net + commission + frais de l'autre — et
            // elle échouerait sur des cas LÉGITIMES : `Reverse` borne les quatre
            // montants séparément, si bien qu'une seconde reprise partielle rompt
            // l'égalité ; et la part « frais de port » d'un remboursement n'a aucune
            // écriture en regard. Poser l'invariant ici mettrait un retour normal en
            // lettre morte. Le raisonnement complet, et ce qu'il faudrait pour lever
            // la réserve, sont dans l'encadré de `WalletLedger`.
            //
            // La référence est le REMBOURSEMENT, pas la commande : deux retours sur la même
            // commande doivent rester distinguables au grand livre — et c'est cette
            // référence qui sert de verrou au rejeu.
            //
            // Le vendeur débité est celui INSCRIT SUR LE GAIN, pas celui de l'événement.
            // On reprend l'argent à qui il a été versé : pour un repas, c'est le dossier
            // de reversement du restaurant (`Restaurant.PayoutSellerId`), qui n'est pas
            // forcément l'identifiant que porte le retour. Même raisonnement pour la
            // devise : celle qui a servi à créditer.
            await _wallets.DebitSellerForRefundAsync(
                gain.SellerId, applique.NetAmount, gain.Currency, e.ReturnRequestId, cancellationToken);

            await _wallets.DebitPlatformCommissionAsync(
                applique.CommissionAmount, gain.Currency, e.ReturnRequestId, cancellationToken);

            await _wallets.DebitPlatformProviderFeeAsync(
                applique.ProviderFeeAmount, gain.Currency, e.ReturnRequestId, cancellationToken);

            commissionTotale += applique.CommissionAmount;
            fraisTotaux += applique.ProviderFeeAmount;
            netTotal += applique.NetAmount;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Remboursement {ReturnRequestId} (réf. {RefundReference}) : contre-passation de {RefundAmount} {Currency} "
            + "sur {Count} gain(s) relu(s) — part {Part:P2} du brut d'origine {BrutOrigine} ; net vendeur {SellerNet} "
            + "repris, commission {Commission} et frais {ProviderFee} restitués ; {ReprisEntierement} gain(s) "
            + "entièrement repris et donc sortis du circuit de reversement.",
            e.ReturnRequestId, e.RefundReference, e.RefundAmount, e.Currency, gains.Count, part, brutOrigine,
            netTotal, commissionTotale, fraisTotaux, reprisEntierement);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUELS GAINS CE REMBOURSEMENT CONCERNE-T-IL ?
    ///
    /// PLUSIEURS GAINS PEUVENT PORTER LA MÊME COMMANDE.
    ///
    /// L'accrual crée UN gain PAR LIGNE de commande, donc potentiellement plusieurs
    /// vendeurs et plusieurs lignes pour un même vendeur. L'événement ne porte qu'un
    /// `SellerId` : il désigne à qui reprendre l'argent, pas quelle ligne revient.
    /// On retient donc TOUS les gains de ce vendeur sur cette commande, et le
    /// prorata tranche la répartition — voir le bloc « remboursement partiel ».
    ///
    /// LE REPLI SUR LE GAIN UNIQUE N'EST PAS UNE APPROXIMATION.
    ///
    /// Une commande de repas n'enregistre qu'UN gain, au nom du DOSSIER DE
    /// REVERSEMENT du restaurant (`Restaurant.PayoutSellerId`) — pas de
    /// l'établissement. Le `SellerId` du retour, lui, vient du module Returns et
    /// peut désigner l'établissement : le filtrage strict ne trouverait alors rien,
    /// et un repas remboursé ne serait jamais contre-passé.
    ///
    /// Quand la commande ne porte qu'un seul gain, il n'y a aucune ambiguïté sur le
    /// bénéficiaire : c'est lui qui a été crédité, c'est lui qu'on débite. Dès qu'il
    /// y en a plusieurs et qu'aucun ne correspond, on refuse de deviner.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<IReadOnlyList<SellerEarning>> RetrouverGainsAsync(
        ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var gainsCommande = await _earnings.ListByOrderAsync(e.OrderId, cancellationToken);

        if (gainsCommande.Count == 0)
        {
            return Array.Empty<SellerEarning>();
        }

        var duVendeur = gainsCommande.Where(g => g.SellerId == e.SellerId).ToList();

        if (duVendeur.Count > 0)
        {
            return duVendeur;
        }

        if (gainsCommande.Count == 1)
        {
            _logger.LogWarning(
                "Remboursement {ReturnRequestId} : le vendeur {SellerId} de l'événement ne correspond à aucun gain "
                + "de la commande {OrderId} ; l'unique gain enregistré (bénéficiaire {BeneficiaireId}) est repris — "
                + "cas attendu pour une commande de repas, dont le gain est au nom du dossier de reversement.",
                e.ReturnRequestId, e.SellerId, e.OrderId, gainsCommande[0].SellerId);

            return gainsCommande;
        }

        // Plusieurs gains, aucun au nom du vendeur remboursé : imputer au hasard
        // débiterait un vendeur qui n'est pour rien dans ce retour. On laisse le
        // handler journaliser l'absence et ne rien écrire.
        return Array.Empty<SellerEarning>();
    }
}
