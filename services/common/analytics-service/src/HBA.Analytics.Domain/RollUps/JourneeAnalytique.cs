namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// La conversion « instant → journée » de tout ce service, en un seul endroit.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES JOURNÉES SONT EN UTC, ET CE N'EST PAS NEUTRE POUR UN VENDEUR BÉNINOIS.
///
/// Le dépôt travaille en UTC partout — les horodatages d'événements, les colonnes
/// `timestamp with time zone`, les purges. Découper les journées autrement ici
/// ferait diverger « le chiffre d'affaires du 6 septembre » de tout le reste de
/// la plateforme, y compris des listes de commandes que le vendeur a sous les
/// yeux dans un autre onglet.
///
/// CE QUE ÇA COÛTE, ET IL FAUT LE SAVOIR. Cotonou est à UTC+1. Une vente conclue
/// à 00h30 heure locale est comptée la VEILLE. Sur une journée entière l'écart
/// est d'une heure de ventes, reporté au jour suivant — invisible sur un mois,
/// visible sur « ma journée d'hier ».
///
/// CE N'EST PAS À CE SERVICE DE TRANCHER SEUL. Le jour où l'on voudra des
/// journées locales, le geste est ici et nulle part ailleurs : c'est la raison
/// d'être de cette classe. Mais il faudra RECALCULER l'historique — une
/// conversion appliquée aux nouvelles lignes seulement produirait une série dont
/// la moitié ne se compare pas à l'autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class JourneeAnalytique
{
    /// <summary>La journée UTC qui contient cet instant.</summary>
    public static DateOnly De(DateTime instantUtc)
        => DateOnly.FromDateTime(instantUtc.Kind == DateTimeKind.Utc
            ? instantUtc
            : instantUtc.ToUniversalTime());
}
