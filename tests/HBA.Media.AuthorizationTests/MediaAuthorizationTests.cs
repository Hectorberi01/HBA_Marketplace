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
/// media-service : personne n'obtient d'URL signée sur le fichier d'un autre, et
/// la durée de validité appartient au serveur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE SUITE COUVRE, ET CE QU'ELLE NE COUVRE PAS.
///
/// Elle éprouve DEUX choses, et il faut les distinguer parce qu'elles ne
/// reposent pas sur le même mécanisme :
///
///   1. LE PIPELINE. 401 sans jeton, anonymat des sondes. Ces décisions se
///      prennent avant le handler, sans une ligne de code métier — c'est la
///      raison pour laquelle la fabrique tient sans base (voir son encadré).
///
///   2. LA GARDE DE PROPRIÉTÉ (ISSUE-020 / ISSUE-021). Elle, s'exécute DANS le
///      handler, après la lecture du média. La fabrique n'a pas de base : sans
///      rien de plus, la requête mourrait en 500 avant d'atteindre `PeutAcceder`,
///      et la garde ne serait jamais éprouvée. `MediaFactory` substitue donc le
///      SEUL maillon qui a besoin de PostgreSQL — le gestionnaire de
///      `GetMediaAccessQuery` — et laisse tourner tout le reste pour de vrai :
///      `PeutAcceder`, le `Math.Clamp` de l'endpoint, le mappage du refus en 404.
///
/// CE QUI RESTE HORS DE PORTÉE ICI. À NE PAS CROIRE COUVERT.
///
///   • LA LECTURE EN BASE ELLE-MÊME. Le vrai `GetMediaAccessQueryHandler` traduit
///     `Visibility == Public` en `IsPublic` et `Status == Deleted` en `IsDeleted`.
///     C'est lui qui est remplacé : si ce mappage s'inversait, TOUS les tests
///     ci-dessous passeraient encore, et n'importe qui lirait les pièces KYB.
///     Le couvrir demande un test d'intégration avec base, sur le modèle de
///     `tests/HBA.Catalog.IntegrationTests`.
///
///   • LE SECOND PLAFOND, CELUI DE L'INFRASTRUCTURE.
///     `MediaModuleApi.CreateSignedUrlAsync` reborne la durée pour les appelants
///     qui ne passent pas par HTTP — le service gRPC, les modules qui résolvent
///     `IMediaModuleApi` en direct. Cette classe est `internal`, exige un
///     `MediaDbContext` et un `IObjectStorage`, et c'est précisément elle que
///     `MediaFactory` remplace : seul le plafond de l'ENDPOINT est prouvé ici.
///     Une régression sur le clamp de l'infrastructure passerait inaperçue.
///
///   • L'EFFET DE LA SUPPRESSION. On prouve que le déposant FRANCHIT la garde,
///     pas que la ligne disparaît : `DeleteMediaCommand` touche la base et meurt
///     en 500 dans ce harnais. C'est la décision d'autorisation qui est éprouvée,
///     jamais la persistance.
///
///   • LES DEUX ROUTES SANS GARDE. `GET /{id}` et `POST /{id}/reprocess` ne
///     vérifient aujourd'hui aucune propriété : tout compte inscrit lit les
///     métadonnées d'un média privé (nom de fichier d'origine, propriétaire,
///     taille) et peut relancer le traitement de n'importe lequel. Aucun test
///     ici ne l'affirme, DANS UN SENS COMME DANS L'AUTRE : écrire une assertion
///     sur le comportement actuel le graverait dans le marbre, et ce n'est pas
///     un comportement voulu — c'est une suite d'ISSUE-020 qui n'a pas été
///     traitée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MediaAuthorizationTests : IClassFixture<MediaFactory>
{
    private readonly MediaFactory _factory;

    public MediaAuthorizationTests(MediaFactory factory) => _factory = factory;

    // ── Le pipeline ─────────────────────────────────────────────────────────

    /// <summary>
    /// AUCUNE ROUTE ANONYME DANS CE SERVICE, ET C'EST UNE RÈGLE ÉCRITE.
    ///
    /// Le bandeau de `MediaEndpoints` la pose : téléverser coûte du stockage et de
    /// la bande passante, une route d'upload ouverte c'est un disque rempli par un
    /// inconnu en une nuit. Ce test échoue si `MapAuthenticatedGroup` redevient un
    /// `MapGroup` nu, ou si un `AllowAnonymous` apparaît sur l'une des cinq routes.
    /// </summary>
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

    /// <summary>
    /// LE TEST CI-DESSUS NE PROUVE PAS QU'UNE ROUTE EXISTE. CELUI-CI SI.
    ///
    /// La `FallbackPolicy` s'applique aussi quand AUCUN point de terminaison n'a
    /// été trouvé : un chemin inventé rend 401 sans jeton, exactement comme une
    /// route réelle. Un 401 seul est donc compatible avec « la route a été
    /// supprimée par mégarde », et une suite qui n'aurait que le test précédent
    /// resterait verte sur un service dont tout le routage aurait disparu.
    ///
    /// Avec un jeton, la politique de repli est satisfaite et le routage tranche :
    /// un 404 signifierait que la route n'est plus là. C'est la seule chose
    /// affirmée ici — le code obtenu par ailleurs (500 faute de base, 415 sur un
    /// upload sans multipart) ne prouve rien d'autre, conformément au corollaire
    /// d'`AuthorizationTestFactory`.
    ///
    /// `GET /{id}` est volontairement absent de la liste : la fabrique lui fait
    /// rendre un 404 LÉGITIME — voir `StockageSimule.GetAsync` — et ce 404-là ne
    /// se distinguerait pas de celui d'une route disparue.
    /// </summary>
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
    ///
    /// Un `/health/live` en 401, et Docker déclare le conteneur malsain puis le
    /// redémarre en boucle, sans une seule erreur applicative dans les journaux.
    /// </summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// L'ASSERTION PORTE SUR L'ANONYMAT, PAS SUR LA SANTÉ.
    ///
    /// `/health/ready` sonde la base, absente ici : elle rend 503, et c'est le
    /// comportement juste. Ce qu'on éprouve est qu'elle réponde SANS jeton — un
    /// 401 sortirait le service de la rotation Kubernetes pour une raison qui
    /// n'a rien à voir avec sa disponibilité.
    /// </summary>
    [Fact]
    public async Task La_sonde_de_disponibilite_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    // ── ISSUE-020 : l'URL signée n'est plus délivrée à n'importe qui ─────────

    /// <summary>
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : LES PIÈCES KYB LISIBLES PAR
    ///    TOUT COMPTE INSCRIT.
    ///
    /// `GET /{id}/download-url` était authentifiée et ne vérifiait rien d'autre.
    /// Un identifiant de média glané dans une réponse d'API — ou deviné — suffisait
    /// à obtenir une URL signée sur une carte d'identité, un registre de commerce
    /// ou une preuve de livraison. Le seul obstacle était de connaître un GUID.
    ///
    /// Ce test échoue si `PeutAcceder` disparaît de `DownloadUrlAsync`, ou si sa
    /// condition sur `CreatedByUserId` se relâche.
    /// </summary>
    [Fact]
    public async Task Un_compte_qui_n_a_pas_depose_le_media_prive_n_obtient_pas_d_url_signee()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Le déposant, lui, garde l'accès à son propre fichier.
    ///
    /// CE TEST EST LE CONTREPOIDS DES AUTRES, ET IL EST INDISPENSABLE.
    ///
    /// Une garde qui refuse TOUT LE MONDE passerait chacun des refus ci-dessus
    /// sans en manquer un seul — et casserait le téléversement de pièces KYB en
    /// production sans qu'aucun test ne bronche. Un refus n'est une bonne
    /// nouvelle que si l'accès légitime reste ouvert.
    /// </summary>
    [Fact]
    public async Task Le_deposant_obtient_l_url_signee_de_son_media_prive()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// UN MÉDIA PUBLIC DOIT RESTER SIGNABLE PAR TOUS, SINON LES VITRINES
    ///    S'ÉTEIGNENT.
    ///
    /// Une photo de produit est déjà lisible par son URL publique permanente :
    /// refuser sa signature n'ajouterait aucune protection et casserait l'affichage
    /// des boutiques. Resserrer `PeutAcceder` sur le seul déposant — la correction
    /// « évidente » que quelqu'un écrira un jour en relisant ISSUE-020 — ferait
    /// échouer ce test avant la mise en production.
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

    /// <summary>
    /// UN MÉDIA SUPPRIMÉ NE SE SIGNE PLUS, MÊME POUR CELUI QUI L'A DÉPOSÉ.
    ///
    /// Les octets survivent à la suppression le temps de la rétention légale.
    /// Les CONSERVER est une obligation ; les SERVIR n'en est pas une. Sans le
    /// premier test de `PeutAcceder` — `if (acces.IsDeleted) return false` — une
    /// pièce d'identité retirée à la demande de son propriétaire resterait
    /// téléchargeable par lui, donc par quiconque obtiendrait son jeton.
    /// </summary>
    [Fact]
    public async Task Un_media_supprime_ne_se_signe_plus_meme_pour_son_deposant()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.SupprimeDuDeposant);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// LE REFUS NE DOIT PAS SE DISTINGUER DE L'ABSENCE — RÈGLE §29.
    ///
    /// Un 403 sur le média d'autrui et un 404 sur un média inexistant feraient de
    /// cette route un oracle : on tire des GUID au hasard, on garde ceux qui
    /// répondent 403, et l'on sait exactement quels dossiers existent. Sur des
    /// pièces d'identité, savoir qu'un fichier EXISTE est déjà une fuite.
    ///
    /// L'assertion compare les deux réponses ENTRE ELLES plutôt qu'à un code
    /// littéral : c'est l'indiscernabilité qui protège, pas le nombre 404.
    /// `meta.requestId` diffère forcément d'une requête à l'autre — seul le bloc
    /// `error` est comparé.
    /// </summary>
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
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : UNE URL SIGNÉE VALABLE UN AN
    ///    SUR UNE PIÈCE D'IDENTITÉ.
    ///
    /// `expiresIn` arrivait tel quel du client. Une URL signée ne circule pas dans
    /// le vide : elle finit dans un historique de navigateur, un en-tête `Referer`,
    /// un journal de mandataire. Sa DURÉE COURTE est ce qui rend la signature
    /// acceptable ; sans plafond serveur, la signature ne protège plus rien.
    ///
    /// Le plafond est de quinze minutes (`DureeSignatureMax`). Ce test échoue si
    /// le `Math.Clamp` de `DownloadUrlAsync` est retiré ou si la borne haute est
    /// desserrée.
    /// </summary>
    [Fact]
    public async Task Une_duree_demandee_au_dela_du_plafond_est_ramenee_a_quinze_minutes()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Deposant));

        var response = await DemanderUrlAsync(client, Medias.PriveDuDeposant, duree: 31_536_000);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DureeAsync(response)).Should().Be(900);
    }

    /// <summary>
    /// LE PLANCHER COMPTE AUSSI, ET POUR UNE RAISON MOINS ÉVIDENTE.
    ///
    /// Une durée d'une seconde n'est pas une attaque, c'est une panne : le mobile
    /// reçoit l'URL, l'affiche, et l'image est déjà expirée. Le symptôme est une
    /// vignette vide, intermittente, imputée au réseau pendant des semaines.
    /// Trente secondes est le plancher (`DureeSignatureMin`).
    /// </summary>
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
    /// Le plafonnement ne devait rien changer aux appelants existants — c'est ce
    /// qui rendait la correction déployable sans coordonner les clients.
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
    /// LA PANNE QUE CE TEST EMPÊCHE DE REVENIR : N'IMPORTE QUI EFFAÇAIT
    ///    N'IMPORTE QUELLE PREUVE.
    ///
    /// `DELETE /media/{id}` était authentifiée et ne vérifiait rien d'autre. Une
    /// pièce KYB, une preuve de livraison, un document de retour : ce sont des
    /// ÉLÉMENTS DE PREUVE, et un compte inscrit quelconque les supprimait avec un
    /// GUID. La perte est irréversible et se découvre le jour du litige.
    /// </summary>
    [Fact]
    public async Task Un_compte_qui_n_a_pas_depose_le_media_ne_peut_pas_l_effacer()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PriveDuDeposant}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// LA DIFFÉRENCE ENTRE LIRE ET EFFACER, ET C'EST TOUT L'INTÉRÊT DE CE TEST.
    ///
    /// Le même média public se signe pour tout le monde (test plus haut) et ne
    /// s'efface que pour son déposant. Une photo de produit est lisible par tous ;
    /// elle n'appartient pas à tous. La tentation de réutiliser `PeutAcceder` pour
    /// la suppression est réelle — les deux gardes se ressemblent à une ligne
    /// près — et ce serait rouvrir à tout compte inscrit le droit de vider la
    /// vitrine d'un concurrent.
    /// </summary>
    [Fact]
    public async Task Le_caractere_public_d_un_media_ne_donne_pas_le_droit_de_l_effacer()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(Comptes.Autre));

        var response = await client.DeleteAsync($"/api/v1/media/{Medias.PublicDuDeposant}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Le déposant FRANCHIT la garde — et rien de plus n'est affirmé ici.
    ///
    /// L'ASSERTION EST `NotBe(NotFound)` ET NON `Be(NoContent)`.
    ///
    /// Une fois la garde passée, `DeleteMediaCommand` cherche sa base et échoue.
    /// Le 500 qui en résulte est, dans ce harnais, la PREUVE que le contrôle a été
    /// franchi — voir le corollaire d'`AuthorizationTestFactory`. Écrire
    /// `Be(NoContent)` exigerait une base : ce serait un test d'intégration, pas
    /// celui-ci.
    /// </summary>
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
    /// `StockageSimule` renvoie le nombre de secondes qu'on lui passe : ce champ
    /// est donc la valeur SORTIE du `Math.Clamp` de l'endpoint, pas celle demandée.
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

/// <summary>
/// Les médias du scénario.
///
/// L'IDENTIFIANT PORTE LE SCÉNARIO, ET IL N'Y A AUCUN ÉTAT PARTAGÉ.
///
/// La variante évidente — un objet mutable que chaque test remplit avant sa
/// requête — obligerait à sérialiser toute la classe et casserait au premier
/// test ajouté dans une seconde classe partageant la fabrique. Ici, le
/// gestionnaire simulé répond d'après le GUID qu'on lui passe : les tests sont
/// indépendants, réordonnables et parallélisables.
/// </summary>
internal static class Medias
{
    /// <summary>Privé, déposé par <see cref="Comptes.Deposant"/>. Le cas de la pièce KYB.</summary>
    public static readonly Guid PriveDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>Public, déposé par <see cref="Comptes.Deposant"/>. Le cas de la photo de produit.</summary>
    public static readonly Guid PublicDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>Supprimé, déposé par <see cref="Comptes.Deposant"/>. Ses octets sont encore là.</summary>
    public static readonly Guid SupprimeDuDeposant = new("aaaaaaaa-0000-0000-0000-000000000003");

    /// <summary>Aucun média ne porte cet identifiant.</summary>
    public static readonly Guid Inexistant = new("aaaaaaaa-0000-0000-0000-00000000000f");
}

/// <summary>media-service en mémoire, avec un catalogue de médias connu d'avance.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE FABRIQUE SUBSTITUE UN GESTIONNAIRE MEDIATR, PAS SEULEMENT DES
///    CLIENTS DE VOISINS. C'EST DÉLIBÉRÉ ET CIBLÉ.
///
/// Les autres suites d'autorisation n'éprouvent que des décisions prises AVANT
/// le handler ; elles n'ont rien à remplacer. Ici, la correction d'ISSUE-020 et
/// d'ISSUE-021 vit DANS le handler, après la lecture du média. Sans substitution,
/// la requête mourrait en 500 sur PostgreSQL absent, et pas une ligne de
/// `PeutAcceder` ne serait jamais exécutée : la suite serait verte et ne
/// prouverait rien.
///
/// Deux enregistrements sont remplacés, et deux seulement :
///
///   • `IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>` — le SEUL
///     maillon de la chaîne qui a besoin d'une base. On lui substitue un
///     catalogue en dur. Tout ce qui suit — `PeutAcceder`, le `Math.Clamp`, le
///     mappage du refus en 404 — s'exécute pour de vrai.
///
///   • `IMediaModuleApi` — le vrai résout un `MediaDbContext` et un stockage
///     objet. Le sien renvoie l'URL signée qu'on lui demande, en écho, ce qui
///     rend la durée retenue OBSERVABLE depuis le corps de la réponse.
///
/// CE QUE CETTE SUBSTITUTION COÛTE EN COUVERTURE est écrit en tête de
/// `MediaAuthorizationTests` : le mappage base → `MediaAccess` et le plafond de
/// l'infrastructure ne sont PAS éprouvés ici. Les croire couverts serait
/// exactement l'erreur que ces tests existent pour empêcher.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MediaFactory : AuthorizationTestFactory<Program>
{
    protected override void ConfigureTestDoubles(IServiceCollection services)
    {
        // `ConfigureTestServices` s'exécute APRÈS l'enregistrement du service :
        // le descripteur posé par le scan MediatR est bien là, et c'est celui-ci
        // qu'on retire. Sans le `RemoveAll`, les deux resteraient inscrits et la
        // résolution dépendrait de l'ordre — un test vert par hasard.
        services.RemoveAll<IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>>();
        services.AddTransient<IRequestHandler<GetMediaAccessQuery, Result<MediaAccess>>, AccesMediaSimule>();

        services.RemoveAll<IMediaModuleApi>();
        services.AddScoped<IMediaModuleApi, StockageSimule>();
    }
}

/// <summary>
/// Répond « qui a déposé ce média, est-il public, est-il supprimé » sans base.
///
/// IL REND LA MÊME ERREUR QUE LE VRAI SUR UN MÉDIA INCONNU —
/// `Error.NotFound("media.not_found", …)`. Une erreur d'un autre type sortirait
/// en 500 ou en 400, et le test d'indiscernabilité comparerait deux réponses qui
/// ne sont pas celles que produit le service.
/// </summary>
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

/// <summary>
/// Stockage objet simulé.
///
/// <c>CreateSignedUrlAsync</c> RENVOIE EN ÉCHO LA DURÉE QU'ON LUI PASSE, et ne
/// la reborne PAS — contrairement au vrai `MediaModuleApi`. C'est ce qui rend
/// visible le plafond de l'ENDPOINT : si les deux bornaient, un `Math.Clamp`
/// retiré de `DownloadUrlAsync` resterait invisible, masqué par le second rideau.
/// Le prix à payer est que le second rideau, lui, n'est pas couvert ici.
///
/// <c>GetAsync</c> rend `null` : ce module ne connaît aucun média, et
/// `GET /{id}` répond donc 404 — un 404 légitime, dont le test de routage tient
/// compte.
/// </summary>
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
