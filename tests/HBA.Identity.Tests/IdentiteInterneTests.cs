using System.Security.Cryptography;
using FluentAssertions;
using HBA.Shared.Hosting.Grpc;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ATTESTATION D'IDENTITÉ D'APPELANT gRPC.
///
/// CE QUE CES TESTS PROTÈGENT, ET POURQUOI RIEN D'AUTRE NE LE PROTÈGE.
///
/// Une vérification de signature qui accepte tout compile, démarre, et laisse
/// passer tous les appels — c'est-à-dire qu'elle se comporte EXACTEMENT comme
/// une vérification correcte tant que personne n'attaque. Le seul moment où la
/// différence se voit est celui où il est trop tard.
///
/// Chaque test ci-dessous fabrique donc un cas qui DOIT être refusé, et vérifie
/// qu'il l'est. Le test de bon fonctionnement (`Accepte…`) ne prouve rien tout
/// seul : c'est l'ensemble qui a du sens.
///
/// MÊME EMPLACEMENT ET MÊME RAISON QUE `DisjoncteurGrpcTests` :
/// `HBA.Shared.Hosting` n'a pas de projet de tests propre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class IdentiteInterneTests
{
    private const string Methode = "/hba.financial.v1.FinancialApi/RefundPayment";
    private const string Appelant = "HBA.Marketplace.ReturnRefund.Api";

    /// <summary>Une paire P-256 neuve, rendue sous la forme attendue par la configuration.</summary>
    private static (string Privee, string Publique) Paire()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
                Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void Accepte_une_attestation_frappee_a_l_instant()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().Be(Appelant);
    }

    /// <summary>
    /// LE CŒUR DU DISPOSITIF : UN JETON NE VAUT QUE POUR SA MÉTHODE.
    /// </summary>
    /// <remarks>
    /// Le réseau interne est en clair (voir `GrpcHostExtensions`). Une
    /// attestation est donc lisible par quiconque est en coupure. Si elle valait
    /// pour n'importe quel RPC, capter un appel anodin — `GetOffers` — donnerait
    /// le droit d'appeler `RefundPayment` pendant trente secondes. Ce lien est ce
    /// qui rend le rejeu à peu près inoffensif.
    /// </remarks>
    [Fact]
    public void Refuse_une_attestation_frappee_pour_une_autre_methode()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        var attestation = IdentiteInterne.Signer(
            Appelant, "/hba.catalog.v1.CatalogApi/GetOffers", privee);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    [Fact]
    public void Refuse_une_attestation_expiree()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        var frappe = DateTimeOffset.UtcNow.AddMinutes(-10);
        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee, frappe);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    /// <summary>
    /// UNE SIGNATURE VALIDE NE REND PAS UNE DATE RAISONNABLE.
    /// </summary>
    /// <remarks>
    /// Ce cas est celui d'un hôte légitime dont le CODE aurait été modifié pour
    /// frapper des laissez-passer permanents. La signature est authentique ; c'est
    /// la durée qui ne l'est pas. Le vérificateur est le seul à ne pas dépendre du
    /// bon comportement du signataire, donc le seul à pouvoir refuser cela.
    /// </remarks>
    [Fact]
    public void Refuse_une_attestation_dont_l_echeance_est_trop_lointaine()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        var frappe = DateTimeOffset.UtcNow.AddHours(1);
        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee, frappe);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    /// <summary>
    /// LE CAS QUE TOUT CE LOT EXISTE POUR FERMER.
    /// </summary>
    /// <remarks>
    /// Un service compromis connaît la clé interne partagée — elle est la même
    /// pour tous — et peut donc se présenter au port gRPC de n'importe qui. Ce
    /// qu'il n'a pas, c'est la clé privée de l'appelant qu'il veut usurper. Ici il
    /// signe avec la sienne et se dit `ReturnRefund` : refusé.
    /// </remarks>
    [Fact]
    public void Refuse_une_attestation_signee_par_une_autre_cle()
    {
        var (privee, _) = Paire();
        var (_, publiqueLegitime) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publiqueLegitime };

        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    [Fact]
    public void Refuse_un_appelant_absent_du_registre()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { ["HBA.Users.Api"] = publique };

        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    /// <summary>
    /// CHANGER LE NOM DANS LA CHARGE UTILE INVALIDE LA SIGNATURE.
    /// </summary>
    /// <remarks>
    /// C'est la propriété qu'on attend, mais elle mérite d'être éprouvée plutôt
    /// que supposée : une implémentation qui vérifierait la signature d'une
    /// charge RECALCULÉE au lieu de la charge REÇUE passerait tous les autres
    /// tests de ce fichier et n'authentifierait rien du tout.
    /// </remarks>
    [Fact]
    public void Refuse_une_charge_utile_modifiee()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string>
        {
            [Appelant] = publique,
            ["HBA.Users.Api"] = publique,
        };

        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee);

        var point = attestation.IndexOf('.');
        var charge = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(Rembourrer(attestation[..point])));

        var falsifiee = charge.Replace(Appelant, "HBA.Users.Api");

        var reforgee = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(falsifiee))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + attestation[point..];

        IdentiteInterne.Verifier(reforgee, Methode, registre).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas-une-attestation")]
    [InlineData(".")]
    [InlineData("a.")]
    [InlineData("!!!.!!!")]
    public void Refuse_une_attestation_malformee(string? attestation)
    {
        var (_, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    [Fact]
    public void Lit_un_registre_et_ignore_les_entrees_illisibles()
    {
        var registre = IdentiteInterne.LireRegistre("a=1; b=2 ;pas-de-signe-egal;=3;c=");

        registre.Should().HaveCount(2);
        registre["a"].Should().Be("1");
        registre["b"].Should().Be("2");
    }

    /// <summary>
    /// SAVOIR QUI APPELLE NE SUFFIT PAS — VOIR `AutorisationsGrpc`.
    /// </summary>
    [Fact]
    public void La_table_d_autorisations_reserve_le_remboursement_a_return_refund()
    {
        AutorisationsGrpc.EstAutorise(Appelant, Methode).Should().BeTrue();
        AutorisationsGrpc.EstAutorise("HBA.Users.Api", Methode).Should().BeFalse();
        AutorisationsGrpc.EstAutorise("HBA.Catalog.Api", Methode).Should().BeFalse();
    }

    /// <summary>
    /// Un appelant inconnu n'a AUCUN droit — il n'en a pas tous.
    /// </summary>
    [Fact]
    public void La_table_d_autorisations_ferme_par_defaut()
    {
        AutorisationsGrpc.EstAutorise("service-inexistant", Methode).Should().BeFalse();
        AutorisationsGrpc.Appelants.Should().Contain("HBA.Order.Api");
    }

    private static string Rembourrer(string valeur)
    {
        var brut = valeur.Replace('-', '+').Replace('_', '/');
        return brut.PadRight((brut.Length + 3) / 4 * 4, '=');
    }
}
