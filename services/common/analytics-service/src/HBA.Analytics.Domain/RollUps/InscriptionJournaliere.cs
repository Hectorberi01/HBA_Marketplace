namespace HBA.Analytics.Domain.RollUps;

/// <summary>Nombre d'inscriptions d'un jour, par nature.</summary>
/// <remarks>
/// UNE INSCRIPTION VENDEUR COMPTE AUSSI DANS LES ACHETEURS. Voir
/// <see cref="NatureDInscription"/> : les deux séries ne s'additionnent pas.
/// </remarks>
public sealed class InscriptionJournaliere
{
    public DateOnly Day { get; init; }

    public string Kind { get; init; } = default!;

    public int Count { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une inscription à la journée.</summary>
    public void Ajouter(DateTime instantUtc)
    {
        Count += 1;
        UpdatedAtUtc = instantUtc;
    }
}
