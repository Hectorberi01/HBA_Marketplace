using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Webhooks;

/// <summary>Identité forte d'un envoi de webhook.</summary>
public readonly record struct WebhookDeliveryId(Guid Value)
{
    public static WebhookDeliveryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum WebhookStatus
{
    /// <summary>En attente d'un envoi ou d'un réessai.</summary>
    Pending = 0,

    /// <summary>Accepté par le partenaire (2xx).</summary>
    Delivered = 1,

    /// <summary>Abandonné après épuisement des tentatives.</summary>
    Abandoned = 2
}

/// <summary>UN APPEL SORTANT VERS UN PARTENAIRE — PERSISTÉ AVANT D'ÊTRE TENTÉ.</summary>
public sealed class WebhookDelivery : AggregateRoot<WebhookDeliveryId>
{
    /// <summary>Nombre total de tentatives avant abandon.</summary>
    public const int MaxAttempts = 6;

    private WebhookDelivery(
        WebhookDeliveryId id, Guid partnerId, Guid eventId, string eventType, string payload)
        : base(id)
    {
        PartnerId = partnerId;
        EventId = eventId;
        EventType = eventType;
        Payload = payload;
        Status = WebhookStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        NextAttemptAtUtc = CreatedAtUtc;
    }

    // Requis par EF Core.
    private WebhookDelivery()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public Guid PartnerId { get; private set; }

    /// <summary>Identifiant de l'événement d'origine, transmis au partenaire.</summary>
    public Guid EventId { get; private set; }

    public string EventType { get; private set; }

    /// <summary>Corps JSON, figé à la mise en file : c'est LUI qui est signé.</summary>
    public string Payload { get; private set; }

    public WebhookStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime NextAttemptAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>Dernier code HTTP obtenu.</summary>
    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public static Result<WebhookDelivery> Enqueue(
        Guid partnerId, Guid eventId, string? eventType, string? payload)
    {
        if (partnerId == Guid.Empty)
        {
            return Result.Failure<WebhookDelivery>(
                Error.Validation("webhook.partner_required", "Un webhook doit désigner son partenaire."));
        }

        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<WebhookDelivery>(
                Error.Validation("webhook.payload_required", "Type et corps de l'événement sont requis."));
        }

        return new WebhookDelivery(WebhookDeliveryId.New(), partnerId, eventId, eventType.Trim(), payload);
    }

    public Result MarkDelivered(int statusCode)
    {
        if (Status is not WebhookStatus.Pending)
        {
            return Result.Failure(Error.Conflict("webhook.not_pending", "Cet envoi n'est plus en attente."));
        }

        Attempts++;
        Status = WebhookStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
        LastStatusCode = statusCode;
        LastError = null;
        return Result.Success();
    }

    /// <summary>UN ÉCHEC REPROGRAMME, OU ABANDONNE.</summary>
    public Result MarkFailed(int? statusCode, string? error, int jitterSeconds = 0)
    {
        if (Status is not WebhookStatus.Pending)
        {
            return Result.Failure(Error.Conflict("webhook.not_pending", "Cet envoi n'est plus en attente."));
        }

        Attempts++;
        LastStatusCode = statusCode;
        LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim()[..Math.Min(error.Trim().Length, 500)];

        if (Attempts >= MaxAttempts)
        {
            // ABANDONNÉ, et non « échoué » : le mot compte.
            Status = WebhookStatus.Abandoned;
            return Result.Success();
        }

        var backoffMinutes = Math.Pow(2, Attempts - 1);
        NextAttemptAtUtc = DateTime.UtcNow
            .AddMinutes(backoffMinutes)
            .AddSeconds(Math.Max(0, jitterSeconds));

        return Result.Success();
    }
}

/// <summary>Accès à la file des webhooks.</summary>
public interface IWebhookDeliveryRepository
{
    /// <summary>Envois dus, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default);

    Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default);
}
