using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Application.DTOs;
using HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;
using HBA.Delivery.Pricing.Domain.Entities;
using HBA.Delivery.Pricing.Domain.Policies;
using HBA.DeliveryPricing.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace HBA.Delivery.Pricing.Infrastructure.Persistence;

public sealed class EfDeliveryPricingStore : IPricingStore
{
    private readonly DeliveryPricingDbContext _db;

    public EfDeliveryPricingStore(DeliveryPricingDbContext db)
    {
        _db = db;
    }

    public async Task<DeliveryQuote> CreateQuoteAsync(
        CreateQuoteRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeedAsync(cancellationToken);

        var rule = await _db.PricingRules
            .Where(r => r.Status == "ACTIVE" && r.ActiveFrom <= DateTimeOffset.UtcNow && (r.ActiveTo == null || r.ActiveTo > DateTimeOffset.UtcNow))
            .OrderByDescending(r => r.Priority)
            .FirstAsync(cancellationToken);

        var distance = request.DistanceMeters ?? ServiceabilityPolicy.HaversineMeters(request.Pickup, request.Dropoff);
        var duration = request.DurationSeconds ?? Math.Max(60, (int)(distance / 5.8));
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
            "ACTIVE");

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

    /// <summary>
    /// Consomme un devis, et ne le laisse consommer QU'UNE FOIS.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE LISAIT, TESTAIT, PUIS ÉCRIVAIT — SANS ATOMICITÉ (§5).
    ///
    /// L'ancienne écriture appelait `ValidateQuoteAsync` (qui relit le devis et
    /// teste `Status == "ACTIVE"`), puis relisait, puis écrivait `CONSUMED`. Entre
    /// le test et l'écriture, rien ne tenait la ligne : deux courses concurrentes
    /// passaient toutes deux la validation, écrivaient toutes deux `CONSUMED` avec
    /// LEUR propre `ConsumedByDeliveryId`, et le dernier écrivain gagnait.
    ///
    /// Résultat : DEUX courses créées sur un seul devis. La plateforme paie deux
    /// livraisons pour une, et le devis n'en désigne qu'une — l'autre course n'a
    /// plus aucune trace de sa tarification.
    ///
    /// LA CORRECTION EST UN `UPDATE` CONDITIONNEL, PAS UN JETON DE CONCURRENCE.
    ///
    /// `UPDATE … WHERE "Id" = @id AND "Status" = 'ACTIVE'` fait le test ET
    /// l'écriture dans le même ordre à la base. PostgreSQL verrouille la ligne le
    /// temps de l'`UPDATE` : le perdant voit zéro ligne affectée, et l'apprend par
    /// une valeur de retour, pas par une exception. Un jeton `xmin` aurait marché
    /// aussi, mais il aurait fallu le poser, le porter au snapshot, et traduire
    /// `DbUpdateConcurrencyException` — pour un résultat moins direct.
    ///
    /// L'ÉVÉNEMENT N'EST PUBLIÉ QUE SI L'ÉCRITURE A EU LIEU. Il l'était
    /// auparavant AVANT le `SaveChanges`, donc y compris par le perdant d'une
    /// course : deux `DeliveryQuoteConsumedIntegrationEvent` partaient pour un seul
    /// devis, avec deux courses différentes. Les consommateurs en aval n'avaient
    /// aucun moyen de savoir lequel comptait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<QuoteValidation> ConsumeQuoteAsync(
        Guid id,
        Guid deliveryId,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var maintenant = DateTimeOffset.UtcNow;

        // ═════════════════════════════════════════════════════════════════════
        // TRANSACTION EXPLICITE, ET ELLE N'EST PAS FACULTATIVE.
        //
        // `ExecuteUpdateAsync` s'exécute IMMÉDIATEMENT — il n'attend pas
        // `SaveChanges`. Sans transaction ouverte, la consommation du devis serait
        // donc validée AVANT que l'outbox ne reçoive son message : un incident
        // entre les deux laisserait un devis consommé et aucun événement, ce qui
        // est exactement la panne que l'outbox existe pour empêcher.
        //
        // Avec la transaction, l'`UPDATE` et l'écriture d'outbox s'engagent
        // ensemble ou pas du tout.
        // ═════════════════════════════════════════════════════════════════════
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // `ExecuteUpdateAsync` CONTOURNE LE SUIVI D'EF, ET C'EST VOULU : il émet
        // un `UPDATE … WHERE` unique, sans lecture préalable. C'est ce qui rend le
        // test et l'écriture indissociables.
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
            // On relit pour DIRE laquelle, ce qui est tout le rôle de cette réponse.
            //
            // ON NE PUBLIE RIEN, et la transaction est annulée : le perdant d'une
            // course ne doit laisser aucune trace. C'était le second défaut de
            // l'ancienne écriture — l'événement partait AVANT le `SaveChanges`, donc
            // les deux concurrents publiaient, et les consommateurs en aval
            // recevaient deux `DeliveryQuoteConsumed` pour un seul devis, désignant
            // deux courses différentes.
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
        var distance = ServiceabilityPolicy.HaversineMeters(request.Pickup, request.Dropoff);
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
