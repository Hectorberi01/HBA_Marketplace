using FluentAssertions;
using HBA.Shared.Hosting.Grpc;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>L'ÉCHÉANCE APPLIQUÉE AUX APPELS gRPC SORTANTS.</summary>
public sealed class EcheancesGrpcTests
{
    [Fact]
    public void Sans_surcharge_le_defaut_de_cinq_secondes_s_applique()
        => new EcheancesGrpcOptions()
            .Pour("hba.merchant.v1.MerchantApi")
            .Should().Be(TimeSpan.FromSeconds(5),
                "c'est la valeur qui existait en dur avant ce réglage : "
                + "le rendre configurable ne doit rien changer par défaut");

    [Fact]
    public void Une_surcharge_est_trouvee_par_le_NOM_COURT_du_service_proto()
    {
        var options = new EcheancesGrpcOptions
        {
            ParService = { ["MerchantApi"] = TimeSpan.FromMilliseconds(800) }
        };

        // C'est `context.Method.ServiceName` qui est passé à l'exécution, donc le
        // nom COMPLET. La clé, elle, est courte — parce qu'une clé à points n'est
        // pas un nom de variable d'environnement assignable sous bash.
        options.Pour("hba.merchant.v1.MerchantApi")
            .Should().Be(TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public void Un_autre_service_garde_le_defaut()
    {
        var options = new EcheancesGrpcOptions
        {
            ParService = { ["MerchantApi"] = TimeSpan.FromMilliseconds(800) }
        };

        options.Pour("hba.media.v1.MediaApi").Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Une_surcharge_nulle_ou_negative_est_IGNOREE_et_non_appliquee(int secondes)
    {
        var options = new EcheancesGrpcOptions
        {
            ParService = { ["MerchantApi"] = TimeSpan.FromSeconds(secondes) }
        };

        options.Pour("hba.merchant.v1.MerchantApi")
            .Should().Be(TimeSpan.FromSeconds(5),
                "une échéance nulle ferait expirer l'appel avant son départ, et la "
                + "panne serait imputée au service appelé");
    }

    [Fact]
    public void Un_nom_sans_point_est_accepte_tel_quel()
        => new EcheancesGrpcOptions { ParService = { ["MerchantApi"] = TimeSpan.FromSeconds(2) } }
            .Pour("MerchantApi")
            .Should().Be(TimeSpan.FromSeconds(2));

    [Fact]
    public void Un_nom_vide_retombe_sur_le_defaut_sans_lever()
        => new EcheancesGrpcOptions().Pour(string.Empty).Should().Be(TimeSpan.FromSeconds(5));
}
