using Avalonia.Media;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Ce qu'un écran d'administration peut faire aujourd'hui.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// TROIS ÉTATS, PARCE QUE « PAS ENCORE FAIT » RECOUVRE DEUX CHOSES TRÈS
/// DIFFÉRENTES.
///
/// L'application Flutter du dépôt tient déjà cette distinction dans
/// `not_migrated.dart`, et pour la même raison : le jour du rebranchement, un
/// écran dont l'amont EXISTE coûte une journée, un écran dont le SERVICE
/// n'existe pas coûte des semaines. Les confondre sous un même « à venir » rend
/// tout planning faux.
///
/// Et c'est visible à l'écran, pas seulement dans le code : l'administrateur qui
/// ouvre « Taxes » doit savoir s'il faut attendre la semaine prochaine ou
/// changer de méthode.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public enum EtatSection
{
    /// <summary>L'écran est écrit et branché.</summary>
    Pret,

    /// <summary>L'amont existe et est relayé ; il reste l'écran à écrire.</summary>
    AEcrire,

    /// <summary>Aucun service, aucune route. Une extraction, pas un écran.</summary>
    SansAmont,
}

/// <summary>Une entrée du panneau latéral.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LA NAVIGATION EST UNE LISTE DE DONNÉES, PAS UNE SUITE DE BOUTONS.
///
/// Ajouter un écran, c'est ajouter une ligne dans `Groupes`. La version
/// précédente posait un bouton par section dans le XAML : trois gestes
/// coordonnés dont l'oubli d'un seul ne casse pas la compilation — il rend un
/// bouton inerte, ou un écran inatteignable.
///
/// `Construire` EST UNE FABRIQUE, PAS UNE INSTANCE.
///
/// Garder une vue-modèle construite afficherait, au retour sur la section, les
/// chiffres du premier affichage — sur des écrans dont toute la fonction est de
/// dire ce qui attend une décision maintenant.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SectionAdmin : ViewModelBase
{
    private bool _active;

    public SectionAdmin(
        string cle,
        string libelle,
        Geometry icone,
        EtatSection etat,
        Func<ViewModelBase> construire)
    {
        Cle = cle;
        Libelle = libelle;
        Icone = icone;
        Etat = etat;
        Construire = construire;
    }

    /// <summary>Identifiant stable, jamais affiché.</summary>
    public string Cle { get; }

    /// <summary>Ce que l'administrateur lit, et l'infobulle en mode réduit.</summary>
    public string Libelle { get; }

    /// <summary>
    /// Silhouette de 24 unités de côté.
    /// </summary>
    /// <remarks>
    /// Typée `Geometry` et non `string` : une chaîne exigerait un convertisseur
    /// de liaison, et une donnée de tracé fautive ne se verrait qu'à
    /// l'affichage, sous la forme d'une icône absente.
    /// </remarks>
    public Geometry Icone { get; }

    public EtatSection Etat { get; }

    public Func<ViewModelBase> Construire { get; }

    /// <summary>Cette section est-elle celle qu'on regarde ?</summary>
    /// <remarks>
    /// PORTÉ PAR LA SECTION, ET NON PAR UNE `ListBox`.
    ///
    /// Une `ListBox` donnerait la sélection sans l'écrire — mais les en-têtes de
    /// groupe devraient alors vivre DANS des éléments sélectionnables, et
    /// prendraient la teinte de sélection avec eux. Un booléen par section coûte
    /// trois lignes et laisse les en-têtes en dehors de la liste.
    /// </remarks>
    public bool Active
    {
        get => _active;
        set => Definir(ref _active, value);
    }

    /// <summary>Une pastille, quand l'écran n'est pas encore là.</summary>
    public string Marque => Etat switch
    {
        EtatSection.AEcrire => "à écrire",
        EtatSection.SansAmont => "amont absent",
        _ => string.Empty,
    };

    public bool AUneMarque => Etat != EtatSection.Pret;
}

/// <summary>Un groupe d'entrées, tel que le panneau les sépare.</summary>
/// <param name="Titre">« OPÉRATIONS », « FINANCE »… Masqué en mode réduit.</param>
public sealed record GroupeAdmin(string Titre, IReadOnlyList<SectionAdmin> Sections);
