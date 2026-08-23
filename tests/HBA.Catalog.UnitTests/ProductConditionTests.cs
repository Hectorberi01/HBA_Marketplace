using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CONDITION COMMERCIALE (§9, §28).
///
/// Ce que ces tests protègent n'est pas une contrainte technique : c'est
/// l'acheteur. Chacune des incohérences refusées ici correspond à une annonce
/// parfaitement valide au sens du schéma, et parfaitement trompeuse à l'écran.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductConditionTests
{
    [Fact]
    public void Un_produit_neuf_ne_peut_pas_declarer_de_defaut()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.New,
            defects: new[]
            {
                new DefautDeclare(ProductDefectType.Cosmetic, "SCREEN", "Micro-rayures", ProductDefectSeverity.Minor)
            });

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.new_with_defects");
    }

    [Fact]
    public void Un_produit_neuf_doit_etre_pleinement_fonctionnel()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.New,
            functionalStatus: ProductFunctionalStatus.PartiallyFunctional,
            defects: new[]
            {
                new DefautDeclare(ProductDefectType.Functional, "CAMERA", "Autofocus lent", ProductDefectSeverity.Moderate)
            });

        resultat.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// « PARTIELLEMENT FONCTIONNEL » SANS DÉFAUT N'INFORME PERSONNE.
    ///
    /// C'est la mention qui fait hésiter sans permettre de décider. Exiger au moins
    /// un défaut transforme un avertissement vague en information utilisable — et
    /// c'est aussi ce qui rend un litige arbitrable.
    /// </summary>
    [Fact]
    public void Un_produit_partiellement_fonctionnel_doit_dire_ce_qui_ne_marche_pas()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.Good,
            functionalStatus: ProductFunctionalStatus.PartiallyFunctional);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.defect_required");
    }

    [Fact]
    public void Un_reconditionne_doit_nommer_son_reconditionneur()
    {
        var resultat = ProductCondition.Create(ProductConditionType.Refurbished, "A");

        // Le mot « reconditionné » vaut une prime de prix ; sans savoir QUI a remis
        // à neuf, il ne vaut rien.
        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.refurbisher_required");
    }

    [Fact]
    public void Un_reconditionne_complet_est_accepte()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.Refurbished,
            grade: "A",
            functionalStatus: ProductFunctionalStatus.FullyFunctional,
            refurbishedByType: RefurbisherType.Professional,
            refurbishmentOperations: new[] { "battery_replaced", "screen_tested", "camera_tested" },
            batteryHealthPercentage: 94,
            batteryReplaced: true);

        resultat.IsSuccess.Should().BeTrue();
        resultat.Value.IsRefurbished.Should().BeTrue();
        resultat.Value.IsUsed.Should().BeTrue("un reconditionné a servi avant d'être remis à neuf");
        resultat.Value.RefurbishmentOperations.Should().Contain("BATTERY_REPLACED");
    }

    /// <summary>
    /// `isUsed` EST DÉDUIT DU TYPE, JAMAIS REÇU DU CLIENT.
    ///
    /// Le §9 montre les deux champs dans le même JSON, ce qui invite à les accepter
    /// tels quels — et autoriserait { "type": "NEW", "isUsed": true }, deux
    /// affirmations contradictoires dont on ne saurait plus laquelle croire à
    /// l'affichage.
    /// </summary>
    [Theory]
    [InlineData(ProductConditionType.New, false)]
    [InlineData(ProductConditionType.OpenBox, false)]
    [InlineData(ProductConditionType.LikeNew, true)]
    [InlineData(ProductConditionType.VeryGood, true)]
    [InlineData(ProductConditionType.Good, true)]
    [InlineData(ProductConditionType.Fair, true)]
    public void Le_caractere_usage_se_deduit_du_type(ProductConditionType type, bool attenduUsage)
        => ProductCondition.Create(type).Value.IsUsed.Should().Be(attenduUsage);

    [Fact]
    public void Un_grade_hors_A_B_C_D_est_refuse()
    {
        var resultat = ProductCondition.Create(ProductConditionType.VeryGood, grade: "S");

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.grade_invalid");
    }

    [Fact]
    public void Un_defaut_sans_description_est_refuse()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.Good,
            defects: new[] { new DefautDeclare(ProductDefectType.Cosmetic, "DOS", "   ", ProductDefectSeverity.Minor) });

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.defect_description_required");
    }

    [Fact]
    public void Un_etat_de_batterie_hors_bornes_est_refuse()
    {
        var resultat = ProductCondition.Create(
            ProductConditionType.Refurbished,
            refurbishedByType: RefurbisherType.Manufacturer,
            batteryHealthPercentage: 140);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.condition.battery_health_invalid");
    }

    /// <summary>
    /// LE RATTACHEMENT DES CLÉS ÉTRANGÈRES, VÉRIFIÉ SANS BASE DE DONNÉES.
    ///
    /// La condition arrive du formulaire AVANT d'avoir une révision, et ses défauts
    /// avant d'avoir une condition. Si `ProductRevision` oublie de les rattacher,
    /// rien ne se voit ici : c'est PostgreSQL qui refuse l'insertion, en parlant
    /// d'une violation de contrainte sans dire quel champ n'a pas été rempli.
    ///
    /// Ce test attrape l'oubli au moment où il est écrit, pas au premier
    /// `dev-up.sh`.
    /// </summary>
    [Fact]
    public void Les_defauts_declares_sont_rattaches_a_leur_condition_et_a_leur_revision()
    {
        var condition = ProductCondition.Create(
            ProductConditionType.VeryGood,
            grade: "A",
            defects: new[]
            {
                new DefautDeclare(ProductDefectType.Cosmetic, "SCREEN", "Micro-rayures visibles à contre-jour", ProductDefectSeverity.Minor)
            }).Value;

        condition.Defects.Should().HaveCount(1);
        condition.Defects.Single().Description.Should().Be("Micro-rayures visibles à contre-jour");

        var produit = UnProduit.Brouillon(UnProduit.Contenu(condition: condition));
        var revision = produit.CurrentRevision;

        revision.Condition.RevisionId.Should().Be(revision.Id);
        revision.Condition.Defects.Single().ConditionId.Should().Be(revision.Condition.Id);
    }
}
