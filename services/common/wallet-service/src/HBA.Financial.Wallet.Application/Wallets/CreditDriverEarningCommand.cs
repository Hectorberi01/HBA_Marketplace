using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRÉDITE UN LIVREUR POUR UNE COURSE REMISE.
///
/// L'IDEMPOTENCE EST LA MOITIÉ DE CETTE COMMANDE.
///
/// L'outbox livre AU MOINS UNE FOIS : si l'un des handlers d'un événement échoue,
/// le message est rejoué EN ENTIER, y compris les handlers qui avaient réussi. Sans
/// clé de déduplication, le livreur est crédité deux fois — et l'erreur ne se voit
/// pas, parce qu'un solde trop élevé ressemble à un solde.
///
/// CRÉDITER DEUX FOIS EST BIEN PIRE QUE DE NE PAS CRÉDITER.
///
/// Un livreur non payé le signale le jour même. Un livreur payé double ne dit rien,
/// l'argent part au retrait, et l'écart n'apparaît qu'au rapprochement comptable —
/// s'il a lieu.
///
/// La clé est la course elle-même : <c>ReferenceType = "driver_earning"</c> +
/// l'identifiant de course. Deux verrous, pas un :
///   • ce handler consulte le grand livre avant d'écrire (contrôle applicatif) ;
///   • un index unique partiel le double en base — car entre la lecture et
///     l'écriture, deux rejeux simultanés peuvent tous deux se croire premiers.
///     Une vérification applicative ne ferme jamais cette fenêtre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Currency">
/// NULLABLE, et pas par négligence : <c>DeliveryCompletedIntegrationEvent</c>
/// déclare sa devise nullable. Exiger une chaîne ici forcerait l'appelant à
/// écrire son propre repli « XOF », et deux valeurs par défaut pour une même
/// notion finissent par diverger. Le repli vit dans le handler ci-dessous, seul
/// endroit qui écrit au portefeuille.
/// </param>
public sealed record CreditDriverEarningCommand(
    Guid DriverId,
    Guid DeliveryId,
    decimal Amount,
    string? Currency) : ICommand;

internal sealed class CreditDriverEarningCommandHandler : ICommandHandler<CreditDriverEarningCommand>
{
    /// <summary>
    /// Type de référence des écritures de gain de course.
    ///
    /// NE PAS RECOPIER CETTE CHAÎNE — c'est aussi le filtre de l'index unique
    /// (<c>ux_wallet_transactions_driver_earning</c>). Une faute de frappe ici
    /// rendrait le contrôle d'idempotence inopérant sans rien casser d'autre.
    ///
    /// NE PAS UTILISER SIMPLEMENT "delivery" : le compte de la plateforme peut
    /// un jour porter le produit de la livraison avec la même référence de course.
    /// Deux écritures différentes sur la même clé, et le contrôle ci-dessous
    /// refuserait de payer un livreur au motif que la plateforme a été créditée.
    /// La référence nomme donc l'ÉCRITURE, pas l'objet référencé.
    /// </summary>
    public const string DriverEarningReferenceType = "driver_earning";

    /// <summary>
    /// Type de référence de la SORTIE côté plateforme.
    ///
    /// IL DOIT DIFFÉRER DE <see cref="DriverEarningReferenceType"/>, ET CE
    /// N'ÉTAIT PAS LE CAS.
    ///
    /// Le débit du solde livraison portait la MÊME paire (type, référence) que le
    /// crédit du livreur. Or l'index <c>ux_wallet_transactions_driver_earning</c>
    /// est unique sur (ReferenceType, ReferenceId) SEULS — il ne comporte ni le
    /// propriétaire ni le compte, contrairement à celui des remboursements.
    ///
    /// Les deux lignes partant dans le MÊME <c>SaveChanges</c>, la contrainte
    /// aurait sauté dès le PREMIER crédit : transaction annulée, livreur non payé,
    /// et le message rejoué indéfiniment sur une erreur qui ne passerait jamais.
    /// Le verrou censé empêcher un double paiement empêchait le paiement.
    ///
    /// Les deux écritures restent corrélées par le même <c>ReferenceId</c> — la
    /// course — ce qui suffit à les lire ensemble. Seul le TYPE les distingue, et
    /// c'est ce que l'index attendait : « une course produit UNE écriture de gain ».
    /// </summary>
    public const string DriverShareReferenceType = "driver_share";

    private readonly IDriverWalletRepository _wallets;
    private readonly IWalletTransactionRepository _ledger;
    private readonly WalletMutations _plateforme;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IWalletUnitOfWork _unitOfWork;

