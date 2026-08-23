namespace HBA.Food.Domain;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'HEURE LOCALE DU BÉNIN, EN UN SEUL ENDROIT.
///
/// POURQUOI CE TYPE EXISTE
///
/// La constante vivait dans <c>Restaurant</c>, seul agrégat à en avoir besoin.
/// Avec les créneaux de carte (§5 du cahier : « Menu Midi » servi 11h-15h), un
/// second agrégat doit convertir — et deux constantes séparées se seraient
/// contredites le jour où l'une aurait bougé.
///
/// CE QUE LE DÉCALAGE FAIT ET NE FAIT PAS
///
/// Les horaires sont saisis par le restaurateur dans SON heure : « j'ouvre à
/// 11 h ». Les comparer à une heure UTC décalerait tout le service d'une heure —
/// le restaurant refuserait des commandes à 11 h et en accepterait à 23 h.
///
/// UNE CONSTANTE ET NON UNE TIMEZONE, DÉLIBÉRÉMENT.
///
/// Le Bénin n'a pas d'heure d'été : le décalage est constant depuis toujours.
/// Passer par <c>TimeZoneInfo</c> ferait dépendre le calcul de la base tzdata du
/// système hôte — et le projet tourne en <c>InvariantGlobalization</c>, où cette
/// base n'est pas garantie présente. Une conversion qui échoue au démarrage d'un
/// conteneur est un incident ; une addition d'une heure n'en est pas un.
///
/// Le jour où HBA servira un pays à heure d'été, ce type devra devenir un service
/// prenant le fuseau de l'établissement — et c'est précisément parce qu'il est
/// seul que cette bascule sera faisable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class BeninTime
{
    /// <summary>UTC+1, sans heure d'été.</summary>
    public const int UtcOffsetHours = 1;

    /// <summary>L'instant UTC, vu depuis Cotonou.</summary>
    public static DateTime ToLocal(DateTime utc) => utc.AddHours(UtcOffsetHours);

    /// <summary>L'inverse, pour rendre une échéance calculée en local.</summary>
    public static DateTime ToUtc(DateTime local) => local.AddHours(-UtcOffsetHours);

    /// <summary>L'heure du jour, en local. C'est elle qu'on compare aux créneaux saisis.</summary>
    public static TimeOnly LocalTimeOfDay(DateTime utc) => TimeOnly.FromDateTime(ToLocal(utc));

    /// <summary>La date locale. Un service de 23 h appartient encore à la veille en UTC.</summary>
    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));
}
