using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Contre-passe les gains d'une vente REMBOURSÉE — argent réellement versé.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Wallet.Application.Earnings.ReverseEarningsOnReturnRefundedHandler")]
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

        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
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
            // ON JOURNALISE EN ERREUR ET ON N'ÉCRIT RIEN — PAS DE REPLI SUR UN
            // CALCUL.
            _logger.LogError(
                "Remboursement {ReturnRequestId} (commande {OrderId}, vendeur {SellerId}) : AUCUN gain d'origine "
                + "retrouvé au grand livre. Rien n'est contre-passé — {RefundAmount} {Currency} sont sortis sans "
                + "reprise côté vendeur ni restitution de commission. Écriture manuelle requise.",
                e.ReturnRequestId, e.OrderId, e.SellerId, e.RefundAmount, e.Currency);

            return;
        }

        // LE BRUT D'ORIGINE, ET NON LE BRUT RESTANT — MALGRÉ LES REPRISES.
        var brutOrigine = gains.Sum(g => g.GrossAmount);

        if (brutOrigine <= 0m)
        {
            _logger.LogError(
                "Remboursement {ReturnRequestId} : les gains de la commande {OrderId} ont un brut nul — "
                + "rien à contre-passer au prorata.",
                e.ReturnRequestId, e.OrderId);

            return;
        }

        // REMBOURSEMENT PARTIEL : PRORATA DU GAIN ENREGISTRÉ, ET RIEN D'AUTRE.
        var part = Math.Min(1m, e.RefundAmount / brutOrigine);

        var commissionTotale = 0m;
        var fraisTotaux = 0m;
        var netTotal = 0m;
        var reprisEntierement = 0;

        foreach (var gain in gains)
        {
            // Le prorata s'applique gain par gain, et non au total : deux gains de
            // la même commande peuvent porter des taux différents (accrual à des
            // dates différentes, marchandise et restauration).
            var brutRendu = Math.Round(gain.GrossAmount * part);
            var commission = Math.Round(gain.CommissionAmount * part);
            var frais = Math.Round(gain.ProviderFeeAmount * part);

            // Le net vendeur est le RESTE, et non un troisième arrondi : c'est ce
            // qui garantit brut = commission + provider + net, au franc près.
            var net = Math.Max(0m, brutRendu - commission - frais);

            // ON INSCRIT LA REPRISE SUR LE GAIN AVANT DE TOUCHER AUX SOLDES.
            var reprise = gain.Reverse(brutRendu, commission, frais, net);

            if (reprise.IsFailure)
            {
                // ON SAUTE LE GAIN, ON N'ÉCRIT RIEN, ET ON NE FAIT PAS ÉCHOUER LE
                // MESSAGE.
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

    /// <summary>QUELS GAINS CE REMBOURSEMENT CONCERNE-T-IL ?</summary>
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
        // débiterait un vendeur qui n'est pour rien dans ce retour.
        return Array.Empty<SellerEarning>();
    }
}