    public CreditDriverEarningCommandHandler(
        IDriverWalletRepository wallets,
        IWalletTransactionRepository ledger,
        WalletMutations plateforme,
        IIntegrationEventPublisher publisher,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _ledger = ledger;
        _plateforme = plateforme;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CreditDriverEarningCommand command, CancellationToken cancellationToken)
    {
        if (command.DriverId == Guid.Empty || command.DeliveryId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "wallet.driver.invalid_reference", "Livreur et course sont requis pour créditer un gain."));
        }

        // LE CONTRÔLE D'IDEMPOTENCE VIENT EN PREMIER.
        //
        // Placé après la création du portefeuille, un rejeu créerait quand même le
        // portefeuille. Inoffensif ici, mais c'est le genre d'effet de bord qui rend
        // un rejeu observable — donc discutable le jour où il faut expliquer une ligne.
        var dejaCredite = await _ledger.ExistsForReferenceAsync(
            DriverEarningReferenceType, command.DeliveryId, cancellationToken);

        if (dejaCredite)
        {
            // Succès, pas erreur : le résultat attendu est atteint. Renvoyer un échec
            // ferait retenter l'outbox indéfiniment sur un message déjà traité.
            return Result.Success();
        }

        var currency = string.IsNullOrWhiteSpace(command.Currency)
            ? "XOF"
            : command.Currency.Trim().ToUpperInvariant();

        var wallet = await _wallets.GetByDriverAsync(command.DriverId, cancellationToken);

        if (wallet is null)
        {
            // Le portefeuille naît à la première course, pas à l'inscription : un
            // livreur qui n'a jamais roulé n'a pas de solde à afficher, et créer une
            // ligne vide pour chaque inscrit n'apporterait rien.
            wallet = DriverWallet.Create(command.DriverId, currency);
            await _wallets.AddAsync(wallet, cancellationToken);
        }
        else if (!string.Equals(wallet.Currency, currency, StringComparison.Ordinal))
        {
            // ON REFUSE PLUTÔT QUE DE CONVERTIR.
            //
            // Additionner deux devises sur un même solde donnerait un nombre qui ne
            // veut rien dire, et personne ne s'en apercevrait avant un retrait. Le cas
            // ne devrait pas se produire — tout est en XOF — mais s'il se produit, il
            // doit s'arrêter ici plutôt que se dissoudre dans un solde.
            return Result.Failure(Error.Conflict(
                "wallet.driver.currency_mismatch",
                $"Le portefeuille de ce livreur est en {wallet.Currency}, la course en {currency}."));
        }

        var credit = wallet.CreditEarning(command.Amount);
        if (credit.IsFailure)
        {
            return credit;
        }

        await _ledger.AddAsync(
            WalletTransaction.ForDriver(
                command.DriverId,
                WalletDirection.Credit,
                command.Amount,
                currency,
                reason: "delivery_earning",
                referenceType: DriverEarningReferenceType,
                referenceId: command.DeliveryId),
            cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // CE QUI SORT DU SOLDE LIVRAISON DE LA PLATEFORME.
        //
        // Le client a payé des frais de livraison, crédités au compte « livraison »
        // de la plateforme à la confirmation de sa commande. La part qu'on verse
        // ici au coursier vient de cette même poche — et rien ne l'en retirait.
        //
        // Le solde affichait donc la recette brute sous une étiquette de marge :
        // sur une course à 2 000 francs dont 1 400 partent au livreur, il montrait
        // 2 000. Trois fois la marge réelle, et aucun moyen de s'en apercevoir sans
        // rapprocher deux grands livres à la main.
        //
        // Même RÉFÉRENCE de course que le crédit ci-dessus — les deux écritures se
        // lisent ensemble — mais un TYPE distinct : voir DriverShareReferenceType,
        // sans quoi l'index d'unicité refusait la toute première écriture.
        // Le contrôle d'idempotence en tête protège les deux, puisqu'elles
        // appartiennent au même SaveChanges.
        // ═════════════════════════════════════════════════════════════════════
        await _plateforme.DebitPlatformShippingAsync(
            command.Amount, currency,
            reason: "driver_share",
            referenceType: DriverShareReferenceType,
            referenceId: command.DeliveryId,
            ct: cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // LE LIVREUR EST PAYÉ EN SILENCE SI CETTE PUBLICATION DISPARAÎT.
        //
        // Rien ne prévenait le coursier. Son solde bougeait, et il ne pouvait
        // l'apprendre qu'en ouvrant l'écran « Revenus » et en comparant deux
        // chiffres de mémoire — le même défaut que les reversements vendeur, qui
        // partaient eux aussi sans un mot avant `PayoutPaidNotificationHandler`.
        //
        // Publié ICI, et pas sur la fin de course : l'événement affirme que
        // l'argent EST au portefeuille. Annoncer un gain depuis delivery-service
        // le promettrait avant que quiconque l'ait écrit, et un crédit refusé
        // laisserait un message qui ment.
        //
        // La publication écrit une ligne d'outbox dans la MÊME transaction que le
        // solde : pas de notification sans crédit, pas de crédit sans notification.
        // ═════════════════════════════════════════════════════════════════════
        await _publisher.PublishAsync(
            new DriverEarningCreditedIntegrationEvent
            {
                DriverId = command.DriverId,
                DeliveryId = command.DeliveryId,
                Amount = command.Amount,
                Currency = currency
            },
            cancellationToken);

        // UN SEUL SaveChanges pour le solde ET l'écriture. Séparés, un échec entre les
        // deux laisserait un solde crédité sans trace au grand livre — ou l'inverse.
        // Aucun des deux cas ne se réconcilie après coup, parce que rien ne dirait
        // lequel fait foi.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
