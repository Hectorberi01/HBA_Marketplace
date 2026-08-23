using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Stores;

public readonly record struct StoreId(Guid Value)
{
    public static StoreId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// État commercial d'une boutique.
///
/// CES ÉTATS NE SONT PAS DÉCORATIFS : fermer une boutique retire ses offres de
/// la vente. Un statut qui n'aurait aucun effet observable serait pire qu'absent —
/// le vendeur croirait avoir fermé.
/// </summary>
public enum StoreStatus
{
    /// <summary>Créée, jamais ouverte. Rien n'est en vente.</summary>
    Draft = 0,

    /// <summary>Ouverte : ses offres peuvent être achetées.</summary>
    Open = 1,

    /// <summary>
    /// Fermée par le VENDEUR (congés, travaux, saison). Réversible d'un geste, et
    /// c'est ce qui la distingue de la suspension.
    /// </summary>
    Closed = 2,

    /// <summary>
    /// Fermée par la PLATEFORME. Le vendeur ne peut pas la rouvrir lui-même —
    /// sinon la sanction ne durerait que le temps d'un clic.
    /// </summary>
    Suspended = 3
}

/// <summary>
/// Coordonnées de contact d'une boutique.
///
/// DISTINCTES DE CELLES DU GÉRANT, ET C'EST TOUT L'INTÉRÊT.
///
/// Le dossier KYB porte le téléphone du gérant. Un vendeur ayant trois boutiques
/// donnait donc le même numéro pour les trois — le commentaire de
/// CreateDeliveryOnShipmentReadyHandler le relevait déjà : « un livreur perdu
/// devant la mauvaise porte appelait quelqu'un qui n'y était pas ».
/// </summary>
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

/// <summary>
/// Créneau d'ouverture d'un jour de la semaine.
///
/// CES HORAIRES N'EMPÊCHENT PAS D'ACHETER, ET C'EST DÉLIBÉRÉ.
///
/// Sur une marketplace, une commande passée à deux heures du matin est normale :
/// elle sera préparée le lendemain. Bloquer la vente hors horaires ferait perdre
/// les commandes du soir, qui sont nombreuses.
///
/// Ils servent à DEUX choses réelles : dire à l'acheteur quand la boutique répond,
/// et permettre à la logistique de ne pas envoyer un livreur devant un rideau
/// baissé. Le jour où HBA Food arrivera — où l'on ne commande pas un repas à
/// l'aveugle — la règle sera différente, et elle sera écrite là-bas.
/// </summary>
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
            //
            // « 22 h – 02 h » demanderait de savoir à quel jour appartient la
            // seconde moitié, et toute lecture naïve afficherait une boutique
            // fermée seize heures. Un commerce qui veille se saisit en deux
            // créneaux, sur deux jours.
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
