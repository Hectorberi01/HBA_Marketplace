using FluentValidation;
using HBA.Shared.Application.Context;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

// LE PORTEFEUILLE CLIENT (D33 dans docs/DECISIONS.md).

/// <summary>Rend un montant au client sur son portefeuille et l'inscrit au grand livre.</summary>
public sealed record CreditCustomerRefundCommand(
    Guid CustomerId, decimal Amount, string Currency, string Reason, string IdempotencyKey)
    : ICommand<CustomerWalletCreditResult>;

public sealed class CreditCustomerRefundCommandValidator : AbstractValidator<CreditCustomerRefundCommand>
{
    public CreditCustomerRefundCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant remboursé doit être positif.");
        RuleFor(c => c.Currency).NotEmpty().WithMessage("La devise est requise.");
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Un motif est requis.");

        // PAS DE RÈGLE SUR `IdempotencyKey` ICI, ET CE N'EST PAS UN OUBLI.
    }
}

internal sealed class CreditCustomerRefundCommandHandler
    : ICommandHandler<CreditCustomerRefundCommand, CustomerWalletCreditResult>
{
    private readonly WalletMutations _wallets;
    private readonly ICustomerWalletRepository _customerWallets;
    private readonly IWalletUnitOfWork _unitOfWork;

    public CreditCustomerRefundCommandHandler(
        WalletMutations wallets,
        ICustomerWalletRepository customerWallets,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _customerWallets = customerWallets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWalletCreditResult>> Handle(
        CreditCustomerRefundCommand command, CancellationToken cancellationToken)
    {
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Failure<CustomerWalletCreditResult>(Error.Validation(
                "wallet.customer.idempotency_key_required",
                "Une clé d'idempotence est obligatoire pour créditer un remboursement : sans elle, un appel rejoué rendrait l'argent deux fois."));
        }

        // La clé de l'appelant, projetée dans l'espace des `Guid` que le grand
        // livre sait indexer.
        var reference = WalletReference.FromIdempotencyKey(command.CustomerId, command.IdempotencyKey);

        // LE REGISTRE EST CONSULTÉ AVANT LA MOINDRE ÉCRITURE.
        var deja = await _wallets.FindCustomerRefundCreditAsync(reference, cancellationToken);
        if (deja is not null)
        {
            // `BalanceAfter` est toujours écrit par ce flux ; le repli existe pour
            // les écritures qui n'en portaient pas (voir
            // `WalletTransaction.BalanceAfter`) plutôt que de rendre un zéro qui
            // serait un mensonge.
            var soldeConnu = deja.BalanceAfter;
            if (soldeConnu is null)
            {
                var portefeuille = await _customerWallets.GetByCustomerAsync(command.CustomerId, cancellationToken);
                soldeConnu = portefeuille?.AvailableBalance ?? 0m;
            }

            return new CustomerWalletCreditResult(
                deja.TransactionId, soldeConnu.Value, deja.Currency, AlreadyApplied: true);
        }

        var credit = await _wallets.CreditCustomerRefundAsync(
            command.CustomerId, command.Amount, command.Currency, command.Reason, reference, cancellationToken);

        if (credit.IsFailure)
        {
            return Result.Failure<CustomerWalletCreditResult>(credit.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var ecriture = credit.Value;
        return new CustomerWalletCreditResult(
            ecriture.TransactionId,
            ecriture.BalanceAfter ?? 0m,
            ecriture.Currency,
            AlreadyApplied: false);
    }
}

/// <summary>
/// Demande de virement d'un client vers son Mobile Money : RETIENT les fonds et
/// crée la demande, dans la MÊME transaction.
/// </summary>
public sealed record RequestCustomerWithdrawalCommand(
    Guid CustomerId, decimal Amount, string Msisdn, string Provider, string? IdempotencyKey = null)
    : ICommand<CustomerWithdrawalView>;

public sealed class RequestCustomerWithdrawalCommandValidator : AbstractValidator<RequestCustomerWithdrawalCommand>
{
    public RequestCustomerWithdrawalCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant doit être positif.");
        RuleFor(c => c.Msisdn).NotEmpty().WithMessage("Le numéro Mobile Money est requis.");
        RuleFor(c => c.Provider).NotEmpty().WithMessage("L'opérateur est requis.");

        // AUCUNE RÈGLE SUR `IdempotencyKey` ICI, ET CE N'EST PAS UN OUBLI.
    }
}

internal sealed class RequestCustomerWithdrawalCommandHandler
    : ICommandHandler<RequestCustomerWithdrawalCommand, CustomerWithdrawalView>
{
    private readonly ICustomerWalletRepository _wallets;
    private readonly ICustomerWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IWalletUnitOfWork _unitOfWork;

    public RequestCustomerWithdrawalCommandHandler(
        ICustomerWalletRepository wallets,
        ICustomerWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWithdrawalView>> Handle(
        RequestCustomerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? HbaRequestContext.Current.IdempotencyKey
            : command.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<CustomerWithdrawalView>(Error.Validation(
                "wallet.customer_withdrawal.idempotency_key_required",
                "L'en-tête Idempotency-Key est obligatoire pour demander un virement : sans lui, un appel réessayé retiendrait deux fois le solde."));
        }

        // L'opérateur doit être un Mobile Money que l'on sait réellement router :
        // c'est ce numéro que l'administrateur recopiera chez le prestataire, et
        // découvrir à ce moment-là que l'opérateur n'existe pas laisserait le
        // client avec des fonds retenus pour une demande impayable.
        if (!WalletPayout.IsMobileMoney(command.Provider))
        {
            return Result.Failure<CustomerWithdrawalView>(Error.Validation(
                "wallet.customer_withdrawal.provider_unsupported",
                "Opérateur Mobile Money non pris en charge (mtnmomo, moovmoney, celtis)."));
        }

        var wallet = await _wallets.GetByCustomerAsync(command.CustomerId, cancellationToken);
        if (wallet is null)
        {
            // Pas de portefeuille = aucun remboursement n'a jamais été crédité.
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.customer.not_found", "Aucun portefeuille pour ce client."));
        }

        // LA RETENUE ET LA DEMANDE DANS LE MÊME SaveChanges.
        var retenue = wallet.Hold(command.Amount);
        if (retenue.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(retenue.Error);
        }

        // La destination est FIGÉE ici : c'est elle, et rien d'autre, que
        // l'administrateur lira dans sa file.
        var withdrawal = CustomerWithdrawal.Create(
            command.CustomerId, command.Amount, wallet.Currency,
            command.Msisdn, command.Provider, idempotencyKey!);

        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _ledger.AddAsync(WalletTransaction.ForCustomer(
            command.CustomerId, WalletDirection.Debit, command.Amount, wallet.Currency,
            "customer_withdrawal_request", WalletMutations.CustomerWithdrawalReferenceType,
            withdrawal.Id.Value, WalletLedger.NewTransactionId(), wallet.AvailableBalance), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomerWalletMapper.ToView(withdrawal);
    }
}

/// <summary>
/// L'administrateur a exécuté le virement chez le prestataire et le marque payé,
/// avec la référence du virement.
/// </summary>
public sealed record MarkCustomerWithdrawalPaidCommand(
    Guid WithdrawalId, Guid AdminId, string ExternalReference) : ICommand<CustomerWithdrawalView>;

public sealed class MarkCustomerWithdrawalPaidCommandValidator : AbstractValidator<MarkCustomerWithdrawalPaidCommand>
{
    public MarkCustomerWithdrawalPaidCommandValidator()
    {
        RuleFor(c => c.WithdrawalId).NotEmpty();
        RuleFor(c => c.AdminId).NotEmpty();

        // La référence du virement est la SEULE preuve que l'argent est parti :
        // aucun webhook ne confirmera ce versement, aucune réconciliation ne
        // l'interrogera.
        RuleFor(c => c.ExternalReference).NotEmpty()
            .WithMessage("La référence du virement est obligatoire.");
    }
}

internal sealed class MarkCustomerWithdrawalPaidCommandHandler
    : ICommandHandler<MarkCustomerWithdrawalPaidCommand, CustomerWithdrawalView>
{
    private readonly ICustomerWithdrawalRepository _withdrawals;
    private readonly IWalletUnitOfWork _unitOfWork;

    public MarkCustomerWithdrawalPaidCommandHandler(
        ICustomerWithdrawalRepository withdrawals, IWalletUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWithdrawalView>> Handle(
        MarkCustomerWithdrawalPaidCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(
            new CustomerWithdrawalId(command.WithdrawalId), cancellationToken);

        if (withdrawal is null)
        {
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.customer_withdrawal.not_found", "Demande de virement introuvable."));
        }

        // Toute transition depuis un autre état que `Requested` est refusée par
        // l'agrégat, en `Conflict` : deux administrateurs sur la même file cliquent
        // parfois à quelques secondes d'écart, et le second doit voir que le
        // dossier lui a échappé plutôt que d'écraser la référence saisie par le
        // premier.
        var paiement = withdrawal.MarkPaid(command.AdminId, command.ExternalReference, DateTime.UtcNow);
        if (paiement.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(paiement.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomerWalletMapper.ToView(withdrawal);
    }
}

/// <summary>Refus admin d'une demande de virement : RESTITUE les fonds retenus.</summary>
public sealed record RejectCustomerWithdrawalCommand(
    Guid WithdrawalId, Guid AdminId, string Reason) : ICommand<CustomerWithdrawalView>;

public sealed class RejectCustomerWithdrawalCommandValidator : AbstractValidator<RejectCustomerWithdrawalCommand>
{
    public RejectCustomerWithdrawalCommandValidator()
    {
        RuleFor(c => c.WithdrawalId).NotEmpty();
        RuleFor(c => c.AdminId).NotEmpty();

        // Un refus non motivé sur de l'argent dû arrive au support sans rien à
        // répondre au client.
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Un motif de refus est obligatoire.");
    }
}

internal sealed class RejectCustomerWithdrawalCommandHandler
    : ICommandHandler<RejectCustomerWithdrawalCommand, CustomerWithdrawalView>
{
    private readonly ICustomerWalletRepository _wallets;
    private readonly ICustomerWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IWalletUnitOfWork _unitOfWork;

    public RejectCustomerWithdrawalCommandHandler(
        ICustomerWalletRepository wallets,
        ICustomerWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerWithdrawalView>> Handle(
        RejectCustomerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(
            new CustomerWithdrawalId(command.WithdrawalId), cancellationToken);

        if (withdrawal is null)
        {
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.customer_withdrawal.not_found", "Demande de virement introuvable."));
        }

        var wallet = await _wallets.GetByCustomerAsync(withdrawal.CustomerId, cancellationToken);
        if (wallet is null)
        {
            // CE CAS NE DEVRAIT PAS EXISTER, ET ON REFUSE PLUTÔT QUE DE L'IGNORER.
            return Result.Failure<CustomerWithdrawalView>(Error.Conflict(
                "wallet.customer.not_found",
                "Aucun portefeuille pour ce client : les fonds retenus ne peuvent pas être restitués automatiquement."));
        }

        var refus = withdrawal.Reject(command.AdminId, command.Reason, DateTime.UtcNow);
        if (refus.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(refus.Error);
        }

        // LA RESTITUTION ET LE REFUS DANS LE MÊME SaveChanges.
        var restitution = wallet.Restore(withdrawal.Amount);
        if (restitution.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(restitution.Error);
        }

        await _ledger.AddAsync(WalletTransaction.ForCustomer(
            withdrawal.CustomerId, WalletDirection.Credit, withdrawal.Amount, withdrawal.Currency,
            "customer_withdrawal_reject", WalletMutations.CustomerWithdrawalReferenceType,
            withdrawal.Id.Value, WalletLedger.NewTransactionId(), wallet.AvailableBalance), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomerWalletMapper.ToView(withdrawal);
    }
}
