using Npgsql;

namespace HBA.Order.IntegrationTests;

/// <summary>L'état d'une commande, lu directement dans la table.</summary>
internal sealed record EtatCommande(string Statut, Guid? PaiementId, string? MotifAnnulation);

/// <summary>CE QUE LA BASE DIT VRAIMENT — EN SQL, PAS PAR `OrderingDbContext`.</summary>
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
    /// Attend que la commande atteigne l'un des statuts voulus, ou rend le dernier
    /// vu.
    /// </summary>
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

    /// <summary>Combien de traces d'inbox pour ce couple (événement, consommateur) ?</summary>
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
