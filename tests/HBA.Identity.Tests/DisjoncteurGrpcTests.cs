using FluentAssertions;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>CE QUI COMPTE COMME UNE PANNE POUR LE DISJONCTEUR gRPC (lot 8.8).</summary>
public sealed class DisjoncteurGrpcTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.DataLoss)]
    [InlineData(StatusCode.Unknown)]
    public void Une_panne_du_service_appele_compte(StatusCode statut)
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(statut)).Should().BeTrue(
            "{0} décrit un service qui ne rend pas le service attendu", statut);

    /// <summary>LE TEST QUI COMPTE LE PLUS.</summary>
    [Theory]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.OutOfRange)]
    public void Un_refus_metier_ne_compte_pas(StatusCode statut)
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(statut)).Should().BeFalse(
            "{0} est une réponse du service, pas une panne du service", statut);

    /// <summary>`Cancelled` VIENT DE L'APPELANT, PAS DE L'APPELÉ.</summary>
    [Fact]
    public void Une_annulation_par_l_appelant_ne_compte_pas()
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(StatusCode.Cancelled)).Should().BeFalse();

    /// <summary>`Unauthenticated` EST UNE FAUTE DE CONFIGURATION, PAS UNE PANNE.</summary>
    [Fact]
    public void Une_cle_interne_refusee_ne_compte_pas()
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(StatusCode.Unauthenticated)).Should().BeFalse();

    /// <summary>CE QUI N'EST PAS UNE `RpcException` N'EST PAS COMPTÉ.</summary>
    [Fact]
    public void Une_exception_locale_ne_compte_pas()
    {
        DisjoncteurClientInterceptor.EstUnePanne(new InvalidOperationException()).Should().BeFalse();
        DisjoncteurClientInterceptor.EstUnePanne(null).Should().BeFalse();
    }

    private static RpcException Echec(StatusCode statut) => new(new Status(statut, "test"));
}
