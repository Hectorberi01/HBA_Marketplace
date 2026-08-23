namespace HBA.Delivery.Pricing.Application.DTOs;

public sealed record QuoteValidation(Guid QuoteId, bool Valid, string Status, long? Total, string? Currency);
