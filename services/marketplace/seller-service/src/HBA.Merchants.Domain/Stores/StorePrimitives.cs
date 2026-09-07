using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Stores;

public readonly record struct StoreId(Guid Value)
{
    public static StoreId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>État commercial d'une boutique.</summary>
public enum StoreStatus
{
    /// <summary>Créée, jamais ouverte. Rien n'est en vente.</summary>
    Draft = 0,

    /// <summary>Ouverte : ses offres peuvent être achetées.</summary>
    Open = 1,

    /// <summary>Fermée par le VENDEUR (congés, travaux, saison).</summary>
    Closed = 2,

    /// <summary>
    /// Fermée par la PLATEFORME. Le vendeur ne peut pas la rouvrir lui-même — sinon
    /// la sanction ne durerait que le temps d'un clic.
    /// </summary>
    Suspended = 3
}

/// <summary>Coordonnées de contact d'une boutique.</summary>
public sealed record BusinessContact
{
    private BusinessContact(string phone, string? email)
    {
        Phone = phone;
        Email = email;
    }

    public string Phone { get; }
    public string? Email { get; }

    public static Result<BusinessContact> Create(string phone, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            // Obligatoire : c'est le numéro qu'appellera un acheteur, et le livreur
            // arrivé devant une porte close.
            return Error.Validation(
                "sellers.store.phone_required", "Le téléphone de la boutique est obligatoire.");
        }

        var normalise = phone.Trim();
        if (normalise.Length is < 8 or > 20)
        {
            return Error.Validation(
                "sellers.store.phone_invalid", "Le téléphone de la boutique est invalide.");
        }

        var courriel = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (courriel is not null && (!courriel.Contains('@', StringComparison.Ordinal) || courriel.Length > 200))
        {
            return Error.Validation(
                "sellers.store.email_invalid", "L'adresse e-mail de la boutique est invalide.");
        }

        return new BusinessContact(normalise, courriel);
    }
}

/// <summary>Créneau d'ouverture d'un jour de la semaine.</summary>
public sealed record StoreOpeningHour
{
    private StoreOpeningHour(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        Day = day;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public DayOfWeek Day { get; }
    public TimeOnly OpensAt { get; }
    public TimeOnly ClosesAt { get; }

    public static Result<StoreOpeningHour> Create(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        if (closesAt <= opensAt)
        {
            // PAS DE CRÉNEAU À CHEVAL SUR MINUIT.
            return Error.Validation(
                "sellers.store.hours_invalid",
                "L'heure de fermeture doit être postérieure à l'heure d'ouverture.");
        }

        return new StoreOpeningHour(day, opensAt, closesAt);
    }

    /// <summary>Ce créneau en recouvre-t-il un autre du même jour ?</summary>
    public bool Overlaps(StoreOpeningHour other)
        => Day == other.Day && OpensAt < other.ClosesAt && other.OpensAt < ClosesAt;
}
