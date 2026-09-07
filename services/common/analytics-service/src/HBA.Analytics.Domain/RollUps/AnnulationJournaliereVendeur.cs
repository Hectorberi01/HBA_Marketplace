namespace HBA.Analytics.Domain.RollUps;

/// <summary>Ce qu'un vendeur a perdu en annulations, un jour donné, dans une devise.</summary>
public sealed class AnnulationJournaliereVendeur
{
    public Guid SellerId { get; init; }

    public DateOnly Day { get; init; }

    public string Currency { get; init; } = default!;

    /// <summary>Nombre de commandes annulées où ce vendeur avait une part.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Somme des parts perdues.</summary>
    public decimal Amount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une part annulée à la journée.</summary>
    public void Ajouter(decimal montant, DateTime instantUtc)
    {
        OrdersCount += 1;
        Amount += montant;
        UpdatedAtUtc = instantUtc;
    }
}
