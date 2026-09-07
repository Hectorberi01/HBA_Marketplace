using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HBA.Merchants.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>LE COMPTE DE REVERSEMENT, LU PAR L'API INTERNE — CONTRE UNE VRAIE BASE.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class CompteDeReversementTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public CompteDeReversementTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>Le compte déclaré par le vendeur est celui que l'API interne rend.</summary>
    [Fact]
    public async Task Le_compte_declare_est_rendu_tel_quel_a_l_api_interne()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Reversement {Guid.NewGuid():N}");
        await Parcours.FixerReversementAsync(vendeur);

        var payout = await LirePayoutAsync(vendeur.SellerId);

        payout.SellerExists.Should().BeTrue();
        payout.Account.Should().NotBeNull(
            "c'est très exactement ce qui valait null pour tout le monde et bloquait "
            + "chaque retrait de la plateforme");

        payout.Account!.Provider.Should().Be("MtnMomo");
        payout.Account.AccountNumber.Should().Be("97000000");
        payout.Account.AccountName.Should().Be("Kossi Adjovi");
    }

    /// <summary>« PAS ENCORE DÉCLARÉ » DOIT SE DISTINGUER DE « VENDEUR INCONNU ».</summary>
    [Fact]
    public async Task Un_vendeur_sans_compte_declare_n_est_pas_un_vendeur_inconnu()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Sans compte {Guid.NewGuid():N}");

        var payout = await LirePayoutAsync(vendeur.SellerId);

        payout.SellerExists.Should().BeTrue("le vendeur vient d'être inscrit");
        payout.Account.Should().BeNull("il n'a simplement pas encore déclaré de compte");
    }

    [Fact]
    public async Task Un_identifiant_qui_ne_designe_personne_rend_vendeur_inconnu()
    {
        var payout = await LirePayoutAsync(Guid.NewGuid());

        payout.SellerExists.Should().BeFalse();
        payout.Account.Should().BeNull();
    }

    /// <summary>UN COMPTE MODIFIÉ EST VISIBLE IMMÉDIATEMENT — PAS DIX MINUTES PLUS TARD.</summary>
    [Fact]
    public async Task Un_compte_corrige_est_lu_immediatement_sans_passer_par_le_cache()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Correction {Guid.NewGuid():N}");
        await Parcours.FixerReversementAsync(vendeur);

        // Une lecture d'abord : c'est elle qui remplirait le cache si ce chemin en
        // avait un.
        (await LirePayoutAsync(vendeur.SellerId)).Account!.AccountNumber.Should().Be("97000000");

        await Parcours.CorrigerReversementAsync(vendeur, "96000000");

        var payout = await LirePayoutAsync(vendeur.SellerId);

        payout.Account!.AccountNumber.Should().Be("96000000",
            "le vendeur vient de corriger son numéro : le versement suivant doit partir "
            + "là, et nulle part ailleurs");
    }

    /// <summary>
    /// Résout l'API interne dans une portée neuve — comme le fait le service gRPC à
    /// chaque appel.
    /// </summary>
    private async Task<SellerPayout> LirePayoutAsync(Guid sellerId)
    {
        // Force la construction de l'hôte avant d'y résoudre quoi que ce soit.
        _ = _fixture.CreateClient();

        using var portee = _fixture.Services.CreateScope();
        var api = portee.ServiceProvider.GetRequiredService<ISellerModuleApi>();

        return await api.GetSellerPayoutAsync(sellerId);
    }
    /// <summary>LA CONTRE-ÉPREUVE DU STEP-UP (§37).</summary>
    [Fact]
    public async Task Une_authentification_trop_ancienne_ne_repointe_pas_le_compte_de_versement()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"StepUp {Guid.NewGuid():N}");
        var ancien = Parcours.AvecAuthentificationAncienne(_fixture, vendeur);

        var reponse = await ancien.Client.PutAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/payout-account",
            new { provider = "MtnMomo", accountNumber = "97000001", accountName = "Kossi Adjovi" });

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reponse.Content.ReadAsStringAsync()).Should().Contain("reauthentication.required");
    }

}
