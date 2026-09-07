using System.Diagnostics;
using FluentAssertions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>LE VERROU VENDEUR SÉRIALISE VRAIMENT — CONTRE UNE VRAIE BASE.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
public sealed class VerrouVendeurTests
{
    /// <summary>
    /// Au-delà, on considère que le second appelant n'est pas bloqué mais bel et
    /// bien perdu.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly MerchantsIntegrationFixture _fixture;

    public VerrouVendeurTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>LE TEST QUI AURAIT ATTRAPÉ LE DÉFAUT.</summary>
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

    /// <summary>LA CONTRE-ÉPREUVE, ET ELLE COMPTE AUTANT QUE LA PREMIÈRE.</summary>
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

    /// <summary>UN ÉCHEC RELÂCHE LE VERROU, IL NE LE LAISSE PAS POSÉ.</summary>
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
    /// propre connexion.
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
