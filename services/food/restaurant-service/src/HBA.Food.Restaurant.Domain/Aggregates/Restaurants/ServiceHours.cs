using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Restaurants;

/// <summary>
/// Un créneau de service d'un jour de la semaine.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CES HORAIRES SONT CONTRAIGNANTS. CEUX D'UNE BOUTIQUE NE LE SONT PAS.
///
/// C'est LA différence entre HBA Food et HBAExpress, et la raison pour laquelle
/// ce type n'est pas <c>Sellers.StoreOpeningHour</c> malgré des champs
/// identiques.
///
/// Sur la marketplace, une commande passée à deux heures du matin est normale :
/// le vendeur l'expédiera le lendemain, et bloquer la vente hors horaires ferait
/// perdre les commandes du soir.
///
/// Un repas ne se prépare pas en différé. Une commande acceptée à deux heures du
/// matin dans un maquis fermé, c'est un client qui a payé, qui attend, et que
/// personne ne rappellera avant l'ouverture. Le refus doit tomber AVANT le
/// paiement.
///
/// Deux règles opposées sur des données de même forme : les fondre dans un type
/// commun aurait obligé chaque lecture à demander « suis-je un restaurant ? ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ServiceHours
{
    private ServiceHours(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        Day = day;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public DayOfWeek Day { get; }
    public TimeOnly OpensAt { get; }
    public TimeOnly ClosesAt { get; }

    public static Result<ServiceHours> Create(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        if (closesAt <= opensAt)
        {
            // PAS DE CRÉNEAU À CHEVAL SUR MINUIT — et la restauration est
            // précisément le métier où l'on serait tenté de l'autoriser.
            //
            // « 19 h – 02 h » demanderait de savoir à quel jour appartient la
            // seconde moitié. Un maquis ouvert le vendredi soir jusqu'à deux
            // heures se saisit en deux créneaux : vendredi 19 h – 23 h 59, samedi
            // 00 h – 02 h. C'est plus verbeux, et c'est sans ambiguïté à la
            // lecture — laquelle décide si l'on accepte une commande.
            return Error.Validation(
                "food.hours.invalid",
                "L'heure de fermeture doit être postérieure à l'heure d'ouverture. "
                + "Un service qui passe minuit se saisit en deux créneaux, sur deux jours.");
        }

        return new ServiceHours(day, opensAt, closesAt);
    }

    /// <summary>Ce créneau couvre-t-il cet instant ?</summary>
    public bool Covers(DayOfWeek day, TimeOnly time)
        => Day == day && time >= OpensAt && time < ClosesAt;

    /// <summary>Ce créneau en recouvre-t-il un autre du même jour ?</summary>
    public bool Overlaps(ServiceHours other)
        => Day == other.Day && OpensAt < other.ClosesAt && other.OpensAt < ClosesAt;
}
