namespace HBA.Gateway.Application.Contracts.Order;

/// <summary>Miroir MINIMAL d'une commande.</summary>
public sealed record OrderBrief(
    Guid Id,
    string Status,
    string Currency,
    decimal GrandTotal,
    DateTime CreatedAtUtc);
