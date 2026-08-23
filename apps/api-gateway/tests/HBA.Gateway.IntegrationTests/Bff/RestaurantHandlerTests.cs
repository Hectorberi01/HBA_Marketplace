using System.Collections;
using System.Reflection;
using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Restaurant;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Financial;
using HBA.Gateway.Application.Contracts.Food;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Restaurant BFF — tableau de bord et écran de cuisine (§13, §14).</summary>
public sealed class RestaurantHandlerTests
{
    private readonly FakeFoodClient _food = new();
    private readonly FakeFinancialClient _financial = new();

    private static readonly Guid RestaurantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PayoutSellerId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private GetRestaurantDashboardHandler Dashboard() => new(_food, _financial);
    private GetRestaurantKitchenHandler Kitchen() => new(_food);

    private void GivenMembership(Guid? payoutSellerId = null, params string[] permissions)
        => _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Success(
            200, Fixtures.Partner(RestaurantId, "Owner", payoutSellerId, permissions));

    // ─────────────────────────── Appartenance ────────────────────────────────

    [Fact]
    public async Task Le_tableau_de_bord_d_un_autre_etablissement_rend_404_et_non_403()
    {
        GivenMembership();

        // §30 : être restaurateur ne suffit pas — encore faut-il être CELUI-là.
        // Un 403 confirmerait que l'établissement existe.
        var act = () => Dashboard().HandleAsync(OtherId, CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task L_ecran_cuisine_d_un_autre_etablissement_rend_404()
    {
        GivenMembership();

        var act = () => Kitchen().HandleAsync(OtherId, CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Sans_rattachement_a_un_etablissement_le_tableau_de_bord_rend_404()
    {
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Failure(404, "aucun établissement");

        var act = () => Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Food_a_terre_rend_503_sur_le_tableau_de_bord()
    {
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Failure(503, "food à terre");

        var act = () => Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    // ─────────────────────────── Portefeuille ────────────────────────────────

    [Fact]
    public async Task Sans_la_permission_finance_l_appel_portefeuille_n_est_meme_pas_emis()
    {
        GivenMembership(PayoutSellerId); // aucune permission
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(RestaurantId));

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        // Filtrer après coup laisserait le montant transiter et apparaître
        // dans les journaux : la seule forme qui ne fuit rien est l'absence d'appel.
        _financial.SellerWalletCalls.Should().Be(0);
        envelope.Data.Wallet.Should().BeNull();
    }

    [Fact]
    public async Task Sans_compte_de_reversement_l_appel_portefeuille_n_est_pas_emis_non_plus()
    {
        GivenMembership(payoutSellerId: null, GetRestaurantDashboardHandler.FinancePermission);
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(RestaurantId));

        await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        _financial.SellerWalletCalls.Should().Be(0);
    }

    [Fact]
    public async Task Avec_la_permission_le_portefeuille_est_lu_sur_le_compte_de_reversement()
    {
        GivenMembership(PayoutSellerId, GetRestaurantDashboardHandler.FinancePermission);
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(RestaurantId));
        _financial.SellerWalletResult = ServiceResult<SellerWallet>.Success(
            200, new SellerWallet(PayoutSellerId, 3_000m, 87_500m, 0m, "XOF"));

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        _financial.SellerWalletCalls.Should().Be(1);
        envelope.Data.Wallet!.AvailableBalance.Should().Be(87_500m);
    }

    [Fact]
    public async Task Financial_a_terre_ne_fait_pas_tomber_le_tableau_de_bord()
    {
        GivenMembership(PayoutSellerId, GetRestaurantDashboardHandler.FinancePermission);
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(RestaurantId));
        _financial.SellerWalletResult = ServiceResult<SellerWallet>.Failure(503, "financial à terre");

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        envelope.Data.Wallet.Should().BeNull();
        envelope.Data.Restaurant.Id.Should().Be(RestaurantId);
    }

    // ─────────────────────────── Compteurs cuisine ───────────────────────────

    [Fact]
    public async Task Le_tableau_de_bord_compte_les_tickets_par_etat()
    {
        GivenMembership();
        // Ce sont des `KitchenTicketStatus` — l'avancement en cuisine — et non
        // des `FoodOrderStatus`. « Accepted » ou « ReadyForPickup » ici ne
        // lèveraient rien : les tickets tomberaient dans aucun seau.
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(
            RestaurantId,
            ("Pending", 2),
            ("Pending", 4),
            ("Preparing", 9),
            ("Ready", 1)));

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        envelope.Data.Kitchen.Pending.Should().Be(2);
        envelope.Data.Kitchen.Preparing.Should().Be(1);
        envelope.Data.Kitchen.Ready.Should().Be(1);
    }

    [Fact]
    public async Task Un_statut_de_commande_au_lieu_d_un_statut_de_ticket_ne_remplit_aucun_seau()
    {
        // Garde-fou contre la confusion des deux énumérations : si quelqu'un
        // recale les seaux sur `FoodOrderStatus`, ce test le dira au lieu de
        // laisser l'écran de cuisine se vider en silence.
        GivenMembership();
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(
            RestaurantId, ("Accepted", 1), ("ReadyForPickup", 1)));

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        envelope.Data.Kitchen.Should().Be(new RestaurantKitchenSummaryDto(0, 0, 0));
    }

