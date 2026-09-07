using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Partners;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Commands;

/// <summary>Un point de la course, tel que l'appelant le décrit.</summary>
public sealed record DeliveryStopInput(
    string? ContactName,
    string? Phone,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Instructions = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>Le colis, tel que l'appelant le décrit.</summary>
public sealed record DeliveryPackageInput(
    string? Description,
    decimal? WeightKg = null,
    bool IsFragile = false,
    bool IsPerishable = false);

/// <summary>
/// Crée une course. Point d'entrée commun à HBAExpress, HBA Food et à l'API
/// partenaire — c'est la même opération pour les trois.
/// </summary>
public sealed record CreateDeliveryCommand(
    string Reference,
    DeliverySource Source,
    DeliveryType Type,
    DeliveryStopInput Pickup,
    DeliveryStopInput Dropoff,
    DeliveryPackageInput Package,

    // CE N'ÉTAIT PAS UNE VALEUR, C'ÉTAIT UNE CONCLUSION — ISSUE-057.
    decimal? DeclaredValue = null,
    bool IsCashOnDelivery = false,
    Guid? PartnerId = null,
    string? QuoteId = null,

    // Heure de livraison souhaitée, pour le seul type « Scheduled ».
    DateTime? ScheduledForUtc = null) : ICommand<Guid>;

internal sealed class CreateDeliveryCommandValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryCommandValidator()
    {
        // Validation de FORME seulement.
        RuleFor(c => c.Reference).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Pickup).NotNull();
        RuleFor(c => c.Dropoff).NotNull();
        RuleFor(c => c.Package).NotNull();
        RuleFor(c => c.DeclaredValue).GreaterThanOrEqualTo(0).When(c => c.DeclaredValue is not null);
    }
}

internal sealed class CreateDeliveryCommandHandler : ICommandHandler<CreateDeliveryCommand, Guid>
{
    private readonly IDeliveryRepository _repository;
    private readonly IPartnerRepository _partners;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly IDeliveryPricingQuoteValidator _pricingQuotes;

    public CreateDeliveryCommandHandler(
        IDeliveryRepository repository,
        IPartnerRepository partners,
        IDeliveryUnitOfWork unitOfWork,
        IDeliveryPricingQuoteValidator pricingQuotes)
    {
        _repository = repository;
        _partners = partners;
        _unitOfWork = unitOfWork;
        _pricingQuotes = pricingQuotes;
    }

    public async Task<Result<Guid>> Handle(CreateDeliveryCommand command, CancellationToken cancellationToken)
    {
        // IDEMPOTENCE PAR LA RÉFÉRENCE, ET NON PAR UN EN-TÊTE.
        var existing = await _repository.GetByReferenceAsync(command.Reference, command.Source, cancellationToken);
        if (existing is not null)
        {
            // UN REJEU NE DOIT PAS RÉVÉLER LA COURSE D'UN AUTRE.
            if (existing.PartnerId != command.PartnerId)
            {
                return Result.Failure<Guid>(
                    Error.Conflict("delivery.reference_taken",
                        "Cette référence de commande est déjà utilisée. Employez une référence propre à votre système."));
            }

            return existing.Id.Value;
        }

        // STATUT ET QUOTA DU PARTENAIRE — VÉRIFIÉS ICI, PAS DANS LE FILTRE HTTP.
        if (command.PartnerId is { } partnerId)
        {
            var guard = await CheckPartnerAsync(new PartnerId(partnerId), cancellationToken);
            if (guard.IsFailure)
            {
                return Result.Failure<Guid>(guard.Error);
            }
        }

        var pickup = BuildStop(command.Pickup);
        if (pickup.IsFailure)
        {
            return Result.Failure<Guid>(pickup.Error);
        }

        var dropoff = BuildStop(command.Dropoff);
        if (dropoff.IsFailure)
        {
            return Result.Failure<Guid>(dropoff.Error);
        }

        var package = DeliveryPackage.Create(
            command.Package.Description, command.Package.WeightKg,
            command.Package.IsFragile, command.Package.IsPerishable);
        if (package.IsFailure)
        {
            return Result.Failure<Guid>(package.Error);
        }

        var delivery = Domain.Deliveries.Delivery.Create(
            command.Reference, command.Source, command.Type,
            pickup.Value, dropoff.Value, package.Value,
            command.DeclaredValue, command.IsCashOnDelivery, command.PartnerId,
            command.ScheduledForUtc);
        if (delivery.IsFailure)
        {
            return Result.Failure<Guid>(delivery.Error);
        }

        // LA RECHERCHE DÉMARRE TOUT DE SUITE — SAUF POUR UNE COURSE PROGRAMMÉE.
        if (delivery.Value.ScheduledForUtc is null)
        {
            var searching = delivery.Value.StartSearching();
            if (searching.IsFailure)
            {
                return Result.Failure<Guid>(searching.Error);
            }
        }

        // LE DEVIS EST CONSOMMÉ ICI, ET UNE SEULE FOIS.
        if (command.QuoteId is not null)
        {
            var quote = await _pricingQuotes.ConsumeQuoteAsync(command.QuoteId, delivery.Value.Id.Value, cancellationToken);
            if (quote.IsFailure)
            {
                return Result.Failure<Guid>(quote.Error);
            }

            if (!quote.Value.Valid || quote.Value.Total is null || string.IsNullOrWhiteSpace(quote.Value.Currency))
            {
                return Result.Failure<Guid>(
                    Error.Conflict("pricing.quote_not_usable", $"Devis inutilisable : {quote.Value.Status}."));
            }

            delivery.Value.AttachQuote(quote.Value.QuoteId, quote.Value.Total.Value, quote.Value.Currency);
        }

        await _repository.AddAsync(delivery.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return delivery.Value.Id.Value;
    }

    /// <summary>Le partenaire a-t-il le droit de créer une course, maintenant ?</summary>
    private async Task<Result> CheckPartnerAsync(PartnerId partnerId, CancellationToken cancellationToken)
    {
        var partner = await _partners.GetByIdAsync(partnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("partner.not_found", "Partenaire introuvable."));
        }

        if (!partner.CanCreateDeliveries)
        {
            return Result.Failure(
                Error.Forbidden("partner.not_active",
                    "Votre compte partenaire n'est pas actif. Contactez votre interlocuteur HBA."));
        }

        // Quota nul = illimité. Le contrôle est APPROXIMATIF sous forte concurrence
        // : deux requêtes simultanées peuvent lire le même compte et passer toutes
        // les deux.
        if (partner.DailyQuota > 0)
        {
            var today = await _partners.CountDeliveriesTodayAsync(partnerId, cancellationToken);
            if (today >= partner.DailyQuota)
            {
                return Result.Failure(
                    Error.Forbidden("partner.quota_exceeded",
                        $"Quota quotidien atteint ({partner.DailyQuota} livraisons). "
                        + "Il se réinitialise à minuit UTC."));
            }
        }

        return Result.Success();
    }

    private static Result<DeliveryStop> BuildStop(DeliveryStopInput input)
    {
        Coordinates? position = null;

        // Les deux coordonnées vont ensemble : une seule des deux est traitée comme
        // aucune.
        if (input.Latitude is { } lat && input.Longitude is { } lon)
        {
            var coordinates = Coordinates.Create(lat, lon);
            if (coordinates.IsFailure)
            {
                return Result.Failure<DeliveryStop>(coordinates.Error);
            }

            position = coordinates.Value;
        }

        return DeliveryStop.Create(
            input.ContactName, input.Phone, input.Commune,
            input.Quartier, input.Landmark, input.Instructions, position);
    }
}
