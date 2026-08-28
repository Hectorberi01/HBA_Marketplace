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

        // ═════════════════════════════════════════════════════════════════════
        // LA GRILLE EST CHOISIE SUR CE QU'ON DEMANDE (audit 2.5).
        //
        // CE QUI ÉTAIT CASSÉ. Cette requête ne filtrait QUE sur le statut et les
        // dates, puis prenait la priorité la plus haute. `ServiceLevel` et
        // `VehicleType` sont portés par `PricingRule`, remplis par la console
        // d'administration, transmis par `CreateQuoteRequest` — et n'entraient
        // dans AUCUNE sélection. Une course EXPRESS en voiture et une course
        // STANDARD en moto recevaient le même prix.
        //
        // Aucun commentaire ne présentait ce comportement comme voulu,
        // contrairement aux autres compromis de ce dépôt. C'est ce qui le
        // distingue d'une simplification assumée : personne ne l'avait décidé.
        //
        // LA CORRESPONDANCE, ET SA PRÉCÉDENCE.
        //
        //   • `ServiceLevel` doit correspondre EXACTEMENT. Il n'y a pas de
        //     joker : une grille est écrite pour un niveau de service, et en
        //     inventer un ici — « ANY », « * » — poserait une convention que la
        //     console d'administration ne connaît pas et ne sait pas produire.
        //
        //   • `VehicleType` est NULLABLE, et le nul EST le joker. C'est déjà le
        //     sens de la colonne : une grille sans véhicule vaut pour tous. On
        //     préfère donc la grille spécifique au véhicule quand elle existe,
        //     et on retombe sur la générique sinon.
        //
        //   • `Priority` départage ce qui reste, comme avant.
        //
        // `Scope` N'ENTRE PAS DANS LA SÉLECTION, et l'audit se trompait sur ce
        // point : il affirmait que `CreateQuoteRequest` le transmet. Ce n'est pas
        // le cas — l'enregistrement ne porte aucun champ de portée. Filtrer
        // dessus supposerait d'abord de décider ce qu'une portée désigne (une
        // zone ? un vendeur ?) et de la faire remonter jusqu'ici. Non fait, et
        // écrit pour qu'on ne le croie pas fait.
        //
        // CE QUE ÇA CHANGE POUR L'EXPLOITATION, ET IL FAUT LE SAVOIR. Demander un
        // niveau de service pour lequel AUCUNE grille active n'existe ne rend plus
        // le prix d'un autre niveau : la création du devis ÉCHOUE, avec un message
        // qui nomme le niveau manquant. C'est voulu — un prix emprunté à une autre
        // grille est facturé au client et ne se voit nulle part, tandis qu'un
        // devis refusé se voit tout de suite. Le seul jeu de données semé ne
        // contient qu'une grille STANDARD / MOTORBIKE.
        // ═════════════════════════════════════════════════════════════════════
        var niveau = request.ServiceLevel ?? "STANDARD";
        var vehicule = request.VehicleType;
        var maintenant = DateTimeOffset.UtcNow;

        var rule = await _db.PricingRules
            .Where(r => r.Status == "ACTIVE"
                        && r.ActiveFrom <= maintenant
                        && (r.ActiveTo == null || r.ActiveTo > maintenant)
                        && r.ServiceLevel == niveau
                        && (r.VehicleType == null || r.VehicleType == vehicule))
            // La grille qui NOMME le véhicule passe devant celle qui vaut pour
            // tous : `false` trie avant `true`, donc `VehicleType == null` en
            // dernier.
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

        // ─────────────────────────────────────────────────────────────────────
        // D'OÙ VIENNENT LES DEUX CHIFFRES QUI FONT LE PRIX — ET ON LE DIT.
        //
        // Ces deux lignes valaient auparavant :
        //     distance = request.DistanceMeters ?? HaversineMeters(...)
        //     duration = request.DurationSeconds ?? Math.Max(60, (int)(distance / 5.8))
        //
        // Le devis produit était identique dans les deux cas — mesure de
        // l'appelant, ou ligne droite calculée ici — et rien, ni dans la réponse
        // ni en base, ne permettait de savoir lequel avait chiffré la course.
        // Sur un litige de facturation, la question ne pouvait plus être posée.
        //
        // La constante 5,8 est devenue `VitesseMoyenneMetresParSeconde`, et un
        // facteur de correction urbaine s'applique à la ligne droite. CE FACTEUR
        // VAUT 1,0 PAR DÉFAUT : le prix calculé ici est aujourd'hui EXACTEMENT
        // celui d'avant. Voir `EstimationItineraireOptions` pour ce que ça laisse
        // ouvert — notamment que la plateforme sous-facture tant qu'il vaut 1,0.
        // ─────────────────────────────────────────────────────────────────────
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
        // LE MÊME FACTEUR QUE POUR LE PRIX, ET C'EST LE POINT.
        //
        // Cette méthode appelait `HaversineMeters` directement, `CreateQuoteAsync`
        // aussi : deux chemins vers le même chiffre, que rien n'obligeait à rester
        // d'accord. Corriger un seul des deux produirait une plateforme qui refuse
        // une course puis la facture — ou l'inverse, ce qui est pire.
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
