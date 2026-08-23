using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// DEMANDE DE VIREMENT D'UN CLIENT DEPUIS SON PORTEFEUILLE VERS SON MOBILE MONEY.
///
/// Créée à l'état <c>Requested</c> — les fonds sont DÉJÀ retenus sur le
/// portefeuille — puis tranchée par un administrateur : <c>Paid</c> avec la
/// référence du virement, ou <c>Rejected</c> avec les fonds restitués.
///
/// AUCUN APPEL PSP, ET C'EST TOUTE LA DIFFÉRENCE AVEC `Withdrawal`.
///
/// Le retrait vendeur déclenche un payout FedaPay : il lui faut un état
/// « demandé au PSP, non confirmé » (`Processing`), une réconciliation, et la
/// règle « on ne rembourse jamais sur issue indéterminée ». Ici, c'est un humain
/// qui exécute le virement chez le prestataire. Il n'y a donc rien à
/// réconcilier — et recopier ces états aurait créé des demandes bloquées dans un
/// `Processing` dont aucun mécanisme ne les sortirait.
///
/// Ce que ce choix ne couvre pas : la validation manuelle est un point de
/// contrôle des sorties d'argent, pas une garantie. Elle tient tant que le volume
/// reste humainement traitable. La rendre automatique se fera le jour où le
/// volume l'exigera — le canal de versement (`IPayoutModuleApi`) existe déjà, il
/// ne manquera qu'une décision (D33).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CustomerWithdrawal : AggregateRoot<CustomerWithdrawalId>
{
    // ctor EF.
    private CustomerWithdrawal()
    {
    }

    private CustomerWithdrawal(
        CustomerWithdrawalId id, Guid customerId, decimal amount, string currency,
        string msisdn, string provider, string idempotencyKey)
        : base(id)
    {
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        Msisdn = msisdn;
        Provider = provider;
        IdempotencyKey = idempotencyKey;
        Status = CustomerWithdrawalStatus.Requested;
        RequestedAtUtc = DateTime.UtcNow;
    }

    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.
    ///
    /// C'EST EXACTEMENT LA FAILLE DÉCRITE PAR `Withdrawal.PayoutProvider`, ET
    /// ON NE LA RÉINTRODUIT PAS.
    ///
    /// Le retrait vendeur ne portait au départ que le montant : à l'approbation,
    /// le handler relisait le compte de versement COURANT. Il suffisait donc de
    /// modifier ce compte entre la demande et la validation pour détourner
    /// l'argent — l'administrateur approuvait un montant qu'il voyait, vers une
    /// destination qu'il ne voyait pas, puisque sa file d'attente affichait elle
    /// aussi le compte lu à l'instant présent.
    ///
    /// Ici le numéro et l'opérateur sont SAISIS par le client au moment de la
    /// demande et gravés dans la ligne. Ce sont eux, et rien d'autre, que
    /// l'administrateur lit dans sa file et recopie chez le prestataire.
    ///
    /// NON NULLABLES : cette table naît avec ces colonnes. La dette de nullabilité
    /// de `Withdrawal` — des demandes antérieures aux colonnes — n'a pas d'équivalent
    /// ici, et il n'y a donc aucun repli sur « le compte courant du client » à écrire.
    ///
    /// En Mobile Money, un virement parti ne revient pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string Msisdn { get; private set; } = default!;

    /// <summary>Opérateur Mobile Money visé, figé à la demande. Voir <see cref="Msisdn"/>.</summary>
    public string Provider { get; private set; } = default!;

    public CustomerWithdrawalStatus Status { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Instant de la décision de l'administrateur (paiement ou refus).</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>
    /// Administrateur qui a tranché.
    ///
    /// Ce n'est pas de la décoration : une sortie d'argent validée à la main sans
    /// nom d'auteur ne peut être ni contestée ni expliquée. C'est la seule chose,
    /// avec la référence externe, qui rattache un mouvement réel à une personne.
    /// </summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>
    /// Référence du virement saisie par l'administrateur (identifiant de transaction
    /// chez le prestataire, numéro de bordereau…). Voir <see cref="MarkPaid"/>.
    /// </summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Motif du refus, ou note libre accompagnant le paiement.</summary>
    public string? AdminNote { get; private set; }

    /// <summary>
    /// Clé d'idempotence de la DEMANDE, telle que le client l'a envoyée dans
    /// l'en-tête `Idempotency-Key` (§5).
    ///
    /// ELLE PROTÈGE LA RETENUE, PAS LE VIREMENT.
    ///
    /// Ce que ferme cette clé, c'est le double-clic qui retiendrait DEUX FOIS le
    /// solde du client et créerait deux demandes pour un seul besoin — le client se
    /// retrouverait avec un portefeuille vide et deux virements en attente. Le
    /// virement lui-même n'est pas protégé par elle : il est exécuté à la main, et
    /// c'est la file d'administration qui empêche de le faire deux fois.
    ///
    /// Le domaine n'en invente pas : voir le refus explicite dans
    /// `RequestCustomerWithdrawalCommandHandler`, et le même raisonnement dans
    /// `CustomerRefund.IdempotencyKey`.
    /// </summary>
    public string IdempotencyKey { get; private set; } = default!;

    /// <summary>Vrai tant que la demande attend la décision de l'administrateur.</summary>
    public bool IsPendingDecision => Status == CustomerWithdrawalStatus.Requested;

    public static CustomerWithdrawal Create(
        Guid customerId, decimal amount, string currency, string msisdn, string provider, string idempotencyKey)
        => new(CustomerWithdrawalId.New(), customerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            msisdn.Trim(), provider.Trim().ToLowerInvariant(), idempotencyKey);

    /// <summary>
    /// L'administrateur a exécuté le virement chez le prestataire et le déclare payé.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA RÉFÉRENCE EXTERNE EST EXIGÉE, ET CE N'EST PAS UN CHAMP DE CONFORT.
    ///
    /// C'est la SEULE preuve que l'argent est parti. Aucun webhook ne confirmera ce
    /// virement, aucune réconciliation ne l'interrogera : la ligne dit « payé »
    /// parce qu'un humain l'a dit. Sans référence, ce « payé » est invérifiable —
    /// le jour où le client affirme n'avoir rien reçu, il n'existe rien à opposer,
    /// rien à rechercher dans le tableau de bord du prestataire, et rien à
    /// rapprocher au relevé bancaire.
    ///
    /// On refuse donc franchement plutôt que d'accepter un « payé » vide qui
    /// paraîtrait clore le dossier et le rendrait en réalité inarbitrable.
    ///
    /// CE QU'ELLE NE PROUVE PAS : que le virement est arrivé, ni qu'il visait le
    /// bon numéro. Elle rend le rapprochement POSSIBLE ; le rapprochement lui-même
    /// est un rapport d'exploitation qui n'est pas dans ce lot (D33).
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result MarkPaid(Guid adminId, string externalReference, DateTime nowUtc)
    {
        if (Status != CustomerWithdrawalStatus.Requested)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer_withdrawal.not_pending",
                "Cette demande de virement a déjà été tranchée."));
        }

        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.reference_required",
                "La référence du virement est obligatoire : sans elle, « payé » n'est vérifiable nulle part."));
        }

        if (adminId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.admin_required",
                "L'auteur de la décision est obligatoire."));
        }

        Status = CustomerWithdrawalStatus.Paid;
        ExternalReference = externalReference.Trim();
        DecidedByUserId = adminId;
        DecidedAtUtc = nowUtc;

        return Result.Success();
    }

    /// <summary>
    /// Refus par l'administrateur. Les fonds sont restitués par le gestionnaire
    /// appelant, dans le MÊME SaveChanges — séparés, un échec entre les deux
    /// laisserait une demande refusée et un solde jamais rendu.
    ///
    /// Le motif est obligatoire : un refus non motivé sur de l'argent dû arrive au
    /// support sans rien à répondre au client.
    /// </summary>
    public Result Reject(Guid adminId, string reason, DateTime nowUtc)
    {
        if (Status != CustomerWithdrawalStatus.Requested)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer_withdrawal.not_pending",
                "Cette demande de virement a déjà été tranchée."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.reason_required",
                "Un motif de refus est obligatoire."));
        }

        if (adminId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.admin_required",
                "L'auteur de la décision est obligatoire."));
        }

        Status = CustomerWithdrawalStatus.Rejected;
        AdminNote = reason.Trim();
        DecidedByUserId = adminId;
        DecidedAtUtc = nowUtc;

        return Result.Success();
    }
}
