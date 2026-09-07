using FluentAssertions;
using HBA.Users.Domain.Preferences;
using Xunit;

namespace HBA.Users.Tests;

/// <summary>Les préférences : invariants du §10.2.</summary>
public sealed class UserPreferencesTests
{
    private static UserPreferences Create()
        => UserPreferences.CreateDefault(Guid.NewGuid()).Value;

    [Fact]
    public void Les_preferences_par_defaut_sont_en_franc_CFA_et_en_francais_du_Benin()
    {
        var preferences = Create();

        preferences.Currency.Should().Be("XOF");
        preferences.Language.Should().Be("fr-BJ");
    }

    /// <summary>CE TEST PROTÈGE UNE OBLIGATION LÉGALE, PAS UNE PRÉFÉRENCE D'ÉQUIPE.</summary>
    [Fact]
    public void Le_consentement_marketing_est_refuse_par_defaut()
    {
        Create().MarketingOptIn.Should().BeFalse();
    }

    /// <summary>
    /// Le pendant du précédent : les notifications transactionnelles — « votre
    /// commande est acceptée » — ne relèvent pas du consentement commercial et
    /// doivent fonctionner dès l'inscription.
    /// </summary>
    [Fact]
    public void Les_notifications_transactionnelles_sont_actives_par_defaut()
    {
        Create().PushEnabled.Should().BeTrue();
    }

    [Fact]
    public void Un_champ_absent_laisse_la_preference_inchangee()
    {
        var preferences = Create();
        preferences.Update(language: "en-US", currency: null, pushEnabled: null, marketingOptIn: null);

        preferences.Language.Should().Be("en-US");
        preferences.Currency.Should().Be("XOF", "une mise à jour partielle ne réinitialise rien");
        preferences.PushEnabled.Should().BeTrue();
    }

    /// <summary>UNE DEVISE INCONNUE DOIT ÊTRE REFUSÉE ICI, PAS PLUS LOIN.</summary>
    [Fact]
    public void Une_devise_non_prise_en_charge_est_refusee()
    {
        var result = Create().Update(null, "EUR", null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("users.preferences.currency_unsupported");
    }

    [Fact]
    public void Une_langue_non_prise_en_charge_est_refusee()
    {
        var result = Create().Update("de-DE", null, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("users.preferences.language_unsupported");
    }

    [Fact]
    public void Des_preferences_sans_compte_sont_refusees()
    {
        UserPreferences.CreateDefault(Guid.Empty).IsFailure.Should().BeTrue();
    }
}
