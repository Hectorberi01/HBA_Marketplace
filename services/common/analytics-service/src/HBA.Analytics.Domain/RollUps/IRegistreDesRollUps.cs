namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// L'accès aux trois tables de roll-up : les projeteurs y accumulent, les
/// requêtes de lecture y puisent.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉCRITURES SONT DES « OBTENIR OU CRÉER », ET N'ENREGISTRENT PAS.
///
/// Aucune de ces méthodes n'appelle `SaveChangesAsync`. C'est la même règle que
/// l'inbox : la trace de consommation et l'effet métier doivent être écrits par
/// la MÊME transaction. Un enregistrement ici en ouvrirait une seconde, et la
/// fenêtre entre les deux est exactement le trou que l'inbox ferme — un
/// redémarrage au mauvais moment laisserait la ligne comptée sans sa trace, donc
/// recomptée au rejeu.
///
/// UNE LIGNE DE ROLL-UP N'EST PAS IDEMPOTENTE PAR ELLE-MÊME.
///
/// « Ajouter une commande » est une INCRÉMENTATION : la rejouer double le
/// chiffre. Rien dans ces méthodes ne peut s'en protéger — elles ne voient pas
/// l'identifiant de l'événement. La garde est l'inbox, et elle seule. C'est
/// pourquoi ce service ne doit JAMAIS être déployé sans sa table
/// `consumer_inbox` : sans elle, `IConsumerInbox` n'est pas résolu, le socle
/// consomme quand même avec un simple avertissement, et le premier rebalancement
/// de partition gonfle des chiffres que personne ne saura corriger.
///
/// LA LECTURE REND LES LIGNES BRUTES, PAS DES SÉRIES REMPLIES.
///
/// Un jour sans vente n'a pas de ligne. C'est la couche Application qui décide
/// s'il faut un zéro ou un trou dans la courbe — deux réponses légitimes selon
/// le graphe, et un choix pris ici les imposerait aux deux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IRegistreDesRollUps
{
    Task<VenteJournaliereVendeur> ObtenirOuCreerVenteAsync(
        Guid sellerId, DateOnly jour, string devise, string nature, CancellationToken cancellationToken = default);

    Task<ActiviteJournalierePlateforme> ObtenirOuCreerActiviteAsync(
        DateOnly jour, string nature, string devise, CancellationToken cancellationToken = default);

    Task<InscriptionJournaliere> ObtenirOuCreerInscriptionAsync(
        DateOnly jour, string nature, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VenteJournaliereVendeur>> LireVentesAsync(
        Guid sellerId, DateOnly du, DateOnly au, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiviteJournalierePlateforme>> LireActiviteAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InscriptionJournaliere>> LireInscriptionsAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default);

    // ═════════════════════════════════════════════════════════════════════════
    // LOT 2 — LES DEUX FAMILLES QUE LES CHAMPS OPTIONNELS ONT DEBLOQUEES.
    //
    // Elles suivent exactement les memes regles que les trois premieres :
    // « obtenir ou creer » n'enregistre pas, l'incrementation n'est pas
    // idempotente par elle-meme, et la lecture rend les lignes brutes sans
    // remplir les journees vides.
    // ═════════════════════════════════════════════════════════════════════════

    Task<AnnulationJournaliereVendeur> ObtenirOuCreerAnnulationAsync(
        Guid sellerId, DateOnly jour, string devise, CancellationToken cancellationToken = default);

    Task<PaiementJournalier> ObtenirOuCreerPaiementAsync(
        DateOnly jour, string fournisseur, string devise, string issue,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnulationJournaliereVendeur>> LireAnnulationsAsync(
        Guid sellerId, DateOnly du, DateOnly au, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaiementJournalier>> LirePaiementsAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default);
}
