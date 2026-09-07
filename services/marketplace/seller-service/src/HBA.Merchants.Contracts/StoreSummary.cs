namespace HBA.Merchants.Contracts;

/// <summary>Un créneau d'ouverture, tel qu'affiché.</summary>
/// <param name="Day">Jour de la semaine, en anglais invariant (« Monday »…).</param>
/// <param name="OpensAt">Heure d'ouverture, format « HH:mm ».</param>
/// <param name="ClosesAt">Heure de fermeture, format « HH:mm ».</param>
public sealed record StoreOpeningHourSummary(string Day, string OpensAt, string ClosesAt);

/// <summary>Une boutique, telle que la voient son vendeur et l'administration.</summary>
/// <param name="IsSelling">
/// <summary> Ses offres sont-elles achetables en ce moment ?</summary>
/// </param>
public sealed record StoreSummary(
    Guid Id,
    Guid SellerId,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    string? ContactEmail,
    string Status,
    bool IsSelling,

    Guid? FulfillmentLocationId,
    string? StatusReason,
    IReadOnlyList<StoreOpeningHourSummary> OpeningHours,
    DateTime CreatedOnUtc)
{
    /// <summary>Projette la vitrine PUBLIQUE d'une boutique.</summary>
    public StorePublicSummary ToPublic()
        => new(Id, Name, LogoUrl, Description, ContactPhone, IsSelling, OpeningHours);
}

/// <summary>Vitrine PUBLIQUE d'une boutique — ce qu'un visiteur anonyme peut voir.</summary>
public sealed record StorePublicSummary(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    bool IsSelling,
    IReadOnlyList<StoreOpeningHourSummary> OpeningHours);
