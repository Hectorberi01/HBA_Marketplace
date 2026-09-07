using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>CRÉDITE UN LIVREUR POUR UNE COURSE REMISE.</summary>
/// <param name="Currency">
/// NULLABLE, et pas par négligence : <c> DeliveryCompletedIntegrationEvent</c>
/// déclare sa devise nullable.
/// </param>
public sealed record CreditDriverEarningCommand(
    Guid DriverId,
    Guid DeliveryId,
    decimal Amount,
    string? Currency) : ICommand;

internal sealed class CreditDriverEarningCommandHandler : ICommandHandler<CreditDriverEarningCommand>
{
    /// <summary>Type de référence des écritures de gain de course.</summary>
    public const string DriverEarningReferenceType = "driver_earning";

    /// <summary>Type de référence de la SORTIE côté plateforme.</summary>
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
        var dejaCredite = await _ledger.ExistsForReferenceAsync(
            DriverEarningReferenceType, command.DeliveryId, cancellationToken);

        if (dejaCredite)
        {
            // Succès, pas erreur : le résultat attendu est atteint.
            return Result.Success();
        }

        var currency = string.IsNullOrWhiteSpace(command.Currency)
            ? "XOF"
            : command.Currency.Trim().ToUpperInvariant();

        var wallet = await _wallets.GetByDriverAsync(command.DriverId, cancellationToken);

        if (wallet is null)
        {
            // Le portefeuille naît à la première course, pas à l'inscription : un
            // livreur qui n'a jamais roulé n'a pas de solde à afficher, et créer
            // une ligne vide pour chaque inscrit n'apporterait rien.
            wallet = DriverWallet.Create(command.DriverId, currency);
            await _wallets.AddAsync(wallet, cancellationToken);
        }
        else if (!string.Equals(wallet.Currency, currency, StringComparison.Ordinal))
        {
            // ON REFUSE PLUTÔT QUE DE CONVERTIR.
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

        // CE QUI SORT DU SOLDE LIVRAISON DE LA PLATEFORME.
        await _plateforme.DebitPlatformShippingAsync(
            command.Amount, currency,
            reason: "driver_share",
            referenceType: DriverShareReferenceType,
            referenceId: command.DeliveryId,
            ct: cancellationToken);

        // LE LIVREUR EST PAYÉ EN SILENCE SI CETTE PUBLICATION DISPARAÎT.
        await _publisher.PublishAsync(
            new DriverEarningCreditedIntegrationEvent
            {
                DriverId = command.DriverId,
                DeliveryId = command.DeliveryId,
                Amount = command.Amount,
                Currency = currency
            },
            cancellationToken);

        // UN SEUL SaveChanges pour le solde ET l'écriture.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
