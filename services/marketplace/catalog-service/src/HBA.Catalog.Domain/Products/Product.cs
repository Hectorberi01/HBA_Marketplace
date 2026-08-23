using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products.Events;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// AGRÉGAT PRODUIT (§7).
///
/// Il possède ses RÉVISIONS, ses variantes et ses médias : rien de tout cela ne
/// se modifie autrement qu'à travers lui.
///
/// IL NE PORTE PLUS AUCUN CHAMP DESCRIPTIF, ET C'EST LE CŒUR DU CHANGEMENT.
///
/// Nom, slug, description, catégorie, marque, prix, condition, attributs et
/// mots-clés vivent dans <see cref="ProductRevision"/>. Ce qui reste ici est ce
/// qui ne dépend d'aucune version : à qui appartient la fiche, où elle en est de
/// son cycle de vie, et quelle révision l'acheteur voit.
///
/// La conséquence est brutale et voulue : <c>product.Name</c> n'existe plus. Tout
/// appelant doit choisir entre <see cref="CurrentRevision"/> — ce que le vendeur
/// édite — et <see cref="PublishedRevision"/> — ce que l'acheteur voit. Ce choix
/// se faisait jusqu'ici par accident, et il se trompait dans le sens le plus
/// coûteux : montrer au public un texte que personne n'avait validé.
///
/// IL NE PORTE PAS NON PLUS LE PRIX TRANSACTIONNEL.
///
/// Décision D12 : c'est <c>ProductOffer</c> qui dit ce que l'acheteur paie, avec
/// la commission et les frais fournisseur. La tarification de la révision est un
/// prix de RÉFÉRENCE vendeur. Voir l'encadré de <see cref="ProductPricing"/>.
///
/// ET IL N'EST PAS SOURCE DE VÉRITÉ DU STOCK (consigne 15).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Boutique porteuse (§7 : <c>store_id uuid NOT NULL</c>).
    ///
    /// NULLABLE ICI, ET CE N'EST PAS UN OUBLI.
    ///
    /// Les fiches créées avant le multi-boutique n'ont pas de boutique, et il
    /// n'existe aucune valeur juste à leur donner : la déduire du vendeur
    /// rattacherait au hasard un produit à l'une de ses boutiques. Un
    /// <c>Guid.Empty</c> aurait été pire — une valeur qui a l'air d'en être une.
    ///
    /// La garde est donc posée où elle se voit : <see cref="SubmitForReview"/>
    /// refuse une fiche sans boutique. Les anciennes lignes restent lisibles et
    /// modifiables, et ne peuvent plus avancer tant que personne ne les rattache.
    /// </summary>
    public Guid? StoreId { get; private set; }

    /// <summary>Code-barres international. Identifie l'ARTICLE, pas sa description.</summary>
    public string? Gtin { get; private set; }
    public string? Ean { get; private set; }

    /// <summary>Clé de regroupement souple des fiches identiques entre vendeurs.</summary>
    public Guid? ProductGroupId { get; private set; }

    public ProductStatus Status { get; private set; }

    /// <summary>Raison de la dernière suspension, destinée au vendeur.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>La révision que le vendeur édite. Jamais nulle.</summary>
    public Guid CurrentRevisionId { get; private set; }

    /// <summary>La révision servie au public. Nulle tant que rien n'a été publié.</summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // CRÉATION ET CONTENU
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crée un produit en brouillon avec sa première révision (§14 : POST /products).
    ///
    /// La description n'est PAS obligatoire ici : le §23 ne l'exige qu'« avant
    /// soumission ». Un vendeur enregistre une ébauche à l'étape 1 du formulaire
    /// et revient la compléter — l'exiger dès la création lui ferait perdre sa
    /// saisie.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MODIFIE LE CONTENU — ET DÉCIDE SEUL S'IL FAUT UNE NOUVELLE RÉVISION (§6).
    ///
    /// Trois chemins, et c'est l'état courant qui choisit :
    ///
    ///   • révision modifiable en place (Draft, Rejected) → on réécrit. Aucune
    ///     nouvelle version : un vendeur qui corrige sa saisie six fois avant de
    ///     soumettre ne doit pas créer six révisions.
    ///
    ///   • fiche déjà validée ou publiée, modification NON critique (mots-clés,
    ///     description courte, coût d'achat) → on réécrit aussi. Repasser en
    ///     validation pour un mot-clé engorgerait la file jusqu'à la rendre
    ///     inutile.
    ///
    ///   • fiche déjà validée ou publiée, modification CRITIQUE → nouvelle
    ///     révision en brouillon. Et <see cref="PublishedRevisionId"/> NE BOUGE
    ///     PAS : c'est toute la promesse du §6 — l'acheteur continue de voir la
    ///     version validée pendant que la suivante attend son tour. Écraser ici
    ///     mettrait en ligne un texte non relu, sans que rien ne le signale.
    ///
    /// Une fiche EN COURS DE VALIDATION ne se modifie pas du tout : voir l'encadré
    /// de PendingReview dans <see cref="ProductStatusTransitions"/>.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // LA GARDE PORTE SUR LA RÉVISION, PAS SUR LE STATUT DU PRODUIT.
        //
        // Elle testait `Status is ProductStatus.PendingReview`, ce qui ne couvrait
        // que les fiches jamais publiées. Sur un produit DÉJÀ EN VENTE dont la
        // nouvelle version attendait validation, le produit reste PUBLISHED (§6) :
        // la garde ne se déclenchait pas, et il restait deux chemins, tous deux
        // mauvais.
        //
        //   • modification non critique → réécriture EN PLACE de la révision que
        //     l'administrateur est en train de lire. Il approuve alors un contenu
        //     qu'il n'a pas vu — la validation devient une signature en blanc,
        //     exactement ce que l'encadré de PendingReview interdit ;
        //   • modification critique → une révision v3 s'ouvre et devient courante,
        //     laissant la v2 en PENDING_REVIEW pour toujours. Elle reste dans la
        //     file de validation, un administrateur l'approuve un jour, et cette
        //     approbation ne porte sur rien.
        //
        // Le verrou appartient donc à la révision, seule à savoir qu'elle est lue.
        // ═════════════════════════════════════════════════════════════════════
        if (courante.Status is RevisionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.not_editable",
                "Cette version est en cours de validation et ne peut pas être modifiée. Attendez la décision de l'administrateur."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE SLUG NE SUIT PAS LE NOM, ET LA RÈGLE VIT ICI, PAS DANS LE HANDLER.
        //
        // Sans cette ligne, `ProductRevision.Create` le redérive du nom : renommer
        // une fiche publiée changerait son URL publique (§17,
        // `GET /products/{slug}`) au moment de la publication de la nouvelle
        // version. Ce que cela casse ne se voit nulle part dans le service — les
        // liens déjà partagés rendent 404, et personne ne fait le rapprochement
        // avec la faute d'orthographe corrigée la veille.
        //
        // La poser dans le handler l'aurait laissée contournable par tout autre
        // appelant : reprise de données, import en masse, commande d'administration.
        // Un appelant qui veut VRAIMENT changer le slug le fournit explicitement.
        // ═════════════════════════════════════════════════════════════════════
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

            // ═════════════════════════════════════════════════════════════════
            // CORRIGER UN REJET RAMÈNE À DRAFT. C'EST UNE TRANSITION, PAS UN
            //    EFFET DE BORD.
            //
            // Le §4 porte l'étiquette « correction » sur la flèche
            // REJECTED → DRAFT, et le §5 n'autorise PENDING_REVIEW que depuis
            // DRAFT. Sans ce passage, une fiche rejetée puis corrigée ne pouvait
            // plus être resoumise du tout : `SubmitForReview` demandait
            // Rejected → PendingReview, que la liste blanche refuse — à juste
            // titre, puisque le cahier fait passer par le brouillon.
            //
            // Défaut trouvé par le test « Rejeter_puis_corriger_puis_resoumettre
            // _mene_a_la_publication », c'est-à-dire par le parcours E2E de rejet
            // du §28. Le vendeur se serait retrouvé avec une fiche corrigée qu'il
            // ne pouvait plus soumettre, et rien dans le message d'erreur —
            // « un produit Rejected ne peut pas passer à PendingReview » —
            // n'aurait dit ce qu'il fallait faire.
            //
            // Le retour de statut ne concerne QUE les fiches jamais publiées :
            // si une révision rejetée coexiste avec une révision en ligne, le
            // produit reste PUBLISHED — c'est la révision seule qui redevient
            // brouillon.
            // ═════════════════════════════════════════════════════════════════
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

    /// <summary>Rattache la fiche à une boutique. Voir l'encadré de <see cref="StoreId"/>.</summary>
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

    /// <summary>
    /// Remplace les mots-clés de la révision courante. Sert la curation éditoriale :
    /// marquer un produit « featured » ne demande qu'un tag, sans colonne dédiée.
    ///
    /// Non critique au sens du §6 : un mot-clé ne change pas ce que l'acheteur lit.
    /// </summary>
    public void SetTags(IReadOnlyList<string>? tags)
    {
        CurrentRevision.RemplacerTags(tags);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CYCLE DE VIE (§4, §5, §15)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Soumet la révision courante à validation (§15).
    ///
    /// CE QUI EST VÉRIFIÉ ICI, ET CE QUI NE PEUT PAS L'ÊTRE.
    ///
    /// Le §15 exige aussi que le vendeur et la boutique soient ACTIFS, que la
    /// catégorie et la marque le soient, et que les attributs requis par la
    /// catégorie soient présents. Rien de tout cela n'est connu du domaine —
    /// vendeur et boutique vivent dans un autre service, les attributs requis
    /// dans une autre table. Ces contrôles-là appartiennent au handler, qui voit
    /// les deux côtés.
    ///
    /// Ce qui est vérifié ici est ce que l'agrégat POSSÈDE. Le mettre ailleurs
    /// laisserait un autre chemin d'appel le contourner.
    /// </summary>
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
        //
        // Zéro laisse la vitrine choisir au hasard — donc l'ordre d'affichage
        // change d'un rendu à l'autre. Deux est pire : chaque consommateur en
        // prend une différente, et la vignette du panier cesse de correspondre à
        // celle de la fiche. Aucun des deux cas ne lève d'erreur nulle part.
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
        // RÉVISION qui avance. Voir l'encadré de RevisionStatus.
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

    /// <summary>Approbation administrateur (§16). Ne publie PAS : c'est le vendeur qui publie.</summary>
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

    /// <summary>
    /// Rejet administrateur avec motifs (§16). Les motifs sont portés par
    /// ProductReview ; l'agrégat n'enregistre que la décision.
    /// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// PUBLICATION — LA RÈGLE ABSOLUE DU §4 VIT ICI.
    ///
    /// « Un vendeur ne peut jamais publier un produit qui n'a pas été approuvé
    ///    par un administrateur. »
    ///
    /// Deux gardes, et il en faut deux :
    ///
    ///   • la RÉVISION doit être approuvée. C'est la garde de fond : ce qui va
    ///     devenir visible est exactement ce qu'un administrateur a lu.
    ///   • le PRODUIT doit venir d'Approved ou d'Unpublished, par la liste blanche
    ///     de <see cref="ProductStatusTransitions"/>.
    ///
    /// La première seule laisserait publier un produit suspendu dont la révision
    /// avait été approuvée avant la sanction. La seconde seule laisserait publier
    /// une révision non relue sur un produit déjà passé par Approved autrefois.
    ///
    /// Le cahier ajoute que le frontend ne constitue jamais la barrière de
    /// sécurité — c'est pourquoi cette méthode ne fait confiance à personne, pas
    /// même au handler qui l'appelle.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Publish(DateTimeOffset nowUtc)
    {
        if (Status is ProductStatus.Published && CurrentRevisionId == PublishedRevisionId)
        {
            return Result.Failure(Error.Conflict(
                "catalog.product.already_published",
                "Ce produit est déjà publié."));
        }

        var courante = CurrentRevision;

        // ═════════════════════════════════════════════════════════════════════
        // « Published » EST ACCEPTÉ ICI, ET CE N'EST PAS UN TROU DANS LA RÈGLE.
        //
        // Défaut trouvé par le test « Republier_apres_depublication_ne_demande_pas
        // _une_nouvelle_validation ». En n'acceptant qu'`Approved`, une fiche
        // dépubliée puis republiée était refusée : la dépublication laisse la
        // révision en `Published` — elle cesse d'être SERVIE, elle ne redevient pas
        // « approuvée ». Le vendeur qui retirait sa fiche une heure ne pouvait plus
        // jamais la remettre en vente sans repasser en validation.
        //
        // Accepter `Published` n'ouvre rien : cet état n'est posé que par CETTE
        // méthode, qui vient elle-même d'exiger `Approved`. On accepte donc une
        // révision qu'un administrateur a bel et bien lue.
        //
        // Ce qui reste refusé, et doit le rester : `Draft`, `PendingReview`,
        // `Rejected` — jamais relues ou refusées — et `Superseded`, remplacée par
        // une version plus récente.
        // ═════════════════════════════════════════════════════════════════════
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
        //
        // Republier après une dépublication remet en ligne LA MÊME révision :
        // `precedente` et `courante` sont alors le même objet. L'appel aveugle le
        // faisait passer par Superseded avant Published — sans effet visible
        // aujourd'hui, parce que l'ordre des deux lignes le rattrapait, et faux le
        // jour où quelqu'un les inverse ou lit l'état entre les deux.
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

    /// <summary>
    /// Retrait volontaire par le vendeur (§5). Réversible sans nouvelle validation :
    /// la révision publiée reste approuvée, elle cesse simplement d'être servie.
    /// </summary>
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

    /// <summary>
    /// Blocage par la plateforme (§16).
    ///
    /// LA RÉVISION PUBLIÉE N'EST PAS EFFACÉE.
    ///
    /// Une suspension est souvent temporaire — le temps d'une vérification. Perdre
    /// le lien vers la révision servie obligerait le vendeur à tout resoumettre
    /// après une levée, ce qui transformerait une mesure conservatoire en sanction
    /// définitive.
    /// </summary>
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

    /// <summary>
    /// Levée de suspension (§16). Rend la fiche à APPROVED, pas à PUBLISHED :
    /// c'est le vendeur qui décide de la remettre en vente, pas la plateforme.
    /// </summary>
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

    /// <summary>
    /// Retrait définitif du cycle courant (§5). La ligne survit — commandes et
    /// historique la référencent encore.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // VARIANTES
    // ═════════════════════════════════════════════════════════════════════════

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

    // ═════════════════════════════════════════════════════════════════════════
    // MÉDIAS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ajoute un média (la première image devient principale).
    ///
    /// LE MÉDIA DOIT EXISTER AVANT D'ÊTRE RATTACHÉ, ET SON EXISTENCE NE SE
    /// VÉRIFIE PAS ICI.
    ///
    /// Catalog ne connaît pas le service média. C'est l'appelant — la couche qui
    /// voit les deux — qui contrôle que le média est de nature `ProductImage` et
    /// qu'il appartient bien à ce produit. Sans ce contrôle en amont, un vendeur
    /// rattacherait à sa fiche l'image d'un autre.
    ///
    /// L'URL est fournie par le même appelant, au même moment : c'est la copie de
    /// lecture décrite sur <see cref="ProductMedia"/>, pas une adresse choisie.
    /// </summary>
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
    /// média. Renvoie faux si rien n'a changé — l'appelant évite alors une écriture.
    /// </summary>
    public bool RefreshMediaUrl(Guid mediaId, string url)
    {
        var touched = false;

        // Une boucle plutôt qu'un `FirstOrDefault` : rien n'interdit qu'un même
        // média soit rattaché deux fois au même produit (deux positions, deux
        // textes alternatifs). N'en rafraîchir qu'un laisserait l'autre périmé.
        foreach (var media in _media.Where(m => m.MediaId == mediaId))
        {
            touched |= media.RefreshUrl(url);
        }

        return touched;
    }

    /// <summary>
    /// Retire un média du produit et renvoie l'entité retirée. Recompacte les
    /// positions et promeut un nouveau média principal si nécessaire.
    ///
    /// LE FICHIER N'EST PAS EFFACÉ ICI, ET IL NE PEUT PAS L'ÊTRE : le domaine
    /// ne connaît pas le service média. Il le NOMME, et l'effacement suit par
    /// l'outbox. Voir <see cref="ProductMediaRemovedDomainEvent"/>.
    /// </summary>
    public Result<ProductMedia> RemoveMedia(Guid mediaId)
    {
        var media = _media.FirstOrDefault(m => m.Id == mediaId);
        if (media is null)
        {
            return Error.NotFound("catalog.media.not_found", $"Média {mediaId} introuvable sur ce produit.");
        }

        _media.Remove(media);

        // Une image d'avant la bascule n'a pas de média : demander au service
        // média d'effacer un identifiant nul ferait remonter une alerte à chaque
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

    /// <summary>
    /// Nomme les fichiers du produit avant que celui-ci ne disparaisse.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// À APPELER AVANT `IProductRepository.Remove`, SANS EXCEPTION.
    ///
    /// Le retrait de l'agrégat emporte les lignes `product_media` en cascade et
    /// LAISSE LES FICHIERS. Sans ce geste, chaque produit supprimé abandonne ses
    /// images dans le stockage sans qu'aucune ligne ne pointe plus vers elles.
    ///
    /// UN ÉVÉNEMENT PAR IMAGE, ET NON UN SEUL PORTANT LA LISTE.
    ///
    /// Si l'effacement de l'une échoue durablement, les autres partent quand
    /// même, et le message resté en souffrance nomme exactement le fichier qui
    /// résiste. Un événement unique les ferait toutes rejouer ensemble,
    /// indéfiniment, à cause d'une seule.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// Réordonne les médias selon la liste d'identifiants fournie (positions 0..n-1).
    /// Les ids inconnus sont ignorés ; les médias non listés sont placés à la fin
    /// dans leur ordre courant. Le premier média devient l'image principale.
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
