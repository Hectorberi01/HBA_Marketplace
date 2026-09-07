using HBA.Shared.Domain.Results;

namespace HBA.Analytics.Application.RollUps.Queries;

/// <summary>
/// La période d'un graphe : bornes vérifiées une seule fois, pour les trois
/// requêtes.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PLAFOND EXISTE PARCE QUE LA RÉPONSE EST UN POINT PAR JOUR.
///
/// Sans borne, `?from=1970-01-01` demande vingt mille points — et le coût n'est
/// pas la lecture, qui reste indexée, mais la SÉRIALISATION et le rendu chez le
/// client. C'est le même défaut que `MerchantTodayDto` documente pour lui-même :
/// une lecture qui devient chère exactement chez les vendeurs qui réussissent.
///
/// 366 JOURS, ET NON 365 : une année bissextile complète doit passer, sinon
/// « l'an dernier » échoue une année sur quatre, et personne ne comprend
/// pourquoi.
///
/// CE QUE CETTE CLASSE NE FAIT PAS : borner le NOMBRE de séries. Un jour où l'on
/// rendra plusieurs devises dans une même réponse, le plafond portera sur le
/// produit jours × devises, pas sur les jours seuls.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record PeriodeDemandee(DateOnly Du, DateOnly Au)
{
    /// <summary>Nombre maximal de journées rendues en une requête.</summary>
    public const int JoursMax = 366;

    /// <summary>Fenêtre par défaut quand l'appelant ne dit rien : le mois écoulé.</summary>
    public const int JoursParDefaut = 30;

    /// <summary>Nombre de journées, bornes incluses.</summary>
    public int Jours => Au.DayNumber - Du.DayNumber + 1;

    /// <summary>
    /// Construit la période, ou dit pourquoi elle est refusée.
    /// </summary>
    /// <remarks>
    /// LES DEUX BORNES SONT OPTIONNELLES, ET LEUR ABSENCE N'EST PAS UNE ERREUR :
    /// un tableau de bord qui s'ouvre sans paramètre doit afficher quelque chose.
    /// C'est le `to` qui ancre la fenêtre par défaut — reculer depuis
    /// « aujourd'hui » quand seul `from` est donné rendrait une période dont la
    /// longueur dépend du jour où on la demande.
    /// </remarks>
    public static Result<PeriodeDemandee> Construire(DateOnly? du, DateOnly? au)
    {
        var fin = au ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var debut = du ?? fin.AddDays(-(JoursParDefaut - 1));

        if (fin < debut)
        {
            return Error.Validation(
                ErrorCodes.ValidationError,
                "La borne « to » précède la borne « from ».");
        }

        var jours = fin.DayNumber - debut.DayNumber + 1;

        if (jours > JoursMax)
        {
            return Error.Validation(
                ErrorCodes.ValidationError,
                $"La période demandée couvre {jours} journées ; le maximum est {JoursMax}.");
        }

        return new PeriodeDemandee(debut, fin);
    }

    /// <summary>Toutes les journées de la période, sans trou.</summary>
    /// <remarks>
    /// LES JOURS SANS VENTE RENDENT UN ZÉRO, PAS UN TROU.
    ///
    /// Une courbe à laquelle il manque des points relie le 3 au 7 par une droite,
    /// et donne à lire une activité continue là où il n'y en a eu aucune. Le
    /// dépôt ne stocke pas les lignes à zéro — elles n'existent que dans la
    /// réponse, et c'est ici qu'elles naissent.
    /// </remarks>
    public IEnumerable<DateOnly> Journees()
    {
        for (var jour = Du; jour <= Au; jour = jour.AddDays(1))
        {
            yield return jour;
        }
    }
}
