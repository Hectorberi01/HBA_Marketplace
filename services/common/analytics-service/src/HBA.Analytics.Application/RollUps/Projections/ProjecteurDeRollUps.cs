using HBA.Analytics.Domain.RollUps;

namespace HBA.Analytics.Application.RollUps.Projections;

/// <summary>La part d'un vendeur dans une commande, en primitifs.</summary>
/// <remarks>
/// C'EST UNE COPIE DE FORME, PAS UN CONTRAT DUPLIQUÉ.
///
/// `OrderSellerShare` est déclaré par order-service, qui en est propriétaire.
/// Le référencer ici ferait entrer `HBA.Order.Contracts` dans la couche
/// Application — voir l'encadré du `.csproj`. Ce type-ci ne voyage sur aucun
/// bus, ne porte pas de `[HbaEvent]`, et ne sera jamais désérialisé : il ne peut
/// donc pas produire l'ambiguïté de résolution que la règle « un fait, un
/// contrat, un propriétaire » existe pour empêcher.
/// </remarks>
public sealed record PartDeVendeur(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>
/// Applique un fait à ses roll-ups. C'est le seul endroit où un événement devient
/// un chiffre.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL N'ENREGISTRE PAS, ET C'EST LA CONDITION DE SA CORRECTION.
///
/// `SaveChangesAsync` est appelé par le socle, à la fin du gestionnaire, dans la
/// même transaction que la trace d'inbox. Enregistrer ici en ouvrirait une
/// seconde : au redémarrage entre les deux, la ligne serait comptée SANS sa
/// trace, donc recomptée au rejeu. Voir l'encadré d'`IRegistreDesRollUps`.
///
/// UNE COMMANDE DE REPAS N'A PAS DE PART VENDEUR, ET ELLE COMPTE QUAND MÊME.
///
/// `BuildSellerShares` écarte délibérément les repas : `SellerShares` est vide.
/// Boucler sur les parts et s'arrêter là ferait disparaître toute la
/// restauration des chiffres de la plateforme. La ligne plateforme est donc
/// écrite AVANT la boucle, et sans condition.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

    /// <summary>Un compte s'est inscrit.</summary>
    public async Task InscriptionAsync(
        DateTime survenuLeUtc, string nature, CancellationToken cancellationToken = default)
    {
        var ligne = await _registre.ObtenirOuCreerInscriptionAsync(
            JourneeAnalytique.De(survenuLeUtc), nature, cancellationToken);

        ligne.Ajouter(DateTime.UtcNow);
    }
}
