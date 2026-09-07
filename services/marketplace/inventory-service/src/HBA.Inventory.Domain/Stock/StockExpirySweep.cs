namespace HBA.Inventory.Domain.Stock;

/// <summary>Ce qu'un passage du balayeur a rendu à la vente sur UN article.</summary>
/// <param name="Count">Nombre de réservations passées en `Expired`.</param>
/// <param name="Quantity">Somme des quantités ainsi rendues à la vente.</param>
public readonly record struct StockExpirySweep(int Count, int Quantity)
{
    /// <summary>Rien n'a expiré sur cet article.</summary>
    public bool IsEmpty => Count == 0;
}
