using System.Security.Claims;
using FluentAssertions;
using HBA.Identity.Domain.Users;
using HBA.Shared.Hosting.Http;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>LE STEP-UP DU §37 — CE QUI SE CASSERAIT SANS QUE PERSONNE NE LE VOIE.</summary>
public sealed class StepUpTests
{
    /// <summary>Fabrique un jeton de test.</summary>
    /// <param name="methodes">
    /// VAUT `pwd` QUAND ON NE PRÉCISE RIEN, ET CE DÉFAUT EST LE CAS NOMINAL.
    /// </param>
    private static ClaimsPrincipal Jeton(DateTimeOffset? authentifieLe, params string[] methodes)
    {
        var claims = new List<Claim>();

        if (authentifieLe is { } instant)
        {
            claims.Add(new Claim(
                StepUpAuthentication.AuthTimeClaim,
                instant.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        string[] employees = methodes.Length > 0
            ? methodes
            : new[] { StepUpAuthentication.PasswordMethod };
        claims.AddRange(employees.Select(m => new Claim(StepUpAuthentication.AuthMethodsClaim, m)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Un jeton SANS aucune méthode déclarée, pour les cas qui l'exigent.</summary>
    private static ClaimsPrincipal JetonSansMethode(DateTimeOffset authentifieLe)
        => new(new ClaimsIdentity(
            new[]
            {
                new Claim(
                    StepUpAuthentication.AuthTimeClaim,
                    authentifieLe.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))
            },
            "Test"));

    [Fact]
    public void Une_authentification_de_la_minute_est_recente()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddMinutes(-1)).HasRecentAuthentication(maintenant).Should().BeTrue();
    }

    /// <summary>Le bord exact de la fenêtre appartient encore à la fenêtre.</summary>
    [Fact]
    public void Le_bord_de_la_fenetre_passe_encore()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant - StepUpAuthentication.Window)
            .HasRecentAuthentication(maintenant).Should().BeTrue();
    }

    [Fact]
    public void Au_dela_de_la_fenetre_il_faut_ressaisir()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant - StepUpAuthentication.Window - TimeSpan.FromSeconds(1))
            .HasRecentAuthentication(maintenant).Should().BeFalse();
    }

    /// <summary>LE CAS QUI DÉCIDE DE TOUT : UN JETON SANS `auth_time`.</summary>
    [Fact]
    public void Un_jeton_sans_auth_time_est_refuse()
    {
        Jeton(authentifieLe: null).HasRecentAuthentication(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    /// <summary>UN `auth_time` DANS LE FUTUR NE VAUT PAS « ÉTERNELLEMENT RÉCENT ».</summary>
    [Fact]
    public void Un_auth_time_trop_en_avance_est_refuse()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddMinutes(5)).HasRecentAuthentication(maintenant).Should().BeFalse();
        Jeton(maintenant.AddSeconds(30)).HasRecentAuthentication(maintenant).Should().BeTrue(
            "une dérive d'horloge de quelques secondes est normale entre deux machines");
    }

    /// <summary>LE CAS QUI A OUVERT UNE FAILLE : `otp` SEUL, FRAÎCHEMENT ÉMIS.</summary>
    [Fact]
    public void Une_connexion_par_otp_seul_ne_vaut_pas_step_up()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddSeconds(-10), "otp")
            .HasRecentAuthentication(maintenant).Should().BeFalse(
                "un code reçu par SMS n'est pas un mot de passe : une carte SIM ne doit "
                + "pas suffire à vider un portefeuille");
    }

    /// <summary>Le mot de passe suffit, seul comme accompagné.</summary>
    [Fact]
    public void Le_mot_de_passe_vaut_step_up_seul_comme_accompagne()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddSeconds(-10), "pwd")
            .HasRecentAuthentication(maintenant).Should().BeTrue();

        Jeton(maintenant.AddSeconds(-10), "pwd", "otp", "mfa")
            .HasRecentAuthentication(maintenant).Should().BeTrue();
    }

    /// <summary>
    /// UN `amr` ABSENT EST REFUSÉ, comme un `auth_time` absent — et pour la même
    /// raison : le traiter comme « méthode inconnue donc acceptée » offrirait le
    /// contournement le plus simple qui soit.
    /// </summary>
    [Fact]
    public void Un_jeton_sans_amr_est_refuse()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        JetonSansMethode(maintenant.AddSeconds(-10))
            .HasRecentAuthentication(maintenant).Should().BeFalse();
    }

    [Fact]
    public void Les_methodes_sont_lues_une_par_claim()
    {
        var jeton = Jeton(DateTimeOffset.UtcNow, "pwd", "otp", "mfa");

        jeton.AuthMethods().Should().Equal("pwd", "otp", "mfa");
    }

    /// <summary>`AuthenticationSnapshot` ÉCLATE SES MÉTHODES, IL N'EN REND PAS UNE SEULE.</summary>
    [Fact]
    public void Le_contexte_mfa_porte_trois_methodes_distinctes()
    {
        var session = AuthenticationSnapshot.ByPasswordAndOtp(
            new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        session.MethodList().Should().Equal("pwd", "otp", "mfa");
        AuthenticationSnapshot.ByPassword(session.AuthenticatedAtUtc).MethodList().Should().Equal("pwd");
    }
}
