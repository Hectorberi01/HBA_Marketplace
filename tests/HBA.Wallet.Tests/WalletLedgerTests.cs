using FluentAssertions;
using HBA.Financial.Wallet.Domain.Wallets;
using Xunit;

namespace HBA.Wallet.Tests;

/// <summary>
/// L'invariant comptable du §10.13. Ces tests protègent une propriété qui, une fois
/// violée, ne se manifeste qu'après des mois — le jour où quelqu'un additionne les
/// mouvements et trouve autre chose que le solde stocké.
/// </summary>
public sealed class WalletLedgerTests
{
    private static WalletTransaction Ecriture(
        Guid operation, WalletDirection sens, decimal montant, string devise = "XOF")
        => WalletTransaction.ForSeller(
            Guid.NewGuid(), WalletAccount.Available, sens, montant, devise,
            "test", transactionId: operation);

    [Fact]
    public void Une_operation_equilibree_est_acceptee()
    {
        var op = WalletLedger.NewTransactionId();

        var resultat = WalletLedger.EnsureBalanced(
        [
            Ecriture(op, WalletDirection.Debit, 5000m),
            Ecriture(op, WalletDirection.Credit, 3000m),
            Ecriture(op, WalletDirection.Credit, 2000m)
        ]);

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_operation_desequilibree_est_refusee()
    {
        var op = WalletLedger.NewTransactionId();

        var resultat = WalletLedger.EnsureBalanced(
        [
            Ecriture(op, WalletDirection.Debit, 5000m),
            Ecriture(op, WalletDirection.Credit, 4999m)
        ]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("wallet.ledger.unbalanced");
    }

    /// <summary>
    /// LE CŒUR DE LA VÉRIFICATION PAR DEVISE.
    ///
    /// 5 000 XOF au débit et 5 000 EUR au crédit s'équilibrent si l'on additionne
    /// des nombres sans regarder leur unité. C'est faux, et c'est exactement le
    /// genre de déséquilibre qu'un contrôle global laisserait passer.
    /// </summary>
    [Fact]
    public void Deux_devises_ne_se_compensent_pas()
    {
        var op = WalletLedger.NewTransactionId();

        var resultat = WalletLedger.EnsureBalanced(
        [
            Ecriture(op, WalletDirection.Debit, 5000m, "XOF"),
            Ecriture(op, WalletDirection.Credit, 5000m, "EUR")
        ]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("wallet.ledger.unbalanced");
    }

    /// <summary>
    /// Vérifier l'équilibre d'écritures qui n'appartiennent pas à la même opération
    /// n'a aucun sens : le résultat serait vrai ou faux par accident.
    /// </summary>
    [Fact]
    public void Des_ecritures_d_operations_differentes_sont_refusees()
    {
        var resultat = WalletLedger.EnsureBalanced(
        [
            Ecriture(WalletLedger.NewTransactionId(), WalletDirection.Debit, 1000m),
            Ecriture(WalletLedger.NewTransactionId(), WalletDirection.Credit, 1000m)
        ]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("wallet.ledger.mixed_transactions");
    }

    [Fact]
    public void Une_operation_sans_ecriture_n_est_pas_une_faute()
    {
        WalletLedger.EnsureBalanced([]).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Sans identifiant fourni, chaque écriture est sa propre opération — ce qui
    /// décrit exactement les appelants non encore regroupés.
    /// </summary>
    [Fact]
    public void Sans_identifiant_chaque_ecriture_est_sa_propre_operation()
    {
        var a = WalletTransaction.ForSeller(
            Guid.NewGuid(), WalletAccount.Available, WalletDirection.Credit, 100m, "XOF", "test");
        var b = WalletTransaction.ForSeller(
            Guid.NewGuid(), WalletAccount.Available, WalletDirection.Credit, 100m, "XOF", "test");

        a.TransactionId.Should().NotBe(b.TransactionId);
        a.TransactionId.Should().NotBeEmpty();
    }

    [Fact]
    public void Le_solde_resultant_est_reporte_quand_il_est_connu()
    {
        var ecriture = WalletTransaction.ForSeller(
            Guid.NewGuid(), WalletAccount.Available, WalletDirection.Credit, 100m, "XOF", "test",
            balanceAfter: 12_500m);

        ecriture.BalanceAfter.Should().Be(12_500m);
    }
}
