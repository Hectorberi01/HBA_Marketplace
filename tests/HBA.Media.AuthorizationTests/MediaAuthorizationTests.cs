using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Media.Application.Assets;
using HBA.Media.Contracts;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using HBA.Tests.Authorization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HBA.Media.AuthorizationTests;

/// <summary>
/// media-service : personne n'obtient d'URL signée sur le fichier d'un autre, et la
/// durée de validité appartient au serveur.
/// </summary>
public sealed class MediaAuthorizationTests : IClassFixture<MediaFactory>
{
    private readonly MediaFactory _factory;

    public MediaAuthorizationTests(MediaFactory factory) => _factory = factory;

    // ── Le pipeline ─────────────────────────────────────────────────────────

    /// <summary>AUCUNE ROUTE ANONYME DANS CE SERVICE, ET C'EST UNE RÈGLE ÉCRITE.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/media/")]
    [InlineData("GET", "/api/v1/media/{id}")]
    [InlineData("GET", "/api/v1/media/{id}/download-url")]
    [InlineData("DELETE", "/api/v1/media/{id}")]
    [InlineData("POST", "/api/v1/media/{id}/reprocess")]
    public async Task Toute_la_surface_media_rend_401_sans_jeton(string methode, string gabarit)
    {
        var route = gabarit.Replace("{id}", Medias.PriveDuDeposant.ToString());

        var response = await Requetes.EnvoyerAsync(_factory.CreateClient(), methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>LE TEST CI-DESSUS NE PROUVE PAS QU'UNE ROUTE EXISTE. CELUI-CI SI.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/media/")]
    [InlineData("GET", "/api/v1/media/{id}/download-url")]
    [InlineData("DELETE", "/api/v1/media/{id}")]
    [InlineData("POST", "/api/v1/media/{id}/reprocess")]
    public async Task Les_routes_media_sont_bien_routees(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));
        var route = gabarit.Replace("{id}", Medias.PriveDuDeposant.ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// LA `FallbackPolicy` FERME TOUT CE QUI NE DÉCLARE RIEN — Y COMPRIS LES
    /// SONDES, SI L'ON RETIRE LEUR `AllowAnonymous`.
    /// </summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>L'ASSERTION PORTE SUR L'ANONYMAT, PAS SUR LA SANTÉ.</summary>
    [Fact]
    public async Task La_sonde_de_disponibilite_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    // ── ISSUE-020 : l'URL signée n'est plus délivrée à n'importe qui ─────────

    /// <summary>
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : LES PIÈCES KYB LISIBLES PAR TOUT
    /// COMPTE INSCRIT.
    /// </summary>
    [Fact]
    public async Task Un_compte_qui_n_a_pas_depose_le_media_prive_n_obtient_pas_d_url_signee()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Le déposant, lui, garde l'accès à son propre fichier.</summary>
    [Fact]
    public async Task Le_deposant_obtient_l_url_signee_de_son_media_prive()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// UN MÉDIA PUBLIC DOIT RESTER SIGNABLE PAR TOUS, SINON LES VITRINES
    /// S'ÉTEIGNENT.
    /// </summary>
    [Fact]
    public async Task Un_media_public_se_signe_pour_tout_compte_authentifie()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await DemanderUrlAsync(client, Medias.PublicDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// L'administrateur passe, parce que le support doit pouvoir instruire un
    /// litige sans demander son fichier au vendeur qui le conteste.
    /// </summary>
    [Fact]
    public async Task Un_administrateur_obtient_l_url_signee_d_un_media_prive_d_autrui()
    {
        var client = _factory.CreateClientWithToken(
            TestTokens.Create(Comptes.Autre, ApiAuthorization.AdminRole));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>UN MÉDIA SUPPRIMÉ NE SE SIGNE PLUS, MÊME POUR CELUI QUI L'A DÉPOSÉ.</summary>
    [Fact]
    public async Task Un_media_supprime_ne_se_signe_plus_meme_pour_son_deposant()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.SupprimeDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>LE REFUS NE DOIT PAS SE DISTINGUER DE L'ABSENCE — RÈGLE §29.</summary>
    [Fact]
    public async Task Un_media_interdit_et_un_media_inexistant_se_repondent_a_l_identique()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var interdit = await DemanderUrlAsync(client, Medias.PriveDuDeposant);
        var inexistant = await DemanderUrlAsync(client, Medias.Inexistant);

        interdit.StatusCode.Should().Be(inexistant.StatusCode);
        (await ErreurAsync(interdit)).Should().Be(await ErreurAsync(inexistant));
    }

    // ── ISSUE-021 : la durée appartient au serveur ──────────────────────────

    /// <summary>
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : UNE URL SIGNÉE VALABLE UN AN SUR
    /// UNE PIÈCE D'IDENTITÉ.
    /// </summary>
    [Fact]
    public async Task Une_duree_demandee_au_dela_du_plafond_est_ramenee_a_quinze_minutes()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant, duree: 31_536_000);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DureeAsync(response)).Should().Be(900);
    }

