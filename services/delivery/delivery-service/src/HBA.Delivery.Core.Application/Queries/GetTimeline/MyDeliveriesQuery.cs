using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Queries;

/// <summary>Un point de passage, tel que le livreur en a besoin pour s'y rendre.</summary>
public sealed record DriverStopDto(
    string ContactName,
    string Phone,
    string CommuneName,
    string? Quartier,
    string Landmark,
    string? Instructions,
    double Latitude,
    double Longitude);

/// <summary>
/// Une course du point de vue du livreur.
///
/// CE DTO NE PORTE PAS LE PIN DE REMISE.
///
/// Le code de preuve appartient au DESTINATAIRE : c'est lui qui le dicte au
/// livreur pour attester qu'il a bien reçu son colis. Le mettre ici viderait la
/// preuve de son sens — un livreur qui lit le code sur son propre écran peut
/// clore la course sans avoir rencontré personne.
/// </summary>
public sealed record MyDeliveryDto(
    Guid DeliveryId,
    string Reference,
    string Status,
    string Type,
    DriverStopDto Pickup,
    DriverStopDto Dropoff,
    string? PackageDescription,
    decimal? PackageWeightKg,
    bool IsFragile,
    string RequiredProof,
    decimal? Price,
    decimal? EstimatedEarning,
    string? Currency,
    DateTime? ScheduledForUtc,
    DateTime? OfferedAtUtc,
    DateTime? OfferExpiresAtUtc);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LE LIVREUR A À FAIRE MAINTENANT.
///
/// CETTE ROUTE MANQUAIT, ET C'EST POURQUOI AUCUN COLIS N'ÉTAIT LIVRABLE.
///
/// Le groupe <c>/api/deliveries/mine</c> ne contenait que des POST : accepter,
/// refuser, faire avancer. Aucun GET. Le livreur pouvait donc répondre à une
/// proposition — à condition d'en connaître l'identifiant, que rien ne lui
/// donnait.
///
/// LE <c>driverId</c> VIENT DU JETON, PAS DE L'APPEL.
///
/// La résolution passe par <c>ResolveDriverQuery</c> côté route, comme les sept
/// commandes voisines. Accepter un identifiant en paramètre permettrait de lire
/// le carnet de courses — donc les coordonnées et téléphones des clients — de
/// n'importe quel livreur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MyDeliveriesQuery(Guid DriverId) : IQuery<IReadOnlyList<MyDeliveryDto>>;

internal sealed class MyDeliveriesQueryHandler : IQueryHandler<MyDeliveriesQuery, IReadOnlyList<MyDeliveryDto>>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDeliveryPayoutSettings _payout;

    public MyDeliveriesQueryHandler(IDeliveryRepository deliveries, IDeliveryPayoutSettings payout)
    {
        _deliveries = deliveries;
        _payout = payout;
    }

    public async Task<Result<IReadOnlyList<MyDeliveryDto>>> Handle(
        MyDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var driverId = new DriverId(query.DriverId);
        var courses = await _deliveries.ListActiveForDriverAsync(driverId, cancellationToken);

        IReadOnlyList<MyDeliveryDto> result = courses.Select(d => Map(d, driverId)).ToList();

        return Result.Success(result);
    }

    private MyDeliveryDto Map(Domain.Deliveries.Delivery d, DriverId driverId)
    {
        // La proposition EN COURS pour CE livreur. Une course peut porter
        // plusieurs propositions — refusées, expirées — et seule celle qui attend
        // encore une réponse porte une échéance à afficher.
        var offre = d.Assignments
            .LastOrDefault(a => a.DriverId == driverId && a.Outcome == AssignmentOutcome.Offered);

        return new MyDeliveryDto(
            d.Id.Value,
            d.Reference,
            d.Status.ToString(),
            d.Type.ToString(),
            ToStop(d.Pickup),
            ToStop(d.Dropoff),
            d.Package.Description,
            d.Package.WeightKg,
            d.Package.IsFragile,
            d.RequiredProof.ToString(),
            d.Price,
            // Ce que la course RAPPORTERAIT. Le gain définitif n'est figé qu'à la
            // remise ; l'annoncer avant serait une promesse, mais ne rien annoncer
            // du tout demande au livreur d'accepter une course dont il ignore le
            // montant — en quarante-cinq secondes.
            EstimatedEarning(d),
            d.Currency,
            d.ScheduledForUtc,
            offre?.OfferedAtUtc,
            offre is null ? null : offre.OfferedAtUtc + Domain.Deliveries.Delivery.OfferTimeout);
    }

    private decimal? EstimatedEarning(Domain.Deliveries.Delivery d)
    {
        // Le gain déjà figé fait autorité — après la remise, ce n'est plus une
        // estimation. Avant, on projette le prix par la part en vigueur.
        //
        // La part est INJECTÉE, jamais recopiée : c'est la même source que celle
        // qui figera le montant à la remise. Une constante locale afficherait un
        // montant que le décompte final contredirait.
        if (d.DriverEarning is not null)
        {
            return d.DriverEarning;
        }

        // Arrondi à l'unité : le franc CFA n'a pas de subdivision en circulation.
        return d.Price is null ? null : Math.Round(d.Price.Value * _payout.DriverShareRate, 0);
    }

    private static DriverStopDto ToStop(DeliveryStop stop)
        => new(
            stop.ContactName,
            stop.Phone,
            stop.CommuneName,
            stop.Quartier,
            stop.Landmark,
            stop.Instructions,
            stop.Position.Latitude,
            stop.Position.Longitude);
}
