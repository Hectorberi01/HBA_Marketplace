using System.Security.Cryptography;
using FluentAssertions;
using HBA.Shared.Hosting.Grpc;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>L'ATTESTATION D'IDENTITÉ D'APPELANT gRPC.</summary>
public sealed class IdentiteInterneTests
{
    private const string Methode = "/hba.financial.v1.FinancialApi/RefundPayment";
    private const string Appelant = "HBA.Marketplace.ReturnRefund.Api";

    /// <summary>
    /// Une paire P-256 neuve, rendue sous la forme attendue par la configuration.
    /// </summary>
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

    /// <summary>LE CŒUR DU DISPOSITIF : UN JETON NE VAUT QUE POUR SA MÉTHODE.</summary>
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

    /// <summary>UNE SIGNATURE VALIDE NE REND PAS UNE DATE RAISONNABLE.</summary>
    [Fact]
    public void Refuse_une_attestation_dont_l_echeance_est_trop_lointaine()
    {
        var (privee, publique) = Paire();
        var registre = new Dictionary<string, string> { [Appelant] = publique };

        var frappe = DateTimeOffset.UtcNow.AddHours(1);
        var attestation = IdentiteInterne.Signer(Appelant, Methode, privee, frappe);

        IdentiteInterne.Verifier(attestation, Methode, registre).Should().BeNull();
    }

    /// <summary>LE CAS QUE TOUT CE LOT EXISTE POUR FERMER.</summary>
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

    /// <summary>CHANGER LE NOM DANS LA CHARGE UTILE INVALIDE LA SIGNATURE.</summary>
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

    /// <summary>SAVOIR QUI APPELLE NE SUFFIT PAS — VOIR `AutorisationsGrpc`.</summary>
    [Fact]
    public void La_table_d_autorisations_reserve_le_remboursement_a_return_refund()
    {
        AutorisationsGrpc.EstAutorise(Appelant, Methode).Should().BeTrue();
        AutorisationsGrpc.EstAutorise("HBA.Users.Api", Methode).Should().BeFalse();
        AutorisationsGrpc.EstAutorise("HBA.Catalog.Api", Methode).Should().BeFalse();
    }

    /// <summary>Un appelant inconnu n'a AUCUN droit — il n'en a pas tous.</summary>
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
