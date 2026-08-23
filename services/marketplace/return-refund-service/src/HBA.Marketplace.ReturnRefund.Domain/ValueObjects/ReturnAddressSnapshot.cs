namespace HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

public sealed record ReturnAddressSnapshot(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string CountryCode,
    string? PhoneNumber);
