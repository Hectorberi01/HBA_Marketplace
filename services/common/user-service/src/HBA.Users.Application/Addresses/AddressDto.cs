using HBA.Shared.Domain.Geography;

namespace HBA.Users.Application.Addresses;

/// <summary>Vue d'une adresse du carnet utilisateur.</summary>
public sealed record AddressDto(
    Guid Id,
    string Label,
    string Recipient,
    string Phone,
    string? CommuneCode,
    string CommuneName,
    string DepartmentName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    string CountryCode,
    double? Latitude,
    double? Longitude,
    bool IsDefault,
    bool IsComplete)
{
    /// <summary>
    /// Une ligne prête à afficher ou à lire au téléphone, du plus précis au plus
    /// large : « En face de la pharmacie Sainte-Rita, Fidjrossè, Cotonou ».
    /// </summary>
    public string SingleLine => string.Join(", ", new[] { Landmark, Quartier, Line1, CommuneName }
        .Where(p => !string.IsNullOrWhiteSpace(p)));

    public static AddressDto From(HBA.Users.Domain.Addresses.Address a) => new(
        a.Id.Value,
        a.Label,
        a.Recipient,
        a.Phone,
        a.CommuneCode,
        a.CommuneName,
        BeninGeography.Commune(a.CommuneCode)?.Department.Name ?? string.Empty,
        a.Quartier,
        a.Landmark,
        a.Line1,
        a.CountryCode,
        a.Latitude,
        a.Longitude,
        a.IsDefault,
        a.IsComplete);
}
