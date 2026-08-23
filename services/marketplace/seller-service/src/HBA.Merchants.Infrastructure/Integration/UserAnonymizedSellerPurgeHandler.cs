using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

// ═════════════════════════════════════════════════════════════════════════════
// CE FICHIER VIT DANS `Infrastructure/Integration`, ET NON DANS `Application`.
//
// Il connaît DEUX mondes : l'événement d'Identity et le dépôt des vendeurs. Et il
// dépend de `IConsumerInbox`, qui vit dans `HBA.Shared.Infrastructure` — que la
// couche Application ne référence pas, délibérément.
//
// Même arbitrage que pour `SellerLifecycleCatalogHandlers` côté catalogue, et que
// pour `CreateUserProfileOnUserRegisteredHandler` chez users : la composition root
// a le droit de tout connaître, la couche métier non.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Merchants.Infrastructure.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN COMPTE EST ANONYMISÉ → SON DOSSIER VENDEUR EST FERMÉ ET SES PIÈCES EFFACÉES.
///
/// SANS CE CONSOMMATEUR, LE DROIT À L'EFFACEMENT S'ARRÊTAIT À IDENTITY.
///
/// `UserAnonymizedIntegrationEvent` n'était consommé que par user-service, qui
/// nettoie son profil. seller-service, lui, ne l'écoutait pas — et il détient ce
/// que la plateforme a de plus sensible : `kyb_documents` pointe vers des CARTES
/// D'IDENTITÉ, des registres de commerce et des documents fiscaux, déposés dans le
/// bucket privé.
///
/// Concrètement : un vendeur exerçait son droit à l'effacement, Identity
/// anonymisait son compte, user-service purgeait son profil — et sa pièce
/// d'identité restait, indéfiniment, sans que plus rien ne la relie à une personne
/// identifiable. C'est-à-dire dans l'état exact où plus personne ne peut la
/// retrouver pour l'effacer.
///
/// CE N'EST PAS LE §10.3, ET C'EST ASSUMÉ.
///
/// Le cahier annonce que ce service consomme `identity.user.registered`. Cet
/// événement-là n'apporterait rien : l'inscription vendeur valide déjà le compte
/// par un appel gRPC SYNCHRONE à Identity, e-mail confirmé compris — le faire en
/// asynchrone reviendrait à accepter une inscription avant de savoir si le compte
/// existe. L'écart est noté dans `docs/AUDIT-SELLER.md`.
///
/// ON FERME, ON NE SUPPRIME PAS L'AGRÉGAT.
///
/// `MarkForDeletion` émet un événement PAR PIÈCE — c'est lui qui fait effacer les
/// fichiers chez media-service — puis annonce la suppression du vendeur. Mais la
/// ligne `sellers` reste : elle est référencée par l'historique des commandes, et
/// la retirer laisserait des commandes pointant vers rien. Le compte est fermé, ses
/// produits quittent la vente, ses pièces disparaissent. Ce qui subsiste ne
/// désigne plus personne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UserAnonymizedSellerPurgeHandler
    : IIntegrationEventHandler<UserAnonymizedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
    private const string ConsumerName = "seller-service.identity-user-anonymized";

    private readonly ISellerRepository _sellers;
    private readonly IConsumerInbox _inbox;
    private readonly ISellerUnitOfWork _unitOfWork;
    private readonly ILogger<UserAnonymizedSellerPurgeHandler> _logger;

    public UserAnonymizedSellerPurgeHandler(
        ISellerRepository sellers,
        IConsumerInbox inbox,
        ISellerUnitOfWork unitOfWork,
        ILogger<UserAnonymizedSellerPurgeHandler> logger)
    {
        _sellers = sellers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserAnonymizedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════
        // GARDE D'IDEMPOTENCE DU §19.5 — ET ELLE EST LOAD-BEARING ICI.
        //
        // Contrairement aux consommateurs du catalogue, celui-ci n'est PAS
        // naturellement idempotent : `MarkForDeletion` réémet un événement
        // d'effacement par pièce à chaque passage. Un simple rééquilibrage de
        // partitions Kafka ferait donc redemander à media-service la suppression de
        // fichiers déjà supprimés — bruit, lettres mortes, et un journal qui
        // raconte un effacement qui n'a pas eu lieu.
        //
        // C'est le cas d'usage que l'encadré de `CreateUserProfileOnUserRegistered
        // Handler` annonçait : « un gestionnaire qui crédite un wallet ou débite un
        // stock n'a AUCUNE idempotence naturelle ». En voici un.
        // ═════════════════════════════════════════════════════════════════════
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var seller = await _sellers.GetByUserIdAsync(e.UserId, cancellationToken);

        if (seller is null)
        {
            // LE CAS NORMAL, ET DE LOIN LE PLUS FRÉQUENT.
            //
            // La grande majorité des comptes anonymisés sont des ACHETEURS : ils
            // n'ont jamais eu de dossier vendeur. Ce n'est ni une erreur ni une
            // anomalie — mais la trace doit quand même être écrite, sans quoi
            // l'événement reviendrait à chaque rejeu pour ne rien faire.
            await MarquerTraiteAsync(e, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // L'ORDRE COMPTE : ON FERME AVANT DE PURGER.
        //
        // `RequestClosure` émet l'événement qui retire les produits de la vente.
        // Purger les pièces d'un vendeur dont le catalogue est encore en ligne
        // laisserait une boutique ouverte, tenue par un dossier vide, que la
        // modération ne pourrait plus instruire.
        //
        // La fermeture est refusée si le compte est DÉJÀ fermé — ce n'est pas une
        // erreur ici, et on continue : la purge, elle, doit avoir lieu dans tous
        // les cas.
        var fermeture = seller.RequestClosure();

        if (fermeture.IsFailure)
        {
            _logger.LogInformation(
                "Vendeur {SellerId} déjà hors activité ({Code}) : seule la purge des pièces s'applique.",
                seller.Id.Value, fermeture.Error.Code);
        }

        // Nomme chaque pièce à effacer, une par une. Voir l'encadré de
        // `Seller.MarkForDeletion` : un seul événement portant la liste aurait
        // suffi techniquement, mais si l'effacement de l'une échoue durablement, les
        // autres partent quand même et le message en souffrance nomme le fichier qui
        // résiste.
        var pieces = seller.KybDocuments.Count;
        seller.MarkForDeletion();

        await MarquerTraiteAsync(e, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ON NE JOURNALISE NI L'IDENTIFIANT DU COMPTE, NI RIEN QUI LE DÉSIGNE.
        //
        // Un journal qui trace l'effacement d'une personne en la nommant conserve
        // précisément ce que l'effacement devait faire disparaître — et les journaux
        // survivent plus longtemps que la base. L'identifiant du VENDEUR suffit à
        // l'exploitation : il ne désigne plus personne une fois le compte anonymisé.
        _logger.LogInformation(
            "Compte anonymisé : vendeur {SellerId} fermé, {Count} pièce(s) KYB marquée(s) pour effacement.",
            seller.Id.Value, pieces);
    }

    private Task MarquerTraiteAsync(UserAnonymizedIntegrationEvent e, CancellationToken cancellationToken)
        => _inbox.MarkProcessedAsync(
            e.Id,
            ConsumerName,
            "identity.user.anonymized",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);
}
