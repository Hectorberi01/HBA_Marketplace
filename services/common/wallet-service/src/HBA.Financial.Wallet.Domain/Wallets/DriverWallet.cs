using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

public readonly record struct DriverWalletId(Guid Value)
{
    public static DriverWalletId New() => new(Guid.NewGuid());
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SOLDE D'UN LIVREUR.
///
/// CE SOLDE N'EXISTAIT PAS, ET LE LIVREUR N'ÉTAIT DONC JAMAIS PAYÉ.
///
/// <c>Delivery.DriverEarning</c> est calculé à la remise, écrit en base et
/// transporté par <c>DeliveryCompletedIntegrationEvent</c>. Vérification faite :
/// aucun consommateur ne lisait ce champ. Le livreur roulait, remettait le colis,
/// voyait ses courses s'accumuler — et rien ne le créditait.
///
/// PAS DE SOLDE « EN ATTENTE », CONTRAIREMENT AU VENDEUR.
///
/// <c>SellerWallet</c> distingue Pending et Available : la vente d'un marchand
/// n'est acquise qu'après le délai de rétractation, et un retour la reprend. Une
/// course, elle, est faite ou ne l'est pas. Le livreur a roulé ; son gain est
/// acquis à la remise, et aucun retour produit ne le lui retire — c'est le
/// vendeur qui supporte le retour, pas celui qui a transporté le colis.
///
/// Copier la structure du vendeur aurait créé un solde « à venir » que rien
/// n'aurait jamais libéré : le livreur aurait vu un montant qu'il n'aurait pas pu
/// retirer, sans explication.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DriverWallet : AggregateRoot<DriverWalletId>
{
    // ctor EF.
    private DriverWallet()
    {
    }

    private DriverWallet(DriverWalletId id, Guid driverId, string currency)
        : base(id)
    {
        DriverId = driverId;
        Currency = currency;
        AvailableBalance = 0m;
        LifetimeEarned = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid DriverId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Ce que le livreur peut retirer aujourd'hui.</summary>
    public decimal AvailableBalance { get; private set; }

    /// <summary>
    /// Total gagné depuis l'inscription, retraits compris.
    ///
    /// Ce n'est pas de la décoration : le solde seul ne dit rien de ce qu'on a
    /// gagné, puisqu'un retrait le remet à zéro. C'est ce cumul que le livreur
    /// regarde pour savoir si le métier le nourrit — et c'est la première
    /// question qu'il pose au support.
    /// </summary>
    public decimal LifetimeEarned { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static DriverWallet Create(Guid driverId, string currency)
        => new(DriverWalletId.New(), driverId, string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    /// <summary>
    /// Crédite le gain d'une course.
    ///
    /// MONTANT STRICTEMENT POSITIF EXIGÉ.
    ///
    /// Un zéro n'est pas refusé par prudence : il signale qu'une course sans prix
    /// a été remise, et l'appelant doit le traiter — pas l'enregistrer comme une
    /// écriture de zéro franc qui masquerait le problème dans le grand livre.
    /// </summary>
    public Result CreditEarning(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.driver.amount_invalid",
                "Le gain d'une course doit être strictement positif."));
        }

        AvailableBalance += amount;
        LifetimeEarned += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Retire du solde.
    ///
    /// REFUSE DE PASSER SOUS ZÉRO. Un solde négatif sur un compte de livreur
    /// signifie qu'on lui réclame de l'argent : si cela doit exister un jour, ce
    /// sera une décision explicite, pas la conséquence d'un retrait mal borné.
    /// </summary>
    public Result Withdraw(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.driver.amount_invalid", "Le montant du retrait doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Conflict(
                "wallet.driver.insufficient_balance",
                "Le montant demandé dépasse le solde disponible."));
        }

        AvailableBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }
}
