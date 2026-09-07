using HBA.Shared.IntegrationEvents;

namespace HBA.Users.Contracts.IntegrationEvents;

/// <summary>LES TROIS ÉVÉNEMENTS DU §10.2, ET LES PREMIERS À PORTER `[HbaEvent]`.</summary>
[HbaEvent("user", "profile", "updated", Version = 1, AggregateType = "UserProfile")]
public sealed record UserProfileChangedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Null signifie « pas d'avatar », pas « inchangé ».</summary>
    public string? AvatarUrl { get; init; }
}

/// <summary>Une adresse a été ajoutée au carnet.</summary>
[HbaEvent("user", "address", "created", Version = 1, AggregateType = "Address")]
public sealed record UserAddressCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required Guid AddressId { get; init; }

    /// <summary>Code commune, suffisant pour les zones de service et la tarification.</summary>
    public string? CommuneCode { get; init; }

    public required bool IsDefault { get; init; }
}

/// <summary>Un appareil a été enregistré pour les notifications push.</summary>
[HbaEvent("user", "device", "registered", Version = 1, AggregateType = "UserDevice")]
public sealed record UserDeviceRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public required Guid DeviceId { get; init; }

    /// <summary>`IOS`, `ANDROID` ou `WEB`.</summary>
    public required string Platform { get; init; }
}
