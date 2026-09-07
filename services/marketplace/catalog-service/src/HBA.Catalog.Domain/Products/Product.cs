using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products.Events;

namespace HBA.Catalog.Domain.Products;

/// <summary>AGRÉGAT PRODUIT (§7).</summary>
public sealed class Product : AggregateRoot<ProductId>
{
    private readonly List<ProductVariant> _variants = new();
    private readonly List<ProductMedia> _media = new();
    private readonly List<ProductRevision> _revisions = new();

    // ctor EF.
    private Product()
    {
    }

    private Product(
        ProductId id,
        Guid sellerId,
        Guid? storeId,
        string? gtin,
        string? ean,
        Guid? productGroupId)
        : base(id)
    {
        SellerId = sellerId;
        StoreId = storeId;
        Gtin = gtin;
        Ean = ean;
        ProductGroupId = productGroupId;
        Status = ProductStatus.Draft;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid SellerId { get; private set; }

    /// <summary>Boutique porteuse (§7 : <c>store_id uuid NOT NULL</c>).</summary>
    public Guid? StoreId { get; private set; }

    /// <summary>Code-barres international.</summary>
    public string? Gtin { get; private set; }
    public string? Ean { get; private set; }

    /// <summary>Clé de regroupement souple des fiches identiques entre vendeurs.</summary>
    public Guid? ProductGroupId { get; private set; }

    public ProductStatus Status { get; private set; }

    /// <summary>Raison de la dernière suspension, destinée au vendeur.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>La révision que le vendeur édite.</summary>
    public Guid CurrentRevisionId { get; private set; }

    /// <summary>La révision servie au public.</summary>
    public Guid? PublishedRevisionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    public IReadOnlyCollection<ProductMedia> Media => _media.AsReadOnly();
    public IReadOnlyCollection<ProductRevision> Revisions => _revisions.AsReadOnly();

    /// <summary>
    /// Ce que le VENDEUR édite. Lève si l'agrégat a été chargé sans ses révisions —
    /// et c'est le bon comportement : un produit sans révision courante est une
    /// donnée corrompue, pas un cas à traiter poliment.
    /// </summary>
    public ProductRevision CurrentRevision
        => _revisions.FirstOrDefault(r => r.Id == CurrentRevisionId)
           ?? throw new InvalidOperationException(
               $"Produit {Id.Value} chargé sans sa révision courante {CurrentRevisionId} — "
               + "le dépôt doit inclure Revisions.");

    /// <summary>
    /// Ce que l'ACHETEUR voit. Nulle tant que rien n'a été publié — et l'API
    /// publique ne doit alors rien rendre du tout (§17).
    /// </summary>
    public ProductRevision? PublishedRevision
        => PublishedRevisionId is null
            ? null
            : _revisions.FirstOrDefault(r => r.Id == PublishedRevisionId);

    // CRÉATION ET CONTENU

    /// <summary>
    /// Crée un produit en brouillon avec sa première révision (§14 : POST
    /// /products).
    /// </summary>
    public static Result<Product> Create(
        Guid sellerId,
        Guid? storeId,
        ContenuProduit contenu,
        string? gtin = null,
        string? ean = null,
        Guid? productGroupId = null)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("catalog.product.seller_required", "Un produit doit appartenir à un vendeur.");
        }

        var product = new Product(
            ProductId.New(),
            sellerId,
            storeId == Guid.Empty ? null : storeId,
            Clean(gtin),
            Clean(ean),
            productGroupId);

        var revision = ProductRevision.Create(product.Id, version: 1, contenu);
        if (revision.IsFailure)
        {
            return Result.Failure<Product>(revision.Error);
        }

        product._revisions.Add(revision.Value);
        product.CurrentRevisionId = revision.Value.Id;

        product.Raise(new ProductCreatedDomainEvent(
            product.Id.Value,
            sellerId,
            revision.Value.CategoryId,
            revision.Value.Name,
            revision.Value.Slug.Value));

