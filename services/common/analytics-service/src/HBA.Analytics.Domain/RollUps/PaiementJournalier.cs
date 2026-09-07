namespace HBA.Analytics.Domain.RollUps;

/// <summary>Les tentatives de paiement d'un jour, par prestataire, devise et issue.</summary>
public sealed class PaiementJournalier
{
    public DateOnly Day { get; init; }

    /// <summary>Le prestataire, en minuscules.</summary>
    public string Provider { get; init; } = default!;

    /// <summary>La devise. « XXX » quand le message ne la portait pas.</summary>
    public string Currency { get; init; } = default!;

    /// <summary>« Captured » ou « Failed ».</summary>
    public string Outcome { get; init; } = default!;

    public int Count { get; set; }

    /// <summary>Somme des montants.</summary>
    public decimal Amount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une tentative à la journée.</summary>
    public void Ajouter(decimal montant, DateTime instantUtc)
    {
        Count += 1;
        Amount += montant;
        UpdatedAtUtc = instantUtc;
    }
}
