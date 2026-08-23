using HBA.Catalog.Domain.Products;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TROUVER UN SLUG LIBRE — EN UNE REQUÊTE, ET À UN SEUL ENDROIT.
///
/// CETTE RECHERCHE COÛTAIT JUSQU'À CENT REQUÊTES PAR CRÉATION DE PRODUIT (§11).
///
/// Elle testait « base », puis « base-2 », « base-3 »… jusqu'à « base-100 », un
/// aller-retour par tentative. Sur un catalogue où les homonymes sont la norme —
/// « robe rouge », « chargeur USB » — un vendeur qui publie sa centième variante
/// déclenchait cent requêtes pour choisir un suffixe.
///
/// ET LA BOUCLE ÉTAIT ÉCRITE DEUX FOIS : dans `CreateProductCommandHandler` et
/// dans `CreateProductWithImagesCommandHandler`, au caractère près. Corriger l'une
/// aurait laissé l'autre — et la seconde était la moins relue des deux.
///
/// LE CHEMIN COURANT RESTE À UNE SEULE REQUÊTE.
///
/// La grande majorité des créations trouvent leur slug de base libre. On le teste
/// donc SEUL d'abord, et l'on ne construit la liste des cent candidats que si ce
/// premier essai échoue. Envoyer cent paramètres à chaque création pour servir le
/// cas rare aurait remplacé un défaut par un autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class SlugLibre
{
    /// <summary>
    /// Le nombre de suffixes numériques tentés avant de basculer sur l'aléatoire.
    /// </summary>
    /// <remarks>
    /// Repris tel quel de l'ancienne boucle (`for n = 2; n &lt;= 100`), pour que ce
    /// lot ne change QUE le nombre de requêtes. Le comportement observable — quel
    /// slug est choisi pour un nom donné — est identique.
    /// </remarks>
    private const int DernierSuffixe = 100;

    /// <summary>
    /// Rend un slug libre dérivé du nom, ou l'erreur de validation du nom.
    /// </summary>
    /// <remarks>
    /// « LIBRE » VEUT DIRE « QU'AUCUNE RÉVISION PUBLIÉE N'OCCUPE ». Un slug
    /// réservé par un brouillon que personne ne publiera jamais ne doit pas être
    /// perdu pour tout le monde — voir `ProductRepository.SlugExistsAsync`.
    ///
    /// ET IL N'EST PAS RÉSERVÉ POUR AUTANT. Entre cette lecture et la
    /// publication, un autre vendeur peut prendre le même. C'était déjà vrai avant,
    /// et l'index unique `ux_product_revisions_published_slug` reste le seul
    /// arbitre : la seconde publication échoue. Ce qui change ici est le coût de la
    /// recherche, pas sa garantie.
    /// </remarks>
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
            // Un slug de base assez long pour qu'ajouter « -2 » dépasse la limite
            // doit être refusé, pas contourné en silence par le suffixe aléatoire
            // qui serait plus long encore.
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
