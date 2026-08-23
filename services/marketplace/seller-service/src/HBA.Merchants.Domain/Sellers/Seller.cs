using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.Domain.Sellers;

/// <summary>
/// Cycle de vie complet d'un vendeur : onboarding, KYB, boutique, commission,
/// coordonnées de payout (cf. dossier, module Sellers). C'est ce module qui fait
/// d'une plateforme une marketplace. Agrégat racine, possède ses documents KYB.
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

    /// <summary>
    /// Le compte propriétaire du dossier.
    ///
    /// UNIQUE EN BASE, ET C'EST UNE RÈGLE MÉTIER, PAS UN INDEX DE CONFORT.
    ///
    /// `IX_sellers_UserId` est unique : un compte ne possède qu'un dossier vendeur.
    /// C'est aussi la clé de `GetByUserIdAsync`, par laquelle toutes les routes
    /// vendeur résolvent « quel dossier ce jeton administre-t-il ».
    ///
    /// IL N'ÉTAIT ÉCRIT QU'À L'INSCRIPTION, ET JAMAIS RÉASSIGNÉ. Voir
    /// <see cref="TransferOwnership"/> : c'est ce qui rendait un dossier
    /// définitivement inadministrable dès que son propriétaire disparaissait.
    /// </summary>
    public Guid UserId { get; private set; }
    public string ShopName { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? Description { get; private set; }
    public SellerStatus Status { get; private set; }
    public KybStatus KybStatus { get; private set; }

    /// <summary>
    /// Pourquoi le dossier a été refusé. Nul si aucun refus, ou refus antérieur à
    /// ce champ.
    ///
    /// CE N'EST PAS DE LA TRAÇABILITÉ, C'EST LA CONDITION DU RÉTABLISSEMENT.
    ///
    /// Sans motif, le vendeur voit « Rejeté » et ne sait pas quoi corriger. Il
    /// redépose la même pièce, la modération la refuse à nouveau, et les deux
    /// s'épuisent. La notification transporte ce texte ; la fiche le conserve pour
    /// quand elle sera relue.
    /// </summary>
    public string? KybRejectionReason { get; private set; }

    /// <summary>
    /// COLONNE MORTE. NE PAS LIRE, NE PAS AFFICHER.
    ///
    /// Elle n'est écrite qu'à l'inscription, avec un défaut, et aucun calcul ne
    /// la consulte. Elle était pourtant SERVIE AU VENDEUR dans SellerSummary : un
    /// marchand y lisait « 10 % » pendant que la configuration en appliquait un
    /// autre — une affirmation fausse sur de l'argent, que rien ne signalait. Le
    /// résumé sert désormais un taux passé de l'extérieur (voir SellerMapper).
    ///
    /// LA COMMISSION NÉGOCIÉE PAR VENDEUR EXISTE — ELLE NE VIT PAS ICI.
    ///
    /// Elle vit dans le MOTEUR DE RÈGLES de Billing (portée « Seller »), que la
    /// comptabilisation des gains interroge désormais. Réactiver cette colonne
    /// recréerait la troisième source qu'on vient de fermer.
    ///
    /// CE QUE LE VENDEUR VOIT PEUT ENCORE ÊTRE FAUX, ET IL FAUT LE SAVOIR.
    ///
    /// Le résumé affiche <c>IPlatformPricing.CommissionRate</c> — le DÉFAUT du
    /// moteur, pas la règle qui s'applique à CE vendeur. Tant qu'aucune règle
    /// « Seller » n'est créée, les deux coïncident. Dès qu'un admin en crée une à
    /// 5 %, l'argent prélève 5 % et l'écran vendeur annonce toujours 10 %.
    ///
    /// Le combler demande que merchant-service interroge Billing, donc un serveur
    /// gRPC financial : <c>financial.proto</c> DÉCLARE déjà <c>ComputeCommission</c>,
    /// mais aucune classe ne l'implémente — le contrat existe, le service non.
    /// C'est un travail à part entière, et le dire vaut mieux que de laisser
    /// croire le contraire.
    ///
    /// Conservée sans être lue : la retirer demanderait une migration pour un
    /// gain nul.
    /// </summary>
    public decimal CommissionRate { get; private set; }
    public PayoutAccount? PayoutAccount { get; private set; }

    /// <summary>
    /// Informations société déclarées par le vendeur (jsonb, null par défaut).
    /// Renseignée à l'auto-inscription depuis l'app ; jamais une preuve — voir
    /// <see cref="SellerCompanyInfo"/> et le KYB.
    /// </summary>
    public SellerCompanyInfo? Metadata { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// STATUT D'AVANT LA SUSPENSION, POUR SAVOIR OÙ RENDRE LE COMPTE.
    ///
    /// SANS LUI, `LiftSuspension` POSAIT `Active` AVEUGLÉMENT.
    ///
    /// Un vendeur encore `Pending` — jamais activé — qui se faisait suspendre puis
    /// rétablir arrivait donc en `Active` sans jamais passer par `Activate()`, et
    /// donc sans que `SellerActivatedDomainEvent` ne soit émis. Tout consommateur
    /// qui attend l'activation pour agir — ouvrir un portefeuille, autoriser la
    /// mise en vente — ne voyait jamais passer ce vendeur-là.
    ///
    /// Le défaut a été trouvé en écrivant le premier test de ce cycle, pas en
    /// production : personne n'avait jamais emprunté ce chemin.
    ///
    /// Nul quand le compte n'est pas suspendu. Ce n'est pas de l'historique — la
    /// valeur est effacée à la levée, une fois consommée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public SellerStatus? SuspendedFromStatus { get; private set; }

    public decimal Rating { get; private set; }
    public int SalesCount { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<KybDocument> KybDocuments => _kybDocuments.AsReadOnly();

    /// <summary>
    /// Onboarde un vendeur rattaché à un compte Identity. L'existence et la
    /// validité du compte sont vérifiées par l'Application (appel in-process à
    /// Identity) avant d'arriver ici.
    /// </summary>
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
    /// Passer <c>null</c> efface la metadata. N'affecte ni le statut ni le KYB.
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
        //
        // Sellers ne connaît pas le service média. C'est l'appelant — la couche qui
        // voit les deux — qui contrôle que le média est de nature `SellerDocument`
        // et qu'il appartient à CE vendeur. Sans ce contrôle en amont, la faille
        // décrite sur `KybDocument.MediaId` se rouvre à l'identique.
        var document = new KybDocument(Guid.NewGuid(), type, mediaId);
        _kybDocuments.Add(document);

        // Le motif du refus précédent ne survit pas au nouveau dépôt : affiché sur
        // un dossier que le vendeur est en train de corriger, il lui ferait croire
        // que sa correction a déjà été refusée.
        if (KybStatus is KybStatus.Rejected)
        {
            KybRejectionReason = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // BASCULE AUTOMATIQUE — DÉPRÉCIÉE. À RETIRER QUAND L'APP ENVERRA
        //    `SubmitKyb`.
        //
        // Le §10.3 fait de la soumission un GESTE : `POST /kyb/submit`, avec la
        // liste des pièces. Ici, le dossier partait en validation dès la PREMIÈRE
        // pièce déposée. Le vendeur qui téléverse sa carte d'identité un lundi et
        // son registre de commerce le jeudi occupait la file d'un administrateur
        // pendant trois jours avec un dossier incomplet — que celui-ci ne pouvait
        // que refuser.
        //
        // `SubmitKyb` existe désormais et fait ce geste. Cette bascule est
        // conservée parce que l'application vendeur DÉJÀ DÉPLOYÉE ne l'appelle
        // pas : la retirer aujourd'hui ferait que plus AUCUN dossier n'atteindrait
        // la file de validation — l'onboarding vendeur s'arrêterait net, sans
        // erreur, sans trace.
        //
        // Même raisonnement que la coquille de dépréciation des routes (D15) : on
        // ajoute le bon chemin, on garde l'ancien vivant, et on le retire quand
        // plus personne ne l'emprunte. La condition de retrait est écrite ici pour
        // qu'on n'ait pas à la redécouvrir.
        // ═════════════════════════════════════════════════════════════════════
        if (KybStatus is KybStatus.NotStarted or KybStatus.Rejected)
        {
            KybStatus = KybStatus.InReview;
            Raise(new SellerKybSubmittedDomainEvent(Id.Value, UserId, _kybDocuments.Count));
        }

        return document;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE VENDEUR DÉCLARE SON DOSSIER COMPLET (§10.3 : POST /kyc/submit).
    ///
    /// CE GESTE N'EXISTAIT PAS, ET SON ABSENCE COÛTAIT DES DEUX CÔTÉS.
    ///
    /// Côté administrateur, la file se remplissait de dossiers incomplets : le
    /// passage en revue était un effet de bord du dépôt de la première pièce.
    /// Côté vendeur, rien ne lui disait quand il avait fini — il déposait, et
    /// espérait.
    ///
    /// IDEMPOTENT SUR UN DOSSIER DÉJÀ EN REVUE.
    ///
    /// L'app pourra l'appeler après chaque dépôt sans conséquence, ce qui rend sa
    /// migration triviale : elle n'a pas à savoir si c'est la première pièce.
    /// Réémettre l'événement, en revanche, relancerait une notification à
    /// l'administrateur pour un dossier qu'il a déjà dans sa file.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
            //
            // Un vendeur qui renouvelle une pièce puis appelle « soumettre » ne doit
            // pas retomber en attente de validation : il vend, et l'interrompre pour
            // une mise à jour de routine coûterait plus que ça ne protège. Même
            // raisonnement que pour l'ajout d'une pièce à un dossier vérifié.
            return Result.Success();
        }

        KybStatus = KybStatus.InReview;
        KybRejectionReason = null;

        Raise(new SellerKybSubmittedDomainEvent(Id.Value, UserId, _kybDocuments.Count));
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PROPRIÉTÉ DU DOSSIER PASSE À UN AUTRE COMPTE.
    ///
    /// CE N'EST QUE LA MOITIÉ DU GESTE. L'autre moitié — déplacer le rôle
    /// système OWNER d'un membre à l'autre — vit dans
    /// <c>SellerMember.TransferOwnership</c>, et les deux DOIVENT être appelées
    /// dans la même transaction. Séparées, elles produiraient un dossier dont le
    /// `UserId` désigne quelqu'un qui n'a pas le rôle, ou l'inverse : deux sources
    /// de vérité qui se contredisent sur la seule question qui compte ici.
    ///
    /// C'est le handler qui les tient ensemble ; l'agrégat ne peut pas, puisqu'il
    /// s'agit de trois agrégats distincts.
    ///
    /// AUCUNE GARDE D'AUTORISATION ICI, ET C'EST DÉLIBÉRÉ.
    ///
    /// Le droit de transférer se lit sur le MEMBRE — `OWNERSHIP_TRANSFER`,
    /// critique et réservée au propriétaire — pas sur le dossier. Dupliquer le
    /// contrôle ici donnerait deux endroits à tenir d'accord, et le jour où ils
    /// divergeraient, c'est le plus permissif qui gagnerait.
    ///
    /// CE QUE CETTE MÉTHODE NE VÉRIFIE PAS : que le compte destinataire ne
    /// possède pas DÉJÀ un autre dossier. `IX_sellers_UserId` est unique — la
    /// violation sortirait en 409 opaque. Le handler pose la question à
    /// `ISellerRepository.ExistsForUserAsync` avant d'appeler, et rend un refus qui
    /// s'explique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// téléversé). La pièce est identifiée par son id ; l'appartenance à la boutique
    /// est garantie par la relation (la pièce est enfant de cet agrégat). Retourne
    /// l'entité retirée pour permettre à l'Application de nettoyer le stockage si
    /// besoin. Le retrait ne change pas le statut KYB de la boutique (une pièce
    /// vérifiée restée en base n'est pas re-demandée ; la re-validation admin reste
    /// possible via l'ajout d'une nouvelle pièce).
    /// </summary>
    public Result<KybDocument> RemoveKybDocument(Guid documentId)
    {
        var document = _kybDocuments.FirstOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return Error.NotFound("sellers.kyb.not_found", "Pièce KYB introuvable pour cette boutique.");
        }

        _kybDocuments.Remove(document);

        // ═════════════════════════════════════════════════════════════════════
        // RETIRER LA DERNIÈRE PIÈCE RAMÈNE LE DOSSIER À « NON COMMENCÉ ».
        //
        // Sans cela, un vendeur qui dépose une pièce puis la retire laissait un
        // dossier `InReview` SANS AUCUNE PIÈCE. Il occupait la file d'attente d'un
        // administrateur qui, en l'ouvrant, ne trouvait rien à regarder — et ne
        // pouvait même pas le rejeter utilement, `RejectKyb` supposant qu'il y a eu
        // quelque chose à examiner.
        //
        // On ne touche QU'À un dossier en revue : un dossier VÉRIFIÉ dont on retire
        // une pièce reste vérifié — la décision de l'administrateur a été prise, et
        // la défaire au retrait d'un document interromprait l'activité d'un vendeur
        // en règle. Un dossier REJETÉ reste rejeté, avec son motif.
        // ═════════════════════════════════════════════════════════════════════
        if (_kybDocuments.Count == 0 && KybStatus is KybStatus.InReview)
        {
            KybStatus = KybStatus.NotStarted;
        }

        // LE FICHIER N'EST PAS EFFACÉ ICI, ET IL NE PEUT PAS L'ÊTRE.
        //
        // Retirer la ligne sans prévenir personne laisserait la pièce d'identité
        // dans le bucket privé, pour toujours, sans que rien ne la désigne — donc
        // sans que le ménage de rétention puisse jamais la trouver.
        //
        // L'événement porte le `MediaId` jusqu'au composition root, qui appelle le
        // service média. Une pièce héritée (sans MediaId) n'en produit pas : son
        // URL est traitée par la reprise.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA MODÉRATION REFUSE LE DOSSIER.
    ///
    /// CE REFUS N'AVAIT AUCUNE CONSÉQUENCE SUR L'ACTIVITÉ.
    ///
    /// <c>Activate()</c> refuse tant que le KYB n'est pas validé : l'ENTRÉE était
    /// gardée. La sortie, non. Un vendeur déjà actif dont la modération rejetait
    /// le dossier — pièce expirée, document falsifié — restait <c>Active</c> et
    /// continuait de vendre. Le rejet ne changeait qu'une colonne que personne ne
    /// relisait.
    ///
    /// ET LE VENDEUR N'APPRENAIT RIEN.
    ///
    /// Aucun motif n'était conservé, aucun événement émis. Le vendeur constatait
    /// un statut « Rejeté » sans savoir quoi corriger — et redéposait la même
    /// pièce. Un refus sans motif n'est pas une décision de modération, c'est une
    /// impasse.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
            // envoyé. Cette garde évite un état que rien ne sait défaire.
            return Result.Failure(Error.Conflict(
                "sellers.kyb.nothing_to_reject",
                "Aucune pièce n'a été déposée : il n'y a rien à rejeter."));
        }

        KybStatus = KybStatus.Rejected;
        KybRejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new SellerKybRejectedDomainEvent(Id.Value, UserId, KybRejectionReason));

        // UN VENDEUR ACTIF EST SUSPENDU PAR LE REFUS.
        //
        // C'est le cœur du correctif. La suspension emprunte exactement le chemin
        // bâti pour l'exploitation : événement, retrait du catalogue, motif
        // lisible sur chaque fiche. Rien de spécifique n'est réinventé ici.
        //
        // On ne touche PAS aux comptes fermés ou déjà suspendus : ils ne vendent
        // plus, et écraser leur statut ferait perdre la raison de leur état.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SUSPEND LE VENDEUR — LA MESURE D'URGENCE DE L'EXPLOITATION.
    ///
    /// ELLE NE FAISAIT RIEN.
    ///
    /// Cette méthode posait le statut et n'émettait AUCUN événement, là où
    /// <see cref="RequestClosure"/> en émet un qui retire les produits de la
    /// vente. L'administrateur suspendait un vendeur frauduleux, voyait
    /// « Suspendu » s'afficher dans sa console — et les acheteurs continuaient
    /// de commander, de payer et d'attendre l'expédition de quelqu'un que la
    /// plateforme venait d'écarter.
    ///
    /// De toutes les décisions de ce module, c'était la plus urgente et la seule
    /// sans effet.
    ///
    /// LA GARDE D'ÉTAT COMPTE AUTANT QUE L'ÉVÉNEMENT.
    ///
    /// Sans elle, suspendre un compte déjà FERMÉ écrasait <c>Closed</c> par
    /// <c>Suspended</c> : la trace de la demande du vendeur disparaissait, et
    /// la réactivation qu'il pouvait demander n'était plus atteignable, puisque
    /// <see cref="RequestReactivation"/> exige un compte fermé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Suspend(string? reason = null)
    {
        if (Status == SellerStatus.Suspended)
        {
            // Idempotent : réémettre l'événement relancerait une suspension de
            // catalogue déjà faite. Ce n'est pas une erreur pour autant —
            // l'appelant a obtenu ce qu'il voulait.
            return Result.Success();
        }

        if (Status is SellerStatus.Closed or SellerStatus.PendingReactivation)
        {
            // La garde décrite dans l'encadré ci-dessus. Un compte fermé n'est
            // déjà plus en vente : le suspendre n'apporterait rien et coûterait
            // la trace de la décision du vendeur.
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

    /// <summary>
    /// Lève une suspension prononcée par l'exploitation.
    ///
    /// NE CONCERNE QUE LES COMPTES SUSPENDUS. Cette méthode n'avait aucune
    /// garde de statut ni aucun appelant : rebranchée telle quelle, elle aurait
    /// réactivé un compte FERMÉ sans passer par
    /// <see cref="ApproveReactivation"/> — c'est-à-dire sans la validation
    /// administrative que tout le parcours de fermeture existe pour imposer.
    ///
    /// Le nom prêtait à confusion : « réactiver » se disait de deux parcours
    /// distincts. Celui-ci lève une sanction, l'autre accueille un vendeur qui
    /// revient.
    /// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // ON REND LE COMPTE LÀ D'OÙ IL VIENT, PAS EN `Active` D'OFFICE.
        //
        // L'ancienne version posait `Active` sans regarder ce que le compte était
        // avant. Un vendeur encore `Pending` — jamais activé — suspendu puis
        // rétabli arrivait donc en activité SANS que `SellerActivatedDomainEvent`
        // ne soit jamais émis : le seul événement produit était « suspension
        // levée ». Un consommateur qui attend l'activation pour ouvrir un
        // portefeuille ou autoriser la mise en vente ne voyait jamais ce vendeur.
        //
        // Il repart donc en `Pending` et devra passer par `Activate()`, qui
        // annoncera son entrée en activité comme pour tout le monde.
        //
        // Le repli sur `Active` couvre les comptes suspendus AVANT l'introduction
        // de `SuspendedFromStatus` : leur colonne est nulle, et c'était le
        // comportement d'alors. Sans ce repli, ils deviendraient impossibles à
        // rétablir — une migration de données ne peut pas deviner un statut qui
        // n'a jamais été écrit.
        // ═════════════════════════════════════════════════════════════════════
        var retour = SuspendedFromStatus ?? SellerStatus.Active;
        SuspendedFromStatus = null;

        Status = retour is SellerStatus.Active ? SellerStatus.Active : SellerStatus.Pending;

        Raise(new SellerSuspensionLiftedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// Fermeture demandée par le vendeur lui-même (suppression partielle). Le compte
    /// bascule en <see cref="SellerStatus.Closed"/> : l'événement déclenche le retrait
    /// des produits de la vente (module Catalog). Le compte n'est PAS effacé — seul
    /// l'admin peut le supprimer définitivement.
    /// </summary>
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

    /// <summary>Le vendeur (compte fermé) demande la réactivation : passe en attente de validation admin.</summary>
    public Result RequestReactivation()
    {
        if (Status != SellerStatus.Closed)
        {
            return Result.Failure(Error.Conflict("sellers.seller.not_closed", "Seul un compte fermé peut demander une réactivation."));
        }

        Status = SellerStatus.PendingReactivation;
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ADMIN APPROUVE LA DEMANDE DE RÉACTIVATION.
    ///
    /// Le compte redevient actif et l'événement permet au Catalog de reprendre le
    /// vendeur en compte. Les produits restés en brouillon depuis la fermeture sont
    /// republiés par le vendeur.
    ///
    /// UNE DEMANDE PRÉALABLE EST DÉSORMAIS EXIGÉE, ET C'EST UN CHANGEMENT.
    ///
    /// La garde acceptait aussi un compte simplement `Closed` — donc sans qu'aucune
    /// demande n'ait jamais été formulée. Le nom de la méthode et son code d'erreur
    /// (`no_reactivation_request`) affirmaient pourtant le contraire, et un test du
    /// lot 1 figeait l'écart sous le préfixe `Ecart_` en attendant l'arbitrage.
    ///
    /// L'arbitrage est rendu : c'est le NOM qui avait raison. Le parcours complet
    /// est `Closed → RequestReactivation → PendingReactivation → ApproveReactivation`,
    /// et `RequestReactivation` existe précisément pour porter la volonté du
    /// vendeur de revenir. Sans cette exigence, un administrateur remettait en
    /// vente un commerçant qui avait fermé boutique, sans que rien dans le système
    /// ne trace qu'il l'avait demandé — le geste ressemblait à une réouverture
    /// consentie, il n'en était pas une.
    ///
    /// CE QU'ON PERD, ET QUI EST ASSUMÉ : rouvrir un compte fermé PAR ERREUR
    /// n'a plus de chemin direct ; le vendeur doit passer par sa propre demande. Si
    /// l'exploitation en a réellement besoin, cela mérite un geste DISTINCT, nommé
    /// pour ce qu'il fait — pas une garde élargie en silence sous un nom qui dit
    /// autre chose.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // MÊME EXIGENCE QUE `Activate` ET `LiftSuspension` — ELLE MANQUAIT ICI.
        //
        // Des trois chemins qui mènent à `Active`, celui-ci était le seul à ne pas
        // vérifier les coordonnées de reversement. La raison vaut pourtant à
        // l'identique, et `LiftSuspension` la porte écrite : un vendeur qui vend
        // sans compte de reversement accumule des gains que rien ne peut lui
        // verser.
        //
        // Un compte fermé puis rétabli revendait donc sans que personne ne puisse
        // le payer — et le problème ne se manifestait qu'au premier versement, des
        // semaines plus tard, du côté de Wallet.
        //
        // Défaut trouvé en écrivant le premier test de ce cycle : le code était
        // cohérent avec lui-même partout sauf ici, et rien ne le signalait.
        // ═════════════════════════════════════════════════════════════════════
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
    /// produits du vendeur avant que l'agrégat ne soit retiré du dépôt. À appeler
    /// juste avant <c>Remove</c> côté Application.
    /// </summary>
    public Result MarkForDeletion()
    {
        // ═════════════════════════════════════════════════════════════════════
        // CHAQUE PIÈCE D'IDENTITÉ EST NOMMÉE, UNE PAR UNE.
        //
        // Le retrait de l'agrégat emporte les lignes `kyb_documents` en cascade et
        // LAISSE LES FICHIERS. Cartes d'identité, registres de commerce, documents
        // fiscaux : sans ces événements, ils resteraient dans le bucket privé sans
        // qu'aucune ligne ne pointe plus vers eux — donc sans aucun moyen de les
        // retrouver un jour pour les effacer.
        //
        // Un seul événement portant la liste aurait suffi techniquement. Un par
        // pièce est préférable : si l'effacement de l'une échoue durablement, les
        // autres partent quand même, et le message resté en souffrance nomme
        // exactement le fichier qui résiste.
        // ═════════════════════════════════════════════════════════════════════
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
    /// Fixe le compteur de ventes à une valeur recalculée depuis la source (Ordering).
    /// Préféré à <see cref="RecordSale"/> pour l'alimentation événementielle : poser le
    /// total exact est idempotent, alors qu'incrémenter double-compterait si l'événement
    /// « commande confirmée » est rejoué.
    /// </summary>
    public void SetSalesCount(int count) => SalesCount = count < 0 ? 0 : count;
}
