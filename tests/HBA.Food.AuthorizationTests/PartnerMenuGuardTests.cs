using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Food.AuthorizationTests;

/// <summary>
/// Les routes de la carte et de la cuisine (VEN5-b, VEN5-c, tâche #227).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUINZE ROUTES PARTENAIRE, AUCUN TEST DE FRONTIÈRE (tâche #215).
///
/// Douze pour la carte — cartes, sections, plats, options, disponibilité — plus
/// pause/reprise du service, plus les deux lectures de la file d'acceptation
/// ajoutées par #227. Toutes gardées par `DenyUnlessStaffAsync`, écrite à la main,
/// et jusqu'ici vérifiée par personne.
///
/// CE SERVICE A DÉJÀ EU LE DÉFAUT QU'ON CHERCHE.
///
/// `accept` et `reject` prenaient le `restaurantId` dans l'URL et se contentaient
/// de lire le porteur du jeton — qu'elles transmettaient au domaine comme ACTEUR,
/// pour la traçabilité. Leurs voisines immédiates, `preparing` et `ready`, passaient
/// bien par la garde. Deux routes sur quatre, dans le même bloc de code.
/// C'est exactement ce qu'un test de frontière attrape et qu'une relecture manque.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CES TESTS N'ÉPROUVENT PAS, ET IL FAUT LE DIRE.
///
/// La fabrique ne monte ni base ni food-service complet : on ne peut pas
/// distinguer « la garde a refusé » de « la garde a passé, puis la base
/// manquait ». Les assertions portent donc sur ce qui se décide AVANT le
/// gestionnaire.
///
/// Le refus sur l'établissement d'AUTRUI, et la distinction entre les permissions
/// `OrderAccept` et `KitchenManage` — un caissier accepte, un cuisinier prépare —
/// demandent un personnel peuplé en base. C'est un test d'intégration, et il reste
/// à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PartnerMenuGuardTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public PartnerMenuGuardTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    private static string Route(string gabarit)
    {
        foreach (var jeton in new[] { "{id}", "{menuId}", "{sectionId}", "{itemId}", "{groupId}", "{orderId}" })
        {
            gabarit = gabarit.Replace(jeton, Guid.NewGuid().ToString());
        }
        return gabarit;
    }

    /// <summary>Aucune route partenaire ne s'ouvre sans jeton.</summary>
    [Theory]
    // ── Cartes et sections (l'écran de la tâche #214 les appelle) ──────────
    [InlineData("POST", "/api/food/partner/restaurants/{id}/menus")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/menus/{menuId}")]
    [InlineData("DELETE", "/api/food/partner/restaurants/{id}/menus/{menuId}")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/menus/{menuId}/visibility")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/menus/{menuId}/categories")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/categories/{sectionId}")]
    [InlineData("DELETE", "/api/food/partner/restaurants/{id}/categories/{sectionId}")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/categories/{sectionId}/position")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/categories/{sectionId}/visibility")]
    // ── Plats et options ──────────────────────────────────────────────────
    [InlineData("POST", "/api/food/partner/restaurants/{id}/categories/{sectionId}/items")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/items/{itemId}")]
    [InlineData("DELETE", "/api/food/partner/restaurants/{id}/items/{itemId}")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/items/{itemId}/price")]
    [InlineData("PUT", "/api/food/partner/restaurants/{id}/items/{itemId}/availability")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/items/{itemId}/option-groups")]
    // ── Service et cuisine ────────────────────────────────────────────────
    [InlineData("POST", "/api/food/partner/restaurants/{id}/pause")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/resume")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/orders/{orderId}/accept")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/orders/{orderId}/reject")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/orders/{orderId}/preparing")]
    [InlineData("POST", "/api/food/partner/restaurants/{id}/orders/{orderId}/ready")]
    public async Task Sans_jeton_l_espace_restaurateur_refuse(string methode, string gabarit)
    {
        var response = await Requetes.EnvoyerAsync(
            _factory.CreateClient(), methode, Route(gabarit));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Les LECTURES partenaire non plus — et c'est le piège de ce service.
    /// </summary>
    /// <remarks>
    /// `food-read` EST ANONYME SUR LA PASSERELLE, ordre 10, tous les GET de
    /// `/api/food`. C'est ce qui fait vivre la vitrine sans compte — et cela
    /// signifie que la protection de ces trois lectures repose ENTIÈREMENT sur le
    /// service, pas sur la passerelle.
    ///
    /// La carte d'un restaurateur porte ses coûts, ses plats masqués et ses
    /// créneaux ; la file d'acceptation porte les notes de ses clients. Si le
    /// groupe partenaire perdait son exigence de jeton, la passerelle ne
    /// rattraperait rien.
    /// </remarks>
    [Theory]
    [InlineData("/api/food/partner/me")]
    [InlineData("/api/food/partner/restaurants/{id}/menu")]
    [InlineData("/api/food/partner/restaurants/{id}/kitchen")]
    [InlineData("/api/food/partner/restaurants/{id}/orders")]
    public async Task Les_lectures_partenaire_exigent_un_jeton(string gabarit)
    {
        var response = await _factory.CreateClient().GetAsync(Route(gabarit));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Un compte valide n'est pas refoulé en 401 sur l'espace partenaire.</summary>
    /// <remarks>
    /// LE GROUPE N'EXIGE AUCUN RÔLE, et c'est voulu : l'accès se décide par
    /// PERMISSION de personnel (§8), pas par rôle global. Un `[Authorize]` de rôle
    /// posé ici enfermerait dehors le caissier qui n'est pas le propriétaire.
    ///
    /// Le test verrouille donc l'inverse d'une fuite : qu'un employé légitime
    /// puisse entrer. C'est ce qui a cassé une fois, quand deux attributs
    /// d'autorisation ont ADDITIONNÉ leurs exigences au lieu de se remplacer.
    /// </remarks>
    [Theory]
    [InlineData("/api/food/partner/me")]
    [InlineData("/api/food/partner/restaurants/{id}/kitchen")]
    [InlineData("/api/food/partner/restaurants/{id}/orders")]
    public async Task Un_compte_valide_n_est_pas_refoule_en_401(string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync(Route(gabarit));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
