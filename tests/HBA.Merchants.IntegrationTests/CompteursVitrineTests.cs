using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX COMPTEURS DE LA VITRINE, QUI VALAIENT ZÉRO POUR TOUT LE MONDE.
///
/// `Seller.UpdateRating` N'AVAIT AUCUN APPELANT. RIEN N'INCRÉMENTAIT
///    `SalesCount`.
///
/// Les deux colonnes existaient, étaient persistées, figuraient dans la projection
/// de vitrine — et restaient à `0`. Un vendeur ayant écoulé trois cents commandes
/// était présenté comme n'ayant jamais rien vendu ni satisfait personne. Sur une
/// place de marché, la preuve sociale sur laquelle repose l'achat était donc
/// constamment fausse, et fausse dans le sens qui décourage.
///
/// CE QUE CES TESTS ÉPROUVENT VRAIMENT : QU'ON POSE, ET QU'ON N'ACCUMULE PAS.
///
/// Les deux gestionnaires reçoivent une valeur RECALCULÉE depuis la source et la
/// posent telle quelle. C'est ce qui les rend idempotents face à un rejeu — Kafka
/// livre au moins une fois. Un compteur incrémental passerait le premier test et
/// échouerait au second : c'est pour cela qu'ils vont par paires, et que la
/// seconde valeur est toujours PLUS PETITE que la première.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class CompteursVitrineTests
{
    /// <summary>
    /// Noms de contrat, écrits en dur. Voir `PurgeRgpdTests` : les calculer avec
    /// `KafkaEventNaming` ferait passer le test quoi qu'il arrive, y compris le
    /// jour où la convention change et où les services déployés cessent de se
    /// comprendre.
    /// </summary>
    private const string TypeNoteRecalculee = "seller.rating.recomputed";

    private const string TypeCommandeConfirmee = "order.confirmed";

    private readonly MerchantsIntegrationFixture _fixture;

    public CompteursVitrineTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// La note publiée par review-service se retrouve sur le vendeur.
    /// </summary>
    [Fact]
    public async Task La_note_recalculee_est_posee_sur_le_vendeur()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Note {Guid.NewGuid():N}");

        await PublierNoteAsync(vendeur.SellerId, moyenne: 4.5, compte: 12);

        (await AttendreNoteAsync(vendeur.SellerId, 4.5m)).Should().Be(4.5m,
            "la colonne existait depuis toujours et n'avait aucun alimenteur");
    }

    /// <summary>
    /// LE TEST QUI DISTINGUE « POSER » DE « ACCUMULER ».
    ///
    /// La seconde moyenne est plus BASSE que la première — un avis vient d'être
    /// modéré. Un gestionnaire qui accumulerait ne saurait pas redescendre ; il
    /// passerait le test précédent et échouerait ici.
    /// </summary>
    [Fact]
    public async Task Une_note_qui_baisse_remplace_l_ancienne()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Baisse {Guid.NewGuid():N}");

        await PublierNoteAsync(vendeur.SellerId, moyenne: 4.8, compte: 10);
        (await AttendreNoteAsync(vendeur.SellerId, 4.8m)).Should().Be(4.8m);

        await PublierNoteAsync(vendeur.SellerId, moyenne: 3.2, compte: 9);

        (await AttendreNoteAsync(vendeur.SellerId, 3.2m)).Should().Be(3.2m,
            "le rejet d'un avis fait BAISSER la moyenne : un compteur qui ne sait "
            + "qu'ajouter laisserait le vendeur porter la note d'un avis modéré");
    }

    /// <summary>
    /// Une commande confirmée fait recalculer le compteur de ventes.
    /// </summary>
    [Fact]
    public async Task Une_commande_confirmee_met_a_jour_le_compteur_de_ventes()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Ventes {Guid.NewGuid():N}");

        // C'EST order-service QUI RÉPOND, PAS L'ÉVÉNEMENT QUI PORTE LE COMPTE.
        //
        // L'événement porte pourtant `ItemCount` par vendeur — et l'utiliser aurait
        // été le piège : il faudrait alors additionner, donc double-compter au
        // premier rejeu.
        _fixture.Commandes.FixerVentes(vendeur.SellerId, 12);

        await PublierCommandeConfirmeeAsync(vendeur.SellerId);

        (await AttendreVentesAsync(vendeur.SellerId, 12)).Should().Be(12);
    }

    /// <summary>
    /// LE MÊME TEST QUE CI-DESSUS, DANS L'AUTRE SENS.
    ///
    /// order-service répond moins la seconde fois — une commande a été annulée
    /// entre-temps. Poser le total le suit ; incrémenter ne le pourrait pas.
    /// </summary>
    [Fact]
    public async Task Un_compteur_de_ventes_qui_baisse_est_suivi()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Retour {Guid.NewGuid():N}");

        _fixture.Commandes.FixerVentes(vendeur.SellerId, 12);
        await PublierCommandeConfirmeeAsync(vendeur.SellerId);
        (await AttendreVentesAsync(vendeur.SellerId, 12)).Should().Be(12);

        _fixture.Commandes.FixerVentes(vendeur.SellerId, 7);
        await PublierCommandeConfirmeeAsync(vendeur.SellerId);

        (await AttendreVentesAsync(vendeur.SellerId, 7)).Should().Be(7,
            "on POSE le total recalculé : un handler qui incrémenterait afficherait 19");
    }

    /// <summary>
    /// Les deux valeurs remontent bien jusqu'à la fiche du vendeur, pas seulement
    /// jusqu'à la colonne.
    /// </summary>
    [Fact]
    public async Task Les_deux_compteurs_remontent_sur_la_fiche_du_vendeur()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Fiche {Guid.NewGuid():N}");

        _fixture.Commandes.FixerVentes(vendeur.SellerId, 5);
        await PublierCommandeConfirmeeAsync(vendeur.SellerId);
        await PublierNoteAsync(vendeur.SellerId, moyenne: 4.0, compte: 3);

        await AttendreVentesAsync(vendeur.SellerId, 5);
        await AttendreNoteAsync(vendeur.SellerId, 4.0m);

        var corps = await vendeur.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/merchants/{vendeur.SellerId}");

        var data = corps.GetProperty("data");
        data.GetProperty("rating").GetDecimal().Should().Be(4.0m);
        data.GetProperty("salesCount").GetInt32().Should().Be(5);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    private Task PublierNoteAsync(Guid sellerId, double moyenne, int compte)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            "service.engagement.v1",
            Guid.NewGuid(),
            TypeNoteRecalculee,
            aggregateType: "seller",
            aggregateId: sellerId.ToString("D"),
            charge: new
            {
                id = Guid.NewGuid(),
                occurredOnUtc = DateTime.UtcNow,
                sellerId,
                average = moyenne,
                count = compte
            });

    private Task PublierCommandeConfirmeeAsync(Guid sellerId)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            "service.order.v1",
            Guid.NewGuid(),
            TypeCommandeConfirmee,
            aggregateType: "order",
            aggregateId: Guid.NewGuid().ToString("D"),
            charge: new
            {
                id = Guid.NewGuid(),
                occurredOnUtc = DateTime.UtcNow,
                orderId = Guid.NewGuid(),
                buyerId = Guid.NewGuid(),
                currency = "XOF",
                promotionCode = (string?)null,
                kind = "Goods",
                restaurantId = (Guid?)null,
                sellerShares = new[]
                {
                    new { sellerId, itemCount = 3, amount = 15000m }
                }
            });

    private Task<decimal> AttendreNoteAsync(Guid sellerId, decimal attendue)
        => AttendreAsync<decimal>(sellerId, "\"Rating\"", v => v == attendue);

    private Task<int> AttendreVentesAsync(Guid sellerId, int attendues)
        => AttendreAsync<int>(sellerId, "\"SalesCount\"", v => v == attendues);

    /// <summary>
    /// UNE ATTENTE ACTIVE, ET LA LECTURE SE FAIT EN SQL.
    ///
    /// En SQL parce que passer par le `DbContext` du service ferait vérifier
    /// l'écriture avec le mécanisme qui l'a produite. Active parce que le temps de
    /// consommation dépend du rééquilibrage initial du groupe Kafka, qui peut
    /// prendre plusieurs secondes — un délai fixe serait instable ou lent.
    /// </summary>
    private async Task<T> AttendreAsync<T>(Guid sellerId, string colonne, Func<T, bool> satisfait)
    {
        var echeance = DateTime.UtcNow.AddSeconds(60);
        T valeur = default!;

        while (DateTime.UtcNow < echeance)
        {
            valeur = await LireAsync<T>(sellerId, colonne);

            if (satisfait(valeur))
            {
                return valeur;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return valeur;
    }

    private async Task<T> LireAsync<T>(Guid sellerId, string colonne)
    {
        await using var connexion = new NpgsqlConnection(_fixture.ConnectionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            $"SELECT {colonne} FROM sellers.sellers WHERE \"Id\" = @id", connexion);

        commande.Parameters.AddWithValue("id", sellerId);

        var brut = await commande.ExecuteScalarAsync();

        return brut is null or DBNull ? default! : (T)Convert.ChangeType(brut, typeof(T));
    }
}
