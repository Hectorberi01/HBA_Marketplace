using System.Diagnostics;
using FluentAssertions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE VERROU VENDEUR SÉRIALISE VRAIMENT — CONTRE UNE VRAIE BASE.
///
/// CES TESTS EXISTENT PARCE QUE LE VERROU N'A JAMAIS RIEN VERROUILLÉ.
///
/// `LockSellerAsync` prenait `pg_advisory_xact_lock` au milieu d'un handler, hors
/// de toute transaction. PostgreSQL traitait l'instruction comme la sienne : verrou
/// pris, validé, RELÂCHÉ — avant même la première lecture. Le commentaire invoquait
/// « l'intercepteur de transaction du module », qui n'existe pas.
///
/// Trois appelants s'appuyaient dessus, dont le transfert de propriété vendeur.
/// Rien ne l'a signalé pendant des mois, parce qu'un verrou qui ne tient pas ne
/// produit AUCUN symptôme tant que la course ne se produit pas — et qu'aucun test
/// ne faisait tourner deux transactions à la fois.
///
/// POURQUOI CE NIVEAU-LÀ, ET NON UN PARCOURS MÉTIER.
///
/// La règle métier — « un vendeur garde au moins un propriétaire actif » — se
/// prouverait mieux par deux révocations concurrentes. Mais elle ferait dépendre
/// la démonstration de tout le flux d'invitation, et son échec serait ambigu : le
/// verrou ? le décompte ? la garde du domaine ? Ici, la question posée est la
/// seule qui manquait : <b>ce verrou bloque-t-il un second appelant ?</b> La
/// réponse est observable directement, sans rien d'autre en jeu.
///
/// IL FAUT UNE VRAIE BASE. `ExecuteUnderSellerLockAsync` ne pose ni verrou ni
/// transaction hors PostgreSQL — les tests en mémoire n'auraient rien éprouvé du
/// tout, et auraient été verts.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
public sealed class VerrouVendeurTests
{
    /// <summary>
    /// Au-delà, on considère que le second appelant n'est pas bloqué mais bel et
    /// bien perdu. Assez long pour absorber la latence d'un conteneur qui démarre,
    /// assez court pour qu'un test cassé ne fasse pas attendre la suite.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly MerchantsIntegrationFixture _fixture;

    public VerrouVendeurTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// LE TEST QUI AURAIT ATTRAPÉ LE DÉFAUT.
    ///
    /// Deux opérations sur LE MÊME vendeur, lancées ensemble. La première entre et
    /// s'arrête ; la seconde doit rester dehors tant que la première n'a pas rendu
    /// la main. Avec l'ancienne écriture, la seconde entrait immédiatement — le
    /// verrou étant déjà relâché — et ce test aurait échoué sur `BeFalse`.
    /// </summary>
    [Fact]
    public async Task Deux_operations_sur_le_meme_vendeur_sont_serialisees()
    {
        var vendeurId = Guid.NewGuid();

        var premiereEstEntree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onRelacheLaPremiere = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondeEstEntree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var premiere = ExecuterAsync(vendeurId, async _ =>
        {
            premiereEstEntree.SetResult();
            await onRelacheLaPremiere.Task;
            return Result.Success();
        });

        await premiereEstEntree.Task.WaitAsync(Patience);

        var seconde = ExecuterAsync(vendeurId, _ =>
        {
            secondeEstEntree.SetResult();
            return Task.FromResult(Result.Success());
        });

        // L'ATTENTE EST LE CŒUR DU TEST, PAS UNE PRÉCAUTION.
        //
        // On laisse à la seconde tout le temps d'entrer si rien ne la retient. Une
        // demi-seconde est très au-delà de ce qu'il faut pour ouvrir une
        // transaction et poser un verrou consultatif sur une base locale — si elle
        // n'est pas entrée, c'est qu'elle est bloquée, et non qu'elle est lente.
        var entreeTrop_tot = await Task.WhenAny(
            secondeEstEntree.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));

        entreeTrop_tot.Should().NotBeSameAs(
            secondeEstEntree.Task,
            "la seconde opération doit rester dehors tant que la première tient le verrou — "
            + "c'est exactement ce que l'ancien `LockSellerAsync` ne faisait pas");

        onRelacheLaPremiere.SetResult();

        (await premiere).IsSuccess.Should().BeTrue();
        (await seconde).IsSuccess.Should().BeTrue();

