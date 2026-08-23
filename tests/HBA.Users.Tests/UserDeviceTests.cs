using FluentAssertions;
using HBA.Users.Domain.Devices;
using Xunit;

namespace HBA.Users.Tests;

/// <summary>Les appareils push : invariants du §10.2, table <c>devices</c>.</summary>
public sealed class UserDeviceTests
{
    private const string Token = "fcm-token-abcdef";

    [Theory]
    [InlineData("ios", "IOS")]
    [InlineData("Android", "ANDROID")]
    [InlineData(" web ", "WEB")]
    public void La_plateforme_est_normalisee(string saisie, string attendu)
    {
        var device = UserDevice.Register(Guid.NewGuid(), saisie, Token);

        device.IsSuccess.Should().BeTrue();
        device.Value.Platform.Should().Be(attendu);
    }

    /// <summary>
    /// SANS CE REFUS, UNE FAUTE DE FRAPPE DEVIENT UN APPAREIL MUET.
    ///
    /// Une plateforme inconnue est acceptée en base, puis le service de notification
    /// ne sait pas quel fournisseur appeler. L'appareil ne reçoit jamais rien, et
    /// rien n'échoue nulle part : l'utilisateur croit simplement que la plateforme
    /// ne lui envoie pas de notifications.
    /// </summary>
    [Fact]
    public void Une_plateforme_inconnue_est_refusee()
    {
        var result = UserDevice.Register(Guid.NewGuid(), "SYMBIAN", Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("users.device.platform_unsupported");
    }

    [Fact]
    public void Un_jeton_vide_est_refuse()
    {
        UserDevice.Register(Guid.NewGuid(), "IOS", "   ").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Un_jeton_trop_long_est_refuse()
    {
        var trop = new string('x', UserDevice.MaxPushToken + 1);

        UserDevice.Register(Guid.NewGuid(), "IOS", trop).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Un_appareil_sans_compte_est_refuse()
    {
        UserDevice.Register(Guid.Empty, "IOS", Token).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Le réenregistrement rafraîchit la date de dernière vue — c'est elle qui permet
    /// de purger les jetons dormants, que les fournisseurs refusent en silence.
    /// </summary>
    [Fact]
    public void Le_reenregistrement_rafraichit_la_date_de_derniere_vue()
    {
        var device = UserDevice.Register(Guid.NewGuid(), "IOS", Token).Value;
        var avant = device.LastSeenAtUtc;

        Thread.Sleep(5);
        device.Touch("android");

        device.LastSeenAtUtc.Should().BeOnOrAfter(avant);
        device.Platform.Should().Be("ANDROID");
    }
}
