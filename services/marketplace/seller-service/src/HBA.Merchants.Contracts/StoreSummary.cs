namespace HBA.Merchants.Contracts;

/// <summary>Un créneau d'ouverture, tel qu'affiché.</summary>
/// <param name="Day">Jour de la semaine, en anglais invariant (« Monday »…).</param>
/// <param name="OpensAt">Heure d'ouverture, format « HH:mm ».</param>
/// <param name="ClosesAt">Heure de fermeture, format « HH:mm ».</param>
public sealed record StoreOpeningHourSummary(string Day, string OpensAt, string ClosesAt);

/// <summary>
/// Une boutique, telle que la voient son vendeur et l'administration.
///
/// PAS D'ADRESSE ICI. Le lieu physique vit dans Inventory
/// (<c>FulfillmentLocation</c>) et n'est référencé que par son identifiant :
/// recopier l'adresse créerait deux vérités pour un même lieu, qui divergeraient
/// au premier déménagement. Les écrans qui ont besoin de l'adresse la demandent à
/// Inventory à partir de <see cref="FulfillmentLocationId"/>.
/// </summary>
public sealed record StoreSummary(
    Guid Id,
    Guid SellerId,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    string? ContactEmail,
    string Status,

    /// <summary>Ses offres sont-elles achetables en ce moment ?</summary>
    bool IsSelling,

    Guid? FulfillmentLocationId,
    string? StatusReason,
    IReadOnlyList<StoreOpeningHourSummary> OpeningHours,
    DateTime CreatedOnUtc)
{
    /// <summary>
    /// Projette la vitrine PUBLIQUE d'une boutique.
    ///
    /// Ni motif de fermeture — il peut mentionner une sanction — ni identifiant de
    /// lieu logistique. Tout ajout à ce record est une décision de divulgation.
    /// </summary>
    public StorePublicSummary ToPublic()
        => new(Id, Name, LogoUrl, Description, ContactPhone, IsSelling, OpeningHours);
}

/// <summary>
/// Vitrine PUBLIQUE d'une boutique — ce qu'un visiteur anonyme peut voir.
///
/// Le téléphone y figure volontairement : c'est un commerce, et c'est le numéro
/// de la BOUTIQUE, pas celui du gérant (voir BusinessContact).
/// </summary>
public sealed record StorePublicSummary(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    bool IsSelling,
    IReadOnlyList<StoreOpeningHourSummary> OpeningHours);
