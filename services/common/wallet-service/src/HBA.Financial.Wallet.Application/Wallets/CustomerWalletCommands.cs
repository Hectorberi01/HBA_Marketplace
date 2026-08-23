using FluentValidation;
using HBA.Shared.Application.Context;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

// ════════════════════════════════════════════════════════════════════════════
// LE PORTEFEUILLE CLIENT (D33 dans docs/DECISIONS.md).
//
// CE CANAL N'EXISTAIT PAS, ET LE CLIENT N'ÉTAIT DONC JAMAIS REMBOURSÉ.
//
// FedaPay n'expose AUCUNE API de remboursement — pas plus que MTN, Moov ou
// PayPal dans ce dépôt. Un retour validé, un remboursement décidé, et l'appel
// répondait `Success: false` : le dossier escaladait en `ManualReview` et
// l'argent ne revenait au client que si quelqu'un y pensait.
//
// Le flux est en DEUX temps, et ce découpage est la décision, pas une commodité :
//   1. le remboursement CRÉDITE le portefeuille — l'argent est rendu tout de
//      suite, à l'intérieur de la plateforme, et le client peut le dépenser ;
//   2. le virement vers son Mobile Money est une DEMANDE distincte, retenue à
//      la demande, exécutée et marquée payée À LA MAIN par un administrateur.
//
// POURQUOI PAS DE VERSEMENT AUTOMATIQUE À L'INSTANT DU REMBOURSEMENT.
//
// `IPayoutModuleApi` existe et sait verser en Mobile Money — les retraits vendeur
// s'en servent. Deux raisons de ne pas l'appeler ici. D'abord un versement parti
// ne revient pas : le déclencher sur un flux qui comporte encore des arbitrages
// — retour litigieux, inspection contestée — transformerait chaque erreur en
// perte sèche. Ensuite le numéro Mobile Money du client n'est porté par aucun
// contexte de retour : il faudrait le lui demander au moment le plus mal choisi,
// celui où il attend son argent.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Rend un montant au client sur son portefeuille et l'inscrit au grand livre.
/// C'est l'implémentation de <see cref="ICustomerWalletApi.CreditRefundAsync"/>,
/// appelée par payment-service quand la passerelle ne sait pas rembourser.
///
/// <para>
/// `IdempotencyKey` est la référence d'idempotence du grand livre : un rejeu ne
/// crédite pas deux fois, il rend le même résultat. Elle est OBLIGATOIRE.
/// </para>
/// </summary>
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
        //
        // Le refus est dans `Handle`, avec son propre code d'erreur. Un
        // `NotEmpty` ici rendrait un message de validation générique là où
        // l'appelant — payment-service, en plein remboursement — a besoin de
        // savoir EXACTEMENT ce qui manque pour ne pas réessayer en boucle.
        // Même raisonnement que dans `InitiateCustomerRefundCommandValidator`.
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
        // ═════════════════════════════════════════════════════════════════════
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        //
        // CE QU'ELLE EMPÊCHE : QUE LA PLATEFORME RENDE DEUX FOIS LE MÊME ARGENT.
        //
        // L'appelant est payment-service, sur un chemin qui réessaie : un
        // remboursement rejoué — file, timeout, reprise — repasserait ici et
        // créditerait une seconde fois. Contrairement à un versement MoMo, cela se
        // rattrape (le solde est encore là) — mais personne ne le verra : le client
        // n'a aucune raison de signaler qu'il a trop reçu, et le rapprochement entre
        // le total des soldes clients et la trésorerie n'existe pas encore (D33).
        //
        // ET ON N'INVENTE PAS DE CLÉ DE REPLI, NI ICI NI AILLEURS.
        //
        // Une clé dérivée du client et du montant paraîtrait protéger et ferait
        // pire : elle interdirait un second remboursement PARTIEL légitime sur la
        // même commande, tout en laissant passer le rejeu dès que le montant change
        // d'un franc. Voir le même raisonnement dans
        // `InitiateCustomerRefundCommandHandler` et `CustomerRefund.IdempotencyKey`.
        //
        // AUCUN REPLI SUR `HbaRequestContext`, CONTRAIREMENT AU RETRAIT CLIENT.
        //
        // `RequestCustomerWithdrawalCommand` naît d'une requête HTTP du client : y
        // lire l'en-tête `Idempotency-Key` est exact. Celle-ci naît d'un APPEL DE
        // MODULE dont la signature exige déjà la clé. La requête HTTP ambiante est
        // alors celle du remboursement de payment-service, qui porte SA propre clé
        // et couvre un autre geste : la reprendre en silence ferait passer deux
        // opérations distinctes pour une seule.
        // ═════════════════════════════════════════════════════════════════════
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Failure<CustomerWalletCreditResult>(Error.Validation(
                "wallet.customer.idempotency_key_required",
                "Une clé d'idempotence est obligatoire pour créditer un remboursement : sans elle, un appel rejoué rendrait l'argent deux fois."));
        }

        // La clé de l'appelant, projetée dans l'espace des `Guid` que le grand livre
        // sait indexer. La portée inclut le client — voir l'encadré de
        // `WalletReference` : deux clients dont les jetons se recoupent verraient
        // sinon le second remboursement pris pour un rejeu du premier.
        var reference = WalletReference.FromIdempotencyKey(command.CustomerId, command.IdempotencyKey);

        // LE REGISTRE EST CONSULTÉ AVANT LA MOINDRE ÉCRITURE.
        //
        // Un rejeu ne doit RIEN créer — pas même le portefeuille. Et il doit rendre
        // le résultat du PREMIER appel : payment-service inscrit ce `TransactionId`
        // dans son propre dossier, et deux identifiants pour un seul remboursement
        // rendraient ce dossier illisible.
        var deja = await _wallets.FindCustomerRefundCreditAsync(reference, cancellationToken);
        if (deja is not null)
        {
            // `BalanceAfter` est toujours écrit par ce flux ; le repli existe pour les
            // écritures qui n'en portaient pas (voir `WalletTransaction.BalanceAfter`)
            // plutôt que de rendre un zéro qui serait un mensonge.
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
/// crée la demande, dans la MÊME transaction. Aucun versement n'est déclenché —
/// un administrateur l'exécute et le marque payé.
///
/// <para>
/// `IdempotencyKey` est laissée à `null` par un appelant HTTP : le gestionnaire la
/// reprend alors de <see cref="HbaRequestContext"/>, donc de l'en-tête
/// `Idempotency-Key`. Le paramètre existe pour les émetteurs qui n'ont pas de
/// requête entrante. Absente des deux côtés, la demande est REFUSÉE.
/// </para>
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
        //
        // Le validateur ne voit que la commande ; il ne sait pas si une requête HTTP
        // porte l'en-tête. Une règle `NotEmpty` rejetterait donc le cas NORMAL — un
        // appelant HTTP qui laisse le gestionnaire lire l'en-tête. Le contrôle est
        // dans `Handle`, où les deux sources sont visibles.
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
        // ═════════════════════════════════════════════════════════════════════
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        //
        // CE QU'ELLE EMPÊCHE : QU'UN DOUBLE-CLIC VIDE LE PORTEFEUILLE.
        //
        // Cette commande RETIENT les fonds. Un appel HTTP réessayé — réseau lent,
        // double-clic, rejeu — repasserait ici, retiendrait une SECONDE fois le
        // même solde et créerait deux demandes pour un seul besoin. Le client
        // verrait son portefeuille à zéro et deux virements en attente, et
        // l'administrateur, deux lignes identiques dans sa file sans rien pour les
        // distinguer — il en paierait probablement les deux.
        //
        // ET ON N'INVENTE PAS DE CLÉ DE REPLI.
        //
        // Une clé dérivée du client et du montant paraîtrait protéger et ferait
        // pire : elle interdirait une seconde demande légitime du même montant —
        // un client qui retire 5 000 XOF deux mois de suite — tout en laissant
        // passer le rejeu dès que le montant change d'un franc. Mieux vaut un 400
        // explicite qu'une garantie qui ment. C'est le raisonnement de
        // `InitiateCustomerRefundCommandHandler`, et il vaut ici à l'identique.
        //
        // Le contrôle est en tête, AVANT la retenue : rien ne doit être écrit pour
        // une requête qu'on va refuser.
        // ═════════════════════════════════════════════════════════════════════
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
        // découvrir à ce moment-là que l'opérateur n'existe pas laisserait le client
        // avec des fonds retenus pour une demande impayable. On refuse AVANT la
        // retenue.
        if (!WalletPayout.IsMobileMoney(command.Provider))
        {
            return Result.Failure<CustomerWithdrawalView>(Error.Validation(
                "wallet.customer_withdrawal.provider_unsupported",
                "Opérateur Mobile Money non pris en charge (mtnmomo, moovmoney, celtis)."));
        }

        var wallet = await _wallets.GetByCustomerAsync(command.CustomerId, cancellationToken);
        if (wallet is null)
        {
            // Pas de portefeuille = aucun remboursement n'a jamais été crédité. Ce
            // n'est pas une panne, c'est « vous n'avez rien à faire virer » : le 404
            // dit la vérité et n'envoie pas le client chercher un écran qui
            // n'existerait pas.
            return Result.Failure<CustomerWithdrawalView>(Error.NotFound(
                "wallet.customer.not_found", "Aucun portefeuille pour ce client."));
        }

        // LA RETENUE ET LA DEMANDE DANS LE MÊME SaveChanges.
        //
        // Séparées, un échec entre les deux laisserait soit un solde débité sans
        // demande — de l'argent disparu du portefeuille du client, sans trace de
        // destinataire — soit une demande sans retenue, que l'administrateur paierait
        // sur un solde encore dépensable.
        var retenue = wallet.Hold(command.Amount);
        if (retenue.IsFailure)
        {
            return Result.Failure<CustomerWithdrawalView>(retenue.Error);
        }

        // La destination est FIGÉE ici : c'est elle, et rien d'autre, que
        // l'administrateur lira dans sa file. Voir l'encadré de
        // `CustomerWithdrawal.Msisdn` — c'est la faille de `Withdrawal.PayoutProvider`
        // qu'on ne réintroduit pas.
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
///
/// <para>
/// AUCUN MOUVEMENT DE PORTEFEUILLE ICI : les fonds ont été retenus à la demande.
/// Les débiter à nouveau les retirerait deux fois.
/// </para>
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
        // l'interrogera. Voir l'encadré de `CustomerWithdrawal.MarkPaid`.
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
        // parfois à quelques secondes d'écart, et le second doit voir que le dossier
        // lui a échappé plutôt que d'écraser la référence saisie par le premier.
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
            //
            // Une demande de virement suppose un portefeuille : c'est lui qui a été
            // retenu. S'il a disparu, refuser laisse la demande en `Requested` — donc
            // visible dans la file, donc traitable par un humain. La marquer refusée
            // sans rien restituer effacerait la trace de fonds retenus que plus rien
            // ne rendrait.
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
        //
        // Séparés, un échec entre les deux laisserait une demande refusée et un solde
        // jamais rendu — de l'argent dû au client, invisible partout sauf dans une
        // ligne de grand livre au débit sans crédit en regard.
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
