using HBA.Catalog.Domain.Products;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Products;

/// <summary>TROUVER UN SLUG LIBRE — EN UNE REQUÊTE, ET À UN SEUL ENDROIT.</summary>
internal static class SlugLibre
{
    /// <summary>
    /// Le nombre de suffixes numériques tentés avant de basculer sur l'aléatoire.
    /// </summary>
    private const int DernierSuffixe = 100;

    /// <summary>Rend un slug libre dérivé du nom, ou l'erreur de validation du nom.</summary>
    public static async Task<Result<Slug>> ResoudreAsync(
        IProductRepository produits, string nom, CancellationToken cancellationToken)
    {
        var baseResult = Slug.Create(nom);
        if (baseResult.IsFailure)
        {
            return baseResult;
        }

        var baseValue = baseResult.Value.Value;

        // Chemin courant : une requête, et c'est fini.
        if (!await produits.SlugExistsAsync(baseValue, cancellationToken))
        {
            return baseResult.Value;
        }

        // Le nom est pris. On demande d'un coup lesquels des cent suffixes le sont
        // aussi, au lieu de les essayer un par un.
        var candidats = new List<Slug>(DernierSuffixe - 1);
        for (var n = 2; n <= DernierSuffixe; n++)
        {
            var candidat = Slug.Create($"{baseValue}-{n}");

            // ON REND L'ERREUR, ON NE L'IGNORE PAS — comme l'ancienne boucle.
            if (candidat.IsFailure)
            {
                return candidat;
            }

            candidats.Add(candidat.Value);
        }

        var pris = (await produits.ListTakenSlugsAsync(candidats, cancellationToken)).ToHashSet();

        foreach (var candidat in candidats)
        {
            if (!pris.Contains(candidat))
            {
                return candidat;
            }
        }

        // Cent homonymes publiés : suffixe aléatoire, quasi impossible à recroiser.
        var suffixe = $"{baseValue}-{Guid.NewGuid():N}";
        return Slug.Create(suffixe[..Math.Min(suffixe.Length, 200)]);
    }
}
