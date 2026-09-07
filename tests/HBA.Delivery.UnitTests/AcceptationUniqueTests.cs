using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Delivery.UnitTests;

/// <summary>ISSUE-028 — « deux livreurs peuvent accepter la même course » (CRITICAL).</summary>
public sealed class AcceptationUniqueTests
{
    // L'AGRÉGAT

    [Fact]
    public void Une_seule_acceptation_reussit_sur_une_course_deja_acceptee()
    {
        var premier = DriverId.New();
        var course = UneCourse.ProposeeA(premier);

        course.AcceptByDriver(premier).IsSuccess.Should().BeTrue();

        // Le second n'a même pas d'offre en cours : la course a quitté
        // `DriverAssigned` au moment où le premier a accepté.
        var second = course.AcceptByDriver(DriverId.New());

        second.IsFailure.Should().BeTrue("la course n'est plus proposée à personne");
        course.AssignedDriverId.Should().Be(premier);
        course.Status.Should().Be(DeliveryStatus.DriverAccepted);
    }

    /// <summary>
    /// Le MÊME livreur qui rejoue son acceptation ne doit pas non plus obtenir un
    /// second succès : `AcceptedAtUtc` serait réécrit, et l'événement
    /// `DeliveryAcceptedDomainEvent` levé deux fois — donc, en bout de chaîne, deux
    /// rémunérations.
    /// </summary>
    [Fact]
    public void Le_meme_livreur_ne_peut_pas_accepter_deux_fois()
    {
        var livreur = DriverId.New();
        var course = UneCourse.ProposeeA(livreur);

        course.AcceptByDriver(livreur).IsSuccess.Should().BeTrue();
        course.AcceptByDriver(livreur).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// CE TEST GARDE LE VERROU OPTIMISTE HONNÊTE — VOIR L'ENCADRÉ DE
    /// `InventoryItem.StockVersion`.
    /// </summary>
    [Fact]
    public void Accepter_ecrit_sur_la_ligne_parente_et_pas_seulement_sur_la_proposition()
    {
        var livreur = DriverId.New();
        var course = UneCourse.ProposeeA(livreur);

        var statutAvant = course.Status;
        var affecteAvant = course.AssignedDriverId;
        var accepteAvant = course.AcceptedAtUtc;

        course.AcceptByDriver(livreur).IsSuccess.Should().BeTrue();

        course.Status.Should().NotBe(statutAvant, "le statut de la COURSE doit changer");
        course.AssignedDriverId.Should().NotBe(affecteAvant, "l'affecté est une colonne de la COURSE");
        course.AcceptedAtUtc.Should().NotBe(accepteAvant, "l'horodatage est une colonne de la COURSE");
    }

    [Fact]
    public void Un_livreur_qui_n_a_pas_recu_l_offre_ne_peut_pas_accepter()
    {
        var course = UneCourse.ProposeeA(DriverId.New());

        course.AcceptByDriver(DriverId.New()).IsFailure.Should().BeTrue();
        course.AssignedDriverId.Should().BeNull();
    }

    // LES QUATRE TESTS DE `DispatchStore` ONT ÉTÉ RETIRÉS AVEC LEUR SUJET (D42).
}
