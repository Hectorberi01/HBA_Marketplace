using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TOUTE LECTURE D'AGRÉGAT PASSE PAR ICI, ET C'EST VITAL.
    ///
    /// <c>Product.CurrentRevision</c> LÈVE si les révisions ne sont pas chargées —
    /// délibérément : un produit sans révision courante est une donnée corrompue,
    /// pas un cas à traiter poliment. Le prix de ce choix est qu'un seul `Include`
    /// oublié fait tomber le service à l'exécution, sur une exception qui parle du
    /// dépôt et pas de la requête fautive.
    ///
    /// D'où cette méthode unique. Ajouter une requête qui charge des Product sans
    /// l'appeler est la seule façon de reproduire la panne.
    ///
    /// ELLE CHARGE TOUTES LES RÉVISIONS, PAS SEULEMENT LA COURANTE.
    ///
    /// Assumé : l'agrégat a besoin de la courante ET de la publiée, et une fiche
    /// très retravaillée en compte une dizaine — pas mille. Filtrer à deux
    /// identifiants dans un `Include` n'est pas exprimable en EF Core sans requête
    /// filtrée, et une requête filtrée qui manquerait sa cible rendrait un agrégat
    /// dont `CurrentRevision` lève : le même défaut, mais intermittent.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static IQueryable<Product> AvecAgregat(IQueryable<Product> source)
        => source
            .Include(p => p.Revisions).ThenInclude(r => r.Condition).ThenInclude(c => c.Defects)
            // SANS CET INCLUDE, LA FICHE TECHNIQUE DISPARAÎT SILENCIEUSEMENT.
            //
            // `Specifications` rendrait une collection vide, la comparaison du §6
            // conclurait à une modification critique — puisque l'ancienne empreinte
            // serait vide — et CHAQUE enregistrement ouvrirait une révision. La file
            // de validation se remplirait de fiches dont rien n'a changé.
            .Include(p => p.Revisions).ThenInclude(r => r.Specifications).ThenInclude(g => g.Items)
            .Include(p => p.Variants)
            .Include(p => p.Media);

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => await _dbContext.Products.AddAsync(product, cancellationToken);

    public void Remove(Product product)
        => _dbContext.Products.Remove(product);

    public async Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
        => await AvecAgregat(_dbContext.Products)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var ids = await OrdonnerParNomCourant(
            _dbContext.Products.Where(p => p.SellerId == sellerId), desc: false)
            .ToListAsync(cancellationToken);

        return await ChargerDansCetOrdreAsync(ids, tracked: false, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await AvecAgregat(_dbContext.Products)
            .Where(p => p.SellerId == sellerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.Products
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(take)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        return await ChargerDansCetOrdreAsync(ids, tracked: false, cancellationToken);
    }

    public async Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // LA RECHERCHE PORTE SUR LA RÉVISION COURANTE, PAS SUR LA PUBLIÉE.
            //
            // C'est une console vendeur/admin : on y cherche la fiche telle qu'on
            // l'a écrite, y compris quand elle attend validation sous un nouveau
            // nom. Chercher dans la publiée rendrait introuvable la fiche qu'on
            // vient justement de renommer — le cas le plus fréquent.
            var term = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(p => _dbContext.ProductRevisions
                .Any(r => r.Id == p.CurrentRevisionId && EF.Functions.ILike(r.Name, term)));
        }

        var statusCounts = await baseQuery
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(p => p.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        IQueryable<ProductId> ordonnee = sort switch
        {
            "name" => OrdonnerParNomCourant(filtered, desc),
            "status" => (desc ? filtered.OrderByDescending(p => p.Status) : filtered.OrderBy(p => p.Status))
                .Select(p => p.Id),
            _ => (desc ? filtered.OrderByDescending(p => p.CreatedAtUtc) : filtered.OrderBy(p => p.CreatedAtUtc))
                .Select(p => p.Id),
        };

        var ids = await ordonnee
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await ChargerDansCetOrdreAsync(ids, tracked: false, cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }

    /// <summary>
    /// CE SLUG-CI NE CONCERNE QUE CE QUI EST PUBLIÉ.
    ///
    /// L'ancienne version interrogeait `products.slug`, colonne qui n'existe plus :
    /// le slug vit sur la révision, et deux révisions du même produit le partagent.
    /// Ce qui doit rester unique est l'URL publique — donc le slug PARMI LES
    /// RÉVISIONS PUBLIÉES, exactement ce que garantit l'index partiel
    /// `ux_product_revisions_published_slug`.
    ///
    /// Vérifier plus large refuserait à un vendeur de réutiliser le nom de sa
    /// propre fiche archivée. Vérifier moins laisserait deux fiches visibles se
    /// disputer la même adresse.
    /// </summary>
    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugResult = Slug.Create(slug);
        if (slugResult.IsFailure)
        {
            return false;
        }

        var slugValue = slugResult.Value;
        return await _dbContext.ProductRevisions
            .AnyAsync(r => r.Slug == slugValue && r.Status == RevisionStatus.Published, cancellationToken);
    }

    /// <summary>
    /// Les slugs déjà occupés parmi ceux proposés — une seule requête.
    /// </summary>
    /// <remarks>
    /// `Contains` SUR UNE LISTE D'OBJETS-VALEURS EST TRADUISIBLE, `StartsWith` SUR
    /// LEUR CHAÎNE NE L'EST PAS. EF convertit chaque élément de la liste et émet un
    /// `IN (…)` ; il ne sait rien faire de `r.Slug.Value`, le convertisseur lui étant
    /// opaque. C'est pour cette raison que l'appelant fournit ses candidats au lieu
    /// de demander un préfixe.
    ///
    /// MÊME FILTRE QUE `SlugExistsAsync` : seules les révisions PUBLIÉES occupent
    /// une adresse. Un brouillon que personne ne publiera jamais ne doit pas
    /// réserver un slug pour tout le monde. Les deux méthodes doivent rester
    /// d'accord — elles répondent à la même question, l'une pour un slug, l'autre
    /// pour cent.
    /// </remarks>
    public async Task<IReadOnlyCollection<Slug>> ListTakenSlugsAsync(
        IReadOnlyCollection<Slug> candidats, CancellationToken cancellationToken = default)
    {
        if (candidats.Count == 0)
        {
            return [];
        }

        var liste = candidats.ToList();

        return await _dbContext.ProductRevisions
            .AsNoTracking()
            .Where(r => r.Status == RevisionStatus.Published && liste.Contains(r.Slug))
            .Select(r => r.Slug)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA VITRINE (§17)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Product?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugResult = Slug.Create(slug);
        if (slugResult.IsFailure)
        {
            // Un slug malformé ne vaut pas une erreur : c'est une URL qui ne
            // désigne rien. L'appelant rendra 404, comme pour un slug inconnu.
            return null;
        }

        var valeur = slugResult.Value;

        // ON PART DU PRODUIT, PAS DE LA RÉVISION.
        //
        // Une révision dépubliée garde `Status = Published` — voir l'encadré du
        // port. Chercher d'abord la révision rendrait donc les fiches retirées de
        // la vente. En partant du produit, la condition de visibilité est posée
        // une fois, au bon endroit.
        var id = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published
                        && _dbContext.ProductRevisions.Any(r =>
                            r.Id == p.PublishedRevisionId && r.Slug == valeur))
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (id == default)
        {
            return null;
        }

        return await AvecAgregat(_dbContext.Products.AsNoTracking())
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<Product> Items, int Total)> SearchPublishedAsync(
        RecherchePublique criteres, CancellationToken cancellationToken = default)
    {
        // LE FILTRE DE VISIBILITÉ EST POSÉ EN PREMIER, ET IL N'EST PAS
        //    PARAMÉTRABLE.
        //
        // Aucun argument de cette méthode ne peut l'élargir. C'est la seule
        // différence structurelle avec `ListPagedAsync`, et c'est celle qui
        // compte.
        var visibles = _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published && p.PublishedRevisionId != null)
            .SelectMany(
                p => _dbContext.ProductRevisions.Where(r => r.Id == p.PublishedRevisionId),
                (p, r) => new { Produit = p, Revision = r });

        if (criteres.SellerId is { } vendeur)
        {
            visibles = visibles.Where(x => x.Produit.SellerId == vendeur);
        }

        if (criteres.CategoryId is { } categorie)
        {
            visibles = visibles.Where(x => x.Revision.CategoryId == categorie);
        }

        if (criteres.BrandId is { } marque)
        {
            visibles = visibles.Where(x => x.Revision.BrandId == marque);
        }

        if (criteres.Condition is { } condition)
        {
            visibles = visibles.Where(x => x.Revision.Condition.Type == condition);
        }

        if (criteres.MinPrice is { } minimum)
        {
            visibles = visibles.Where(x => x.Revision.Pricing.BasePrice >= minimum);
        }

        if (criteres.MaxPrice is { } maximum)
        {
            visibles = visibles.Where(x => x.Revision.Pricing.BasePrice <= maximum);
        }

        if (!string.IsNullOrWhiteSpace(criteres.Query))
        {
            // RECHERCHE SUR LA RÉVISION PUBLIÉE, PAS SUR LA COURANTE.
            //
            // L'inverse rendrait trouvable une fiche par un nom que personne n'a
            // encore validé — et le clic mènerait à une fiche affichant l'ancien
            // nom, celui de la révision publiée.
            var terme = $"%{criteres.Query.Trim()}%";
            visibles = visibles.Where(x =>
                EF.Functions.ILike(x.Revision.Name, terme)
                || EF.Functions.ILike(x.Revision.Description, terme));
        }

        var total = await visibles.CountAsync(cancellationToken);

        var (page, pageSize) = (Math.Max(1, criteres.Page), Math.Clamp(criteres.PageSize, 1, 100));

        var ordonnee = TriPublic.Normaliser(criteres.Sort) switch
        {
            TriPublic.PrixCroissant => visibles.OrderBy(x => x.Revision.Pricing.BasePrice),
            TriPublic.PrixDecroissant => visibles.OrderByDescending(x => x.Revision.Pricing.BasePrice),
            TriPublic.Nom => visibles.OrderBy(x => x.Revision.Name),
            _ => visibles.OrderByDescending(x => x.Produit.PublishedAtUtc),
        };

        var ids = await ordonnee
            .Select(x => x.Produit.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await ChargerDansCetOrdreAsync(ids, tracked: false, cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Product> Items, int Total)> ListPendingReviewAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // ON JOINT LA RÉVISION COURANTE, ET ON FILTRE SUR SON STATUT À ELLE.
        //
        // Voir l'encadré du port : un produit publié dont la nouvelle version
        // attend validation reste `Published`. Filtrer sur le statut du produit
        // viderait la file de ses entrées les plus urgentes.
        var attente = _dbContext.Products
            .AsNoTracking()
            .SelectMany(
                p => _dbContext.ProductRevisions.Where(r =>
                    r.Id == p.CurrentRevisionId && r.Status == RevisionStatus.PendingReview),
                (p, r) => new { Produit = p, Revision = r });

        var total = await attente.CountAsync(cancellationToken);

        var ids = await attente
            .OrderBy(x => x.Revision.SubmittedAtUtc)
            .Select(x => x.Produit.Id)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(cancellationToken);

        var items = await ChargerDansCetOrdreAsync(ids, tracked: false, cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSkusByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default)
    {
        if (variantIds.Count == 0)
        {
            // Court-circuit volontaire : sans lui, EF traduirait `IN ()`, que
            // PostgreSQL refuse.
            return new Dictionary<Guid, string>();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ON PROJETTE LA PROPRIÉTÉ ENTIÈRE, ET ON LA DÉPAQUETTE CÔTÉ CLIENT.
        //
        // `Sku` et `ProductId` portent un CONVERTISSEUR DE VALEUR. Pour EF, la
        // propriété EST la colonne : il sait traduire `v.Sku == valeur`, pas
        // `v.Sku.Value` — cela lui demanderait de descendre DANS le type converti.
        // La requête n'échoue pas à la compilation : elle lève à l'exécution, et
        // ressort en 500 « Une erreur inattendue est survenue ».
        // ═════════════════════════════════════════════════════════════════════
        var lignes = await _dbContext.Products
            .AsNoTracking()
            .SelectMany(p => p.Variants)
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku })
            .ToListAsync(cancellationToken);

        return lignes.ToDictionary(l => l.Id, l => l.Sku.Value);
    }

    /// <summary>
    /// Les noms COURANTS des produits donnés.
    ///
    /// Courants et non publiés : cet écran est celui des mises en vente du vendeur,
    /// qui doit reconnaître sa fiche sous le nom qu'il vient de lui donner.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = productIds.Select(g => new ProductId(g)).ToList();

        var lignes = await _dbContext.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Name = _dbContext.ProductRevisions
                    .Where(r => r.Id == p.CurrentRevisionId)
                    .Select(r => r.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return lignes
            .Where(l => l.Name is not null)
            .ToDictionary(l => l.Id.Value, l => l.Name!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Deux temps : on ORDONNE des identifiants, puis on CHARGE les agrégats.
    //
    // POURQUOI PAS UNE SEULE REQUÊTE AVEC JOINTURE.
    //
    // Trier par le nom de la révision courante demande une jointure. Or EF Core
    // abandonne les `Include` dès qu'une requête projette autre chose que
    // l'entité racine — la jointure rendrait donc des Product SANS révisions,
    // et `CurrentRevision` lèverait à la première lecture. Le défaut n'apparaît
    // qu'au tri par nom, c'est-à-dire sur un chemin qu'aucun test de fumée ne
    // prend.
    //
    // Deux allers-retours, et une pagination qui reste faite par la base.
    // ═════════════════════════════════════════════════════════════════════════

    private IQueryable<ProductId> OrdonnerParNomCourant(IQueryable<Product> source, bool desc)
    {
        var avecNom = source.Select(p => new
        {
            p.Id,
            Nom = _dbContext.ProductRevisions
                .Where(r => r.Id == p.CurrentRevisionId)
                .Select(r => r.Name)
                .FirstOrDefault()
        });

        return (desc
                ? avecNom.OrderByDescending(x => x.Nom)
                : avecNom.OrderBy(x => x.Nom))
            .Select(x => x.Id);
    }

    private async Task<IReadOnlyList<Product>> ChargerDansCetOrdreAsync(
        IReadOnlyList<ProductId> ids, bool tracked, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<Product>();
        }

        var source = tracked ? _dbContext.Products : _dbContext.Products.AsNoTracking();

        var charges = await AvecAgregat(source)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        // L'ORDRE DE LA BASE EST PERDU PAR LE `WHERE ... IN`, ON LE REPOSE ICI.
        //
        // Sans ce reclassement, la page 2 d'une liste triée par nom rendrait les
        // bons produits dans le mauvais ordre — un défaut qu'on impute d'abord au
        // client, puisque le tri « marche » sur la page 1.
        var parId = charges.ToDictionary(p => p.Id);
        return ids.Where(parId.ContainsKey).Select(id => parId[id]).ToList();
    }
}
