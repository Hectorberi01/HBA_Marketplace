using System.Security.Claims;
using FluentAssertions;
using HBA.Identity.Domain.Users;
using HBA.Shared.Hosting.Http;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE STEP-UP DU §37 — CE QUI SE CASSERAIT SANS QUE PERSONNE NE LE VOIE.
///
/// CES TESTS NE GARDENT PAS UNE FONCTIONNALITÉ, ILS GARDENT UNE DISTINCTION.
///
/// Tout le mécanisme tient à ce que `auth_time` ne soit PAS l'instant d'émission
/// du jeton. Le confondre avec `iat` ne casse rien de visible : la connexion
/// marche, le rafraîchissement marche, les virements passent. Ils passent
/// simplement TOUJOURS, y compris pour qui a trouvé un poste ouvert au marché.
///
/// C'est le genre de régression qu'une revue ne voit pas et qu'aucun test
/// fonctionnel n'attrape — d'où ceux-ci, qui portent directement sur la
/// distinction.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class StepUpTests
{
    /// <summary>
    /// Fabrique un jeton de test.
    /// </summary>
    /// <param name="methodes">
    /// VAUT `pwd` QUAND ON NE PRÉCISE RIEN, ET CE DÉFAUT EST LE CAS NOMINAL.
    ///
    /// Auparavant, un appel sans méthode produisait un jeton SANS `amr` — et les
    /// tests de fenêtre passaient quand même, parce que le prédicat ne lisait que
    /// `auth_time`. Chaque test portait donc l'hypothèse muette « un jeton sans
    /// méthode déclarée est un jeton valable », qui est précisément ce que le
    /// step-up refuse depuis l'ouverture de la connexion par OTP.
    ///
    /// Le défaut à `pwd` rend les tests de fenêtre indépendants de la question des
    /// méthodes ; l'absence d'`amr` se demande explicitement, et a son propre test.
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

    /// <summary>
    /// LE CAS QUI DÉCIDE DE TOUT : UN JETON SANS `auth_time`.
    ///
    /// Ce sont les jetons émis avant le lot 0b. Les traiter comme « instant
    /// d'authentification inconnu, donc on laisse passer » offrirait le
    /// contournement le plus simple du step-up : présenter un vieux jeton. Ils
    /// expirent d'eux-mêmes en quelques minutes, et le pire que subisse leur
    /// porteur est une saisie de mot de passe de trop.
    /// </summary>
    [Fact]
    public void Un_jeton_sans_auth_time_est_refuse()
    {
        Jeton(authentifieLe: null).HasRecentAuthentication(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    /// <summary>
    /// UN `auth_time` DANS LE FUTUR NE VAUT PAS « ÉTERNELLEMENT RÉCENT ».
    ///
    /// Une horloge d'émetteur en avance rendrait sinon un jeton frais pour
    /// toujours. La minute de tolérance absorbe la dérive normale entre machines ;
    /// au-delà, le refus est un symptôme utile, pas un cas nominal.
    /// </summary>
    [Fact]
    public void Un_auth_time_trop_en_avance_est_refuse()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddMinutes(5)).HasRecentAuthentication(maintenant).Should().BeFalse();
        Jeton(maintenant.AddSeconds(30)).HasRecentAuthentication(maintenant).Should().BeTrue(
            "une dérive d'horloge de quelques secondes est normale entre deux machines");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CAS QUI A OUVERT UNE FAILLE : `otp` SEUL, FRAÎCHEMENT ÉMIS.
    ///
    /// `HasRecentAuthentication` ne lisait que `auth_time`, alors que son propre
    /// encadré annonçait « ce compte a-t-il saisi son MOT DE PASSE il y a moins de
    /// cinq minutes ». L'écart n'a rien coûté tant que tout jeton naissait d'un mot
    /// de passe. `POST /auth/verify-otp` (ISSUE-062) est le premier chemin qui en
    /// émet sans : qui recevait un SMS obtenait aussitôt un jeton « frais » et
    /// franchissait les six gardes sensibles du dépôt — virement, compte bancaire,
    /// transfert de propriété vendeur, mouvements de stock.
    ///
    /// Ce test est le seul endroit du dépôt où cette règle est éprouvée. S'il
    /// disparaît, la règle redevient une phrase dans un commentaire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_connexion_par_otp_seul_ne_vaut_pas_step_up()
    {
        var maintenant = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Jeton(maintenant.AddSeconds(-10), "otp")
            .HasRecentAuthentication(maintenant).Should().BeFalse(
                "un code reçu par SMS n'est pas un mot de passe : une carte SIM ne doit "
                + "pas suffire à vider un portefeuille");
    }

    /// <summary>
    /// Le mot de passe suffit, seul comme accompagné. Le step-up demande `pwd`, pas
    /// « le facteur le plus fort » : exiger `mfa` refuserait la connexion par mot de
    /// passe d'un compte qui n'a pas activé de second facteur — donc la quasi-totalité
    /// des vendeurs, sur les routes qui comptent.
    /// </summary>
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
    /// contournement le plus simple qui soit. `JwtTokenGenerator` pose toujours ce
    /// claim, donc son absence désigne un jeton d'avant, qui expire de lui-même.
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

    /// <summary>
    /// `AuthenticationSnapshot` ÉCLATE SES MÉTHODES, IL N'EN REND PAS UNE SEULE.
    ///
    /// `amr` est un TABLEAU dans OIDC. Une valeur unique `"pwd otp mfa"` serait lue
    /// comme une méthode de ce nom, qui ne figure dans aucun registre — et un
    /// client qui cherche `otp` dans la liste ne le trouverait pas.
    /// </summary>
    [Fact]
    public void Le_contexte_mfa_porte_trois_methodes_distinctes()
    {
        var session = AuthenticationSnapshot.ByPasswordAndOtp(
            new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        session.MethodList().Should().Equal("pwd", "otp", "mfa");
        AuthenticationSnapshot.ByPassword(session.AuthenticatedAtUtc).MethodList().Should().Equal("pwd");
    }
}