    /// <summary>LE PLANCHER COMPTE AUSSI, ET POUR UNE RAISON MOINS ÉVIDENTE.</summary>
    [Fact]
    public async Task Une_duree_trop_courte_est_relevee_au_plancher()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant, duree: 1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DureeAsync(response)).Should().Be(30);
    }

    /// <summary>
    /// Sans paramètre, le défaut d'avant la correction est conservé : cinq minutes.
    /// </summary>
    [Fact]
    public async Task Sans_parametre_la_duree_vaut_cinq_minutes()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DureeAsync(response)).Should().Be(300);
    }

    // ── ISSUE-021 : la suppression n'est plus offerte à tout le monde ────────

    /// <summary>
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : N'IMPORTE QUI EFFAÇAIT N'IMPORTE
    /// QUELLE PREUVE.
    /// </summary>
    [Fact]
    public async Task Un_compte_qui_n_a_pas_depose_le_media_ne_peut_pas_l_effacer()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PriveDuDeposant}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>LA DIFFÉRENCE ENTRE LIRE ET EFFACER, ET C'EST TOUT L'INTÉRÊT DE CE TEST.</summary>
    [Fact]
    public async Task Le_caractere_public_d_un_media_ne_donne_pas_le_droit_de_l_effacer()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PublicDuDeposant}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Le déposant FRANCHIT la garde — et rien de plus n'est affirmé ici.</summary>
    [Fact]
    public async Task Le_deposant_franchit_la_garde_de_suppression()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PriveDuDeposant}");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// L'administrateur aussi : c'est lui qui retire une pièce sur demande de
    /// suppression, quand le compte du déposant est déjà clos.
    /// </summary>
    [Fact]
    public async Task Un_administrateur_franchit_la_garde_de_suppression()
    {
        var client = _factory.CreateClientWithToken(
            TestTokens.Create(Comptes.Autre, ApiAuthorization.AdminRole));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PriveDuDeposant}");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    // ── Outillage ───────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> DemanderUrlAsync(
        HttpClient client, Guid media, int? duree = null)
    {
        var route = $"/api/v1/media/{media}/download-url";

        if (duree is { } valeur)
        {
            route += $"?expiresIn={valeur}";
        }

        return client.GetAsync(route);
    }

    /// <summary>
    /// La durée réellement retenue par le serveur, telle qu'elle repart au client.
    /// </summary>
    private static async Task<int> DureeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("expiresInSeconds").GetInt32();
    }

    /// <summary>Le bloc `error` de l'enveloppe, sans `meta` — qui varie par requête.</summary>
    private static async Task<string> ErreurAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetRawText();
    }
}

/// <summary>Les deux comptes de la démonstration : celui qui a déposé, et l'autre.</summary>
internal static class Comptes
{
    public static readonly Guid Deposant = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Autre = new("22222222-2222-2222-2222-222222222222");
}

/// <summary>Les médias du scénario.</summary>
internal static class Medias
{
    /// <summary>Privé, déposé par <see cref="Comptes.Deposant"/>.</summary>
    public static readonly Guid PriveDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>Public, déposé par <see cref="Comptes.Deposant"/>.</summary>
    public static readonly Guid PublicDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>Supprimé, déposé par <see cref="Comptes.Deposant"/>.</summary>
    public static readonly Guid SupprimeDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000003");

    /// <summary>Aucun média ne porte cet identifiant.</summary>
    public static readonly Guid Inexistant = new("aaaaaaaa-0000-0000-0000-00000000000f");
}

/// <summary>media-service en mémoire, avec un catalogue de médias connu d'avance.</summary>
public sealed class MediaFactory : AuthorizationTestFactory<Program>
{
    protected override void ConfigureTestDoubles(IServiceCollection services)
    {
        // `ConfigureTestServices` s'exécute APRÈS l'enregistrement du service : le
        // descripteur posé par le scan MediatR est bien là, et c'est celui-ci qu'on
        // retire.
        services.RemoveAll<IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>>();
        services.AddTransient<IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>, AccesMediaSimule>();

        services.RemoveAll<IMediaModuleApi>();
        services.AddScoped<IMediaModuleApi, StockageSimule>();
    }
}

/// <summary>Répond « qui a déposé ce média, est-il public, est-il supprimé » sans base.</summary>
internal sealed class AccesMediaSimule : IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>
{
    public Task<Result<MediaAccess>> Handle(GetMediaAccessQuery request, CancellationToken cancellationToken)
    {
        Result<MediaAccess> resultat;

        if (request.MediaId == Medias.PriveDuDeposant)
        {
            resultat = new MediaAccess(request.MediaId, Comptes.Deposant, IsPublic: false, IsDeleted: false);
        }
        else if (request.MediaId == Medias.PublicDuDeposant)
        {
            resultat = new MediaAccess(request.MediaId, Comptes.Deposant, IsPublic: true, IsDeleted: false);
        }
        else if (request.MediaId == Medias.SupprimeDuDeposant)
        {
            resultat = new MediaAccess(request.MediaId, Comptes.Deposant, IsPublic: false, IsDeleted: true);
        }
        else
        {
            resultat = Error.NotFound("media.not_found", "Média introuvable.");
        }

        return Task.FromResult(resultat);
    }
}

/// <summary>Stockage objet simulé.</summary>
internal sealed class StockageSimule : IMediaModuleApi
{
    public Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
        => Task.FromResult<MediaView?>(null);

    public Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaView>>([]);

    public Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaView>>([]);

    public Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default)
        => Task.FromResult<SignedMediaUrl?>(
            new SignedMediaUrl("https://stockage.test/objet?signature=factice", expiresSeconds));
}
