namespace HBA.Admin.Desktop.ViewModels;

/// <summary>L'écran affiché à la place d'une section qui n'existe pas encore.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// IL DIT CE QUI MANQUE, ET CE QUE CELA COÛTE — PAS « BIENTÔT DISPONIBLE ».
///
/// C'est le même parti que `NotMigratedScreen` de l'application vendeur, poussé
/// d'un cran : là-bas l'écran s'adresse à un vendeur, ici à un administrateur de
/// la plateforme, qui décide des priorités. Lui dire « bientôt » ne l'aide pas ;
/// lui dire « l'amont est prêt, il reste l'écran » ou « aucun service n'existe »
/// lui donne exactement ce qu'il faut pour arbitrer.
///
/// AUCUN BOUTON « RÉESSAYER ». Il n'y a rien à réessayer : ce n'est pas une
/// panne, c'est une absence. En proposer un ferait croire à un incident
/// passager, et on y reviendrait dix fois.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PageAVenirViewModel : ViewModelBase
{
    public PageAVenirViewModel(SectionAdmin section, string detail)
    {
        Titre = section.Libelle;
        Detail = detail;

        Resume = section.Etat == EtatSection.AEcrire
            ? "L'amont existe et la passerelle le relaie déjà. Il reste l'écran à écrire."
            : "Aucun service ne rend cette donnée aujourd'hui.";

        Consequence = section.Etat == EtatSection.AEcrire
            ? "C'est un lot d'interface : les routes sont là, testées, et appelables."
            : "C'est une extraction de service, pas un écran — l'ordre de grandeur n'est pas le même.";
    }

    public string Titre { get; }

    /// <summary>Une phrase : ce qui manque.</summary>
    public string Resume { get; }

    /// <summary>Une phrase : ce que cela implique pour la suite.</summary>
    public string Consequence { get; }

    /// <summary>Les routes concernées, ou ce qu'il faudrait construire.</summary>
    public string Detail { get; }
}
