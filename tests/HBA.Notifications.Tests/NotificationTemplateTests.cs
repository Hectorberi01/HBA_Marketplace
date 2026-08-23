using FluentAssertions;
using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Communication.Notifications.Domain.Templates;
using Xunit;

namespace HBA.Notifications.Tests;

/// <summary>
/// Les gabarits du §10.15. Le rendu est le point sensible : un message part chez un
/// utilisateur réel, et un défaut s'y voit chez lui, pas chez nous.
/// </summary>
public sealed class NotificationTemplateTests
{
    private static NotificationTemplate Gabarit(
        string corps = "Bonjour {firstName}, {restaurant} prépare votre commande.",
        string? sujet = "Commande acceptée")
        => NotificationTemplate.Create(
            "food.order.accepted", NotificationChannel.Email, "fr-BJ", sujet, corps).Value;

    [Fact]
    public void Un_rendu_complet_remplace_tous_les_placeholders()
    {
        var rendu = Gabarit().Render(new Dictionary<string, string>
        {
            ["firstName"] = "Awa",
            ["restaurant"] = "Chez Awa"
        });

        rendu.IsSuccess.Should().BeTrue();
        rendu.Value.Body.Should().Be("Bonjour Awa, Chez Awa prépare votre commande.");
        rendu.Value.Subject.Should().Be("Commande acceptée");
    }

    /// <summary>
    /// LE TEST QUI COMPTE LE PLUS DE CE FICHIER.
    ///
    /// Trois comportements étaient possibles sur valeur absente : laisser
    /// `{firstName}` visible, mettre une chaîne vide, ou refuser. Les deux premiers
    /// produisent un message PARTI et illisible — « Bonjour {firstName} » ou
    /// « Bonjour , votre commande… ». Le troisième produit un échec qu'on répare.
    ///
    /// Si quelqu'un « assouplit » un jour ce comportement pour éviter des échecs,
    /// ce test tombera — et c'est exactement ce qu'on veut.
    /// </summary>
    [Fact]
    public void Une_valeur_absente_refuse_le_rendu_au_lieu_d_envoyer_un_texte_troue()
    {
        var rendu = Gabarit().Render(new Dictionary<string, string> { ["firstName"] = "Awa" });

        rendu.IsFailure.Should().BeTrue();
        rendu.Error.Code.Should().Be("notifications.template.placeholder_missing");
        rendu.Error.Message.Should().Contain("restaurant");
    }

    /// <summary>Une valeur vide n'est pas une valeur : « Bonjour , » n'est pas un message.</summary>
    [Fact]
    public void Une_valeur_vide_est_traitee_comme_absente()
    {
        var rendu = Gabarit("Bonjour {firstName}.", null)
            .Render(new Dictionary<string, string> { ["firstName"] = "" });

        rendu.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Le_sujet_est_facultatif_pour_les_canaux_qui_n_en_ont_pas()
    {
        var sms = NotificationTemplate.Create(
            "food.order.ready", NotificationChannel.Sms, "fr-BJ", null, "Votre commande est prête.").Value;

        var rendu = sms.Render(new Dictionary<string, string>());

        rendu.IsSuccess.Should().BeTrue();
        rendu.Value.Subject.Should().BeNull();
    }

    /// <summary>
    /// Le rendu reporte le code ET la version. Sans la version, on ne peut pas
    /// savoir quel texte a réellement été envoyé à quelqu'un qui réclame six mois
    /// plus tard — le gabarit a changé depuis.
    /// </summary>
    [Fact]
    public void Le_rendu_reporte_le_gabarit_et_sa_version()
    {
        var rendu = Gabarit("Bonjour.", null).Render(new Dictionary<string, string>());

        rendu.Value.TemplateCode.Should().Be("food.order.accepted");
        rendu.Value.TemplateVersion.Should().Be(1);
    }

    [Fact]
    public void Un_gabarit_sans_code_ou_sans_corps_est_refuse()
    {
        NotificationTemplate.Create("  ", NotificationChannel.Sms, "fr-BJ", null, "corps")
            .IsFailure.Should().BeTrue();
        NotificationTemplate.Create("code", NotificationChannel.Sms, "fr-BJ", null, "  ")
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void La_locale_par_defaut_est_celle_du_Benin()
    {
        NotificationTemplate.Create("code", NotificationChannel.Sms, null, null, "corps")
            .Value.Locale.Should().Be("fr-BJ");
    }

    /// <summary>
    /// Un texte sans placeholder doit passer tel quel — le rendu ne doit rien
    /// exiger de plus que ce que le gabarit demande.
    /// </summary>
    [Fact]
    public void Un_gabarit_sans_placeholder_se_rend_sans_valeurs()
    {
        var rendu = Gabarit("Votre commande est prête.", null)
            .Render(new Dictionary<string, string>());

        rendu.IsSuccess.Should().BeTrue();
        rendu.Value.Body.Should().Be("Votre commande est prête.");
    }
}
