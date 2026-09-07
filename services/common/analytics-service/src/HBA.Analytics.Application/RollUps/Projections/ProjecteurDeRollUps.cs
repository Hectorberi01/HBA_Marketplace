using HBA.Analytics.Domain.RollUps;

namespace HBA.Analytics.Application.RollUps.Projections;

/// <summary>La part d'un vendeur dans une commande, en primitifs.</summary>
public sealed record PartDeVendeur(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>Applique un fait à ses roll-ups.</summary>
public sealed class ProjecteurDeRollUps
{
    private readonly IRegistreDesRollUps _registre;

    public ProjecteurDeRollUps(IRegistreDesRollUps registre) => _registre = registre;

    /// <summary>Une commande confirmée entre dans les deux familles de roll-up.</summary>
    public async Task CommandeConfirmeeAsync(
        DateTime survenuLeUtc,
        string devise,
        string natureDuFil,
        IReadOnlyCollection<PartDeVendeur> parts,
        CancellationToken cancellationToken = default)
    {
        var jour = JourneeAnalytique.De(survenuLeUtc);
        var nature = NatureDeCommande.Normaliser(natureDuFil);
        var maintenant = DateTime.UtcNow;

        var articles = parts.Sum(part => part.ItemCount);
        var volume = parts.Sum(part => part.Amount);

        var plateforme = await _registre.ObtenirOuCreerActiviteAsync(jour, nature, devise, cancellationToken);
        plateforme.Ajouter(articles, volume, maintenant);

        foreach (var part in parts)
        {
            var vendeur = await _registre.ObtenirOuCreerVenteAsync(
                part.SellerId, jour, devise, nature, cancellationToken);

            vendeur.Ajouter(part.ItemCount, part.Amount, maintenant);
        }
    }

    /// <summary>Une commande annulée débite les vendeurs qui y avaient une part.</summary>
    public async Task CommandeAnnuleeAsync(
        DateTime survenuLeUtc,
        string? devise,
        IReadOnlyCollection<PartDeVendeur>? parts,
        CancellationToken cancellationToken = default)
    {
        if (parts is null || parts.Count == 0)
        {
            return;
        }

        var jour = JourneeAnalytique.De(survenuLeUtc);
        var deviseRangee = FournisseurDePaiement.NormaliserLaDevise(devise);
        var maintenant = DateTime.UtcNow;

        foreach (var part in parts)
        {
            var ligne = await _registre.ObtenirOuCreerAnnulationAsync(
                part.SellerId, jour, deviseRangee, cancellationToken);

            ligne.Ajouter(part.Amount, maintenant);
        }
    }

    /// <summary>Une tentative de paiement a rendu son verdict.</summary>
    public async Task PaiementAsync(
        DateTime survenuLeUtc,
        string issue,
        string? fournisseur,
        decimal? montant,
        string? devise,
        CancellationToken cancellationToken = default)
    {
        var ligne = await _registre.ObtenirOuCreerPaiementAsync(
            JourneeAnalytique.De(survenuLeUtc),
            FournisseurDePaiement.Normaliser(fournisseur),
            FournisseurDePaiement.NormaliserLaDevise(devise),
            issue,
            cancellationToken);

        ligne.Ajouter(montant ?? 0m, DateTime.UtcNow);
    }

    /// <summary>Un compte s'est inscrit.</summary>
    public async Task InscriptionAsync(
        DateTime survenuLeUtc, string nature, CancellationToken cancellationToken = default)
    {
        var ligne = await _registre.ObtenirOuCreerInscriptionAsync(
            JourneeAnalytique.De(survenuLeUtc), nature, cancellationToken);

        ligne.Ajouter(DateTime.UtcNow);
    }
}
