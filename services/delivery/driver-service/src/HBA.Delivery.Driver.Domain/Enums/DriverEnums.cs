namespace HBA.Delivery.Driver.Domain.Enums;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉTAT DU DOSSIER — ET RIEN D'AUTRE.
///
/// CE N'EST PAS LA DISPONIBILITÉ DU LIVREUR, et la confusion entre les deux est
/// exactement celle que `DeliveryDriver.cs` décrit chez delivery-service : un
/// livreur suspendu pour un motif grave qui se remet « disponible » depuis son
/// téléphone. Ici on ne décrit que ce que l'exploitation décide après avoir
/// regardé des pièces. Ce que le livreur décide de son service, lui, vit dans
/// `deliveries.drivers`.
///
/// AVANT CE LOT, CES ÉTATS ÉTAIENT DES CHAÎNES (« ACTIVE », « VERIFIED »)
/// COMPARÉES À LA MAIN dans `DriverStore.CheckEligibility`. Une faute de frappe
/// dans l'une des deux moitiés de la comparaison rendait simplement « non
/// éligible », sans erreur et sans trace.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Écarté après avoir été vérifié. Ne se lève que par l'exploitation.</summary>
    Suspended = 4
}

/// <summary>
/// Pièces qu'un livreur dépose.
///
/// AUCUNE VALEUR N'EST RETIRÉE DE CETTE ÉNUMÉRATION SANS MIGRATION : elle est
/// persistée en toutes lettres (`character varying`), et une valeur inconnue en
/// base ferait échouer la matérialisation de tout le dossier, pas seulement de la
/// pièce concernée.
/// </summary>
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
///
/// Les deux services ne partagent plus de projet de domaine depuis le lot 5.4
/// (D34) — rétablir une `ProjectReference` pour économiser six valeurs
/// reconstituerait le cycle qu'il vient de couper. Le lien passe par le nom :
/// `DriverAccountVerifiedDomainEvent` transporte le véhicule EN TEXTE, et
/// delivery-service le relit avec `Enum.TryParse`.
///
/// LE PRIX : UNE VALEUR AJOUTÉE ICI ET PAS LÀ-BAS EST SILENCIEUSE. Le
/// consommateur retombera sur `Motorcycle` par défaut, et le livreur sera dispatché
/// pour un véhicule qu'il n'a pas. Les deux listes se rapprochent à la main, comme
/// les seuils de `VehicleCapacity` (voir D34).
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
