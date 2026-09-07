namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce qu'un vendeur a vendu, un jour donné, dans une devise et pour une nature de
/// commande.
/// </summary>
public sealed class VenteJournaliereVendeur
{
    public Guid SellerId { get; init; }

    public DateOnly Day { get; init; }

    public string Currency { get; init; } = default!;

    public string Kind { get; init; } = default!;

    /// <summary>Nombre de commandes confirmées où ce vendeur a une part.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Nombre d'articles vendus, toutes lignes confondues.</summary>
    public int ItemsCount { get; set; }

    /// <summary>Somme des parts vendeur. Voir l'encadré : ce n'est pas le gain net.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Dernière écriture. Sert au diagnostic d'un rattrapage, pas au calcul.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une part de commande à la journée.</summary>
    public void Ajouter(int articles, decimal montant, DateTime instantUtc)
    {
        OrdersCount += 1;
        ItemsCount += articles;
        Revenue += montant;
        UpdatedAtUtc = instantUtc;
    }
}
