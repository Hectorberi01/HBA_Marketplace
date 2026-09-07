using HBA.Delivery.Driver.Domain.Enums;

namespace HBA.Delivery.Driver.Domain.Policies;

/// <summary>CE QU'IL FAUT AVOIR DÉPOSÉ POUR ÊTRE REGARDÉ.</summary>
public static class DriverDocumentPolicy
{
    /// <summary>Pièces sans lesquelles un dossier n'est pas soumis.</summary>
    public static readonly IReadOnlyList<DriverDocumentType> Required =
    [
        DriverDocumentType.IdentityCard,
        DriverDocumentType.DrivingLicence,
        DriverDocumentType.ProfilePhoto
    ];

    /// <summary>Celles qui manquent, nommées, pour que le message soit actionnable.</summary>
    public static IReadOnlyList<string> MissingRequired(IEnumerable<DriverDocumentType> submitted)
    {
        var deposees = submitted.ToHashSet();

        return Required
            .Where(type => !deposees.Contains(type))
            .Select(type => type.ToString())
            .ToList();
    }
}
