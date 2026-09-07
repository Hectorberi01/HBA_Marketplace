using HBA.Deliveries.Contracts;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Queries;

/// <summary>Détail d'une course.</summary>
public sealed record GetDeliveryQuery(Guid DeliveryId, Guid? RequiredPartnerId)
    : IQuery<DeliverySummary>;

/// <summary>Suivi d'une course : état, et position du livreur pendant le transport.</summary>
public sealed record GetDeliveryTrackingQuery(Guid DeliveryId, Guid? RequiredPartnerId)
    : IQuery<DeliveryTracking>;

/// <summary>LES LECTURES PASSENT PAR L'API DU MODULE, PAS PAR LE DÉPÔT.</summary>
internal sealed class DeliveryQueryHandler
    : IQueryHandler<GetDeliveryQuery, DeliverySummary>,
      IQueryHandler<GetDeliveryTrackingQuery, DeliveryTracking>
{
    private readonly IDeliveryModuleApi _api;
    private readonly IDeliveryRepository _repository;

    public DeliveryQueryHandler(IDeliveryModuleApi api, IDeliveryRepository repository)
    {
        _api = api;
        _repository = repository;
    }

    public async Task<Result<DeliverySummary>> Handle(GetDeliveryQuery query, CancellationToken cancellationToken)
    {
        var authorized = await EnsureBelongsToAsync(query.DeliveryId, query.RequiredPartnerId, cancellationToken);
        if (authorized.IsFailure)
        {
            return Result.Failure<DeliverySummary>(authorized.Error);
        }

        var delivery = await _api.GetAsync(query.DeliveryId, cancellationToken);

        return delivery is null
            ? Result.Failure<DeliverySummary>(NotFound)
            : delivery;
    }

    public async Task<Result<DeliveryTracking>> Handle(
        GetDeliveryTrackingQuery query, CancellationToken cancellationToken)
    {
        var authorized = await EnsureBelongsToAsync(query.DeliveryId, query.RequiredPartnerId, cancellationToken);
        if (authorized.IsFailure)
        {
            return Result.Failure<DeliveryTracking>(authorized.Error);
        }

        var tracking = await _api.GetTrackingAsync(query.DeliveryId, cancellationToken);

        return tracking is null
            ? Result.Failure<DeliveryTracking>(NotFound)
            : tracking;
    }

    /// <summary>« INTROUVABLE » ET NON « INTERDIT » — C'EST DÉLIBÉRÉ.</summary>
    private async Task<Result> EnsureBelongsToAsync(
        Guid deliveryId, Guid? requiredPartnerId, CancellationToken cancellationToken)
    {
        if (requiredPartnerId is null)
        {
            return Result.Success();
        }

        var delivery = await _repository.GetByIdAsync(new DeliveryId(deliveryId), cancellationToken);

        return delivery is not null && delivery.PartnerId == requiredPartnerId
            ? Result.Success()
            : Result.Failure(NotFound);
    }

    private static Error NotFound => Error.NotFound("delivery.not_found", "Course introuvable.");
}
