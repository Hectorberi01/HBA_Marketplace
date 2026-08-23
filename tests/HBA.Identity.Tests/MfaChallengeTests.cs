using FluentAssertions;
using HBA.Identity.Domain.Mfa;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>
/// Les défis à usage unique du §10.1. Ce sont des tests de SÉCURITÉ : chacun garde
/// une protection dont l'absence ne se voit pas en exécution normale.
/// </summary>
public sealed class MfaChallengeTests
{
    private const string Hash = "hash-du-code";

    private static MfaChallenge Issue() =>
        MfaChallenge.Issue(Guid.NewGuid(), MfaChannels.Sms, Hash).Value;

    [Fact]
    public void Un_bon_code_valide_le_defi()
    {
        var challenge = Issue();

        challenge.Verify(h => h == Hash).Should().Be(MfaVerificationOutcome.Verified);
        challenge.ConsumedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// SANS CETTE RÈGLE, UN CODE LU SUR UN ÉCRAN VERROUILLÉ SERT DEUX FOIS.
    ///
    /// Un code valide et non expiré resterait rejouable jusqu'à sa date limite :
    /// une fois par le titulaire, une fois par qui a vu la notification.
    /// </summary>
    [Fact]
    public void Un_code_deja_consomme_ne_sert_pas_une_seconde_fois()
    {
        var challenge = Issue();
        challenge.Verify(h => h == Hash);

        challenge.Verify(h => h == Hash).Should().Be(MfaVerificationOutcome.AlreadyUsed);
    }

    /// <summary>
    /// LE PLAFOND DE TENTATIVES EST LA SEULE VRAIE PROTECTION.
    ///
    /// Six chiffres, c'est un million de combinaisons : un script les épuise en
    /// quelques minutes. Ni le hachage ni l'expiration n'y changent quoi que ce
    /// soit — seul le compteur arrête le balayage.
    /// </summary>
    [Fact]
    public void Au_dela_du_plafond_le_defi_est_mort()
    {
        var challenge = Issue();

        for (var i = 0; i < MfaChallenge.MaxAttempts; i++)
        {
            challenge.Verify(_ => false).Should().Be(MfaVerificationOutcome.WrongCode);
        }

        challenge.Verify(h => h == Hash).Should().Be(MfaVerificationOutcome.TooManyAttempts);
    }

    /// <summary>
    /// LA TENTATIVE COMPTE MÊME SUR UN DÉFI EXPIRÉ.
    ///
    /// Ne compter que sur défi vivant rendrait le plafond contournable : il
    /// suffirait d'attendre l'expiration pour repartir de zéro.
    /// </summary>
    [Fact]
    public void Une_tentative_sur_defi_expire_est_quand_meme_comptee()
    {
        var challenge = Issue();
        challenge.Expire();

        challenge.Verify(_ => false).Should().Be(MfaVerificationOutcome.Expired);
        challenge.Attempts.Should().Be(1);
    }

    [Fact]
    public void Un_defi_expire_refuse_meme_le_bon_code()
    {
        var challenge = Issue();
        challenge.Expire();

        challenge.Verify(h => h == Hash).Should().Be(MfaVerificationOutcome.Expired);
    }

    [Theory]
    [InlineData("sms", "SMS")]
    [InlineData("Email", "EMAIL")]
    public void Le_canal_est_normalise(string saisie, string attendu)
    {
        MfaChallenge.Issue(Guid.NewGuid(), saisie, Hash).Value.Channel.Should().Be(attendu);
    }

    [Fact]
    public void Un_canal_inconnu_est_refuse()
    {
        var result = MfaChallenge.Issue(Guid.NewGuid(), "PIGEON", Hash);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("identity.mfa.channel_unsupported");
    }

    [Fact]
    public void Un_defi_sans_compte_ou_sans_code_est_refuse()
    {
        MfaChallenge.Issue(Guid.Empty, MfaChannels.Sms, Hash).IsFailure.Should().BeTrue();
        MfaChallenge.Issue(Guid.NewGuid(), MfaChannels.Sms, "  ").IsFailure.Should().BeTrue();
    }
}
