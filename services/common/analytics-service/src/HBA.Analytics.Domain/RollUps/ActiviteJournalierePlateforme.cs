namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce que la plateforme a vendu, un jour donné, par nature de commande et par
/// devise.
/// </summary>
public sealed class ActiviteJournalierePlateforme
{
    public DateOnly Day { get; init; }

    public string Kind { get; init; } = default!;

    public string Currency { get; init; } = default!;

    /// <summary>Commandes confirmées. Exact, quelle que soit la nature.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Articles vendus. Vaut zéro pour une commande de repas — voir l'encadré.</summary>
    public int ItemsCount { get; set; }

    /// <summary>Volume marchand : somme des parts vendeur.</summary>
    public decimal Gmv { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une commande confirmée à la journée.</summary>
    public void Ajouter(int articles, decimal volume, DateTime instantUtc)
    {
        OrdersCount += 1;
        ItemsCount += articles;
        Gmv += volume;
        UpdatedAtUtc = instantUtc;
    }
}
