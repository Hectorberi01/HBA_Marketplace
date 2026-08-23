using Npgsql;

namespace HBA.Order.IntegrationTests;

/// <summary>L'état d'une commande, lu directement dans la table.</summary>
internal sealed record EtatCommande(string Statut, Guid? PaiementId, string? MotifAnnulation);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LA BASE DIT VRAIMENT — EN SQL, PAS PAR `OrderingDbContext`.
///
/// PASSER PAR LE CONTEXTE VÉRIFIERAIT L'ÉCRITURE AVEC LE MÉCANISME QUI L'A
///    PRODUITE.
///
/// Un mauvais mapping de colonne serait alors invisible des deux côtés à la fois.
/// Le SQL voit la table telle que la MIGRATION l'a créée — et c'est précisément
/// ce qu'on veut éprouver ici : `AddOrderPaymentId` était écrite, relue,
/// versionnée, et EF ne l'a jamais vue faute d'attributs `[DbContext]` et
/// `[Migration]`. La colonne `"PaymentId"` existait dans le modèle et dans le
/// snapshot, et dans AUCUNE base. Lire cette colonne en SQL est donc, à soi seul,
/// la preuve que la migration s'applique.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class BaseDeTest
{
    public static async Task<EtatCommande?> LireCommandeAsync(string connexionString, Guid commandeId)
    {
        await using var connexion = new NpgsqlConnection(connexionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            """
            SELECT "Status", "PaymentId", "CancellationReason"
            FROM ordering.orders
            WHERE "Id" = @id
            """,
            connexion);

        commande.Parameters.AddWithValue("id", commandeId);

        await using var lecteur = await commande.ExecuteReaderAsync();

        if (!await lecteur.ReadAsync())
        {
            return null;
        }

        return new EtatCommande(
            lecteur.GetString(0),
            lecteur.IsDBNull(1) ? null : lecteur.GetGuid(1),
            lecteur.IsDBNull(2) ? null : lecteur.GetString(2));
    }

    /// <summary>
    /// Attend que la commande atteigne l'un des statuts voulus, ou rend le dernier vu.
    /// </summary>
    /// <remarks>
    /// UNE ATTENTE ACTIVE, PAS UN `Task.Delay` FIXE.
    ///
    /// Entre la publication du paiement et l'écriture en base il y a le
    /// rééquilibrage initial du groupe de consommateurs, la désérialisation, la
    /// résolution du type par balayage des assemblies, l'inbox et la transaction.
    /// La durée dépend de la machine. Un délai fixe assez court rend le test
    /// instable ; assez long, il ralentit toute la suite — et un test instable est
    /// pire qu'un test absent : on finit par le désactiver, et par désactiver ses
    /// voisins avec lui.
    /// </remarks>
    public static async Task<EtatCommande?> AttendreStatutAsync(
        string connexionString, Guid commandeId, params string[] statuts)
    {
        var echeance = DateTime.UtcNow.AddSeconds(90);
        EtatCommande? dernier = null;

        while (DateTime.UtcNow < echeance)
        {
            dernier = await LireCommandeAsync(connexionString, commandeId);

            if (dernier is not null && statuts.Contains(dernier.Statut, StringComparer.Ordinal))
            {
                return dernier;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return dernier;
    }

    /// <summary>
    /// Combien de traces d'inbox pour ce couple (événement, consommateur) ?
    /// </summary>
    /// <remarks>
    /// LA CLÉ EST LE COUPLE, PAS L'ÉVÉNEMENT SEUL.
    ///
    /// `AjoutInboxConsommateur` pose une clé primaire composite
    /// `(EventId, ConsumerName)` : deux gestionnaires distincts doivent pouvoir
    /// traiter le MÊME message, chacun une fois. Compter par événement seul
    /// confondrait « le rejeu a été ignoré » avec « un seul gestionnaire écoute ».
    /// </remarks>
    public static async Task<int> CompterTracesAsync(
        string connexionString, Guid eventId, string consommateur)
    {
        await using var connexion = new NpgsqlConnection(connexionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM ordering.consumer_inbox
            WHERE "EventId" = @eventId AND "ConsumerName" = @consumer
            """,
            connexion);

        commande.Parameters.AddWithValue("eventId", eventId);
        commande.Parameters.AddWithValue("consumer", consommateur);

        return Convert.ToInt32(await commande.ExecuteScalarAsync());
    }

    /// <summary>Attend qu'au moins <paramref name="attendu"/> traces existent.</summary>
    public static async Task<int> AttendreTracesAsync(
        string connexionString, Guid eventId, string consommateur, int attendu)
    {
        var echeance = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < echeance)
        {
            var compte = await CompterTracesAsync(connexionString, eventId, consommateur);

            if (compte >= attendu)
            {
                return compte;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return await CompterTracesAsync(connexionString, eventId, consommateur);
    }
}
