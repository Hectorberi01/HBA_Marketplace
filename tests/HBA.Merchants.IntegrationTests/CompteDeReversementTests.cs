using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HBA.Merchants.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTE DE REVERSEMENT, LU PAR L'API INTERNE — CONTRE UNE VRAIE BASE.
///
/// CE CHEMIN N'AVAIT AUCUN TEST, ET SON ABSENCE A COÛTÉ TOUT LE RETRAIT VENDEUR.
///
/// wallet-service lisait `SellerSummary.Payout`. Le champ existe sur le record
/// C# ; le proto gRPC ne le transporte pas. À distance il valait donc `null` pour
/// TOUS les vendeurs, et chaque demande de retrait était refusée par « Aucun
/// compte de versement Mobile Money configuré » — message que le vendeur lisait
/// avec son numéro MTN sous les yeux. La validation administrative d'une demande
/// existante, elle, échouait AVEC remboursement, sur le même motif faux.
///
/// Rien ne l'avait vu, parce que rien n'avait jamais demandé ce compte à travers
/// un chemin réel. Ces trois cas le demandent.
///
/// ILS PASSENT PAR `ISellerModuleApi` RÉSOLU DANS L'HÔTE, PAS PAR LE DÉPÔT.
///
/// C'est l'implémentation in-process — celle que le service gRPC appelle pour
/// servir le RPC. Tester le dépôt à la place éprouverait la requête et pas le
/// contrat : c'est le contrat qui a menti.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class CompteDeReversementTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public CompteDeReversementTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Le compte déclaré par le vendeur est celui que l'API interne rend.
    /// </summary>
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

    /// <summary>
    /// « PAS ENCORE DÉCLARÉ » DOIT SE DISTINGUER DE « VENDEUR INCONNU ».
    ///
    /// Les deux rendaient `null`, et cette confusion est la moitié du défaut : le
    /// support ne pouvait pas distinguer un vendeur à qui il manque une étape
    /// d'onboarding d'un identifiant qui ne désigne personne — ni, du coup, d'un
    /// champ que le transport avait perdu en route. `SellerPayout` existe pour
    /// rendre les trois cas impossibles à confondre.
    /// </summary>
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

    /// <summary>
    /// UN COMPTE MODIFIÉ EST VISIBLE IMMÉDIATEMENT — PAS DIX MINUTES PLUS TARD.
    ///
    /// `GetSellerAsync` met sa réponse en cache : un nom de boutique périmé n'a
    /// jamais fait de mal. Un NUMÉRO MOBILE MONEY périmé, si — c'est l'argent
    /// envoyé à l'ancien numéro d'un vendeur qui vient de corriger une faute de
    /// frappe. `GetSellerPayoutAsync` ne passe donc pas par le cache, et ce test
    /// est ce qui empêchera quelqu'un de l'y remettre « par symétrie ».
    /// </summary>
    [Fact]
    public async Task Un_compte_corrige_est_lu_immediatement_sans_passer_par_le_cache()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Correction {Guid.NewGuid():N}");
        await Parcours.FixerReversementAsync(vendeur);

        // Une lecture d'abord : c'est elle qui remplirait le cache si ce chemin en
        // avait un. Sans elle, le test passerait même avec un cache.
        (await LirePayoutAsync(vendeur.SellerId)).Account!.AccountNumber.Should().Be("97000000");

        await Parcours.CorrigerReversementAsync(vendeur, "96000000");

        var payout = await LirePayoutAsync(vendeur.SellerId);

        payout.Account!.AccountNumber.Should().Be("96000000",
            "le vendeur vient de corriger son numéro : le versement suivant doit partir "
            + "là, et nulle part ailleurs");
    }

    /// <summary>
    /// Résout l'API interne dans une portée neuve — comme le fait le service gRPC
    /// à chaque appel.
    /// </summary>
    private async Task<SellerPayout> LirePayoutAsync(Guid sellerId)
    {
        // Force la construction de l'hôte avant d'y résoudre quoi que ce soit.
        _ = _fixture.CreateClient();

        using var portee = _fixture.Services.CreateScope();
        var api = portee.ServiceProvider.GetRequiredService<ISellerModuleApi>();

        return await api.GetSellerPayoutAsync(sellerId);
    }
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CONTRE-ÉPREUVE DU STEP-UP (§37).
    ///
    /// Repointer un compte de versement détourne tous les virements à venir. C'est
    /// exactement ce qu'on fait d'un poste laissé ouvert au marché : la permission
    /// dit que le rôle a le droit, pas que le titulaire est devant l'écran.
    ///
    /// Ce test existe parce que le reste de la suite porte désormais un `auth_time`
    /// frais. Sans lui, ajouter ce claim reviendrait à débrancher le contrôle dans
    /// tout le dépôt sans que rien ne le signale — le mode de défaillance le plus
    /// coûteux de cette architecture.
    ///
    /// Le refus doit être IDENTIFIABLE : une application mobile qui reçoit ce 403
    /// doit savoir qu'il faut redemander le mot de passe, et non afficher
    /// « vous n'avez pas les droits ».
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
