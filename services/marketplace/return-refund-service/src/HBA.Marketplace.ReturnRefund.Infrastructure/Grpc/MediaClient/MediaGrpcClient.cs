using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.MediaClient;

/// <summary>
/// BOUCHON : aucune preuve photo n'est réellement vérifiée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE CLASSE N'A PAS DE CLIENT gRPC. ELLE NE CONTACTE PERSONNE.
///
/// Pas de champ injecté, pas de constructeur : rien qu'une expression-corps qui
/// fabrique sa réponse sur place. Elle satisfait l'interface, le conteneur la
/// résout, l'appelant reçoit un succès — et LA PREUVE N'EST PAS VÉRIFIÉE.
/// Seul le fait que la chaîne ne soit pas vide est contrôlé : ni l'existence du
/// média, ni son propriétaire. `AddEvidenceCommandHandler` accepte donc n'importe
/// quel identifiant comme preuve — y compris le média d'un autre client.
///
/// Un vrai client existe déjà : `HBA.Media.Contracts.Grpc.MediaGrpcClient`.
/// Il implémente `IMediaModuleApi`, pas `IMediaGrpcClient` : brancher l'un sur
/// l'autre suppose de décider ce que « valider » veut dire ici (existence seule,
/// ou existence ET appartenance).
///
/// C'est pire qu'une `NotImplementedException` : celle-là se verrait au premier
/// appel. Ici, tout se déroule normalement et rien ne se passe.
///
/// POURQUOI ELLE EST ENCORE LÀ.
///
/// L'implémentation réelle demande de trancher le contrat de validation, puis de déléguer au client
/// média partagé — des décisions de contrat qui
/// dépassent le lot en cours. Ce qui a été fait à la place :
/// `ReturnRefundModuleInstaller.GuardSimulatedGrpcAdapters` REFUSE LE DÉMARRAGE
/// EN PRODUCTION tant que cette classe est enregistrée, et l'annonce bruyamment
/// partout ailleurs. Même règle que `SimulatedPayoutGateway` côté paiements.
///
/// CE QUI L'AVAIT LAISSÉE PASSER.
///
/// `scripts/check-grpc-stubs.py` balayait `<dépôt>/src`, chemin hérité du
/// monolithe et inexistant dans ce monorepo. Il rendait « 0 bouchon » depuis
/// toujours. Réparé, il désigne cette classe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class MediaGrpcClient : IMediaGrpcClient
{
    public Task<Result> ValidateMediaAsync(string mediaId, Guid ownerId, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(mediaId)
            ? Task.FromResult(Result.Failure(Error.Validation("return_refund.media_required", "La preuve media est obligatoire.")))
            : Task.FromResult(Result.Success());
}
