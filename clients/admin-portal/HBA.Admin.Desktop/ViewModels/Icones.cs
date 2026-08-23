using Avalonia.Media;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les silhouettes du panneau latéral.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// TRACÉS ÉCRITS À LA MAIN, PAS UNE POLICE D'ICÔNES.
///
/// Une police ajouterait un paquet, une licence et quelques centaines de
/// kilo-octets. Ces tracés tiennent en une ligne chacun, sur la grille de 24
/// unités qu'emploient les bibliothèques usuelles — un dessin emprunté ailleurs
/// s'y insère donc sans remise à l'échelle.
///
/// ILS SONT PLEINS, PAS FILAIRES, ET C'EST UNE CONTRAINTE DU MOYEN.
///
/// `PathIcon` remplit son tracé ; il ne sait pas le contourner d'un trait
/// d'épaisseur donnée. Un dessin filaire demanderait un `Path` avec `Stroke`,
/// donc un gabarit par icône. À vingt unités, la silhouette pleine se lit aussi
/// bien — c'est le parti de la plupart des jeux d'icônes de barre latérale.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class Icones
{
    private static Geometry G(string donnees) => Geometry.Parse(donnees);

    // ── Vue d'ensemble ───────────────────────────────────────────────────────
    public static readonly Geometry TableauDeBord =
        G("M3 3H10V10H3Z M14 3H21V8H14Z M14 12H21V21H14Z M3 14H10V21H3Z");

    // ── Opérations ───────────────────────────────────────────────────────────
    public static readonly Geometry Boutique =
        G("M4 3H20L22 8H2Z M4 10H20V21H14V15H10V21H4Z");

    public static readonly Geometry Panier =
        G("M2 3H5L7 14H19L21 6H8L8.4 8H18.4L17.3 12H8.6L6.6 3.5 6.4 3H2Z M8 17A2 2 0 1 0 8 21A2 2 0 1 0 8 17Z M17 17A2 2 0 1 0 17 21A2 2 0 1 0 17 17Z");

    public static readonly Geometry Carte =
        G("M2 5H22V9H2Z M2 11H22V19H2Z M4 15H9V17H4Z");

    public static readonly Geometry Retour =
        G("M4 5V11H10L7.5 8.5A6 6 0 1 1 6 15H4A8 8 0 1 0 9 7L11 5Z");

    public static readonly Geometry Billet =
        G("M2 6H22V18H2Z M12 9A3 3 0 1 0 12 15A3 3 0 1 0 12 9Z");

    public static readonly Geometry Livreur =
        G("M12 2A3 3 0 1 0 12 8A3 3 0 1 0 12 2Z M5 22V17A7 7 0 0 1 19 17V22H16V17A4 4 0 0 0 8 17V22Z");

    public static readonly Geometry Tarif =
        G("M3 4H21V8H3Z M3 10H14V14H3Z M3 16H18V20H3Z");

    // ── Gestion ──────────────────────────────────────────────────────────────
    public static readonly Geometry Utilisateurs =
        G("M9 3A3.5 3.5 0 1 0 9 10A3.5 3.5 0 1 0 9 3Z M2 21V18A6 6 0 0 1 16 18V21Z M17 10A3 3 0 1 0 17 4A3 3 0 1 0 17 10Z M18 12A5 5 0 0 1 22 17V21H18V18A7 7 0 0 0 16.5 12.2Z");

    public static readonly Geometry Cle =
        G("M14 3A6 6 0 1 0 14 15A6 6 0 0 0 19.5 11H22V9H19.5A6 6 0 0 0 14 3Z M11 7A2 2 0 1 0 11 11A2 2 0 1 0 11 7Z");

    public static readonly Geometry Arborescence =
        G("M3 3H9V8H3Z M13 3H21V7H13Z M13 10H21V14H13Z M13 17H21V21H13Z M6 8V19H11V17H8V15H11V13H8V10H11V8Z");

    public static readonly Geometry Etiquette =
        G("M3 3H12L21 12L12 21L3 12Z M7 6A1.6 1.6 0 1 0 7 9.2A1.6 1.6 0 1 0 7 6Z");

    public static readonly Geometry Colis =
        G("M12 2L21 7V17L12 22L3 17V7Z M12 4.5L5.5 8L12 11.5L18.5 8Z");

    // ── Finance ──────────────────────────────────────────────────────────────
    public static readonly Geometry Portefeuille =
        G("M3 6H19V9H21V19H3Z M16 12H21V16H16Z");

    public static readonly Geometry Facture =
        G("M5 2H15L19 6V22H5Z M7 9H17V11H7Z M7 13H17V15H7Z M7 17H13V19H7Z");

    public static readonly Geometry Balance =
        G("M11 2H13V5H20V7H13V20H18V22H6V20H11V7H4V5H11Z M4 9L1 15H7Z M20 9L17 15H23Z");

    // ── Plateforme ───────────────────────────────────────────────────────────
    public static readonly Geometry Megaphone =
        G("M3 9H7L18 4V20L7 15H5V20H3Z M20 9H23V15H20Z");

    public static readonly Geometry Entrepot =
        G("M2 8L12 3L22 8V21H2Z M6 12H18V14H6Z M6 16H18V18H6Z");

    public static readonly Geometry Taxe =
        G("M5 2H19V22L17 20L15 22L13 20L11 22L9 20L7 22L5 20Z M8 7H16V9H8Z M8 12H16V14H8Z");

    // ── Contenu et supervision ───────────────────────────────────────────────
    public static readonly Geometry Banniere =
        G("M2 4H22V18H2Z M6 14L10 9L13 13L16 11L19 14Z M17 6A1.6 1.6 0 1 0 17 9.2A1.6 1.6 0 1 0 17 6Z");

    public static readonly Geometry Bouclier =
        G("M12 2L21 5V11C21 16 17 20 12 22C7 20 3 16 3 11V5Z M11 7H13V13H11Z M11 15H13V17H11Z");

    public static readonly Geometry Cloche =
        G("M12 2A6 6 0 0 0 6 8V13L4 17H20L18 13V8A6 6 0 0 0 12 2Z M9 19H15A3 3 0 0 1 9 19Z");

    public static readonly Geometry Empreinte =
        G("M12 2A9 9 0 0 0 3 11V15H5V11A7 7 0 0 1 19 11V22H21V11A9 9 0 0 0 12 2Z M12 6A5 5 0 0 0 7 11V22H9V11A3 3 0 0 1 15 11V22H17V11A5 5 0 0 0 12 6Z M11 11H13V22H11Z");

    public static readonly Geometry Enveloppe =
        G("M2 5H22V7L12 13L2 7Z M2 9.5L12 15.5L22 9.5V19H2Z");

    public static readonly Geometry Histogramme =
        G("M3 20H21V22H3Z M5 12H8V19H5Z M10.5 7H13.5V19H10.5Z M16 3H19V19H16Z");

    public static readonly Geometry Pouls =
        G("M2 12H6L9 4L14 20L17 12H22V14H18.5L14.5 22L9.5 6L7.5 14H2Z");

    // ── Chrome ───────────────────────────────────────────────────────────────
    /// <summary>Une étoile pleine — les avis et leur note.</summary>
    /// <remarks>
    /// CINQ BRANCHES TRACÉES À LA MAIN, COMME LES VINGT-SEPT AUTRES.
    ///
    /// Le projet n'embarque aucune police d'icônes ni aucun jeu de ressources :
    /// une police ajouterait un fichier à charger et une dépendance de plus pour
    /// dessiner trente formes. Les points ci-dessous sont ceux d'une étoile
    /// inscrite dans le carré de 24 unités que partagent toutes les autres.
    /// </remarks>
    public static readonly Geometry Etoile =
        G("M12 2.5L14.9 8.4L21.5 9.4L16.7 14L17.9 20.5L12 17.4L6.1 20.5L7.3 14L2.5 9.4L9.1 8.4Z");

    // ÉTINCELLES : LA MISE EN AVANT, PAS LA NOTE.
    //
    // `Etoile` sert déjà la modération des avis — une note donnée par un
    // acheteur. Ce que la plateforme choisit de pousser est un geste distinct,
    // et deux entrées de menu portant le même dessin se confondraient d'un
    // coup d'oeil. Grande étincelle à quatre branches, petite en appui.
    public static readonly Geometry Etincelles =
        G("M10 2L12 7.5L17.5 9.5L12 11.5L10 17L8 11.5L2.5 9.5L8 7.5Z "
          + "M17.5 14L18.6 17L21.5 18L18.6 19L17.5 22L16.4 19L13.5 18L16.4 17Z");

    public static readonly Geometry Marque =
        G("M12 2L21 5V11C21 16 17 20 12 22C7 20 3 16 3 11V5Z M10.8 14.6L8 11.8L9.4 10.4L10.8 11.8L14.6 8L16 9.4Z");

    public static readonly Geometry ChevronGauche =
        G("M15 5L17 7L12 12L17 17L15 19L8 12Z");

    public static readonly Geometry ChevronDroit =
        G("M9 5L7 7L12 12L7 17L9 19L16 12Z");

    public static readonly Geometry Sortie =
        G("M4 3H12V5H6V19H12V21H4Z M14 7L20 12L14 17V13H9V11H14Z");
}
