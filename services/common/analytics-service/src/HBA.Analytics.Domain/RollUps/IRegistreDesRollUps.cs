namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// L'accès aux trois tables de roll-up : les projeteurs y accumulent, les requêtes
/// de lecture y puisent.
/// </summary>
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

    // LOT 2 — LES DEUX FAMILLES QUE LES CHAMPS OPTIONNELS ONT DEBLOQUEES.

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
