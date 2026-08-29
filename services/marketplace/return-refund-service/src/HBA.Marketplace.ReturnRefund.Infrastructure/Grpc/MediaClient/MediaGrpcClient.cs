using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Media.Grpc.V1;
using HBA.Shared.Domain.Results;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.MediaClient;

/// <summary>
/// Vérifie qu'une preuve photo existe réellement et appartient bien à celui qui
/// la produit.
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
/// Concrètement, n'importe quel identifiant inventé passait. Un client contestant
/// un retour pouvait joindre `"peu-importe"` comme preuve, et le dossier se
/// fermait avec une preuve qui n'existe pas. Le jour d'un litige, il n'y a rien à
/// produire — et rien, dans les journaux, ne dit que la preuve n'a jamais existé.
///
/// CE QUE CETTE IMPLÉMENTATION VÉRIFIE, ET DANS CET ORDRE :
///
///   1. l'identifiant est un GUID — media-service n'en connaît pas d'autres ;
///   2. le média EXISTE (`NotFound` de media-service) ;
///   3. il APPARTIENT à `ownerId`.
///
/// Le troisième point est le seul qui compte vraiment. Sans lui, il suffirait de
/// citer l'identifiant de la preuve d'un AUTRE dossier — un identifiant qui
/// existe, donc qui passe les deux premiers contrôles.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE IMPLÉMENTATION NE COUVRE PAS.
///
/// ELLE NE VÉRIFIE PAS LE CONTENU. Que la photo montre le colis, personne ici ne
/// peut le dire. `Status` n'est pas contrôlé non plus : un fichier encore en
/// traitement est accepté, parce que le refuser obligerait le client à revenir
/// plus tard sans savoir quand.
///
/// ELLE NE DISTINGUE PAS « media-service est en panne » DE « le média n'existe
/// pas » AUTREMENT QUE PAR LE CODE D'ERREUR. Une indisponibilité rend
/// `return_refund.media_indisponible` et NON un refus : refuser un retour parce
/// qu'un service voisin redémarre ferait porter au client une panne qui n'est pas
/// la sienne. C'est l'appelant qui décide de réessayer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class MediaGrpcClient : IMediaGrpcClient
{
    private readonly MediaApi.MediaApiClient _client;
    private readonly ILogger<MediaGrpcClient> _logger;

    public MediaGrpcClient(MediaApi.MediaApiClient client, ILogger<MediaGrpcClient> logger)
    {
        _client = client;
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
        // media-service rendrait `InvalidArgument`, qu'on traduirait en la même
        // erreur — au prix d'un aller-retour. Et surtout, un `Guid.Parse` qui lève
        // au fond d'un client gRPC produit une trace qui désigne le transport
        // plutôt que la saisie.
        if (!Guid.TryParse(mediaId, out _))
        {
            return Result.Failure(Error.Validation(
                "return_refund.media_identifiant_invalide",
                "L'identifiant de la preuve n'est pas un identifiant de média valide."));
        }

        try
        {
            var reponse = await _client.GetAsync(
                new GetMediaRequest { Id = mediaId },
                cancellationToken: cancellationToken);

            // `HasMedia` distingue « absent » de « présent et vide ». Sans ce test,
            // un média inexistant donnerait un `MediaView` par défaut, dont
            // l'`OwnerId` est une chaîne vide — qui ne correspondrait à aucun
            // propriétaire, donc un refus au bon endroit mais pour la mauvaise
            // raison, et un message qui parlerait de propriété au lieu d'absence.
            if (!reponse.HasMedia)
            {
                _logger.LogWarning(
                    "Preuve de retour introuvable côté media-service : {MediaId}.", mediaId);

                return Result.Failure(Error.Validation(
                    "return_refund.media_introuvable",
                    "La preuve désignée n'existe pas."));
            }

            if (!Guid.TryParse(reponse.Media.OwnerId, out var proprietaire)
                || proprietaire != ownerId)
            {
                // ON NE DIT PAS À QUI ELLE APPARTIENT.
                //
                // Le message ne nomme ni le propriétaire réel ni le fait que le
                // média existe : répondre « ce média appartient à quelqu'un
                // d'autre » transformerait ce point en oracle permettant
                // d'énumérer les preuves d'autrui.
                _logger.LogWarning(
                    "Preuve de retour rejetée : le média {MediaId} n'appartient pas à {OwnerId}.",
                    mediaId, ownerId);

                return Result.Failure(Error.Validation(
                    "return_refund.media_non_autorise",
                    "La preuve désignée n'est pas exploitable pour ce dossier."));
            }

            return Result.Success();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // media-service peut répondre `NotFound` plutôt qu'un `HasMedia` faux
            // selon la version. Les deux chemins mènent au même refus.
            _logger.LogWarning(
                "Preuve de retour introuvable (NotFound) : {MediaId}.", mediaId);

            return Result.Failure(Error.Validation(
                "return_refund.media_introuvable",
                "La preuve désignée n'existe pas."));
        }
        catch (RpcException ex)
        {
            // UNE PANNE DE media-service N'EST PAS UN REFUS DE RETOUR.
            //
            // Rendre ici la même erreur que « preuve invalide » ferait refuser des
            // dossiers légitimes pendant un redéploiement, et le client n'aurait
            // aucun moyen de comprendre pourquoi sa photo, bien réelle, est
            // rejetée. Un code distinct laisse l'appelant réessayer.
            _logger.LogError(
                ex,
                "media-service injoignable pour vérifier la preuve {MediaId} ({Statut}).",
                mediaId, ex.StatusCode);

            return Result.Failure(Error.DependencyUnavailable(
                "return_refund.media_indisponible",
                "La preuve n'a pas pu être vérifiée : le service de médias est indisponible."));
        }
    }
}
