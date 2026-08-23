namespace HBA.Inventory.Domain.Stock;

public interface IInventoryItemRepository
{
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Articles de stock situés dans un ensemble de localisations.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Ce module ne connaît pas la notion de vendeur : un article appartient à une
    /// LOCALISATION, et c'est la localisation qui porte un `OwnerId`. Pour lister le
    /// stock d'une boutique, l'appelant résout donc d'abord ses localisations, puis
    /// interroge cette méthode.
    ///
    /// Sans elle, la seule voie était `ListBySkuAsync` appelée une fois par référence
    /// du catalogue — autant de requêtes que de SKU à chaque affichage de l'écran
    /// Stock, pour reconstituer une liste que la base sait produire d'un coup.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListByLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les articles sous leur seuil de réapprovisionnement, dans la limite de
    /// <paramref name="take"/>.
    /// </summary>
    /// <remarks>
    /// LA BORNE EST NOUVELLE, ET LE FILTRE A CHANGÉ DE CÔTÉ. Cette méthode
    /// chargeait toute la table de stock avec toutes ses réservations, puis
    /// filtrait en mémoire (§12). Elle filtre désormais en base. Voir l'encadré de
    /// l'implémentation : le prédicat SQL recopie `Available <= ReorderThreshold`,
    /// et un second filtre en mémoire garantit qu'une divergence ne produirait
    /// jamais de faux positif.
    ///
    /// ET LA BORNE EST PASSÉE AVANT LE `CancellationToken`, CE QUI A CASSÉ
    ///     L'APPELANT — de la meilleure façon possible.
    ///
    /// L'appel existant était `ListLowStockAsync(cancellationToken)`, positionnel.
    /// Ajouter `int take = 200` en tête l'a fait lier le jeton d'annulation au
    /// `take` : erreur de compilation CS1503, immédiate et nommée.
    ///
    /// C'est un coup de chance qu'il faut savoir lire. Si le nouveau paramètre
    /// avait été d'un type compatible — un `bool`, un `TimeSpan?` — le code aurait
    /// COMPILÉ et se serait mis à passer la mauvaise valeur, en silence. Insérer un
    /// paramètre optionnel avant le `CancellationToken` change le sens de tous les
    /// appels positionnels existants ; le compilateur ne le rattrape que par
    /// accident.
    /// </remarks>
    Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(
        int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Articles portant au moins une réservation `Active` dont l'échéance est
    /// dépassée. Alimente le balayage d'expiration (ISSUE-031).
    ///
    /// PAR LOT, ET C'EST DÉLIBÉRÉ.
    ///
    /// Au PREMIER passage après correction, cette requête peut ramener tout
    /// l'historique d'immobilisation de la plateforme : `ExpiresAtUtc` n'a jamais
    /// été relue, donc chaque panier abandonné depuis la mise en service est encore
    /// « en cours ». Charger cela d'un coup — avec `Include(Reservations)` sur
    /// chaque article — tiendrait la base et la mémoire pour rien. Le balayeur
    /// repasse toutes les quelques minutes ; le rattrapage se fait en plusieurs
    /// tours, sans que personne n'attende.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListWithExpirableReservationsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Efface les réservations TERMINÉES antérieures à <paramref name="avantUtc"/>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE NE DÉCROISSAIT JAMAIS, ET C'ÉTAIT ÉCRIT DEPUIS LE LOT 3.5.
    ///
    /// `StockReservation` dit lui-même : « la table ne décroît plus […] les
    /// repositories chargent `Include(i => i.Reservations)` en entier pour
    /// pouvoir calculer `Reserved`. Sur un article très vendu, la collection
    /// finira par peser. » C'est resté un manque connu pendant six lots.
    ///
    /// Six `Include(i => i.Reservations)` du service se dégradent linéairement
    /// avec le NOMBRE DE VENTES de l'article — y compris ceux qui sont par
    /// ailleurs correctement bornés, puisque la borne porte sur les articles, pas
    /// sur leurs enfants.
    ///
    /// ON NE FILTRE PAS L'`Include` SUR `IsActive`, ET C'EST LE POINT DÉLICAT.
    ///
    /// Ce serait le réflexe : `Reserved` ne somme que les actives, `Reserve` ne
    /// cherche que les actives. Mais `ConfirmReservation` teste
    /// `_reservations.Any(r => r.OrderId == orderId && r.Status == Confirmed)`
    /// pour être IDEMPOTENT — il lit donc une ligne TERMINALE. Un `Include`
    /// filtré la lui cacherait, et une confirmation rejouée décrémenterait le
    /// stock une seconde fois. Le remède serait pire que le mal.
    ///
    /// La purge est donc le bon levier : elle borne la collection sans retirer à
    /// l'agrégat l'historique dont il se sert.
    ///
    /// LA RÉTENTION DOIT DÉPASSER TOUTE FENÊTRE DE REJEU.
    ///
    /// C'est la conséquence directe du paragraphe précédent : si l'on efface une
    /// ligne `Confirmed` avant qu'un rejeu Kafka ne puisse encore arriver,
    /// l'idempotence de `ConfirmReservation` tombe. Le défaut par défaut est de
    /// QUATRE-VINGT-DIX JOURS, très au-delà de la rétention d'un topic (jours) et
    /// des reprises d'outbox (minutes).
    ///
    /// SEULEMENT LES TERMINÉES. Une réservation `Active` immobilise du stock :
    /// l'effacer le rendrait à la vente sans que rien ne le dise — exactement le
    /// contraire de ce que fait le balayeur d'expiration, qui, lui, l'écrit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <param name="avantUtc">Borne haute : les lignes terminées AVANT cet instant.</param>
    /// <param name="plafond">Nombre maximum de lignes effacées en un tour.</param>
    /// <returns>Le nombre de lignes réellement effacées.</returns>
    Task<int> PurgeTerminalReservationsAsync(
        DateTime avantUtc, int plafond, CancellationToken cancellationToken = default);
}
