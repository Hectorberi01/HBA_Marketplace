namespace HBA.Delivery.Driver.Domain.Enums;

/// <summary>L'ÉTAT DU DOSSIER — ET RIEN D'AUTRE.</summary>
public enum DriverVerificationStatus
{
    /// <summary>Inscrit ; les pièces obligatoires ne sont pas toutes déposées.</summary>
    PendingDocuments = 0,

    /// <summary>Dossier complet, en attente d'un regard humain.</summary>
    UnderReview = 1,

    /// <summary>Autorisé à travailler.</summary>
    Verified = 2,

    /// <summary>Refusé. Le livreur peut redéposer ses pièces.</summary>
    Rejected = 3,

    /// <summary>Écarté après avoir été vérifié.</summary>
    Suspended = 4
}

/// <summary>Pièces qu'un livreur dépose.</summary>
public enum DriverDocumentType
{
    /// <summary>Pièce d'identité ou carte consulaire.</summary>
    IdentityCard = 0,

    /// <summary>Permis de conduire. Exigé pour tout véhicule motorisé.</summary>
    DrivingLicence = 1,

    /// <summary>Carte grise du véhicule.</summary>
    VehicleRegistration = 2,

    /// <summary>Attestation d'assurance.</summary>
    Insurance = 3,

    /// <summary>Photo de profil, montrée au client à l'arrivée.</summary>
    ProfilePhoto = 4
}

public enum DriverDocumentStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// CETTE ÉNUMÉRATION EN DOUBLE AVEC `VehicleType` DE delivery-service, ET C'EST
/// ASSUMÉ.
/// </summary>
public enum DriverVehicleType
{
    Motorcycle = 0,
    Bicycle = 1,
    Car = 2,
    Van = 3,
    OnFoot = 4,
    Tricycle = 5
}

public static class DriverVehicleTypes
{
    /// <summary>Ce véhicule porte-t-il une plaque d'immatriculation ?</summary>
    public static bool RequiresPlate(DriverVehicleType type) =>
        type is DriverVehicleType.Motorcycle
             or DriverVehicleType.Car
             or DriverVehicleType.Van
             or DriverVehicleType.Tricycle;
}
