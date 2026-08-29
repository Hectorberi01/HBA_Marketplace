using Grpc.Core;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Media.Contracts;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.MediaClient;

/// <summary>
/// Vérifie qu'une preuve photo existe réellement et appartient bien au dossier
/// qui la produit.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI ÉTAIT CASSÉ : CE CLIENT NE CONTACTAIT PERSONNE.
///
/// La version précédente vérifiait UNIQUEMENT que l'identifiant n'était pas une
/// chaîne vide, puis rendait `Success`. Conséquence, écrite noir sur blanc dans
/// le garde-fou de `ReturnRefundModuleInstaller` : « aucune preuve photo n'est
/// vérifiée — ni son existence, ni son propriétaire ».
///
/// N'importe quel identifiant inventé passait. Un dossier se fermait avec une
/// preuve qui n'existe pas, et le jour d'un litige il n'y a rien à produire —
/// sans que rien, dans les journaux, ne dise que la preuve n'a jamais existé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// IL S'APPUIE SUR `IMediaModuleApi`, ET NON SUR LE CLIENT gRPC ENGENDRÉ.
///
/// La première version parlait directement à `MediaApi.MediaApiClient`. Elle
/// n'aurait pas compilé : elle construisait `GetMediaRequest { Id = ... }` et
/// lisait `HasMedia`, alors que le contrat dit `MediaId` et `Found`.
///
/// Ce n'est pas qu'une faute de frappe. `HBA.Media.Contracts.Grpc` porte DÉJÀ un
/// client complet — celui que tous les autres services emploient, avec ses
/// intercepteurs d'identité interne et sa configuration de canal. En refaire un
/// second aurait dupliqué cette plomberie, et les deux auraient divergé au
/// premier changement du contrat.
///
/// Effet de bord évité : les deux classes se seraient appelées `MediaGrpcClient`.
/// Un `using HBA.Media.Contracts.Grpc;` de plus dans l'installeur, et le
/// compilateur aurait rendu CS0104 sur un nom ambigu — une erreur qui désigne la
/// ligne d'enregistrement, pas la collision qui la cause.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE CLASSE VÉRIFIE, ET DANS CET ORDRE :
///
///   1. l'identifiant est un GUID — media-service n'en connaît pas d'autres ;
///   2. le média EXISTE ;
///   3. il APPARTIENT à `ownerId`.
///
/// Le troisième point est le seul qui compte vraiment. Sans lui, il suffirait de
/// citer l'identifiant de la preuve d'un AUTRE dossier — un identifiant qui
/// existe, donc qui franchit les deux premiers contrôles.
///
/// CE QU'ELLE NE COUVRE PAS.
///
/// Elle ne regarde pas le CONTENU : que la photo montre le colis, personne ici
/// ne peut le dire. Elle ne contrôle pas `Status` non plus — un fichier encore
/// en traitement est accepté, parce que le refuser obligerait le client à
/// revenir plus tard sans savoir quand.
///
/// Et elle distingue une PANNE de media-service d'un refus de preuve. Rendre la
/// même erreur dans les deux cas ferait refuser des dossiers légitimes pendant
/// un redéploiement, et le client n'aurait aucun moyen de comprendre pourquoi sa
/// photo, bien réelle, est rejetée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class MediaValidationClient : IMediaGrpcClient
{
    private readonly IMediaModuleApi _media;
    private readonly ILogger<MediaValidationClient> _logger;

    public MediaValidationClient(IMediaModuleApi media, ILogger<MediaValidationClient> logger)
    {
        _media = media;
        _logger = logger;
    }

    public async Task<Result> ValidateMediaAsync(
        string mediaId, Guid ownerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
        {
            return Result.Failure(Error.Validation(
                "return_refund.media_required", "La preuve media est obligatoire."));
        }

        // UN IDENTIFIANT MAL FORMÉ SE REFUSE ICI, PAS SUR LE RÉSEAU.
        //
        // `IMediaModuleApi.GetAsync` prend un `Guid` : un identifiant invalide
        // ferait lever `Guid.Parse` au fond de cette méthode, avec une trace qui
        // désigne le transport plutôt que la saisie.
        if (!Guid.TryParse(mediaId, out var identifiant))
        {
            return Result.Failure(Error.Validation(
                "return_refund.media_identifiant_invalide",
                "L'identifiant de la preuve n'est pas un identifiant de média valide."));
        }

        MediaView? media;
        try
        {
            media = await _media.GetAsync(identifiant, cancellationToken);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                ex,
                "media-service injoignable pour vérifier la preuve {MediaId} ({Statut}).",
                mediaId, ex.StatusCode);

            return Result.Failure(Error.DependencyUnavailable(
                "return_refund.media_indisponible",
                "La preuve n'a pas pu être vérifiée : le service de médias est indisponible."));
        }

        if (media is null)
        {
            _logger.LogWarning(
                "Preuve de retour introuvable côté media-service : {MediaId}.", mediaId);

            return Result.Failure(Error.Validation(
                "return_refund.media_introuvable", "La preuve désignée n'existe pas."));
        }

        if (media.OwnerId != ownerId)
        {
            // ON NE DIT PAS À QUI ELLE APPARTIENT.
            //
            // Répondre « ce média appartient à quelqu'un d'autre » ferait de ce
            // point un oracle : on pourrait énumérer les preuves d'autrui en
            // distinguant « n'existe pas » de « existe, mais pas à vous ». Le
            // message est donc le même dans les deux cas pour l'appelant, et
            // seul le journal fait la différence.
            _logger.LogWarning(
                "Preuve de retour rejetée : le média {MediaId} n'appartient pas à {OwnerId}.",
                mediaId, ownerId);

            return Result.Failure(Error.Validation(
                "return_refund.media_non_autorise",
                "La preuve désignée n'est pas exploitable pour ce dossier."));
        }

        return Result.Success();
    }
}
