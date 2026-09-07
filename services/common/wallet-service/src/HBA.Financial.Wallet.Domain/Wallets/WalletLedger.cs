using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Invariant comptable du §10.13 : dans une opération, la somme des débits égale la
/// somme des crédits.
/// </summary>
public static class WalletLedger
{
    /// <summary>Vérifie qu'un ensemble d'écritures formant UNE opération s'équilibre.</summary>
    public static Result EnsureBalanced(IReadOnlyCollection<WalletTransaction> entries)
    {
        if (entries.Count == 0)
        {
            return Result.Success();
        }

        var transactions = entries.Select(e => e.TransactionId).Distinct().ToList();

        if (transactions.Count > 1)
        {
            return Result.Failure(Error.Validation(
                "wallet.ledger.mixed_transactions",
                "Ces écritures n'appartiennent pas à la même opération : "
                + $"{transactions.Count} identifiants distincts."));
        }

        foreach (var parDevise in entries.GroupBy(e => e.Currency, StringComparer.OrdinalIgnoreCase))
        {
            var credits = parDevise.Where(e => e.Direction == WalletDirection.Credit).Sum(e => e.Amount);
            var debits = parDevise.Where(e => e.Direction == WalletDirection.Debit).Sum(e => e.Amount);

            if (credits != debits)
            {
                return Result.Failure(Error.Validation(
                    "wallet.ledger.unbalanced",
                    $"Opération {transactions[0]} déséquilibrée en {parDevise.Key} : "
                    + $"{credits} au crédit contre {debits} au débit."));
            }
        }

        return Result.Success();
    }

    /// <summary>Identifiant d'une nouvelle opération.</summary>
    public static Guid NewTransactionId() => Guid.NewGuid();
}

/// <summary>
/// TRADUIT UNE CLÉ D'IDEMPOTENCE TEXTUELLE EN LA RÉFÉRENCE `Guid` QUE LE GRAND
/// LIVRE SAIT INDEXER.
/// </summary>
public static class WalletReference
{
    /// <summary>Référence stable pour le couple (propriétaire, clé d'idempotence).</summary>
    public static Guid FromIdempotencyKey(Guid ownerId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Une référence d'idempotence ne se dérive pas d'une clé vide : l'appelant doit refuser avant.",
                nameof(idempotencyKey));
        }

        var graine = $"{ownerId:D}:{idempotencyKey.Trim()}";
        var condensat = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(graine));

        return new Guid(condensat.AsSpan(0, 16));
    }
}
