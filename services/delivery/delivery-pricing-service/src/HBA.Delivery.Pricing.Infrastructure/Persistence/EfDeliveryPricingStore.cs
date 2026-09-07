using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Application.DTOs;
using HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;
using HBA.Delivery.Pricing.Domain.Entities;
using HBA.Delivery.Pricing.Domain.Policies;
using HBA.DeliveryPricing.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HBA.Delivery.Pricing.Infrastructure.Persistence;

public sealed class EfDeliveryPricingStore : IPricingStore
{
    private readonly DeliveryPricingDbContext _db;
    private readonly EstimationItineraireOptions _estimation;

    public EfDeliveryPricingStore(
        DeliveryPricingDbContext db,
        IOptions<EstimationItineraireOptions> estimation)
    {
        _db = db;
        _estimation = estimation.Value;
    }

    public async Task<DeliveryQuote> CreateQuoteAsync(
        CreateQuoteRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeedAsync(cancellationToken);

        // LA GRILLE EST CHOISIE SUR CE QU'ON DEMANDE (audit 2.5).
        var niveau = request.ServiceLevel ?? "STANDARD";
        var vehicule = request.VehicleType;
        var maintenant = DateTimeOffset.UtcNow;

        var rule = await _db.PricingRules
            .Where(r => r.Status == "ACTIVE"
                        && r.ActiveFrom <= maintenant
                        && (r.ActiveTo == null || r.ActiveTo > maintenant)
                        && r.ServiceLevel == niveau
                        && (r.VehicleType == null || r.VehicleType == vehicule))
            // La grille qui NOMME le véhicule passe devant celle qui vaut pour tous
            // : `false` trie avant `true`, donc `VehicleType == null` en dernier.
            .OrderBy(r => r.VehicleType == null)
            .ThenByDescending(r => r.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        if (rule is null)
        {
            throw new InvalidOperationException(
                $"Aucune grille tarifaire active pour le niveau de service « {niveau} »"
                + (vehicule is null ? string.Empty : $" et le véhicule « {vehicule} »")
                + ". Créer la grille correspondante dans la console d'administration, ou "
                + "publier une grille sans véhicule qui vaudra pour tous. Le devis est REFUSÉ "
                + "plutôt que chiffré avec la grille d'un autre niveau — voir l'encadré ci-dessus.");
        }

        // D'OÙ VIENNENT LES DEUX CHIFFRES QUI FONT LE PRIX — ET ON LE DIT.
        var distanceFournie = request.DistanceMeters is not null;

        var distance = request.DistanceMeters
            ?? ServiceabilityPolicy.DistanceRoutiereEstimeeMetres(
                   request.Pickup, request.Dropoff, _estimation.FacteurCorrectionUrbaine);

        var duration = request.DurationSeconds
            ?? Math.Max(
                   _estimation.DureeMinimaleSecondes,
                   (int)(distance / _estimation.VitesseMoyenneMetresParSeconde));

        var breakdown = PricingPolicy.BuildBreakdown(rule, distance, duration, request.Discount);
        var subtotal = PricingPolicy.CalculateSubtotal(rule, breakdown);
        var total = PricingPolicy.CalculateTotal(subtotal, request.Discount);

        var quote = new DeliveryQuote(
            Guid.NewGuid(),
            request.SellerId,
            request.StoreId,
            request.Pickup,
            request.Dropoff,
            distance,
            duration,
            request.VehicleType,
            request.ServiceLevel ?? "STANDARD",
            subtotal,
            breakdown,
            request.Discount,
            total,
            request.Currency ?? "XOF",
            DateTimeOffset.UtcNow.AddMinutes(10),
            "2026.08.1",
            "ACTIVE")
        {
            SourceEstimation = distanceFournie
                ? SourcesEstimation.FournieParAppelant
                : SourcesEstimation.LigneDroiteCorrigee,

            // Aucun facteur n'est appliqué à une distance fournie : la corriger
            // reviendrait à majorer une mesure déjà routière.
            FacteurCorrectionApplique = distanceFournie ? 0m : _estimation.FacteurCorrectionUrbaine
        };

        _db.DeliveryQuotes.Add(quote);

        await publisher.PublishAsync(new DeliveryQuoteCreatedIntegrationEvent
        {
            QuoteId = quote.Id,
            Total = quote.Total,
            Currency = quote.Currency,
            ExpiresAtUtc = quote.ExpiresAt.UtcDateTime
        }, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task<DeliveryQuote?> GetQuoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await _db.DeliveryQuotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return null;
        }

        if (quote.Status == "ACTIVE" && quote.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            quote = quote with { Status = "EXPIRED" };
            _db.DeliveryQuotes.Update(quote);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return quote;
    }

    public async Task<QuoteValidation> ValidateQuoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await GetQuoteAsync(id, cancellationToken);
        return new QuoteValidation(id, quote?.Status == "ACTIVE", quote is null ? "NOT_FOUND" : quote.Status, quote?.Total, quote?.Currency);
    }

    /// <summary>Consomme un devis, et ne le laisse consommer QU'UNE FOIS.</summary>
    public async Task<QuoteValidation> ConsumeQuoteAsync(
        Guid id,
        Guid deliveryId,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var maintenant = DateTimeOffset.UtcNow;

        // TRANSACTION EXPLICITE, ET ELLE N'EST PAS FACULTATIVE.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // `ExecuteUpdateAsync` CONTOURNE LE SUIVI D'EF, ET C'EST VOULU : il émet un
        // `UPDATE … WHERE` unique, sans lecture préalable.
        var lignes = await _db.DeliveryQuotes
            .Where(q => q.Id == id && q.Status == "ACTIVE")
            .ExecuteUpdateAsync(
                q => q
                    .SetProperty(x => x.Status, "CONSUMED")
                    .SetProperty(x => x.ConsumedByDeliveryId, deliveryId)
                    .SetProperty(x => x.ConsumedAt, maintenant),
                cancellationToken);

        if (lignes == 0)
        {
            // Rien n'a été écrit : soit le devis n'existe pas, soit il a déjà été
            // consommé — par cette course (rejeu) ou par une autre (course perdue).
            await transaction.RollbackAsync(cancellationToken);
            return await ValidateQuoteAsync(id, cancellationToken);
        }

        var consomme = await GetQuoteAsync(id, cancellationToken);

        await publisher.PublishAsync(new DeliveryQuoteConsumedIntegrationEvent
        {
            QuoteId = id,
            DeliveryId = deliveryId
        }, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new QuoteValidation(id, true, "CONSUMED", consomme?.Total, consomme?.Currency);
    }

    public async Task<IReadOnlyList<PricingRule>> ListRulesAsync(CancellationToken cancellationToken = default) =>
        await _db.PricingRules.OrderByDescending(rule => rule.Priority).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<DeliveryZone>> ListZonesAsync(CancellationToken cancellationToken = default) =>
        await _db.DeliveryZones.OrderBy(zone => zone.Name).ToArrayAsync(cancellationToken);

    public async Task<PricingRule> AddRuleAsync(
        PricingRuleRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var rule = new PricingRule(Guid.NewGuid(), request.Name, request.Scope, request.ServiceLevel ?? "STANDARD", request.VehicleType, request.BaseFee, request.PerKmFee, request.PerMinuteFee, request.MinFee, request.MaxFee, request.ActiveFrom, request.ActiveTo, request.Priority, request.SurgeMultiplier ?? 1m, "ACTIVE");
        _db.PricingRules.Add(rule);

        await publisher.PublishAsync(new DeliveryPricingRuleCreatedIntegrationEvent
        {
            PricingRuleId = rule.Id,
            Name = rule.Name
        }, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<PricingRule?> UpdateRuleAsync(
        Guid id,
        PricingRuleRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.PricingRules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var updated = current with
        {
            Name = request.Name,
            Scope = request.Scope,
            ServiceLevel = request.ServiceLevel ?? current.ServiceLevel,
            VehicleType = request.VehicleType ?? current.VehicleType,
            BaseFee = request.BaseFee,
            PerKmFee = request.PerKmFee,
            PerMinuteFee = request.PerMinuteFee,
            MinFee = request.MinFee,
            MaxFee = request.MaxFee,
            ActiveFrom = request.ActiveFrom,
            ActiveTo = request.ActiveTo,
            Priority = request.Priority,
            SurgeMultiplier = request.SurgeMultiplier ?? current.SurgeMultiplier
        };

        _db.Entry(current).State = EntityState.Detached;
        _db.PricingRules.Update(updated);

        await publisher.PublishAsync(new DeliveryPricingRuleUpdatedIntegrationEvent
        {
            PricingRuleId = updated.Id,
            Name = updated.Name
        }, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<PricingRule?> SetRuleStatusAsync(
        Guid id,
        bool active,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.PricingRules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var updated = current with { Status = active ? "ACTIVE" : "INACTIVE" };
        _db.Entry(current).State = EntityState.Detached;
        _db.PricingRules.Update(updated);

        IntegrationEvent integrationEvent = active
            ? new DeliveryPricingRuleActivatedIntegrationEvent { PricingRuleId = id }
            : new DeliveryPricingRuleDeactivatedIntegrationEvent { PricingRuleId = id };

        await publisher.PublishAsync(integrationEvent, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public Task<Serviceability> GetServiceabilityAsync(ServiceabilityRequest request, CancellationToken cancellationToken = default)
    {
        // LE MÊME FACTEUR QUE POUR LE PRIX, ET C'EST LE POINT.
        var distance = ServiceabilityPolicy.DistanceRoutiereEstimeeMetres(
            request.Pickup, request.Dropoff, _estimation.FacteurCorrectionUrbaine);

        return Task.FromResult(new Serviceability(
            ServiceabilityPolicy.IsServiceable(distance),
            distance,
            ServiceabilityPolicy.IsServiceable(distance) ? null : "OUT_OF_SERVICE_AREA"));
    }

    private async Task EnsureSeedAsync(CancellationToken cancellationToken)
    {
        if (await _db.PricingRules.AnyAsync(cancellationToken))
        {
            return;
        }

        _db.PricingRules.Add(new PricingRule(Guid.NewGuid(), "Cotonou standard", "GLOBAL", "STANDARD", "MOTORBIKE", 700, 125, 0, 700, 5000, DateTimeOffset.UtcNow.AddDays(-1), null, 100, 1m, "ACTIVE"));
        _db.DeliveryZones.Add(new DeliveryZone(Guid.NewGuid(), "Cotonou centre", "zone_cotonou_centre", true, true));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
