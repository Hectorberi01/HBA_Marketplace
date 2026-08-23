using System.Reflection;
using System.Text;
using System.Text.Json;
using HBA.Shared.IntegrationEvents;

namespace HBA.Shared.Infrastructure.Kafka;

public static class KafkaEventNaming
{
    private static readonly string[] AggregateIdCandidates =
    [
        "AggregateId",
        "OrderId",
        "ShipmentId",
        "DeliveryId",
        "ProductId",
        "SellerId",
        "StoreId",
        "UserId",
        "CustomerId",
        "PaymentId",
        "InvoiceId",
        "WalletId",
        "CartId",
        "ReviewId",
        "MessageId",
        "MediaId",
        "AssetId",
        "Id"
    ];

    public static string Producer(string? configuredProducer, string? serviceName)
    {
        var producer = FirstNonEmpty(configuredProducer, serviceName, Environment.GetEnvironmentVariable("SERVICE_NAME"), "unknown-service");
        return NormalizeServiceName(producer);
    }

    /// <summary>
    /// Le sujet d'un producteur. Délègue à <see cref="HbaTopics"/>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE DÉRIVAIT LE SUJET ELLE-MÊME, ET C'ÉTAIT LA MOITIÉ D'ISSUE-001.
    ///
    /// Elle retirait « -service » du nom du conteneur : `seller-service` donnait
    /// `service.seller.v1`, quand tous les consommateurs écoutaient
    /// `service.merchant.v1`. Six domaines étaient dans ce cas, et rien ne pouvait
    /// le signaler — un message part, il est acquitté, il n'arrive nulle part.
    ///
    /// Elle est conservée plutôt que supprimée parce qu'elle est citée dans des
    /// encadrés et des tests ; mais elle ne décide plus. Une seule table décide,
    /// et c'est celle que lit aussi la liste d'abonnement du consommateur.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static string Topic(KafkaEventBusOptions options, string producer)
        => HbaTopics.Pour(options, producer);

    public static string EventType(Type eventType)
    {
        var name = eventType.Name;
        if (name.EndsWith("IntegrationEvent", StringComparison.Ordinal))
        {
            name = name[..^"IntegrationEvent".Length];
        }

        return ToSeparatedLower(name, '.');
    }

    public static string AggregateType(string eventType)
    {
        var index = eventType.IndexOf('.');
        return index <= 0 ? eventType : eventType[..index];
    }

    public static string AggregateId(IntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();

        foreach (var candidate in AggregateIdCandidates)
        {
            var property = type.GetProperty(candidate, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(integrationEvent);

            if (value is null)
            {
                continue;
            }

            var text = value switch
            {
                Guid guid => guid.ToString("D"),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return integrationEvent.Id.ToString("D");
    }

    public static string? TenantId(IntegrationEvent integrationEvent)
        => ReadStringProperty(integrationEvent, "TenantId")
           ?? ReadStringProperty(integrationEvent, "MerchantId")
           ?? ReadStringProperty(integrationEvent, "SellerId");

    public static JsonElement Data(IntegrationEvent integrationEvent, JsonSerializerOptions serializerOptions)
        => JsonSerializer.SerializeToElement(integrationEvent, integrationEvent.GetType(), serializerOptions);

    public static string UlidFrom(Guid id, DateTime occurredOnUtc)
    {
        Span<byte> bytes = stackalloc byte[16];
        var timestamp = new DateTimeOffset(DateTime.SpecifyKind(occurredOnUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        var guidBytes = id.ToByteArray();
        guidBytes.AsSpan(0, 10).CopyTo(bytes[6..]);

        return "evt_" + EncodeCrockfordBase32(bytes);
    }

    private static string? ReadStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var value = property?.GetValue(instance);

        return value switch
        {
            null => null,
            Guid guid when guid == Guid.Empty => null,
            Guid guid => guid.ToString("D"),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string NormalizeServiceName(string serviceName)
        => serviceName.Trim().ToLowerInvariant().Replace('_', '-');

    private static string FirstNonEmpty(params string?[] values)
        => values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string ToSeparatedLower(string value, char separator)
    {
        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0 && !char.IsUpper(value[i - 1]))
            {
                builder.Append(separator);
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string EncodeCrockfordBase32(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Span<char> output = stackalloc char[26];

        var bitBuffer = 0;
        var bitCount = 0;
        var outputIndex = 0;

        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5 && outputIndex < output.Length)
            {
                bitCount -= 5;
                output[outputIndex++] = alphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        if (outputIndex < output.Length && bitCount > 0)
        {
            output[outputIndex++] = alphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
        }

        while (outputIndex < output.Length)
        {
            output[outputIndex++] = '0';
        }

        return new string(output);
    }
}
