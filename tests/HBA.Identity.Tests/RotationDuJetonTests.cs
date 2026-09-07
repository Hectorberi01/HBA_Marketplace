using FluentAssertions;
using HBA.Identity.Domain.Users;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>LE RAFRAÎCHISSEMENT NE REJUVÉNIT PAS L'AUTHENTIFICATION.</summary>
public sealed class RotationDuJetonTests
{
    private static readonly DateTime Connexion = new(2026, 8, 19, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void La_rotation_reporte_l_instant_d_authentification_de_la_connexion()
    {
        var user = Compte();
        var session = AuthenticationSnapshot.ByPasswordAndOtp(Connexion);

        user.IssueRefreshToken("empreinte-1", Connexion.AddDays(30), session.AuthenticatedAtUtc, session.Methods);

        // Six heures plus tard, le client rafraîchit.
        var maintenant = Connexion.AddHours(6);
        var issue = user.UseRefreshToken("empreinte-1", maintenant, out var reporte);

        issue.Should().Be(RefreshTokenOutcome.Rotated);
        reporte.Should().NotBeNull();

        // L'ASSERTION QUI COMPTE : l'instant est celui de la CONNEXION, pas celui
        // de la rotation.
        reporte!.Value.AuthenticatedAtUtc.Should().Be(Connexion);
        reporte.Value.AuthenticatedAtUtc.Should().NotBe(maintenant);
        reporte.Value.Methods.Should().Be(session.Methods);
    }

    /// <summary>
    /// Et le report survit à une chaîne de rotations, pas seulement à la première.
    /// </summary>
    [Fact]
    public void L_instant_traverse_plusieurs_rotations_sans_bouger()
    {
        var user = Compte();
        var session = AuthenticationSnapshot.ByPassword(Connexion);
        user.IssueRefreshToken("empreinte-1", Connexion.AddDays(30), session.AuthenticatedAtUtc, session.Methods);

        var courant = "empreinte-1";

        for (var tour = 1; tour <= 5; tour++)
        {
            var maintenant = Connexion.AddMinutes(10 * tour);

            user.UseRefreshToken(courant, maintenant, out var reporte)
                .Should().Be(RefreshTokenOutcome.Rotated);

            reporte!.Value.AuthenticatedAtUtc.Should().Be(
                Connexion, "chaque rotation RECOPIE l'instant, elle ne le recalcule pas");

            courant = $"empreinte-{tour + 1}";
            user.IssueRefreshToken(
                courant, Connexion.AddDays(30), reporte.Value.AuthenticatedAtUtc, reporte.Value.Methods);
        }
    }

    /// <summary>Un rejeu ne rend aucun contexte — il n'y a rien à reporter.</summary>
    [Fact]
    public void Un_rejeu_ne_reporte_rien()
    {
        var user = Compte();
        user.IssueRefreshToken("empreinte-1", Connexion.AddDays(30), Connexion, AuthenticationSnapshot.Password);

        user.UseRefreshToken("empreinte-1", Connexion.AddHours(1), out _);

        user.UseRefreshToken("empreinte-1", Connexion.AddHours(2), out var reporte)
            .Should().Be(RefreshTokenOutcome.Replayed);

        reporte.Should().BeNull();
    }

    private static User Compte()
        => User.Register(
            firstName: "Awa",
            lastName: "Koné",
            email: Email.Create("awa@example.com").Value,
            phoneNumber: PhoneNumber.Create("+2250700000000").Value,
            passwordHash: "hash",
            emailVerificationTokenHash: "empreinte-verif",
            emailVerificationExpiresOnUtc: Connexion.AddDays(1)).Value;
}
