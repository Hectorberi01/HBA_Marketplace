using Grpc.Core;
using HBA.Ordering.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Orders.Grpc.V1;
using ServiceLine = HBA.Orders.Contracts.OrderLineSummary;
using ServiceOrder = HBA.Orders.Contracts.OrderSummary;

using SharedLine = HBA.Ordering.Contracts.OrderLineSummary;
using SharedOrder = HBA.Ordering.Contracts.OrderSummary;
using System.Globalization;
using System.Runtime.CompilerServices;

// COPIE DEPUIS `HBA.Ordering.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Financial.Payments.Infrastructure.Grpc.Mappers;

internal static class OrderingGrpcParsing
{
    public static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    public static DateTime ParseDate(string? value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.MinValue;

    // Chaîne vide et null se confondent en protobuf3 : un champ absent arrive comme
    // "".
    public static string? Vide(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Un montant venu du fil.</summary>
    public static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}
