using System.Collections.Concurrent;
using HBA.Deliveries.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// delivery-service EN MÉMOIRE — LE VOISIN QU'ORDER-SERVICE APPELLE APRÈS AVOIR
/// CONFIRMÉ.
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
}

