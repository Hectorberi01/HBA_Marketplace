using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Addresses;

/// <summary>ADRESSE DE LIVRAISON — MODÈLE BÉNINOIS.</summary>
public sealed class Address : AggregateRoot<AddressId>
{
    public const int MaxLabel = 60;
    public const int MaxRecipient = 120;
    public const int MaxLine = 200;
    public const int MaxQuartier = 120;
    public const int MaxLandmark = 200;
    public const int MaxCommuneCode = 40;
    public const int MaxPhone = 20;

    private Address()
    {
    }

    private Address(AddressId id, Guid userId, bool isDefault)
        : base(id)
    {
        UserId = userId;
        IsDefault = isDefault;
        CountryCode = BeninGeography.CountryCode;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// « Maison », « Bureau »… Confort de choix dans le carnet, sans effet métier.
    /// </summary>
    public string Label { get; private set; } = "Adresse";

    /// <summary>Nom de la personne à qui remettre le colis.</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>Numéro à appeler à l'arrivée, normalisé en <c>+229XXXXXXXXXX</c>.</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Code d'une des 77 communes (voir <see cref="BeninGeography"/>).</summary>
    public string? CommuneCode { get; private set; }

    /// <summary>
    /// Quartier ou village. Texte libre : aucun référentiel national ne les
    /// recense.
    /// </summary>
    public string? Quartier { get; private set; }

    /// <summary>Point de repère (« en face de la pharmacie X »).</summary>
    public string? Landmark { get; private set; }

    /// <summary>Rue, carré, numéro de maison — quand ils existent.</summary>
    public string? Line1 { get; private set; }

    /// <summary>
    /// ISO 3166-1 alpha-2. Vaut « BJ » ; la colonne existe pour une ouverture
    /// ultérieure.
    /// </summary>
    public string CountryCode { get; private set; } = BeninGeography.CountryCode;

    /// <summary>POSITION GPS — FACULTATIVE, ET ELLE DOIT LE RESTER.</summary>
    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    /// <summary>La position est-elle exploitable ? Les deux coordonnées ou aucune.</summary>
    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    public bool IsDefault { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>Libellé de la commune, résolu à l'affichage.</summary>
    public string CommuneName => BeninGeography.CommuneName(CommuneCode);

    /// <summary>L'adresse est-elle livrable en l'état ?</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Recipient)
        && BeninGeography.IsValidPhone(Phone)
        && BeninGeography.IsKnownCommune(CommuneCode)
        && !string.IsNullOrWhiteSpace(Landmark)
        && HasCoordinates;

    public static Result<Address> Create(
        Guid userId, string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1,
        double? latitude, double? longitude, bool isDefault)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<Address>(Error.Validation("users.address.user_required", "Utilisateur requis."));
        }

        var address = new Address(AddressId.New(), userId, isDefault);
        var applied = address.Apply(label, recipient, phone, communeCode, quartier, landmark, line1, latitude, longitude);

        return applied.IsFailure ? Result.Failure<Address>(applied.Error) : Result.Success(address);
    }

    /// <summary>Met à jour les champs modifiables.</summary>
    public Result Update(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1,
        double? latitude, double? longitude)
        => Apply(label, recipient, phone, communeCode, quartier, landmark, line1, latitude, longitude);

    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    private Result Apply(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1,
        double? latitude, double? longitude)
    {
        var cleanRecipient = Trim(recipient);
        if (cleanRecipient is null)
        {
            return Result.Failure(Error.Validation(
                "users.address.recipient_required",
                "Le nom du destinataire est obligatoire : le livreur doit savoir à qui remettre le colis."));
        }

        // Le numéro est NORMALISÉ, pas seulement validé : on stocke une forme
        // unique (+229 suivi de 10 chiffres), quelle que soit celle saisie.
        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure(Error.Validation(
                "users.address.phone_invalid",
                $"Un numéro de téléphone béninois valide est obligatoire ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        // On accepte le code comme le libellé : les données reprises et les imports
        // n'ont que des libellés.
        var resolvedCommune = BeninGeography.ResolveCommuneCode(communeCode);
        if (resolvedCommune is null)
        {
            return Result.Failure(Error.Validation(
                "users.address.commune_required",
                "La commune est obligatoire et doit faire partie des 77 communes du Bénin."));
        }

        if (latitude is < -90 or > 90)
        {
            return Result.Failure(Error.Validation(
                "users.address.latitude_invalid", "Latitude invalide."));
        }

        if (longitude is < -180 or > 180)
        {
            return Result.Failure(Error.Validation(
                "users.address.longitude_invalid", "Longitude invalide."));
        }

        var cleanLandmark = Trim(landmark);
        if (cleanLandmark is null)
        {
            return Result.Failure(Error.Validation(
                "users.address.landmark_required",
                "Le point de repère est obligatoire (ex. « en face de la pharmacie Sainte-Rita »)."));
        }

        // LA POSITION DEVIENT OBLIGATOIRE — À L'ÉCRITURE SEULEMENT.
        if (latitude is null || longitude is null)
        {
            return Result.Failure(Error.Validation(
                "users.address.position_required",
                "La position est obligatoire : elle sert à calculer la distance, donc le prix de la "
                + "livraison. Placez le point sur la carte, même approximativement — le point de repère "
                + "reste ce qui permet au livreur de trouver la porte."));
        }

        Label = Cap(Trim(label) ?? "Adresse", MaxLabel);
        Recipient = Cap(cleanRecipient, MaxRecipient);
        Phone = normalizedPhone;
        CommuneCode = resolvedCommune;
        Quartier = Clean(quartier, MaxQuartier);
        Landmark = Cap(cleanLandmark, MaxLandmark);
        Line1 = Clean(line1, MaxLine);
        CountryCode = BeninGeography.CountryCode;

        // Les deux sont désormais garanties non nulles par la garde ci-dessus.
        Latitude = latitude;
        Longitude = longitude;

        return Result.Success();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Borne une valeur DÉJÀ connue non nulle.</summary>
    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Nettoie et borne : <c>null</c> si vide, tronqué si trop long.</summary>
    private static string? Clean(string? value, int max)
    {
        var trimmed = Trim(value);
        return trimmed is null ? null : Cap(trimmed, max);
    }
}
