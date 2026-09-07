using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.Domain.Sellers;

/// <summary>
/// Cycle de vie complet d'un vendeur : onboarding, KYB, boutique, commission,
/// coordonnées de payout (cf.
/// </summary>
public sealed class Seller : AggregateRoot<SellerId>
{
    private readonly List<KybDocument> _kybDocuments = new();

    private Seller()
    {
    }

    private Seller(SellerId id, Guid userId, string shopName, decimal commissionRate)
        : base(id)
    {
        UserId = userId;
        ShopName = shopName;
        CommissionRate = commissionRate;
        Status = SellerStatus.Pending;
        KybStatus = KybStatus.NotStarted;
        Rating = 0m;
        SalesCount = 0;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new SellerRegisteredDomainEvent(id.Value, userId, shopName));
    }

    /// <summary>Le compte propriétaire du dossier.</summary>
    public Guid UserId { get; private set; }
    public string ShopName { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? Description { get; private set; }
    public SellerStatus Status { get; private set; }
    public KybStatus KybStatus { get; private set; }

    /// <summary>Pourquoi le dossier a été refusé.</summary>
    public string? KybRejectionReason { get; private set; }

    /// <summary>COLONNE MORTE. NE PAS LIRE, NE PAS AFFICHER.</summary>
    public decimal CommissionRate { get; private set; }
    public PayoutAccount? PayoutAccount { get; private set; }

    /// <summary>Informations société déclarées par le vendeur (jsonb, null par défaut).</summary>
    public SellerCompanyInfo? Metadata { get; private set; }

    /// <summary>STATUT D'AVANT LA SUSPENSION, POUR SAVOIR OÙ RENDRE LE COMPTE.</summary>
    public SellerStatus? SuspendedFromStatus { get; private set; }

    public decimal Rating { get; private set; }
    public int SalesCount { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<KybDocument> KybDocuments => _kybDocuments.AsReadOnly();

    /// <summary>Onboarde un vendeur rattaché à un compte Identity.</summary>
    public static Result<Seller> Register(
        Guid userId, string shopName, decimal commissionRate, SellerCompanyInfo? metadata = null)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation("sellers.seller.user_required", "Le compte utilisateur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(shopName))
        {
            return Error.Validation("sellers.seller.shop_name_required", "Le nom de la boutique est obligatoire.");
        }

        if (commissionRate is < 0m or > 1m)
        {
            return Error.Validation("sellers.seller.commission_invalid", "Le taux de commission doit être compris entre 0 et 1.");
        }

        var seller = new Seller(SellerId.New(), userId, shopName.Trim(), commissionRate);
        seller.Metadata = metadata;
        return seller;
    }

    /// <summary>
    /// Met à jour les informations société déclarées (édition du profil vendeur).
    /// </summary>
    public Result UpdateMetadata(SellerCompanyInfo? metadata)
    {
        Metadata = metadata;
        return Result.Success();
    }

    public Result UpdateProfile(string shopName, string? logoUrl, string? description)
    {
        if (string.IsNullOrWhiteSpace(shopName))
        {
            return Result.Failure(Error.Validation("sellers.seller.shop_name_required", "Le nom de la boutique est obligatoire."));
        }

        ShopName = shopName.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    public Result SetPayoutAccount(PayoutAccount payoutAccount)
    {
        PayoutAccount = payoutAccount;
        return Result.Success();
    }

    /// <summary>Ajoute une pièce KYB ; bascule la vérification en revue.</summary>
    public Result<KybDocument> AddKybDocument(KybDocumentType type, Guid mediaId)
    {
        if (mediaId == Guid.Empty)
        {
            return Error.Validation("sellers.kyb.file_required", "Le fichier de la pièce KYB est obligatoire.");
        }

        // L'EXISTENCE ET L'APPARTENANCE DU MÉDIA NE SONT PAS VÉRIFIÉES ICI.
        var document = new KybDocument(Guid.NewGuid(), type, mediaId);
        _kybDocuments.Add(document);

        // Le motif du refus précédent ne survit pas au nouveau dépôt : affiché sur
        // un dossier que le vendeur est en train de corriger, il lui ferait croire
        // que sa correction a déjà été refusée.
        if (KybStatus is KybStatus.Rejected)
        {
            KybRejectionReason = null;
        }

        // BASCULE AUTOMATIQUE — DÉPRÉCIÉE. À RETIRER QUAND L'APP ENVERRA
        // `SubmitKyb`.
        if (KybStatus is KybStatus.NotStarted or KybStatus.Rejected)
        {
            KybStatus = KybStatus.InReview;
            Raise(new SellerKybSubmittedDomainEvent(Id.Value, UserId, _kybDocuments.Count));
        }

        return document;
    }

    /// <summary>LE VENDEUR DÉCLARE SON DOSSIER COMPLET (§10.3 : POST /kyc/submit).</summary>
    public Result SubmitKyb()
    {
        if (_kybDocuments.Count == 0)
        {
            return Result.Failure(Error.Conflict(
                "sellers.kyb.no_documents",
                "Déposez au moins une pièce avant de soumettre votre dossier."));
        }

        if (KybStatus is KybStatus.InReview)
        {
            // Déjà soumis : l'appelant a obtenu ce qu'il voulait.
            return Result.Success();
        }

        if (KybStatus is KybStatus.Verified)
        {
            // ON NE DÉ-VÉRIFIE PAS UN DOSSIER EN RÈGLE.
            return Result.Success();
        }

        KybStatus = KybStatus.InReview;
        KybRejectionReason = null;

        Raise(new SellerKybSubmittedDomainEvent(Id.Value, UserId, _kybDocuments.Count));
        return Result.Success();
    }

    /// <summary>LA PROPRIÉTÉ DU DOSSIER PASSE À UN AUTRE COMPTE.</summary>
    public Result TransferOwnership(Guid nouveauProprietaireUserId)
    {
        if (nouveauProprietaireUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "sellers.ownership.recipient_required", "Le nouveau propriétaire est requis."));
        }

        if (nouveauProprietaireUserId == UserId)
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.already_owner", "Ce compte est déjà propriétaire du dossier."));
        }

        UserId = nouveauProprietaireUserId;
        return Result.Success();
    }

    /// <summary>
    /// Retire une pièce KYB de la boutique (le vendeur supprime un document qu'il a
    /// téléversé).
    /// </summary>
    public Result<KybDocument> RemoveKybDocument(Guid documentId)
    {
        var document = _kybDocuments.FirstOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return Error.NotFound("sellers.kyb.not_found", "Pièce KYB introuvable pour cette boutique.");
        }

        _kybDocuments.Remove(document);

        // RETIRER LA DERNIÈRE PIÈCE RAMÈNE LE DOSSIER À « NON COMMENCÉ ».
        if (_kybDocuments.Count == 0 && KybStatus is KybStatus.InReview)
        {
            KybStatus = KybStatus.NotStarted;
        }

        // LE FICHIER N'EST PAS EFFACÉ ICI, ET IL NE PEUT PAS L'ÊTRE.
        if (!document.IsLegacy)
        {
            Raise(new KybDocumentRemovedDomainEvent(Id.Value, UserId, document.MediaId));
        }

        return document;
    }

    /// <summary>Valide le KYB (modération) : marque les pièces vérifiées.</summary>
    public Result ApproveKyb()
    {
        if (_kybDocuments.Count == 0)
        {
            return Result.Failure(Error.Conflict("sellers.kyb.no_documents", "Aucune pièce KYB à valider."));
        }

        foreach (var document in _kybDocuments)
        {
            document.MarkVerified();
        }

        KybStatus = KybStatus.Verified;
        KybRejectionReason = null;
        Raise(new SellerKybVerifiedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>LA MODÉRATION REFUSE LE DOSSIER.</summary>
    public Result RejectKyb(string? reason = null)
    {
        if (KybStatus == KybStatus.Rejected)
        {
            // Idempotent : réémettre relancerait notification et suspension.
            return Result.Success();
        }

        if (KybStatus == KybStatus.NotStarted)
        {
            // Rejeter un dossier jamais déposé n'a pas de sens, et laisserait le
            // vendeur devant un refus qu'il ne peut pas corriger : il n'a rien
            // envoyé.
            return Result.Failure(Error.Conflict(
                "sellers.kyb.nothing_to_reject",
                "Aucune pièce n'a été déposée : il n'y a rien à rejeter."));
        }

        KybStatus = KybStatus.Rejected;
        KybRejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new SellerKybRejectedDomainEvent(Id.Value, UserId, KybRejectionReason));

        // UN VENDEUR ACTIF EST SUSPENDU PAR LE REFUS.
        if (Status == SellerStatus.Active)
        {
            var suspension = Suspend(ComposeKybSuspensionReason(KybRejectionReason));
            if (suspension.IsFailure)
            {
                return suspension;
            }
        }

        return Result.Success();
    }

    /// <summary>Motif de suspension quand elle découle d'un refus de dossier.</summary>
    private static string ComposeKybSuspensionReason(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? "dossier KYB rejeté"
            : $"dossier KYB rejeté : {reason}";

    /// <summary>Active le vendeur (KYB validé et coordonnées de payout requis).</summary>
    public Result Activate()
    {
        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé avant l'activation."));
        }

        if (PayoutAccount is null)
        {
            return Result.Failure(Error.Conflict("sellers.seller.payout_required", "Les coordonnées de reversement sont requises pour l'activation."));
        }

        Status = SellerStatus.Active;
        Raise(new SellerActivatedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>SUSPEND LE VENDEUR — LA MESURE D'URGENCE DE L'EXPLOITATION.</summary>
    public Result Suspend(string? reason = null)
    {
        if (Status == SellerStatus.Suspended)
        {
            // Idempotent : réémettre l'événement relancerait une suspension de
            // catalogue déjà faite.
            return Result.Success();
        }

        if (Status is SellerStatus.Closed or SellerStatus.PendingReactivation)
        {
            // La garde décrite dans l'encadré ci-dessus.
            return Result.Failure(Error.Conflict(
                "sellers.seller.closed_cannot_suspend",
                "Un compte fermé ne peut pas être suspendu : son catalogue est déjà retiré de la vente."));
        }

        // Voir l'encadré de `SuspendedFromStatus` : c'est ce qui permet à la levée
        // de rendre le compte là d'où il vient, plutôt que de le poser en `Active`.
        SuspendedFromStatus = Status;
        Status = SellerStatus.Suspended;

        Raise(new SellerSuspendedDomainEvent(Id.Value, UserId, reason));
        return Result.Success();
    }

    /// <summary>Lève une suspension prononcée par l'exploitation.</summary>
    public Result LiftSuspension()
    {
        if (Status != SellerStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.not_suspended", "Seul un compte suspendu peut être rétabli."));
        }

        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé."));
        }

        if (PayoutAccount is null)
        {
            // Même exigence que Activate() : un vendeur qui vend sans compte de
            // reversement accumule des gains que rien ne peut lui verser.
            return Result.Failure(Error.Conflict(
                "sellers.seller.payout_required", "Les coordonnées de reversement sont requises."));
        }

        // ON REND LE COMPTE LÀ D'OÙ IL VIENT, PAS EN `Active` D'OFFICE.
        var retour = SuspendedFromStatus ?? SellerStatus.Active;
        SuspendedFromStatus = null;

        Status = retour is SellerStatus.Active ? SellerStatus.Active : SellerStatus.Pending;

        Raise(new SellerSuspensionLiftedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>Fermeture demandée par le vendeur lui-même (suppression partielle).</summary>
    public Result RequestClosure()
    {
        if (Status is SellerStatus.Closed or SellerStatus.PendingReactivation)
        {
            return Result.Failure(Error.Conflict("sellers.seller.already_closed", "Le compte est déjà fermé."));
        }

        Status = SellerStatus.Closed;
        Raise(new SellerClosedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// Le vendeur (compte fermé) demande la réactivation : passe en attente de
    /// validation admin.
    /// </summary>
    public Result RequestReactivation()
    {
        if (Status != SellerStatus.Closed)
        {
            return Result.Failure(Error.Conflict("sellers.seller.not_closed", "Seul un compte fermé peut demander une réactivation."));
        }

        Status = SellerStatus.PendingReactivation;
        return Result.Success();
    }

    /// <summary>L'ADMIN APPROUVE LA DEMANDE DE RÉACTIVATION.</summary>
    public Result ApproveReactivation()
    {
        if (Status is not SellerStatus.PendingReactivation)
        {
            return Result.Failure(Error.Conflict("sellers.seller.no_reactivation_request", "Aucune demande de réactivation en cours."));
        }

        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé."));
        }

        // MÊME EXIGENCE QUE `Activate` ET `LiftSuspension` — ELLE MANQUAIT ICI.
        if (PayoutAccount is null)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.payout_required",
                "Les coordonnées de reversement sont requises pour réactiver le compte."));
        }

        Status = SellerStatus.Active;
        SuspendedFromStatus = null;
        Raise(new SellerReactivatedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// Prépare la suppression DÉFINITIVE (admin) : émet l'événement qui purge les
    /// produits du vendeur avant que l'agrégat ne soit retiré du dépôt.
    /// </summary>
    public Result MarkForDeletion()
    {
        // CHAQUE PIÈCE D'IDENTITÉ EST NOMMÉE, UNE PAR UNE.
        foreach (var document in _kybDocuments.Where(d => !d.IsLegacy))
        {
            Raise(new KybDocumentRemovedDomainEvent(Id.Value, UserId, document.MediaId));
        }

        Raise(new SellerDeletedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>Met à jour la note moyenne (0 à 5), alimentée par le module Reviews.</summary>
    public Result UpdateRating(decimal rating)
    {
        if (rating is < 0m or > 5m)
        {
            return Result.Failure(Error.Validation("sellers.seller.rating_invalid", "La note doit être comprise entre 0 et 5."));
        }

        Rating = rating;
        return Result.Success();
    }

    /// <summary>Incrémente le compteur de ventes, alimenté par Ordering.</summary>
    public void RecordSale() => SalesCount++;

    /// <summary>
    /// Fixe le compteur de ventes à une valeur recalculée depuis la source
    /// (Ordering).
    /// </summary>
    public void SetSalesCount(int count) => SalesCount = count < 0 ? 0 : count;
}
