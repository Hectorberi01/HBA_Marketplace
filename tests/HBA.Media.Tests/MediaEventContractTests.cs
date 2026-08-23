using System.Reflection;
using FluentAssertions;
using HBA.Media.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Media.Tests;

/// <summary>
/// Les trois événements du service média.
///
/// CE FICHIER EXISTE PARCE QU'UN CONTRAT PUBLIC NE PRÉVIENT PAS QUAND IL CASSE.
///
/// Renommer un champ ou une classe compile ; le consommateur d'en face ne lève
/// rien non plus, il désérialise simplement un champ absent en valeur par défaut
/// et continue. C'est la seule barrière qui se déclenche AVANT que la panne ne
/// devienne silencieuse.
/// </summary>
public sealed class MediaEventContractTests
{
    public static TheoryData<Type, string> EvenementsAttendus => new()
    {
        { typeof(MediaReadyIntegrationEvent), "media.ready" },
        { typeof(MediaDeletedIntegrationEvent), "media.deleted" },
        { typeof(MediaProcessingFailedIntegrationEvent), "media.processing_failed" }
    };

    /// <summary>
    /// Les noms sont ceux du §16 — `media.ready`, `media.deleted` — et non une
    /// forme dérivée du nom de classe. C'est ce que les autres équipes ont lu.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_porte_le_nom_du_cahier_des_charges(Type type, string attendu)
    {
        var descripteur = type.GetCustomAttribute<HbaEventAttribute>();

        descripteur.Should().NotBeNull($"{type.Name} doit porter [HbaEvent]");
        descripteur!.EventType.Should().Be(attendu);
    }

    /// <summary>
    /// SANS LE PROPRIÉTAIRE, UN CONSOMMATEUR NE SAIT PAS SI L'ÉVÉNEMENT LE CONCERNE.
    ///
    /// « Le média X est prêt » n'est actionnable que si l'on sait à quoi X se
    /// rattache. Faute de quoi chaque consommateur devrait rappeler media-service
    /// pour le découvrir — c'est-à-dire rétablir exactement la dépendance de
    /// disponibilité que l'événement existe pour supprimer.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Tout_evenement_media_designe_son_proprietaire(Type type, string _)
    {
        var champs = type.GetProperties().Select(p => p.Name).ToArray();

        champs.Should().Contain("MediaId");
        champs.Should().Contain("OwnerType");
        champs.Should().Contain("OwnerId");
    }

    /// <summary>
    /// LE PROPRIÉTAIRE VOYAGE EN CHAÎNE, PAS EN ÉNUMÉRATION.
    ///
    /// Un `MediaOwnerType` dans le contrat forcerait tout consommateur à référencer
    /// le domaine de media-service — la frontière que ce service met un soin
    /// particulier à ne jamais franchir, et qui est ce qui a permis de l'extraire
    /// sans rien démêler.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Le_type_de_proprietaire_reste_une_chaine(Type type, string _)
        => type.GetProperty("OwnerType")!.PropertyType.Should().Be<string>();

    /// <summary>
    /// AUCUNE URL SIGNÉE DANS UN ÉVÉNEMENT.
    ///
    /// Une URL signée vit quelques minutes ; Kafka conserve l'événement des jours.
    /// Elle serait périmée avant d'être lue, et un rejeu n'en ferait rien. Ce que
    /// l'événement transporte est une CLÉ d'objet ; qui a besoin d'un accès le
    /// demande au moment où il en a besoin.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Aucun_evenement_ne_transporte_d_url(Type type, string _)
    {
        var suspects = type.GetProperties()
            .Select(p => p.Name)
            .Where(nom => nom.Contains("Url", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        suspects.Should().BeEmpty(
            "une URL signée expire bien avant la fin de rétention Kafka de l'événement");
    }

    /// <summary>
    /// L'ÉCHEC DE TRAITEMENT NE DIT PAS QUE LE FICHIER EST PERDU.
    ///
    /// Il transporte une raison de diagnostic, et surtout PAS de clé d'objet : un
    /// consommateur qui recevrait la clé serait tenté d'aller effacer les octets,
    /// alors que l'original est intact et que le §14 prévoit de relancer le
    /// traitement.
    /// </summary>
    [Fact]
    public void L_echec_de_traitement_porte_une_raison_et_pas_de_cle()
    {
        var champs = typeof(MediaProcessingFailedIntegrationEvent)
            .GetProperties().Select(p => p.Name).ToArray();

        champs.Should().Contain("Reason");
        champs.Should().NotContain("ObjectKey");
    }

    /// <summary>
    /// `media.ready` est le seul à porter la clé : c'est lui qui annonce un fichier
    /// utilisable, donc le seul dont un consommateur ait à retenir l'emplacement.
    /// </summary>
    [Fact]
    public void Le_media_pret_porte_la_cle_de_l_original()
        => typeof(MediaReadyIntegrationEvent).GetProperties()
            .Select(p => p.Name).Should().Contain("ObjectKey");

    /// <summary>
    /// La version est explicite. Un contrat sans version ne peut pas évoluer :
    /// le jour où un champ change de sens, rien ne distingue l'ancien du nouveau.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_est_versionne_et_nomme_son_agregat(Type type, string _)
    {
        var descripteur = type.GetCustomAttribute<HbaEventAttribute>()!;

        descripteur.Version.Should().BePositive();
        descripteur.AggregateType.Should().Be("MediaAsset");
    }
}
