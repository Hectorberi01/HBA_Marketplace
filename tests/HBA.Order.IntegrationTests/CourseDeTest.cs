using System.Collections.Concurrent;
using HBA.Deliveries.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// delivery-service EN MÉMOIRE — LE VOISIN QU'ORDER-SERVICE APPELLE APRÈS AVOIR
/// CONFIRMÉ.
///
/// IL N'EST PAS LÀ POUR FAIRE PASSER LE TEST, MAIS POUR QU'IL RESTE LISIBLE.
///
/// La confirmation d'une commande publie `OrderConfirmed`, qu'order-service
/// CONSOMME LUI-MÊME : `CreateDeliveryOnOrderConfirmedHandler` demande alors une
/// course. Sans double, cet appel gRPC frapperait le port fermé posé par la
/// fixture, lèverait, et le consommateur le rejouerait trois fois — deux puis
/// quatre secondes d'attente — avant de l'abandonner en Critical. Le test
/// passerait quand même, noyé dans six secondes de traces sans rapport.
///
/// ET IL REND UN OBSERVABLE QUI COMPTE : LA COURSE EST-ELLE DEMANDÉE ?
///
/// C'est le maillon dont l'absence empêchait de PAYER LES VENDEURS : sans course,
/// pas de `DeliveryCompleted`, donc jamais « livrée », donc escrow jamais libéré.
/// Une commande confirmée dont personne ne demande la course est payée et
/// immobile — le tester coûte une ligne.
///
/// SINGLETON : les appels viennent du consommateur Kafka, le test les lit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class CourseDeTest : IDeliveryDispatchApi
{
    private readonly ConcurrentDictionary<string, Guid> _courses = new();
    private readonly ConcurrentQueue<string> _annulations = new();

    /// <summary>Les références pour lesquelles une course a été demandée.</summary>
    public IReadOnlyList<string> CoursesDemandees => _courses.Keys.ToArray();

    /// <summary>Les références dont l'annulation a été demandée.</summary>
    public IReadOnlyList<string> AnnulationsDemandees => _annulations.ToArray();

    public Task<DeliveryCreationResult> CreateAsync(
        CreateDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var id = _courses.GetOrAdd(request.Reference, _ => Guid.NewGuid());
        return Task.FromResult(new DeliveryCreationResult(Succeeded: true, DeliveryId: id, Reason: null));
    }

    /// <summary>
    /// `Found = false` QUAND AUCUNE COURSE N'A ÉTÉ CRÉÉE, ET C'EST LA RÉPONSE
    /// HONNÊTE.
    ///
    /// Une commande annulée pour paiement échoué n'a jamais été confirmée : la
    /// course n'existe pas. `CancelDeliveryOnOrderCancelledHandler` compte
    /// là-dessus — il sort en silence sur `Found = false` et JOURNALISE EN ERREUR
    /// sur `Found = true, Cancelled = false`. Répondre « trouvée » ferait sonner
    /// une alerte « un colis circule pour une commande annulée » à chaque échec de
    /// paiement.
    /// </summary>
    public Task<DeliveryCancellationResult> CancelByReferenceAsync(
        string reference, string source, string? reason, CancellationToken cancellationToken = default)
    {
        _annulations.Enqueue(reference);

        return Task.FromResult(_courses.TryRemove(reference, out _)
            ? new DeliveryCancellationResult(Found: true, Cancelled: true, Reason: null)
            : new DeliveryCancellationResult(Found: false, Cancelled: false, Reason: null));
    }

    // LES DEUX MÉTHODES DE DEVIS ONT QUITTÉ CE FAUX AVEC LEUR CONTRAT.
    //
    // `RequestQuoteAsync` et `LookupQuoteAsync` enveloppaient deux RPC de
    // delivery-service SANS CORPS DE SERVEUR. Le devis se relit maintenant chez
    // delivery-pricing — voir `DevisDeTest`, qui reprend le raisonnement : un
    // faux complaisant y serait bien pire qu'une levée.
}

