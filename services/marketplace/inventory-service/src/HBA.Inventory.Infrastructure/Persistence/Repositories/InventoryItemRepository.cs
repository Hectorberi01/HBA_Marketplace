using Microsoft.EntityFrameworkCore;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Infrastructure.Persistence;

internal sealed class InventoryItemRepository : IInventoryItemRepository
{
    /// <summary>
    /// Le plafond de l'alerte de réapprovisionnement.
    /// </summary>
    /// <remarks>
    /// C'est une alerte, pas un inventaire. Deux cents lignes sous seuil, c'est
    /// déjà plus que ce qu'un gestionnaire traite dans sa journée ; dix mille
    /// n'auraient aucun sens de plus que deux cents. La borne dit ce que la liste
    /// EST — et rend impossible qu'elle redevienne un balayage complet.
    /// </remarks>
    private const int PlafondDAlerte = 200;

    private readonly InventoryDbContext _dbContext;

    public InventoryItemRepository(InventoryDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems.AddAsync(item, cancellationToken);

    public async Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return null;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Array.Empty<InventoryItem>();
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListByLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        // Un IN () vide est une requête inutile, et selon le fournisseur une syntaxe
        // invalide. Une boutique sans localisation n'a aucun stock.
        if (locationIds.Count == 0)
        {
            return Array.Empty<InventoryItem>();
        }

        // `Include(Reservations)` est indispensable : `Available` et `IsLowStock` sont
        // calculés à partir des réservations. Sans lui, tout le stock apparaîtrait
        // comme intégralement disponible — un mensonge dans le sens le plus coûteux,
        // celui qui fait vendre ce qu'on n'a pas.
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => locationIds.Contains(i.LocationId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Les articles sous leur seuil de réapprovisionnement, filtrés PAR LA BASE.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE CHARGEAIT LA TABLE DE STOCK ENTIÈRE (§12).
    ///
    /// `SELECT *` sur `inventory_items`, avec `Include(Reservations)`, puis filtre
    /// en mémoire. Le commentaire d'alors — « Available dépend des réservations
    /// (calculé en mémoire) : on charge puis on filtre » — décrivait exactement ce
    /// qui se passait, et présentait la conséquence comme une fatalité.
    ///
    /// ET LE COÛT CROISSAIT SUR DEUX AXES, PAS UN. Depuis ISSUE-045, les
    /// réservations ne sont plus supprimées mais MARQUÉES : la collection de chaque
    /// article grandit indéfiniment. Charger tous les articles avec toutes leurs
    /// réservations, c'est charger tout l'historique du stock de la plateforme —
    /// pour rendre la poignée de lignes sous leur seuil.
    ///
    /// LE PRÉDICAT SQL RECOPIE `Available <= ReorderThreshold`, ET LES DEUX
    ///     DOIVENT RESTER D'ACCORD.
    ///
    /// `Available` vaut `OnHand - Reserved`, et `Reserved` somme les réservations
    /// ACTIVES (`InventoryItem.cs:86`). Le `Where` ci-dessous écrit cette somme en
    /// sous-requête corrélée, parce qu'EF ne sait pas traduire une propriété
    /// calculée en C#.
    ///
    /// C'est une duplication, donc un risque : si la définition de `Reserved`
    /// changeait sans que ce prédicat suive, le filtre SQL et `IsLowStock`
    /// diraient deux choses différentes. D'où le second filtre, en mémoire, juste
    /// après — il ne peut que RETIRER des lignes, jamais en ajouter. Une divergence
    /// produirait donc un manque visible (un article sous seuil non listé), jamais
    /// un faux positif silencieux.
    ///
    /// ET UNE BORNE, sur le modèle de `ListWithExpirableReservationsAsync` juste
    /// en dessous : un tri stable puis un `Take`. Une alerte de réapprovisionnement
    /// qui rendrait dix mille lignes ne serait pas une alerte.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(
        int take = PlafondDAlerte, CancellationToken cancellationToken = default)
    {
        var candidats = await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i =>
                i.OnHand
                - i.Reservations
                    .Where(r => r.Status == ReservationStatus.Active)
                    .Sum(r => r.Quantity)
                <= i.ReorderThreshold)
            .OrderBy(i => i.Id)
            .Take(take <= 0 ? PlafondDAlerte : take)
            .ToListAsync(cancellationToken);

        // Confirmation par le domaine — voir l'encadré : ce filtre ne peut que
        // retirer, jamais ajouter.
        return candidats.Where(i => i.IsLowStock).ToList();
    }

