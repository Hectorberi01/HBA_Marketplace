using System.Net;
using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ISSUE-048 — « AUCUNE REVALIDATION DU PRIX NI DU STATUT « PUBLIÉ » ENTRE L'AJOUT
/// AU PANIER ET LE PAIEMENT. »
/// </summary>
[Collection(OrderIntegrationCollection.Nom)]
public sealed class RevalidationDuPrixTests
{
    private readonly OrderIntegrationFixture _fixture;

    public RevalidationDuPrixTests(OrderIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Le cas nominal, et il vaut d'être écrit : sans lui, les trois refus qui
    /// suivent pourraient tous passer sur un checkout qui refuse TOUJOURS.
    /// </summary>
    [Fact]
    public async Task Un_panier_conforme_au_catalogue_passe()
    {
        _fixture.Catalogue.Reinitialiser();

        var (_, _, commander) = Parcours.PreparerCommande(_fixture);

        var reponse = await commander();

        reponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "le prix du panier et celui du catalogue sont les mêmes — "
            + "si ce test tombe, les deux doubles ont divergé sur PrixUnitaire");
    }

    [Fact]
    public async Task Une_offre_disparue_du_catalogue_refuse_la_commande()
    {
        _fixture.Catalogue.Reinitialiser();

        var (_, offres, commander) = Parcours.PreparerCommande(_fixture);
        _fixture.Catalogue.Retirer(offres[0]);

        var reponse = await commander();

        reponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Parcours.LireLeCodeMetierAsync(reponse))
            .Should().Be("ordering.offer_unavailable");
    }

    [Fact]
    public async Task Une_offre_devenue_invendable_refuse_la_commande()
    {
        _fixture.Catalogue.Reinitialiser();

        var (_, offres, commander) = Parcours.PreparerCommande(_fixture);
        _fixture.Catalogue.RendreNonAchetable(offres[0]);

        var reponse = await commander();

        reponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Parcours.LireLeCodeMetierAsync(reponse))
            .Should().Be("ordering.offer_not_purchasable",
                "une offre présente mais retirée de la vente n'est pas la même chose "
                + "qu'une offre disparue : l'acheteur peut attendre qu'elle revienne");
    }

    /// <summary>LE CHECKOUT REFUSE, IL NE RETARIFIE PAS — ET C'EST LE POINT.</summary>
    [Fact]
    public async Task Un_prix_qui_a_bouge_refuse_la_commande_au_lieu_de_la_retarifier()
    {
        _fixture.Catalogue.Reinitialiser();

        var (_, offres, commander) = Parcours.PreparerCommande(_fixture);
        _fixture.Catalogue.PoserLePrix(offres[0], PanierDeTest.PrixUnitaire + 500m);

        var reponse = await commander();

        reponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Parcours.LireLeCodeMetierAsync(reponse))
            .Should().Be("ordering.price_changed");
    }

    /// <summary>UNE BAISSE REFUSE AUSSI, ET CE N'EST PAS UNE SÉVÉRITÉ GRATUITE.</summary>
    [Fact]
    public async Task Une_baisse_de_prix_refuse_aussi()
    {
        _fixture.Catalogue.Reinitialiser();

        var (_, offres, commander) = Parcours.PreparerCommande(_fixture);
        _fixture.Catalogue.PoserLePrix(offres[0], PanierDeTest.PrixUnitaire - 500m);

        var reponse = await commander();

        reponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Parcours.LireLeCodeMetierAsync(reponse))
            .Should().Be("ordering.price_changed");
    }
}