        await secondeEstEntree.Task.WaitAsync(Patience);
    }

    /// <summary>
    /// LA CONTRE-ÉPREUVE, ET ELLE COMPTE AUTANT QUE LA PREMIÈRE.
    ///
    /// Un verrou qui sérialiserait TOUS les vendeurs passerait le test précédent et
    /// mettrait la plateforme à genoux : chaque mutation d'équipe attendrait celles
    /// de tous les autres commerçants. La clé est dérivée des huit premiers octets
    /// du GUID ; ce test vérifie que deux vendeurs distincts ne s'attendent pas.
    ///
    /// CE QU'IL NE PROUVE PAS : l'absence de collision. Deux GUID peuvent
    /// partager leurs huit premiers octets et donc leur clé de verrou. C'est admis
    /// et sans conséquence de correction — au pire deux commerçants sérialisent
    /// leurs mutations pendant quelques millisecondes. Ce test emploie des GUID
    /// tirés au hasard : la collision ne s'y produira pas.
    /// </summary>
    [Fact]
    public async Task Deux_vendeurs_differents_ne_s_attendent_pas()
    {
        var premierVendeur = Guid.NewGuid();
        var secondVendeur = Guid.NewGuid();

        var premiereEstEntree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onRelacheLaPremiere = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondeEstEntree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var premiere = ExecuterAsync(premierVendeur, async _ =>
        {
            premiereEstEntree.SetResult();
            await onRelacheLaPremiere.Task;
            return Result.Success();
        });

        await premiereEstEntree.Task.WaitAsync(Patience);

        var seconde = ExecuterAsync(secondVendeur, _ =>
        {
            secondeEstEntree.SetResult();
            return Task.FromResult(Result.Success());
        });

        // Elle doit entrer SANS attendre la première : c'est la différence avec le
        // test précédent, et la seule chose qui distingue un verrou par vendeur
        // d'un verrou global.
        await secondeEstEntree.Task.WaitAsync(Patience);

        onRelacheLaPremiere.SetResult();

        (await premiere).IsSuccess.Should().BeTrue();
        (await seconde).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// UN ÉCHEC RELÂCHE LE VERROU, IL NE LE LAISSE PAS POSÉ.
    ///
    /// C'est la raison d'être de la variante `_xact_` : PostgreSQL la relâche au
    /// `ROLLBACK` comme au `COMMIT`. Avec la variante de SESSION, un chemin d'échec
    /// laisserait le verrou en place et bloquerait toute l'équipe de ce vendeur
    /// jusqu'au redémarrage du service — panne dont on ne soupçonnerait pas la
    /// cause.
    /// </summary>
    [Fact]
    public async Task Un_echec_relache_le_verrou()
    {
        var vendeurId = Guid.NewGuid();

        var echec = await ExecuterAsync(vendeurId, _ => Task.FromResult(
            Result.Failure(Error.Conflict("test.refus", "Refus délibéré."))));

        echec.IsFailure.Should().BeTrue();

        // Si le verrou était resté posé, cet appel n'aboutirait jamais.
        var chrono = Stopwatch.StartNew();
        var suivant = await ExecuterAsync(vendeurId, _ => Task.FromResult(Result.Success()))
            .WaitAsync(Patience);
        chrono.Stop();

        suivant.IsSuccess.Should().BeTrue();
        chrono.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "le verrou tombe avec la transaction annulée, il n'y a rien à attendre");
    }

    /// <summary>
    /// Chaque appel dans SA PROPRE portée — donc son propre `DbContext` et sa
    /// propre connexion. Deux opérations qui partageraient un contexte
    /// partageraient sa transaction, et il n'y aurait aucune course à observer :
    /// le test passerait en ne prouvant rien.
    /// </summary>
    private async Task<Result> ExecuterAsync(
        Guid vendeurId, Func<CancellationToken, Task<Result>> operation)
    {
        // Force la construction de l'hôte avant d'y résoudre quoi que ce soit.
        _ = _fixture.CreateClient();

        using var portee = _fixture.Services.CreateScope();
        var uniteDeTravail = portee.ServiceProvider.GetRequiredService<ISellerUnitOfWork>();

        return await uniteDeTravail.ExecuteUnderSellerLockAsync(
            vendeurId, operation, CancellationToken.None);
    }
}
