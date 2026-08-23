using HBA.Shared.IntegrationEvents;

namespace HBA.Users.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS ÉVÉNEMENTS DU §10.2, ET LES PREMIERS À PORTER `[HbaEvent]`.
///
/// ILS NE S'APPELLENT PAS COMME LEUR NOM MÉTIER, ET C'EST VOLONTAIRE.
///
/// `user.profile.updated` est porté par `UserProfileChangedIntegrationEvent` et
/// non par `UserProfileUpdatedIntegrationEvent` — parce que ce dernier EXISTE
/// DÉJÀ, publié par identity-service quand un compte change de nom. Deux classes
/// homonymes dans deux espaces de noms compilent, mais toute lecture du code
/// devient un exercice de vigilance : lequel des deux ce `using` désigne-t-il ?
///
/// C'est exactement la situation que `[HbaEvent]` rend inoffensive. Le nom de
/// classe sert la lisibilité du code C# ; l'attribut porte le contrat public. Les
/// deux peuvent diverger sans conséquence, alors que la dérivation depuis le nom
/// de classe aurait forcé l'un des deux à céder.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[HbaEvent("user", "profile", "updated", Version = 1, AggregateType = "UserProfile")]
public sealed record UserProfileChangedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// Null signifie « pas d'avatar », pas « inchangé ». L'événement décrit un
    /// ÉTAT après changement, pas un delta : un consommateur qui ne reçoit que les
    /// champs modifiés ne peut pas se reconstruire après un rejeu partiel.
    /// </summary>
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// Une adresse a été ajoutée au carnet.
///
/// ELLE NE TRANSPORTE NI RUE NI POINT GPS.
///
/// Un événement d'intégration traverse le bus, se pose sur un topic conservé
/// plusieurs jours et se retrouve dans les journaux de chaque consommateur. Une
/// adresse postale et des coordonnées sont des données personnelles : le §19.7
/// interdit explicitement d'y placer des données personnelles inutiles. Les
/// consommateurs qui ont besoin de l'adresse la lisent par gRPC, avec les
/// contrôles d'accès qui vont avec.
/// </summary>
[HbaEvent("user", "address", "created", Version = 1, AggregateType = "Address")]
public sealed record UserAddressCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required Guid AddressId { get; init; }

    /// <summary>Code commune, suffisant pour les zones de service et la tarification.</summary>
    public string? CommuneCode { get; init; }

    public required bool IsDefault { get; init; }
}

/// <summary>
/// Un appareil a été enregistré pour les notifications push.
///
/// LE JETON PUSH N'EST PAS DANS L'ÉVÉNEMENT.
///
/// Il vaut envoi de notification à qui le détient. Le placer sur un topic
/// reviendrait à le distribuer à tout consommateur présent et futur. Le service
/// de notification le lit par gRPC au moment d'émettre.
/// </summary>
[HbaEvent("user", "device", "registered", Version = 1, AggregateType = "UserDevice")]
public sealed record UserDeviceRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required Guid DeviceId { get; init; }

    /// <summary>`IOS`, `ANDROID` ou `WEB`.</summary>
    public required string Platform { get; init; }
}