    [Fact]
    public async Task Food_a_terre_sur_la_cuisine_degrade_les_compteurs_sans_vider_l_ecran()
    {
        GivenMembership();
        _food.KitchenResult = ServiceResult<KitchenBoard>.Failure(503, "food à terre");

        var envelope = await Dashboard().HandleAsync(RestaurantId, CancellationToken.None);

        envelope.Data.Kitchen.Should().Be(new RestaurantKitchenSummaryDto(0, 0, 0));
        envelope.Data.Service.AcceptsOrdersNow.Should().BeTrue();
        envelope.Warnings.Should().Contain(w => w.Source == "Food");
    }

    // ─────────────────────────── Écran de cuisine ────────────────────────────

    [Fact]
    public async Task L_ecran_cuisine_range_les_tickets_en_trois_colonnes()
    {
        GivenMembership();
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(
            RestaurantId,
            ("Pending", 3),
            ("Preparing", 6),
            ("Preparing", 11),
            ("Ready", 0)));

        var envelope = await Kitchen().HandleAsync(RestaurantId, CancellationToken.None);

        envelope.Data.Pending.Should().HaveCount(1);
        envelope.Data.Preparing.Should().HaveCount(2);
        envelope.Data.Ready.Should().HaveCount(1);
    }

    [Fact]
    public async Task L_anciennete_d_un_ticket_est_calculee_par_le_serveur()
    {
        GivenMembership();
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(
            200, Fixtures.Kitchen(RestaurantId, ("Preparing", 5)));

        var envelope = await Kitchen().HandleAsync(RestaurantId, CancellationToken.None);

        // L'horloge d'une tablette de cuisine n'est pas fiable : le compteur
        // doit venir du serveur, pas du client.
        envelope.Data.Preparing.Single().ElapsedSeconds.Should().BeInRange(280, 320);
    }

    [Fact]
    public async Task L_ecran_cuisine_n_appelle_ni_Financial_ni_autre_chose_que_Food()
    {
        GivenMembership(PayoutSellerId, GetRestaurantDashboardHandler.FinancePermission);
        _food.KitchenResult = ServiceResult<KitchenBoard>.Success(200, Fixtures.Kitchen(RestaurantId));

        await Kitchen().HandleAsync(RestaurantId, CancellationToken.None);

        // Même avec la permission finance : ce qui n'est pas demandé ne peut pas
        // fuiter sur une tablette allumée toute la journée en cuisine.
        _financial.SellerWalletCalls.Should().Be(0);
    }

    // ─────────────────── §14 : aucun montant dans le KDS ─────────────────────

    [Fact]
    public void Aucun_type_de_l_ecran_cuisine_ne_porte_de_montant()
    {
        var forbidden = new[]
        {
            "Price", "Amount", "Total", "Revenue", "Commission", "Wallet",
            "Balance", "Fee", "Currency", "Payout", "Invoice",
        };

        var offenders = Reachable(typeof(RestaurantKitchenDto))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (Type: type, property.Name)))
            .Where(member => forbidden.Any(word =>
                member.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(member => $"{member.Type.Name}.{member.Name}")
            .ToList();

        offenders.Should().BeEmpty(
            "le §14 interdit tout montant sur l'écran de cuisine : {0}",
            string.Join(", ", offenders));
    }

    /// <summary>Le type et tous ceux qu'il transporte, transitivement.</summary>
    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    queue.Enqueue(argument);
                }

                continue;
            }

            if (type.Namespace?.StartsWith("HBA.Gateway", StringComparison.Ordinal) != true
                || !seen.Add(type))
            {
                continue;
            }

            yield return type;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                queue.Enqueue(property.PropertyType);
            }
        }
    }
}