    public async Task<IReadOnlyList<InventoryItem>> ListWithExpirableReservationsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return Array.Empty<InventoryItem>();
        }

        // LE FILTRE EST TRADUIT EN SQL, L'`Include` RAMÈNE TOUT LE RESTE.
        //
        // Le `Where` sélectionne les articles qui ONT quelque chose à expirer ; il
        // ne restreint PAS la collection chargée. C'est indispensable : `Reserved`
        // et `Available` se calculent sur l'ensemble des réservations de l'agrégat,
        // et un `Include` filtré rendrait un agrégat amputé — donc un `Available`
        // faux, dans le sens qui fait vendre ce qu'on n'a pas.
        //
        // L'ordre par `Id` n'a aucun sens métier : il rend seulement le lot STABLE
        // d'un tour à l'autre, ce que `Take` sans tri ne garantit pas.
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => i.Reservations.Any(r =>
                r.Status == ReservationStatus.Active && r.ExpiresAtUtc <= nowUtc))
            .OrderBy(i => i.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Efface les réservations terminées antérieures à la borne. Voir l'encadré du
    /// contrat, qui porte le raisonnement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX REQUÊTES, ET PAS UNE. C'EST DÉLIBÉRÉ.
    ///
    /// `ExecuteDeleteAsync` ne se combine pas avec `Take` : EF refuse de traduire
    /// un `DELETE` borné. Sans borne, un premier passage sur une base ancienne
    /// effacerait des millions de lignes en une transaction — verrou long,
    /// journal de transactions gonflé, et un `VACUUM` à la clé.
    ///
    /// On relève donc d'abord les identifiants du lot, puis on efface CE lot. Le
    /// tri par identifiant ne porte aucun sens métier : il rend le lot stable d'un
    /// tour à l'autre, ce que `Take` sans tri ne garantit pas.
    ///
    /// `ExecuteDeleteAsync` COURT-CIRCUITE `SaveChangesAsync`, DONC L'OUTBOX,
    /// LE JOURNAL D'AUDIT ET LES ÉVÉNEMENTS DE DOMAINE.
    ///
    /// C'est correct ICI et seulement ici : une purge n'est pas un geste métier.
    /// Personne n'a à apprendre qu'une ligne d'historique a été effacée par
    /// péremption — et publier un événement par ligne noierait le bus. Le
    /// travailleur journalise le VOLUME, ce qui est la seule trace utile.
    ///
    /// ET ELLE NE PASSE PAS PAR L'AGRÉGAT. Charger les `InventoryItem` pour
    /// supprimer leurs enfants ferait exactement ce que cette purge existe pour
    /// éviter : ramener en mémoire les collections qui pèsent.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<int> PurgeTerminalReservationsAsync(
        DateTime avantUtc, int plafond, CancellationToken cancellationToken = default)
    {
        // « TERMINÉE » SE LIT SUR LE STATUT, PAS SUR L'ÉCHÉANCE.
        //
        // Une réservation dont `ExpiresAtUtc` est passée mais qui est encore
        // `Active` n'a PAS été balayée : elle immobilise toujours du stock, et
        // l'effacer le rendrait à la vente sans trace. C'est le travail du
        // balayeur d'expiration, qui l'écrit ; ce n'est pas celui de la purge.
        var termines = new[]
        {
            ReservationStatus.Confirmed,
            ReservationStatus.Released,
            ReservationStatus.Expired
        };

        var lot = await _dbContext.Set<StockReservation>()
            .Where(r => termines.Contains(r.Status)
                && (r.ConfirmedAtUtc < avantUtc
                    || r.ReleasedAtUtc < avantUtc
                    || r.ExpiredAtUtc < avantUtc))
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(plafond)
            .ToListAsync(cancellationToken);

        if (lot.Count == 0)
        {
            return 0;
        }

        return await _dbContext.Set<StockReservation>()
            .Where(r => lot.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems.AnyAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }
}
