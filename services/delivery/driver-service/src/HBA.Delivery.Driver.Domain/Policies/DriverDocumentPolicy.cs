using HBA.Delivery.Driver.Domain.Enums;

namespace HBA.Delivery.Driver.Domain.Policies;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'IL FAUT AVOIR DÉPOSÉ POUR ÊTRE REGARDÉ.
///
/// AVANT CE LOT, IL N'Y AVAIT AUCUNE PIÈCE : `DriverStore` naissait avec un
/// livreur déjà « VERIFIED », et rien dans le service ne savait ce qu'était une
/// pièce justificative. « Vérifié » ne voulait donc rien dire.
///
/// CE QUE CETTE LISTE NE COUVRE PAS.
///
/// Elle est la MÊME pour tous les véhicules. Un livreur à vélo doit donc déposer
/// un permis de conduire, ce qui est absurde ; un livreur en camionnette devrait
/// déposer une assurance marchandises, ce qui n'est pas demandé. Faire dépendre la
/// liste du véhicule est la bonne forme, mais elle suppose de savoir quel véhicule
/// est déclaré AU MOMENT où l'on juge la complétude — donc de réordonner
/// l'inscription. Ce n'est pas fait ici, et c'est un choix, pas un oubli.
///
/// ET SURTOUT : PERSONNE NE LIT CES PIÈCES. `Verify` les approuve toutes d'un
/// bloc sur décision de l'exploitation. Le contrôle posé est « le dossier est
/// complet », jamais « les pièces sont authentiques ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
