namespace HBA.Inventory.Domain.Stock;

/// <summary>
/// Ce qu'un passage du balayeur a rendu à la vente sur UN article.
///
/// LE VOLUME EST LA RAISON D'ÊTRE DE CE TYPE, PAS UN ORNEMENT.
///
/// L'audit exige que le balayage d'expiration journalise le VOLUME libéré
/// (ISSUE-031). Rendre un simple booléen, ou même un nombre de lignes, ne dirait
/// pas ce qu'on cherche à savoir : combien d'UNITÉS de marchandise dormaient.
/// C'est le seul chiffre qui permette de constater, au premier démarrage après
/// correction, l'ampleur de ce que l'absence de balayeur avait immobilisé — et,
/// les jours suivants, que le balayeur travaille toujours.
/// </summary>
/// <param name="Count">Nombre de réservations passées en `Expired`.</param>
/// <param name="Quantity">Somme des quantités ainsi rendues à la vente.</param>
public readonly record struct StockExpirySweep(int Count, int Quantity)
{
    /// <summary>Rien n'a expiré sur cet article.</summary>
    public bool IsEmpty => Count == 0;
}
