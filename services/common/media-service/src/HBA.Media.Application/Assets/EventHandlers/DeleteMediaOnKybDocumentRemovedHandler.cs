using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Media.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Media.Application.Assets.EventHandlers;

/// <summary>
/// Une pièce KYB est retirée → son fichier disparaît du stockage.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE FICHIER SURVIVAIT À LA PIÈCE, INDÉFINIMENT.
///
/// Supprimer une pièce KYB retirait la ligne côté merchant-service et laissait
/// l'objet dans MinIO. Rien ne le référençait plus, rien ne le signalait, et
/// personne ne pouvait deviner qu'il fallait le nettoyer.
///
/// Il ne s'agit pas seulement d'espace disque : une pièce KYB est un document
/// d'identité ou un extrait de registre. Le conserver après que son propriétaire
/// a demandé son retrait est une donnée personnelle gardée sans raison.
///
/// PAR ÉVÉNEMENT, ET NON PAR APPEL gRPC.
///
/// merchant-service pourrait appeler media-service pour ordonner la suppression.
/// Ce serait lui donner le droit de détruire des objets qu'il ne possède pas.
///
/// Ici il annonce un FAIT — « cette pièce a été retirée » — et media-service,
/// qui possède le fichier, décide de ce que ce fait implique. C'est le même
/// arbitrage que pour le remboursement à l'annulation d'une commande.
///
/// ON LÈVE SI LA SUPPRESSION ÉCHOUE.
///
/// Contrairement à une notification manquée, un fichier non supprimé ne se
/// rattrape pas tout seul : plus rien ne le référence, donc plus rien ne
/// redemandera sa suppression. La reprise bornée du consommateur donne trois
/// chances, puis journalise en Critical — ce qu'on veut d'une donnée
/// personnelle qui aurait dû partir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class DeleteMediaOnKybDocumentRemovedHandler
    : IIntegrationEventHandler<KybDocumentRemovedIntegrationEvent>
{
    /// <summary>La nature attendue d'une pièce KYB (<c>MediaType.SellerDocument</c>).</summary>
    private const string NatureAttendue = "SellerDocument";

    /// <summary>Le propriétaire attendu (<c>MediaOwnerType.Seller</c>).</summary>
    private const string ProprietaireAttendu = "Seller";

    private readonly ISender _sender;
    private readonly IMediaModuleApi _media;
    private readonly ILogger<DeleteMediaOnKybDocumentRemovedHandler> _logger;

    public DeleteMediaOnKybDocumentRemovedHandler(
        ISender sender,
        IMediaModuleApi media,
        ILogger<DeleteMediaOnKybDocumentRemovedHandler> logger)
    {
        _sender = sender;
        _media = media;
        _logger = logger;
    }

    public async Task HandleAsync(
        KybDocumentRemovedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════
        // ON VÉRIFIE CE QU'ON S'APPRÊTE À DÉTRUIRE. CE HANDLER NE LE FAISAIT PAS.
        //
        // L'encadré ci-dessus explique qu'on passe par un ÉVÉNEMENT plutôt que par
        // un appel gRPC, pour ne pas donner à merchant-service « le droit de
        // détruire des objets qu'il ne possède pas ». Le raisonnement était juste
        // et la mise en œuvre l'annulait : le handler prenait l'identifiant du
        // message et supprimait, sans regarder à quoi il correspondait. Le droit
        // était donc bien donné, simplement par un autre chemin.
        //
        // Combiné à l'absence de contrôle de propriété côté seller-service —
        // fermée dans le même lot — cela formait une primitive de SUPPRESSION
        // ARBITRAIRE : rattacher le média d'autrui à son dossier, le retirer, et
        // media-service effaçait la photo produit d'un concurrent, un visuel de
        // restaurant, ou le dossier KYB d'un autre vendeur.
        //
        // Refermer les deux bouts n'est pas redondant : seller-service n'est pas
        // le seul émetteur possible de ce type d'événement, et un consommateur qui
        // détruit sur parole redevient dangereux au premier émetteur suivant.
        //
        // CE CONTRÔLE NE PEUT PAS ÊTRE UNE JOINTURE. Media ne connaît pas les
        // vendeurs (§20) : il compare le couple (OwnerType, OwnerId) que le
        // téléversement a posé, et rien d'autre. C'est précisément ce que ce
        // couple existe pour permettre.
        // ═════════════════════════════════════════════════════════════════════
        var media = await _media.GetAsync(e.MediaId, cancellationToken);

        if (media is null)
        {
            // Même cas que « déjà supprimé » plus bas : Kafka livre au moins une
            // fois, et un rejeu tombe sur un média absent. C'est le résultat voulu.
            _logger.LogDebug(
                "Média {MediaId} déjà absent — suppression ignorée (rejeu).", e.MediaId);

            return;
        }

        if (!string.Equals(media.OwnerType, ProprietaireAttendu, StringComparison.OrdinalIgnoreCase)
            || media.OwnerId != e.SellerId
            || !string.Equals(media.MediaType, NatureAttendue, StringComparison.OrdinalIgnoreCase))
        {
            // ON NE LÈVE PAS, ET ON NE SUPPRIME PAS.
            //
            // Lever ferait rejouer trois fois un message qui ne réussira jamais :
            // ce n'est pas une panne transitoire, c'est un message qui demande
            // quelque chose d'illégitime. On le journalise en Warning — un fichier
            // NON supprimé est un incident bien moins grave qu'un fichier détruit à
            // tort, et celui-là serait irréversible.
            _logger.LogWarning(
                "Suppression REFUSÉE : le média {MediaId} appartient à {OwnerType}/{OwnerId} et est "
                + "de nature {Nature} — l'événement le réclame au nom du vendeur {SellerId}. "
                + "Aucun fichier n'a été supprimé.",
                e.MediaId, media.OwnerType, media.OwnerId, media.MediaType, e.SellerId);

            return;
        }

        var resultat = await _sender.Send(new DeleteMediaCommand(e.MediaId), cancellationToken);

        if (resultat.IsSuccess)
        {
            _logger.LogInformation(
                "Média {MediaId} supprimé : pièce KYB retirée par le vendeur {SellerId}.",
                e.MediaId, e.SellerId);

            return;
        }

        // « DÉJÀ SUPPRIMÉ » N'EST PAS UN ÉCHEC.
        //
        // Kafka livre au moins une fois. Un rejeu tombe sur un média absent, et
        // c'est exactement le résultat recherché.
        if (resultat.Error.Code.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Média {MediaId} déjà absent — suppression ignorée (rejeu).", e.MediaId);

            return;
        }

        _logger.LogError(
            "Média {MediaId} NON supprimé alors que la pièce KYB du vendeur {SellerId} a été "
            + "retirée — {Code} : {Message}. Un document personnel reste stocké sans raison.",
            e.MediaId, e.SellerId, resultat.Error.Code, resultat.Error.Message);

        throw new InvalidOperationException(
            $"Suppression du média {e.MediaId} impossible : "
            + $"{resultat.Error.Code} — {resultat.Error.Message}");
    }
}
