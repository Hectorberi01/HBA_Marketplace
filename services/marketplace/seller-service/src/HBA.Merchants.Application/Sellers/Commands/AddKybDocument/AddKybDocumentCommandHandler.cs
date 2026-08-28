using HBA.Shared.Application.Abstractions;
using HBA.Media.Contracts;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Members;

namespace HBA.Merchants.Application.Sellers.Commands.AddKybDocument;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// RATTACHE UNE PIÈCE KYB DÉJÀ TÉLÉVERSÉE — APRÈS AVOIR VÉRIFIÉ À QUI ELLE EST.
///
/// CE HANDLER N'A LONGTEMPS VÉRIFIÉ QUE LE TYPE DE LA PIÈCE.
///
/// `Seller.AddKybDocument` refuse un `mediaId` vide et rien d'autre. Son encadré
/// délègue le reste « à l'appelant — la couche qui voit les deux — qui contrôle
/// que le média est de nature `SellerDocument` et qu'il appartient à CE vendeur ».
/// Cette couche, c'était ici. Elle ne le faisait pas, et le BFF Vendeur auquel la
/// documentation renvoyait ensuite est un squelette qui n'expose aucun cas
/// d'usage. Le contrôle était délégué à personne.
///
/// DEUX EXPLOITATIONS, L'UNE ET L'AUTRE À LA PORTÉE D'UN VENDEUR INSCRIT :
///
///   1. LIRE LES PAPIERS D'IDENTITÉ D'UN CONCURRENT. Rattacher le `mediaId`
///      d'autrui à son propre dossier suffisait : la pièce ressortait dans SA
///      fiche, et media-service signe l'URL de lecture sans vérifier le droit
///      métier — sa route le dit elle-même : « le troisième [contrôle] appartient
///      au service propriétaire ». C'était nous. C'est mot pour mot la faille que
///      le passage de `FileUrl` à `MediaId` devait fermer.
///
///   2. EFFACER N'IMPORTE QUEL MÉDIA DE LA PLATEFORME. Rattacher puis retirer :
///      le retrait lève `KybDocumentRemovedDomainEvent`, que media-service
///      transforme en suppression. Photos produit d'un concurrent, visuels de
///      restaurant, dossier KYB d'autrui.
///
/// CE FICHIER FIXE AUSSI UNE CONVENTION, PARCE QU'IL EST LE PREMIER À EN AVOIR
///    BESOIN : une pièce KYB est un média `(OwnerType=Seller, OwnerId=sellerId,
///    MediaType=SellerDocument)`. `OwnerId` est l'identifiant du VENDEUR, pas
///    celui du compte utilisateur — un compte peut cesser d'être rattaché au
///    dossier, le dossier reste.
///
/// Le gabarit vient de `AddProductMediaCommandHandler`, qui applique la même
/// chaîne depuis le lot média du catalogue.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN TROISIÈME CONTRÔLE A ÉTÉ AJOUTÉ LE 28 AOÛT, ET LES DEUX PREMIERS NE LE
/// COUVRAIENT PAS.
///
/// Les contrôles d'appartenance et de nature comparent `OwnerType`, `OwnerId` et
/// `MediaType` — TROIS VALEURS DÉCLARÉES PAR L'APPELANT au téléversement, que
/// media-service ne vérifie pas et ne peut pas vérifier (§20). Ils ferment bien
/// les deux exploitations décrites plus haut, parce qu'aucune des deux ne permet
/// de RÉÉCRIRE l'appartenance d'un média existant. Mais ils ne ferment pas la
/// création d'un média NEUF portant une appartenance mensongère.
///
/// `CreatedByUserId` vient du jeton : c'est le seul champ que l'appelant ne
/// choisit pas. Le déposant doit désormais être membre de CE dossier vendeur.
///
/// C'EST UN CHEMIN LATENT, ET IL FAUT LE DIRE : aucune route ne liste
/// aujourd'hui les médias d'un propriétaire, donc rien ne peut présenter à un
/// gérant une pièce forgée comme faisant partie de son dossier. Le contrôle est
/// posé avant le besoin.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class AddKybDocumentCommandHandler : ICommandHandler<AddKybDocumentCommand, Guid>
{
    /// <summary>La nature imposée par le §12 pour une pièce légale de vendeur.</summary>
    private const string NatureAttendue = "SellerDocument";

    /// <summary>Le propriétaire attendu, côté media-service (<c>MediaOwnerType.Seller</c>).</summary>
    private const string ProprietaireAttendu = "Seller";

    private readonly ISellerRepository _sellerRepository;
    private readonly IMediaModuleApi _media;
    private readonly MemberAccessResolver _acces;
    private readonly ISellerUnitOfWork _unitOfWork;

    public AddKybDocumentCommandHandler(
        ISellerRepository sellerRepository,
        IMediaModuleApi media,
        MemberAccessResolver acces,
        ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _media = media;
        _acces = acces;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddKybDocumentCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable.");
        }

        if (!Enum.TryParse<KybDocumentType>(command.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("sellers.kyb.type_invalid", "Type de pièce KYB invalide.");
        }

        var media = await _media.GetAsync(command.MediaId, cancellationToken);

        if (media is null)
        {
            return Error.NotFound(
                "sellers.kyb.media_not_found",
                "Ce fichier n'existe pas. Téléversez la pièce avant de la rattacher au dossier.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE CONTRÔLE QUI FERME LES DEUX EXPLOITATIONS.
        //
        // Le média doit appartenir à CE vendeur, pas seulement exister. Sans cette
        // comparaison, connaître un identifiant suffisait — et les identifiants de
        // média circulent : la vitrine en rend un par image de fiche produit.
        //
        // 403 et non 404 : dire « introuvable » pour un média qui existe rendrait
        // ce message indiscernable du précédent, et un vendeur qui s'est trompé de
        // fichier ne saurait pas lequel des deux problèmes il a.
        // ═════════════════════════════════════════════════════════════════════
        if (!string.Equals(media.OwnerType, ProprietaireAttendu, StringComparison.OrdinalIgnoreCase)
            || media.OwnerId != command.SellerId)
        {
            return Error.Forbidden(
                "sellers.kyb.media_not_owned",
                "Ce fichier n'appartient pas à ce dossier vendeur.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ET LE DÉPOSANT DOIT ÊTRE DE CETTE ÉQUIPE — CE QUE LE CONTRÔLE
        // CI-DESSUS NE DIT PAS.
        //
        // POURQUOI LE PRÉCÉDENT NE SUFFIT PAS. `OwnerType` et `OwnerId` sont
        // DÉCLARÉS PAR L'APPELANT au téléversement, et media-service ne les
        // vérifie pas : il ignore ce qu'est un vendeur (§20), et le dit lui-même.
        // N'importe quel compte connecté peut donc déposer un fichier en
        // annonçant « OwnerType=Seller, OwnerId=<un concurrent>,
        // MediaType=SellerDocument », et le contrôle ci-dessus le laisserait
        // passer — il compare une valeur fournie par celui qu'il contrôle.
        //
        // `CreatedByUserId` vient du JETON présenté au téléversement. C'est le
        // seul champ de `MediaView` que l'appelant ne choisit pas.
        //
        // « LE MÊME COMPTE » SERAIT TROP STRICT : dans une équipe, un membre
        // téléverse les pièces et le gérant les rattache. On exige donc que le
        // déposant soit MEMBRE DE CE DOSSIER, ce qui couvre l'équipe et exclut
        // tout compte extérieur. La question se résout ici, en local — ce service
        // EST le service vendeur, aucun appel distant.
        //
        // CE CHEMIN EST LATENT, PAS ACTIF. Aucune route ne liste aujourd'hui les
        // médias d'un propriétaire, donc rien ne peut présenter à un gérant une
        // pièce forgée comme faisant partie de son dossier. Le contrôle est posé
        // AVANT le besoin : le jour où cette route existera, elle ne doit pas
        // ouvrir la brèche en même temps qu'elle rend service. Et c'est le
        // dossier KYB — la pièce d'identité d'un commerçant — qui est en jeu.
        // ═════════════════════════════════════════════════════════════════════
        if (command.RequestedByUserId == Guid.Empty)
        {
            return Error.Forbidden(
                "sellers.kyb.media_uploader_unknown",
                "Appelant inconnu : impossible de vérifier qui a déposé ce fichier.");
        }

        if (media.CreatedByUserId != command.RequestedByUserId)
        {
            var deposant = await _acces.ResolveAsync(
                command.SellerId, media.CreatedByUserId, cancellationToken);

            if (deposant.IsFailure)
            {
                return Error.Forbidden(
                    "sellers.kyb.media_not_uploader",
                    "Ce fichier a été déposé par un compte étranger à ce dossier vendeur.");
            }
        }

        // LA NATURE, EN PLUS DE LA PROPRIÉTÉ — ET LES DEUX SERVENT.
        //
        // Un vendeur possède aussi ses propres images de boutique, publiques et
        // servies par le CDN. Sans ce contrôle, il pouvait présenter une photo de
        // devanture comme sa carte d'identité : le dossier partait en validation,
        // et l'administrateur découvrait la pièce manquante en l'ouvrant.
        //
        // C'est aussi ce qui garantit que le fichier est PRIVÉ : la politique du
        // §12 impose `Private` à cette nature, sans variantes ni URL permanente.
        if (!string.Equals(media.MediaType, NatureAttendue, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(
                "sellers.kyb.media_wrong_kind",
                $"Ce fichier est de nature « {media.MediaType} » ; un dossier KYB attend une pièce légale.");
        }

        // UN MÉDIA PAS ENCORE PRÊT N'EST PAS UN MÉDIA ABSENT.
        //
        // Le traitement est asynchrone. Rattacher avant sa fin mettrait dans la
        // file de validation un dossier dont l'administrateur ne pourrait pas
        // ouvrir la pièce. Le message dit d'attendre, pas de recommencer.
        if (!string.Equals(media.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return Error.BusinessRule(
                "sellers.kyb.media_not_ready",
                "Ce fichier est encore en cours de traitement. Réessayez dans quelques instants.");
        }

        var result = seller.AddKybDocument(type, command.MediaId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id;
    }
}
