using System.Reflection;
using FluentAssertions;
using HBA.Media.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Media.Tests;

/// <summary>Les trois événements du service média.</summary>
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
    /// forme dérivée du nom de classe.
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
    /// SANS LE PROPRIÉTAIRE, UN CONSOMMATEUR NE SAIT PAS SI L'ÉVÉNEMENT LE
    /// CONCERNE.
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

    /// <summary>LE PROPRIÉTAIRE VOYAGE EN CHAÎNE, PAS EN ÉNUMÉRATION.</summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Le_type_de_proprietaire_reste_une_chaine(Type type, string _)
        => type.GetProperty("OwnerType")!.PropertyType.Should().Be<string>();

    /// <summary>AUCUNE URL SIGNÉE DANS UN ÉVÉNEMENT.</summary>
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

    /// <summary>L'ÉCHEC DE TRAITEMENT NE DIT PAS QUE LE FICHIER EST PERDU.</summary>
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

    /// <summary>La version est explicite.</summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_est_versionne_et_nomme_son_agregat(Type type, string _)
    {
        var descripteur = type.GetCustomAttribute<HbaEventAttribute>()!;

        descripteur.Version.Should().BePositive();
        descripteur.AggregateType.Should().Be("MediaAsset");
    }
}