        return product;
    }

    /// <summary>MODIFIE LE CONTENU — ET DÉCIDE SEUL S'IL FAUT UNE NOUVELLE RÉVISION (§6).</summary>
    public Result UpdateContenu(ContenuProduit contenu)
    {
        if (contenu is null)
        {
            return Result.Failure(Error.Validation("catalog.product.content_required", "Le contenu du produit est obligatoire."));
        }

        if (Status is ProductStatus.Archived)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.not_editable",
                "Un produit archivé ne se modifie plus."));
        }

        var courante = CurrentRevision;

        // LA GARDE PORTE SUR LA RÉVISION, PAS SUR LE STATUT DU PRODUIT.
        if (courante.Status is RevisionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.not_editable",
                "Cette version est en cours de validation et ne peut pas être modifiée. Attendez la décision de l'administrateur."));
        }

        // LE SLUG NE SUIT PAS LE NOM, ET LA RÈGLE VIT ICI, PAS DANS LE HANDLER.
        if (contenu.Slug is null)
        {
            contenu = contenu with { Slug = courante.Slug };
        }

        if (courante.EstModifiableEnPlace || !courante.EstModificationCritique(contenu))
        {
            var remplacement = courante.Remplacer(contenu);
            if (remplacement.IsFailure)
            {
                return remplacement;
            }

            // CORRIGER UN REJET RAMÈNE À DRAFT. C'EST UNE TRANSITION, PAS UN EFFET
            // DE BORD.
            if (courante.Status is RevisionStatus.Rejected)
            {
                courante.MarquerCorrigee();

                if (PublishedRevisionId is null && Status is ProductStatus.Rejected)
                {
                    var retour = ChangerStatut(ProductStatus.Draft);
                    if (retour.IsFailure)
                    {
                        return retour;
                    }
                }
            }

            UpdatedAtUtc = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        var nouvelle = ProductRevision.Create(Id, courante.Version + 1, contenu);
        if (nouvelle.IsFailure)
        {
            return Result.Failure(nouvelle.Error);
        }

        _revisions.Add(nouvelle.Value);
        CurrentRevisionId = nouvelle.Value.Id;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        Raise(new ProductRevisionOpenedDomainEvent(
            Id.Value,
            nouvelle.Value.Id,
            nouvelle.Value.Version,
            PublishedRevisionId));

        return Result.Success();
    }

    /// <summary>Rattache la fiche à une boutique.</summary>
    public Result AssignStore(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("catalog.product.store_required", "La boutique est obligatoire."));
        }

        StoreId = storeId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Remplace les mots-clés de la révision courante.</summary>
    public void SetTags(IReadOnlyList<string>? tags)
    {
        CurrentRevision.RemplacerTags(tags);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    // CYCLE DE VIE (§4, §5, §15)

    /// <summary>Soumet la révision courante à validation (§15).</summary>
    public Result SubmitForReview(DateTimeOffset nowUtc)
    {
        if (Status is ProductStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.already_submitted",
                "Ce produit est déjà en attente de validation."));
        }

        if (StoreId is null)
        {
            return Result.Failure(Error.BusinessRule(
                "catalog.product.store_required",
                "Ce produit n'est rattaché à aucune boutique. Rattachez-le avant de le soumettre."));
        }

        var courante = CurrentRevision;

        if (string.IsNullOrWhiteSpace(courante.Description))
        {
            return Result.Failure(Error.BusinessRule(
                "catalog.product.description_required",
                "La description est obligatoire avant soumission."));
        }

        if (_media.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "catalog.product.image_required",
                "Ajoutez au moins une image avant de soumettre ce produit."));
        }

        // « EXACTEMENT UNE » IMAGE PRINCIPALE, PAS « AU MOINS UNE » (§12, §23).
        var principales = _media.Count(m => m.IsPrimary);
        if (principales != 1)
        {
            return Result.Failure(Error.BusinessRule(
                "catalog.product.main_image_required",
                principales == 0
                    ? "Désignez une image principale avant de soumettre ce produit."
                    : "Ce produit a plusieurs images principales : il n'en faut qu'une."));
        }

        // Le produit publié qui repasse en validation garde son statut : c'est la
        // RÉVISION qui avance.
        if (PublishedRevisionId is null)
        {
            var transition = ChangerStatut(ProductStatus.PendingReview);
            if (transition.IsFailure)
            {
                return transition;
            }
        }

        courante.MarquerSoumise(nowUtc);
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        Raise(new ProductSubmittedForReviewDomainEvent(Id.Value, SellerId, courante.Id, courante.Version));
        return Result.Success();
    }

    /// <summary>Approbation administrateur (§16).</summary>
    public Result Approve(Guid reviewedBy, DateTimeOffset nowUtc)
    {
        var courante = CurrentRevision;

        if (courante.Status is not RevisionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.review_not_pending",
                "Seule une révision soumise peut être approuvée."));
        }

        if (PublishedRevisionId is null)
        {
            var transition = ChangerStatut(ProductStatus.Approved);
            if (transition.IsFailure)
            {
                return transition;
            }
        }

        courante.MarquerApprouvee(nowUtc);
        ApprovedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        Raise(new ProductApprovedDomainEvent(Id.Value, SellerId, courante.Id, reviewedBy));
        return Result.Success();
    }

    /// <summary>Rejet administrateur avec motifs (§16).</summary>
    public Result Reject(Guid reviewedBy, DateTimeOffset nowUtc)
    {
        var courante = CurrentRevision;

        if (courante.Status is not RevisionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.review_not_pending",
                "Seule une révision soumise peut être rejetée."));
        }

        if (PublishedRevisionId is null)
        {
            var transition = ChangerStatut(ProductStatus.Rejected);
            if (transition.IsFailure)
            {
                return transition;
            }
        }

        courante.MarquerRejetee(nowUtc);
        UpdatedAtUtc = nowUtc;

        Raise(new ProductRejectedDomainEvent(Id.Value, SellerId, courante.Id, reviewedBy));
        return Result.Success();
    }

    /// <summary>PUBLICATION — LA RÈGLE ABSOLUE DU §4 VIT ICI.</summary>
    public Result Publish(DateTimeOffset nowUtc)
    {
        if (Status is ProductStatus.Published && CurrentRevisionId == PublishedRevisionId)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.already_published",
                "Ce produit est déjà publié."));
        }

        var courante = CurrentRevision;

        // « Published » EST ACCEPTÉ ICI, ET CE N'EST PAS UN TROU DANS LA RÈGLE.
        if (courante.Status is not (RevisionStatus.Approved or RevisionStatus.Published))
        {
            return Result.Failure(Error.BusinessRule(
                "catalog.product.not_approved",
                "Le produit doit être validé par un administrateur avant publication."));
        }

        if (Status is not (ProductStatus.Approved or ProductStatus.Unpublished or ProductStatus.Published))
        {
            return Result.Failure(ProductStatusTransitions.CannotTransition(Status, ProductStatus.Published));
        }

        // NE PAS MARQUER « REMPLACÉE » LA RÉVISION QU'ON REPUBLIE.
        var precedente = PublishedRevision;
        var remplacee = precedente is not null && precedente.Id != courante.Id ? precedente : null;
        remplacee?.MarquerRemplacee();

        courante.MarquerPubliee(nowUtc);
        PublishedRevisionId = courante.Id;

        if (Status is not ProductStatus.Published)
        {
            var transition = ChangerStatut(ProductStatus.Published);
            if (transition.IsFailure)
            {
                return transition;
            }
        }

        PublishedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        // `PreviousRevisionId` reste nul quand on republie la même révision : un
        // consommateur qui réindexe doit pouvoir distinguer « nouveau contenu » de
        // « remise en vente à l'identique ».
        Raise(new ProductPublishedDomainEvent(Id.Value, SellerId, courante.Id, remplacee?.Id));
        return Result.Success();
    }

    /// <summary>Retrait volontaire par le vendeur (§5).</summary>
    public Result Unpublish()
    {
        var transition = ChangerStatut(ProductStatus.Unpublished);
        if (transition.IsFailure)
        {
            return transition;
        }

        Raise(new ProductUnpublishedDomainEvent(Id.Value, SellerId));
        return Result.Success();
    }

    /// <summary>Blocage par la plateforme (§16).</summary>
    public Result Suspend(string? reason)
    {
        var transition = ChangerStatut(ProductStatus.Suspended);
        if (transition.IsFailure)
        {
            return transition;
        }

        SuspensionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Raise(new ProductSuspendedDomainEvent(Id.Value, SellerId, SuspensionReason));
        return Result.Success();
    }

    /// <summary>Levée de suspension (§16).</summary>
    public Result Restore()
    {
        var transition = ChangerStatut(ProductStatus.Approved);
        if (transition.IsFailure)
        {
            return transition;
        }

        SuspensionReason = null;
        Raise(new ProductRestoredDomainEvent(Id.Value, SellerId));
        return Result.Success();
    }

    /// <summary>Retrait définitif du cycle courant (§5).</summary>
    public Result Archive()
    {
        var transition = ChangerStatut(ProductStatus.Archived);
        if (transition.IsFailure)
        {
            return transition;
        }

        ArchivedAtUtc = UpdatedAtUtc;
        Raise(new ProductArchivedDomainEvent(Id.Value, SellerId));
        return Result.Success();
    }

    private Result ChangerStatut(ProductStatus vers)
    {
        if (!ProductStatusTransitions.IsAllowed(Status, vers))
        {
            return Result.Failure(ProductStatusTransitions.CannotTransition(Status, vers));
        }

        Status = vers;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    // VARIANTES

    /// <summary>Ajoute une variante (SKU unique au sein du produit).</summary>
    public Result<ProductVariant> AddVariant(
        string sku,
        IReadOnlyDictionary<string, string>? variantAttributes,
        string? barcode,
        int weightGrams,
        int? lengthMm = null,
        int? widthMm = null,
        int? heightMm = null)
    {
        if (weightGrams < 0)
        {
            return Error.Validation("catalog.variant.weight_negative", "Le poids ne peut pas être négatif.");
        }

        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<ProductVariant>(skuResult.Error);
        }

        if (_variants.Any(v => v.Sku == skuResult.Value))
        {
            return Error.Conflict("catalog.variant.sku_duplicate", $"Le SKU « {skuResult.Value.Value} » existe déjà sur ce produit.");
        }

        Dimensions? dimensions = null;
        if (lengthMm.HasValue || widthMm.HasValue || heightMm.HasValue)
        {
            var dimensionsResult = Dimensions.Create(lengthMm ?? 0, widthMm ?? 0, heightMm ?? 0);
            if (dimensionsResult.IsFailure)
            {
                return Result.Failure<ProductVariant>(dimensionsResult.Error);
            }

            dimensions = dimensionsResult.Value;
        }

        var attributes = variantAttributes is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(variantAttributes);

        var variant = new ProductVariant(Guid.NewGuid(), skuResult.Value, attributes, Clean(barcode), weightGrams, dimensions);
        _variants.Add(variant);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return variant;
    }

    /// <summary>Met à jour une variante existante (SKU, attributs, code-barres, poids).</summary>
    public Result UpdateVariant(
        Guid variantId,
        string sku,
        IReadOnlyDictionary<string, string>? variantAttributes,
        string? barcode,
        int weightGrams)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(Error.NotFound("catalog.variant.not_found", $"Variante {variantId} introuvable sur ce produit."));
        }

        if (weightGrams < 0)
        {
            return Result.Failure(Error.Validation("catalog.variant.weight_negative", "Le poids ne peut pas être négatif."));
        }

        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure(skuResult.Error);
        }

        if (_variants.Any(v => v.Id != variantId && v.Sku == skuResult.Value))
        {
            return Result.Failure(Error.Conflict("catalog.variant.sku_duplicate", $"Le SKU « {skuResult.Value.Value} » existe déjà sur ce produit."));
        }

        var attributes = variantAttributes is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(variantAttributes);

        variant.Update(skuResult.Value, attributes, Clean(barcode), weightGrams);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Retire une variante du produit.</summary>
    public Result RemoveVariant(Guid variantId)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(Error.NotFound("catalog.variant.not_found", $"Variante {variantId} introuvable sur ce produit."));
        }

        _variants.Remove(variant);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    // MÉDIAS

    /// <summary>Ajoute un média (la première image devient principale).</summary>
    public Result<ProductMedia> AddMedia(
        Guid mediaId,
        string url,
        ProductMediaType type,
        string? altText,
        bool isPrimary)
    {
        if (mediaId == Guid.Empty)
        {
            return Error.Validation("catalog.media.media_required", "Le média est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return Error.Validation("catalog.media.url_required", "L'URL du média est obligatoire.");
        }

        var makePrimary = isPrimary || _media.Count == 0;
        if (makePrimary)
        {
            foreach (var existing in _media)
            {
                existing.UnsetPrimary();
            }
        }

        var media = new ProductMedia(
            Guid.NewGuid(),
            mediaId,
            url.Trim(),
            type,
            altText?.Trim() ?? string.Empty,
            _media.Count,
            makePrimary,
            // Une image déposée aujourd'hui n'a pas d'ancienne référence : ce champ
            // n'est renseigné que par les lignes que EF matérialise depuis la base.
            legacyExternalId: null);

        _media.Add(media);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return media;
    }

    /// <summary>
    /// Rafraîchit la copie de lecture d'un média après retraitement côté service
    /// média.
    /// </summary>
    public bool RefreshMediaUrl(Guid mediaId, string url)
    {
        var touched = false;

        // Une boucle plutôt qu'un `FirstOrDefault` : rien n'interdit qu'un même
        // média soit rattaché deux fois au même produit (deux positions, deux
        // textes alternatifs).
        foreach (var media in _media.Where(m => m.MediaId == mediaId))
        {
            touched |= media.RefreshUrl(url);
        }

        return touched;
    }

    /// <summary>Retire un média du produit et renvoie l'entité retirée.</summary>
    public Result<ProductMedia> RemoveMedia(Guid mediaId)
    {
        var media = _media.FirstOrDefault(m => m.Id == mediaId);
        if (media is null)
        {
            return Error.NotFound("catalog.media.not_found", $"Média {mediaId} introuvable sur ce produit.");
        }

        _media.Remove(media);

        // Une image d'avant la bascule n'a pas de média : demander au service média
        // d'effacer un identifiant nul ferait remonter une alerte à chaque
        // nettoyage de vieille fiche, jusqu'à masquer les vraies.
        if (!media.IsLegacy)
        {
            Raise(new ProductMediaRemovedDomainEvent(Id.Value, media.MediaId));
        }

        // Recompacte les positions (0..n-1).
        var ordered = _media.OrderBy(m => m.Position).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i);
        }

        // Si on a retiré l'image principale, le premier média restant la devient.
        if (media.IsPrimary && ordered.Count > 0)
        {
            ordered[0].MakePrimary();
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return media;
    }

    /// <summary>Nomme les fichiers du produit avant que celui-ci ne disparaisse.</summary>
    public void PrepareForDeletion()
    {
        foreach (var media in _media.Where(m => !m.IsLegacy))
        {
            Raise(new ProductMediaRemovedDomainEvent(Id.Value, media.MediaId));
        }
    }

    /// <summary>Définit le média principal (les autres perdent le statut principal).</summary>
    public Result SetPrimaryMedia(Guid mediaId)
    {
        var target = _media.FirstOrDefault(m => m.Id == mediaId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("catalog.media.not_found", $"Média {mediaId} introuvable sur ce produit."));
        }

        foreach (var m in _media)
        {
            m.UnsetPrimary();
        }
        target.MakePrimary();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Réordonne les médias selon la liste d'identifiants fournie (positions
    /// 0..n-1).
    /// </summary>
    public Result ReorderMedia(IReadOnlyList<Guid> orderedMediaIds)
    {
        if (_media.Count == 0)
        {
            return Result.Success();
        }

        var ordered = orderedMediaIds
            .Select(id => _media.FirstOrDefault(m => m.Id == id))
            .Where(m => m is not null)
            .Cast<ProductMedia>()
            .ToList();

        // Ajoute les médias non mentionnés à la fin (ordre courant).
        foreach (var m in _media.OrderBy(m => m.Position))
        {
            if (!ordered.Contains(m))
            {
                ordered.Add(m);
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i);
            ordered[i].UnsetPrimary();
        }
        ordered[0].MakePrimary();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
