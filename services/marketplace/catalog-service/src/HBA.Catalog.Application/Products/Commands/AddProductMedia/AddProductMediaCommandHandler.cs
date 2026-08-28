using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;
using HBA.Media.Contracts;
using HBA.Merchants.Contracts;

namespace HBA.Catalog.Application.Products.Commands.AddProductMedia;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// RATTACHE UN MÉDIA DÉJÀ DÉPOSÉ À UNE FICHE (§12, §14).
///
/// LE CONTRÔLE D'APPARTENANCE ÉTAIT DEMANDÉ PAR LE DOMAINE ET FAIT PAR PERSONNE.
///
/// `Product.AddMedia` porte cet encadré depuis toujours :
///
///     « Catalog ne connaît pas le service média. C'est l'appelant — la couche qui
///       voit les deux — qui contrôle que le média est de nature `ProductImage` et
///       qu'il appartient bien à ce produit. Sans ce contrôle en amont, un vendeur
///       rattacherait à sa fiche l'image d'un autre. »
///
/// L'appelant, c'était ce handler, et il ne contrôlait rien : il acceptait un
/// identifiant ET une URL fournis par le client. Un vendeur pouvait donc afficher
/// sur sa fiche la photo d'un concurrent — ou n'importe quelle URL, y compris hors
/// de la plateforme.
///
/// L'URL VIENT DÉSORMAIS DU SERVICE MÉDIA, PAS DU CLIENT.
///
/// C'est la moitié la plus importante de la correction. Vérifier l'identifiant tout
/// en recopiant l'URL du client laisserait afficher n'importe quelle image sous
/// couvert d'un média valide.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class AddProductMediaCommandHandler : ICommandHandler<AddProductMediaCommand, Guid>
{
    /// <summary>Les formats du §12. Le service média applique les mêmes ; on ne le croit pas sur parole.</summary>
    private static readonly string[] TypesAcceptes = { "image/jpeg", "image/png", "image/webp" };

    private readonly IProductRepository _productRepository;
    private readonly IMediaModuleApi _media;
    private readonly IMerchantAccessApi _merchants;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AddProductMediaCommandHandler(
        IProductRepository productRepository,
        IMediaModuleApi media,
        IMerchantAccessApi merchants,
        ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _media = media;
        _merchants = merchants;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        if (!Enum.TryParse<ProductMediaType>(command.Type, ignoreCase: true, out var mediaType))
        {
            return Error.Validation("catalog.media.type_invalid", "Type de média invalide (Image ou Video).");
        }

        var media = await _media.GetAsync(command.MediaId, cancellationToken);

        if (media is null)
        {
            return Error.NotFound(
                "catalog.media.not_found",
                "Ce média n'existe pas. Déposez le fichier avant de le rattacher à une fiche.");
        }

        // LE MÉDIA DOIT APPARTENIR À CE PRODUIT, PAS SEULEMENT EXISTER.
        //
        // Sans cette comparaison, il suffirait de connaître l'identifiant d'un média
        // — que la vitrine rend dans chaque fiche — pour l'afficher sur la sienne.
        if (!string.Equals(media.OwnerType, "Product", StringComparison.OrdinalIgnoreCase)
            || media.OwnerId != command.ProductId)
        {
            return Error.Forbidden(
                "catalog.media.not_owned",
                "Ce média n'appartient pas à ce produit.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ET IL DOIT AVOIR ÉTÉ DÉPOSÉ PAR CELUI QUI LE RATTACHE (audit 2.2).
        //
        // POURQUOI LE CONTRÔLE PRÉCÉDENT NE SUFFIT PAS — ET C'EST LE POINT.
        //
        // `OwnerType` et `OwnerId` sont DÉCLARÉS PAR L'APPELANT au téléversement,
        // et media-service ne les vérifie pas : il ignore ce qu'est un produit
        // (§20), et son propre commentaire le dit. N'importe quel compte connecté
        // peut donc déposer un fichier en annonçant « OwnerType=Product,
        // OwnerId=<le produit d'un concurrent> », et le contrôle ci-dessus
        // PASSERA — il compare une valeur fournie par celui qu'il contrôle.
        //
        // `CreatedByUserId`, lui, vient du JETON présenté au téléversement. C'est
        // le seul champ de `MediaView` que l'appelant ne choisit pas.
        //
        // CE QUE ÇA FERME EXACTEMENT, ET C'EST PLUS ÉTROIT QU'IL N'Y PARAÎT.
        // Un tiers qui aurait forgé un média au nom d'un produit ne peut pas le
        // rattacher — mais il ne le pouvait déjà pas, `DenyUnlessProductOwnerAsync`
        // gardant la route. Ce qui est fermé ICI est le cas inverse et plus
        // retors : le VENDEUR LÉGITIME qui rattacherait, de bonne foi, un média
        // forgé par un tiers au nom de son produit — parce qu'un écran le lui
        // aurait présenté comme faisant partie de sa fiche.
        //
        // CE CHEMIN EST LATENT, PAS ACTIF. Aucun écran ne peut aujourd'hui
        // présenter à un vendeur un média qu'il n'a pas déposé : il n'existe
        // aucune route de listage par propriétaire. Ce contrôle est donc posé
        // AVANT le besoin, pour que le jour où cette route existera, elle n'ouvre
        // pas la brèche en même temps qu'elle rend service.
        //
        // CE QUE ÇA NE FERME PAS. N'importe quel compte peut toujours CRÉER de
        // telles lignes chez media-service : elles occupent du stockage et portent
        // une appartenance mensongère. Aucun chemin de lecture ne les expose
        // aujourd'hui — `ListByOwnerAsync` n'a ni route ni appelant — mais la
        // méthode existe, et le jour où quelqu'un l'emploie, ces lignes
        // apparaîtront dans le dossier de leur victime. Le remède est au dépôt,
        // pas ici.
        // ═════════════════════════════════════════════════════════════════════
        if (command.RequestedByUserId == Guid.Empty)
        {
            return Error.Forbidden(
                "catalog.media.not_uploader",
                "Appelant inconnu : impossible de vérifier qui a déposé ce fichier.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // « LE MÊME COMPTE » AURAIT ÉTÉ TROP STRICT, ET AURAIT CASSÉ UNE ÉQUIPE.
        //
        // Une première version exigeait `CreatedByUserId == RequestedByUserId`.
        // C'était faux pour la moitié des boutiques : un `SellerMember` téléverse
        // les photos, un autre monte la fiche. La route accepte les deux —
        // `DenyUnlessProductOwnerAsync` raisonne sur la CAPACITÉ, pas sur la
        // personne — et le handler les aurait départagés en refusant le second.
        //
        // La règle est donc : le déposant doit avoir, LUI AUSSI, le droit de
        // modifier les fiches de CE vendeur. Cela couvre l'équipe entière et
        // exclut tout compte extérieur, ce qui est exactement la propriété
        // recherchée.
        //
        // LE CHEMIN RAPIDE ÉVITE UN APPEL. Dans le cas courant — la même personne
        // dépose et rattache — la question est déjà tranchée, et on n'ouvre pas
        // un aller-retour vers seller-service pour se le confirmer.
        // ─────────────────────────────────────────────────────────────────────
        if (media.CreatedByUserId != command.RequestedByUserId)
        {
            var deposantAutorise = await _merchants.HasCapabilityAsync(
                media.CreatedByUserId,
                product.SellerId,
                storeId: null,
                MerchantCapabilities.ProductUpdate,
                cancellationToken);

            if (!deposantAutorise)
            {
                return Error.Forbidden(
                    "catalog.media.not_uploader",
                    "Ce fichier a été déposé par un compte étranger à cette boutique.");
            }
        }

        // Le §12 réserve les images produit à cette nature. Un justificatif de
        // livraison ou une pièce d'identité déposés ailleurs ne doivent pas pouvoir
        // se retrouver en vitrine.
        if (!string.Equals(media.MediaType, "ProductImage", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(
                "catalog.media.wrong_kind",
                $"Ce média est de nature « {media.MediaType} » ; une fiche produit attend une image produit.");
        }

        if (mediaType is ProductMediaType.Image
            && !TypesAcceptes.Contains(media.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation(
                "catalog.media.format_unsupported",
                $"Format « {media.ContentType} » non pris en charge. Attendu : JPEG, PNG ou WebP.");
        }

        // UN MÉDIA PAS ENCORE PRÊT N'EST PAS UN MÉDIA ABSENT.
        //
        // Le traitement — vignettes, retrait EXIF, scan de sécurité (§12) — est
        // asynchrone. Rattacher avant la fin afficherait une image que le CDN ne sert
        // pas encore, et le vendeur croirait à une erreur de sa part. Le message dit
        // d'attendre, pas de recommencer.
        if (!string.Equals(media.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return Error.BusinessRule(
                "catalog.media.not_ready",
                "Ce média est encore en cours de traitement. Réessayez dans quelques instants.");
        }

        if (string.IsNullOrWhiteSpace(media.Url))
        {
            return Error.BusinessRule(
                "catalog.media.not_public",
                "Ce média n'est pas public et ne peut pas être affiché sur une fiche produit.");
        }

        var result = product.AddMedia(command.MediaId, media.Url, mediaType, command.AltText, command.IsPrimary);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id;
    }
}
