using FluentAssertions;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;
using Xunit;

namespace HBA.Wallet.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS COMPENSATIONS DU LOT 3.3 — ISSUE-015, 050, 051.
///
/// Ces trois anomalies avaient la même forme : un geste de compensation ÉCRIT dans
/// le domaine, et jamais appelé. `MarkPayoutFailed`, `EarningStatus.Reversed`,
/// `WalletLedger.EnsureBalanced` — trois mécanismes prêts, trois fils sans courant.
///
/// Ce que ces tests protègent est précisément ce qui ne se voit pas autrement : un
/// vendeur débité et jamais payé, un gain remboursé qui reste payable, une
/// répartition qui cesse d'épuiser ce qui a été encaissé. Aucun des trois ne
/// produit d'erreur au moment où il survient.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CompensationsFinancieresTests
{
    // ── ISSUE-015 · un virement refusé se compense ──────────────────────────

    [Fact]
    public void Un_virement_refuse_passe_en_echec()
    {
        var lot = Lot(out var payoutId);

        lot.MarkPayoutFailed(payoutId).IsSuccess.Should().BeTrue();
        lot.Payouts.Single().Status.Should().Be(PayoutStatus.Failed);
    }

    /// <summary>
    /// SANS CETTE GARDE, LE RECRÉDIT SORTIRAIT DEUX FOIS.
    ///
    /// Le gestionnaire recrédite le portefeuille du vendeur à chaque passage. Une
    /// seconde exécution — double-clic, rejeu — doit donc être refusée par le
    /// domaine, pas par la prudence de l'appelant.
    /// </summary>
    [Fact]
    public void Un_virement_deja_paye_ne_peut_plus_echouer()
    {
        var lot = Lot(out var payoutId);
        lot.MarkPayoutPaid(payoutId, "psp-123").IsSuccess.Should().BeTrue();

        var refus = lot.MarkPayoutFailed(payoutId);

        refus.IsFailure.Should().BeTrue();
        lot.Payouts.Single().Status.Should().Be(PayoutStatus.Paid);
    }

    [Fact]
    public void Marquer_deux_fois_en_echec_ne_change_rien()
    {
        var lot = Lot(out var payoutId);

        lot.MarkPayoutFailed(payoutId).IsSuccess.Should().BeTrue();
        lot.MarkPayoutFailed(payoutId).IsSuccess.Should().BeTrue();
        lot.Payouts.Single().Status.Should().Be(PayoutStatus.Failed);
    }

    // ── ISSUE-050 · un gain remboursé cesse d'être payable ──────────────────

    [Fact]
    public void Une_reprise_totale_sort_le_gain_du_circuit()
    {
        var gain = Gain(brut: 10_000m, commission: 1_000m, frais: 200m);

        var reprise = gain.Reverse(10_000m, 1_000m, 200m, 8_800m);

        reprise.IsSuccess.Should().BeTrue();
        gain.Status.Should().Be(EarningStatus.Reversed);
        gain.RemainingNetAmount.Should().Be(0m);
    }

    /// <summary>
    /// Un client renvoie un article sur trois. Sortir toute la commande du circuit
    /// priverait le vendeur des deux qu'il a réellement vendus.
    /// </summary>
    [Fact]
    public void Une_reprise_partielle_laisse_le_reste_payable()
    {
        var gain = Gain(brut: 9_000m, commission: 900m, frais: 180m);

        gain.Reverse(3_000m, 300m, 60m, 2_640m).IsSuccess.Should().BeTrue();

        gain.Status.Should().NotBe(EarningStatus.Reversed);
        gain.RemainingGrossAmount.Should().Be(6_000m);
        gain.RemainingNetAmount.Should().Be(gain.NetAmount - 2_640m);
    }

    /// <summary>
    /// LE CAS QUI COÛTE DE L'ARGENT : deux retours successifs dont le cumul
    /// dépasse la vente. Sans borne, le vendeur se verrait reprendre plus qu'il n'a
    /// jamais touché.
    /// </summary>
    [Fact]
    public void Le_cumul_des_reprises_ne_depasse_jamais_le_gain()
    {
        var gain = Gain(brut: 10_000m, commission: 1_000m, frais: 200m);

        gain.Reverse(7_000m, 700m, 140m, 6_160m).IsSuccess.Should().BeTrue();
        var seconde = gain.Reverse(7_000m, 700m, 140m, 6_160m);

        seconde.IsSuccess.Should().BeTrue();
        seconde.Value.GrossAmount.Should().Be(3_000m, "la seconde reprise est rabotée au reliquat");
        gain.RemainingGrossAmount.Should().Be(0m);
        gain.RemainingNetAmount.Should().Be(0m);
        gain.Status.Should().Be(EarningStatus.Reversed);
    }

    [Fact]
    public void Un_gain_entierement_repris_refuse_toute_reprise_supplementaire()
    {
        var gain = Gain(brut: 5_000m, commission: 500m, frais: 100m);
        gain.Reverse(5_000m, 500m, 100m, 4_400m);

        var troisieme = gain.Reverse(1_000m, 0m, 0m, 1_000m);

        troisieme.IsFailure.Should().BeTrue();
        troisieme.Error.Code.Should().Be("settlement.earning.already_reversed");
    }

    // ── ISSUE-051 · l'invariant comptable a enfin une contrepartie ──────────

    /// <summary>
    /// La forme exacte d'une confirmation de commande : le brut encaissé au débit du
    /// compte extérieur, sa répartition au crédit. C'est cette opération, et elle
    /// seule, que le lot 3.3 place sous l'invariant.
    /// </summary>
    [Fact]
    public void Une_confirmation_de_commande_s_equilibre()
    {
        var op = WalletLedger.NewTransactionId();

        var resultat = WalletLedger.EnsureBalanced(
        [
            WalletTransaction.ForExternal(
                WalletDirection.Debit, 11_200m, "XOF", "order_confirmed", transactionId: op),
            WalletTransaction.ForSeller(
                Guid.NewGuid(), WalletAccount.Pending, WalletDirection.Credit, 8_800m, "XOF",
                "order_confirmed", transactionId: op),
            WalletTransaction.ForPlatform(
                WalletAccount.Commission, WalletDirection.Credit, 1_000m, "XOF",
                "commission", transactionId: op),
            WalletTransaction.ForPlatform(
                WalletAccount.Provider, WalletDirection.Credit, 200m, "XOF",
                "provider_fee", transactionId: op),
            WalletTransaction.ForPlatform(
                WalletAccount.Shipping, WalletDirection.Credit, 1_200m, "XOF",
                "shipping_fee", transactionId: op)
        ]);

        resultat.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// LE TEST QUI JUSTIFIE TOUT LE RESTE : une composante oubliée.
    ///
    /// Les frais provider ne sont pas crédités. L'opération encaisse 11 200 et n'en
    /// répartit que 11 000. Avant ce lot, ces 200 francs disparaissaient sans que
    /// rien ne le signale, jusqu'au jour où quelqu'un additionnerait les mouvements.
    /// </summary>
    [Fact]
    public void Une_repartition_incomplete_est_refusee()
    {
        var op = WalletLedger.NewTransactionId();

        var resultat = WalletLedger.EnsureBalanced(
        [
            WalletTransaction.ForExternal(
                WalletDirection.Debit, 11_200m, "XOF", "order_confirmed", transactionId: op),
            WalletTransaction.ForSeller(
                Guid.NewGuid(), WalletAccount.Pending, WalletDirection.Credit, 8_800m, "XOF",
                "order_confirmed", transactionId: op),
            WalletTransaction.ForPlatform(
                WalletAccount.Commission, WalletDirection.Credit, 1_000m, "XOF",
                "commission", transactionId: op),
            WalletTransaction.ForPlatform(
                WalletAccount.Shipping, WalletDirection.Credit, 1_200m, "XOF",
                "shipping_fee", transactionId: op)
        ]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("wallet.ledger.unbalanced");
    }

    /// <summary>
    /// L'écriture de contrepartie ne porte aucun solde : ce compte n'en a pas.
    /// Voir l'encadré de <see cref="WalletOwnerType.External"/>.
    /// </summary>
    [Fact]
    public void La_contrepartie_externe_ne_porte_aucun_solde()
    {
        var ecriture = WalletTransaction.ForExternal(
            WalletDirection.Credit, 5_000m, "XOF", "settlement_payout");

        ecriture.OwnerType.Should().Be(WalletOwnerType.External);
        ecriture.Account.Should().Be(WalletAccount.External);
        ecriture.OwnerId.Should().Be(Guid.Empty);
        ecriture.BalanceAfter.Should().BeNull();
    }

    // ── Fabriques ───────────────────────────────────────────────────────────

    private static SettlementBatch Lot(out Guid payoutId)
    {
        var creation = SettlementBatch.Create(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            "XOF");

        creation.IsSuccess.Should().BeTrue();
        var lot = creation.Value;
        payoutId = lot.AddPayout(Guid.NewGuid(), 10_000m, 1_000m, 8_800m);
        return lot;
    }

    private static SellerEarning Gain(decimal brut, decimal commission, decimal frais)
    {
        var creation = SellerEarning.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            brut, commission, frais, "XOF");

        creation.IsSuccess.Should().BeTrue();
        return creation.Value;
    }
}
