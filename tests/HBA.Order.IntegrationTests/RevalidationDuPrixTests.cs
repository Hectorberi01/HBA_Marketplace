using System.Net;
using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-048 — « AUCUNE REVALIDATION DU PRIX NI DU STATUT « PUBLIÉ » ENTRE
/// L'AJOUT AU PANIER ET LE PAIEMENT. »
///
/// LE PANIER FIGE SON PRIX À L'AJOUT, ET C'EST NORMAL — CE QUI NE L'ÉTAIT PAS,
/// C'EST QUE PLUS PERSONNE NE LE RELISE ENSUITE.
///
/// Un panier vit des jours. Pendant ce temps l'offre peut être dépubliée, le
/// vendeur peut fermer sa boutique, la promotion peut expirer. Le checkout
/// construisait pourtant la commande sur les seules valeurs du panier : un
/// article retiré de la vente se commandait encore, et une promotion terminée
/// continuait de s'appliquer indéfiniment à qui avait pensé à remplir son panier
/// pendant qu'elle courait.
///
/// CE QUI SE JOUE ICI, C'EST QUE LES TROIS CAUSES RESTENT DISTINCTES.
///
/// « Cet article n'est plus proposé », « il n'est plus disponible » et « son prix
/// a changé » appellent trois gestes différents de l'acheteur : retirer la ligne,
/// attendre, ou accepter le nouveau prix. Les fondre en un seul « votre panier a
/// changé » rendrait le refus inactionnable — et c'est ce qui arrive tout seul si
/// l'on assertit sur le statut HTTP, puisque les trois sont des 409.
///
/// D'où la lecture de `error.details[].reason` : le code MÉTIER, pas la famille.
///
/// CE QUE CES TESTS NE PROUVENT PAS. Le catalogue est un double
/// (<see cref="CatalogueDeTest"/>) : ils éprouvent la DÉCISION du checkout face à
/// une réponse donnée, pas la fidélité de catalog-service à rendre cette réponse.
/// La correspondance entre `OfferStatus` et `IsPurchasable` appartient à
/// `SuspensionDuCatalogueTests`, côté catalogue.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// LE CHECKOUT REFUSE, IL NE RETARIFIE PAS — ET C'EST LE POINT.
    ///
    /// Réaligner silencieusement ferait payer À LA HAUSSE un montant que l'acheteur
    /// n'a jamais vu ; à la baisse, cela produirait un total différent de celui
    /// affiché à l'écran juste avant. Ce test échouerait si quelqu'un « améliorait »
    /// le checkout en le rendant tolérant : la commande serait créée, le statut
    /// serait 201, et personne ne remarquerait le changement de contrat.
    /// </summary>
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

    /// <summary>
    /// UNE BAISSE REFUSE AUSSI, ET CE N'EST PAS UNE SÉVÉRITÉ GRATUITE.
    ///
    /// Il serait tentant de laisser passer un prix qui a BAISSÉ : l'acheteur ne
    /// peut que gagner. Mais le total commandé serait alors différent de celui
    /// affiché au récapitulatif, donc du montant que l'acheteur a autorisé — et sur
    /// un paiement mobile où le montant est confirmé par SMS, l'écart se voit et
    /// devient une réclamation. Le refus renvoie l'acheteur à son panier, qui
    /// affichera le nouveau prix.
    /// </summary>
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
