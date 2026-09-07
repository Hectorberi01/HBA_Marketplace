using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Media.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Media.Application.Assets;
using HBA.Media.Application.Assets.EventHandlers;

namespace HBA.Media.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Une pièce KYB est retirée → son fichier disparaît du stockage.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Media.Application.Assets.EventHandlers.DeleteMediaOnKybDocumentRemovedHandler")]
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
        // ON VÉRIFIE CE QU'ON S'APPRÊTE À DÉTRUIRE. CE HANDLER NE LE FAISAIT PAS.
        var media = await _media.GetAsync(e.MediaId, cancellationToken);

        if (media is null)
        {
            // Même cas que « déjà supprimé » plus bas : Kafka livre au moins une
            // fois, et un rejeu tombe sur un média absent.
            _logger.LogDebug(
                "Média {MediaId} déjà absent — suppression ignorée (rejeu).", e.MediaId);

            return;
        }

        if (!string.Equals(media.OwnerType, ProprietaireAttendu, StringComparison.OrdinalIgnoreCase)
            || media.OwnerId != e.SellerId
            || !string.Equals(media.MediaType, NatureAttendue, StringComparison.OrdinalIgnoreCase))
        {
            // ON NE LÈVE PAS, ET ON NE SUPPRIME PAS.
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
