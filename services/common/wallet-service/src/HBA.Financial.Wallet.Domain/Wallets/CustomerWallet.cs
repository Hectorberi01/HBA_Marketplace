using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SOLDE D'UN CLIENT.
///
/// CE SOLDE N'EXISTAIT PAS, ET LE CLIENT N'ÉTAIT DONC JAMAIS REMBOURSÉ.
///
/// FedaPay n'expose AUCUNE API de remboursement — pas plus que MTN, Moov ou
/// PayPal dans ce dépôt. Un retour validé, un remboursement décidé, et l'appel
/// répondait `Success: false` : le dossier escaladait en `ManualReview` et
/// l'argent ne revenait jamais au client autrement qu'à la main, quand quelqu'un
/// y pensait. Voir D33 dans docs/DECISIONS.md.
///
/// Ce portefeuille est l'autre chemin : l'argent est rendu IMMÉDIATEMENT, à
/// l'intérieur de la plateforme. Le client peut le dépenser sur une commande
/// suivante sans rien demander à personne ; s'il veut le sortir en Mobile Money,
/// il en fait la DEMANDE (voir <see cref="CustomerWithdrawal"/>).
///
/// PAS DE SOLDE « À VENIR », COMME POUR LE LIVREUR ET CONTRAIREMENT AU VENDEUR.
///
/// `SellerWallet` distingue Pending et Available parce qu'une vente n'est acquise
/// qu'après le délai de rétractation. Un remboursement, lui, est acquis à la
/// seconde où il est décidé : l'argent est déjà celui du client, on le lui rend.
/// Un solde « à venir » que rien ne libérerait afficherait au client une somme
/// qu'il ne pourrait ni dépenser ni retirer, sans explication.
///
/// CE QUE CE SOLDE EST, ET QU'IL FAUT DIRE : UNE DETTE.
///
/// Chaque franc ici est dû à quelqu'un. La plateforme ne dispose d'aucun
/// rapprochement entre le total de ces soldes et sa trésorerie réelle, et aucune
/// règle de péremption n'est posée — un solde de portefeuille est une créance, et
/// y toucher sans avis juridique n'est pas une décision d'ingénierie. Les deux
/// points sont ouverts et nommés en D33.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CustomerWallet : AggregateRoot<CustomerWalletId>
{
    // ctor EF.
    private CustomerWallet()
    {
    }

    private CustomerWallet(CustomerWalletId id, Guid customerId, string currency)
        : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        AvailableBalance = 0m;
        LifetimeRefunded = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid CustomerId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Ce que le client peut dépenser ou demander à faire virer aujourd'hui.</summary>
    public decimal AvailableBalance { get; private set; }

    /// <summary>
    /// Total remboursé depuis toujours, virements sortis compris.
    ///
    /// Même raison d'être que <c>DriverWallet.LifetimeEarned</c> : le solde seul ne
    /// dit rien de ce qui a été rendu, puisqu'un virement le remet à zéro. C'est ce
    /// cumul que le client oppose au support quand il conteste un remboursement, et
    /// la première chose qu'on lui demandera de vérifier.
    /// </summary>
    public decimal LifetimeRefunded { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerWallet Create(Guid customerId, string currency)
        => new(CustomerWalletId.New(), customerId,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    /// <summary>
    /// Crédite un remboursement.
    ///
    /// MONTANT STRICTEMENT POSITIF EXIGÉ.
    ///
    /// Un zéro n'est pas refusé par prudence : il signale qu'un remboursement sans
    /// montant a été décidé quelque part en amont, et l'appelant doit le traiter —
    /// pas l'enregistrer comme une écriture de zéro franc qui masquerait le problème
    /// dans le grand livre, et surtout pas comme un dossier « remboursé » alors que
    /// le client n'a rien reçu.
    /// </summary>
    public Result CreditRefund(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid",
                "Le montant d'un remboursement doit être strictement positif."));
        }

        AvailableBalance += amount;
        LifetimeRefunded += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Retient les fonds à la demande de virement.
    ///
    /// REFUSE DE PASSER SOUS ZÉRO. Un solde négatif sur un compte client signifie
    /// qu'on lui réclame de l'argent qu'on lui devait : si cela doit exister un
    /// jour, ce sera une décision explicite, pas la conséquence d'une demande mal
    /// bornée.
    ///
    /// LA RETENUE SE FAIT À LA DEMANDE, PAS À LA VALIDATION DE L'ADMINISTRATEUR.
    ///
    /// Laisser les fonds au solde jusqu'au virement les rendrait dépensables entre
    /// les deux : le client commande, l'administrateur vire, et la plateforme a payé
    /// deux fois le même argent. La demande est peut-être refusée — c'est à quoi
    /// sert <see cref="Restore"/>.
    /// </summary>
    public Result Hold(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid", "Le montant du virement demandé doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer.insufficient_balance",
                "Le montant demandé dépasse le solde disponible."));
        }

        AvailableBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Restitue les fonds d'une demande refusée.
    ///
    /// IL N'EXISTE AUCUN CHEMIN « ÉCHOUÉ » ICI, ET C'EST STRUCTUREL.
    ///
    /// Le retrait vendeur restitue aussi sur un échec de payout, parce qu'un PSP peut
    /// refuser. Le virement d'un client est exécuté À LA MAIN chez le prestataire :
    /// il n'y a pas d'issue technique à rattraper. Si l'administrateur n'a pas pu
    /// virer, il REFUSE la demande — geste explicite, motivé, tracé — et cette
    /// méthode rend l'argent.
    ///
    /// Ce qu'elle ne couvre pas : un virement réellement parti puis marqué « refusé »
    /// par erreur recréditerait un solde déjà versé. Aucune règle automatique ne peut
    /// l'attraper ; c'est ce que la référence externe obligatoire sur `MarkPaid` rend
    /// au moins vérifiable après coup.
    /// </summary>
    public Result Restore(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid", "Le montant restitué doit être positif."));
        }

        AvailableBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }
}
