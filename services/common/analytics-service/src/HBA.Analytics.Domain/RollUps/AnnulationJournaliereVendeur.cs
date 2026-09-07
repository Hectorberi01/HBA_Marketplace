namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce qu'un vendeur a perdu en annulations, un jour donné, dans une devise.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PAS DE `Kind` DANS CETTE CLÉ, CONTRAIREMENT AUX VENTES — ET C'EST UNE LIMITE
///    DU CONTRAT, PAS UN CHOIX.
///
/// `OrderConfirmed` porte `Kind` ; `OrderCancelled` ne le porte pas. Le lot 2 lui
/// a ajouté `SellerShares` et `Currency`, pas la nature. Cette table ne peut donc
/// pas distinguer marchandise et repas, et un graphe « annulations par nature »
/// n'est pas constructible aujourd'hui.
///
/// UNE ANNULATION SANS PART VENDEUR NE PRODUIT AUCUNE LIGNE ICI, et deux cas
/// distincts s'y confondent :
///
///   • une commande de REPAS — `BuildSellerShares` écarte les lignes de
///     restauration, la liste est VIDE. Il n'y a effectivement aucun vendeur à
///     débiter ;
///   • un message d'AVANT le lot 2 — la liste est `null`. Là, il y avait un
///     vendeur, et on ne sait pas lequel.
///
/// Le projecteur les traite pareil parce qu'il n'a rien à écrire dans les deux
/// cas. La conséquence à connaître : IL N'EXISTE AUCUNE SÉRIE D'ANNULATIONS AU
/// NIVEAU DE LA PLATEFORME dans ce lot. La construire demanderait `Kind` sur
/// l'événement — un quatrième champ optionnel, à décider.
///
/// LES TROIS ANNULATIONS NE PÈSENT PAS PAREIL, ET RIEN ICI NE LES SÉPARE.
///
/// Une commande annulée AVANT paiement n'a jamais été une vente ; une commande
/// soldée par l'exploitation après arbitrage en était une. Les deux atterrissent
/// dans la même ligne. `Reason` est le seul champ qui les distingue, et c'est du
/// texte libre — le ranger en colonne supposerait un vocabulaire fermé que
/// l'événement n'a pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AnnulationJournaliereVendeur
{
    public Guid SellerId { get; init; }

    public DateOnly Day { get; init; }

    public string Currency { get; init; } = default!;

    /// <summary>Nombre de commandes annulées où ce vendeur avait une part.</summary>
    public int OrdersCount { get; set; }

    /// <summary>
    /// Somme des parts perdues.
    /// </summary>
    /// <remarks>
    /// C'EST LE MONTANT DE LA COMMANDE, PAS CELUI QUI SERA REMBOURSÉ. Les deux
    /// coïncident tant qu'une annulation est totale ; un remboursement partiel,
    /// s'il arrive, sera un autre fait avec son propre événement.
    /// </remarks>
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
