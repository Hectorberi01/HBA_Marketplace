using HBA.Catalog.Domain.Attributes;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE RÉFÉRENTIEL D'ATTRIBUTS (§10) ET LA VALIDATION QU'IL PERMET (§23).
///
/// SANS LA VALIDATION, CES DEUX TABLES NE SERAIENT QU'UNE DÉCORATION.
///
/// Un formulaire dynamique et rien qui vérifie ce qui a été saisi : un
/// `screen_size` déclaré DECIMAL accepterait « grand », un `color` à choix
/// accepterait une valeur absente de la liste, et la vitrine filtrerait sur des
/// valeurs qu'aucun filtre ne propose.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AttributsDeCategorieTests
{
    private static readonly Guid Categorie = UnProduit.Categorie;

    private static AttributDeCategorie Attribut(
        AttributeDefinition definition, bool requis = false, int ordre = 0)
        => new(definition, CategoryAttribute.Create(Categorie, definition.Id, requis, false, ordre).Value);

    private static AttributeDefinition Definir(
        string code, AttributeValueType type, params string[] options)
        => AttributeDefinition.Create(code, code, type, null, options).Value;

    // ═════════════════════════════════════════════════════════════════════════
    // LES DÉFINITIONS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UN CHAMP À CHOIX SANS CHOIX EST IMPOSSIBLE À REMPLIR.
    ///
    /// Le formulaire vendeur en fait une liste déroulante ; vide et obligatoire,
    /// elle rend la fiche insoumettable. Le vendeur ne voit qu'un champ requis et
    /// vide — rien ne lui dit que c'est la définition qui est incomplète.
    /// </summary>
    [Theory]
    [InlineData(AttributeValueType.Select)]
    [InlineData(AttributeValueType.MultiSelect)]
    public void Un_attribut_a_choix_sans_valeur_est_refuse(AttributeValueType type)
    {
        var resultat = AttributeDefinition.Create("couleur", "Couleur", type);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.attribute.options_required");
    }

    [Fact]
    public void Un_attribut_simple_ne_porte_pas_de_liste_de_valeurs()
        => AttributeDefinition.Create("poids", "Poids", AttributeValueType.Decimal, "KG", new[] { "lourd" })
            .Error.Code.Should().Be("catalog.attribute.options_unexpected");

    /// <summary>
    /// LE CODE EST NORMALISÉ, PARCE QUE C'EST LUI QUI VOYAGE.
    ///
    /// Il est la clé sous laquelle la valeur est rangée dans
    /// `product_revisions.attributes` et sur laquelle la vitrine filtre. Sans
    /// normalisation, `color`, `Color` et `COLOR` deviennent trois filtres.
    /// </summary>
    [Theory]
    [InlineData("Screen Size", "screen_size")]
    [InlineData("COLOR", "color")]
    public void Le_code_est_normalise(string saisi, string attendu)
        => AttributeDefinition.Create(saisi, "Libellé", AttributeValueType.Text)
            .Value.Code.Should().Be(attendu);

    [Theory]
    [InlineData("1color")]
    [InlineData("color-size")]
    [InlineData("")]
    public void Un_code_mal_forme_est_refuse(string code)
        => AttributeDefinition.Create(code, "Libellé", AttributeValueType.Text)
            .Error.Code.Should().Be("catalog.attribute.code_invalid");

    // ═════════════════════════════════════════════════════════════════════════
    // LA VALIDATION D'UNE FICHE CONTRE LE SCHÉMA
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Une_categorie_sans_schema_nimpose_rien()
        => ValidationDesAttributs.Valider(Array.Empty<AttributDeCategorie>(), null)
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Un_attribut_requis_absent_est_refuse()
    {
        var schema = new[] { Attribut(Definir("storage", AttributeValueType.Select, "128GB", "256GB"), requis: true) };

        var resultat = ValidationDesAttributs.Valider(schema, new Dictionary<string, string>());

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.attribute.required_missing");
    }

    [Fact]
    public void Un_attribut_facultatif_absent_passe()
    {
        var schema = new[] { Attribut(Definir("screen_size", AttributeValueType.Decimal)) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string>())
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// LES ATTRIBUTS INCONNUS SONT IGNORÉS, PAS REFUSÉS.
    ///
    /// Les fiches saisies avant l'existence de ces définitions portent des clés qui
    /// ne correspondent à rien. Les refuser rendrait chacune impossible à
    /// resoumettre — donc à corriger — alors que rien dans leur contenu n'est faux.
    /// </summary>
    [Fact]
    public void Un_attribut_inconnu_du_schema_est_ignore()
    {
        var schema = new[] { Attribut(Definir("storage", AttributeValueType.Select, "256GB")) };
        var valeurs = new Dictionary<string, string> { ["legacy_field"] = "n'importe quoi" };

        ValidationDesAttributs.Valider(schema, valeurs).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_valeur_hors_liste_est_refusee()
    {
        var schema = new[] { Attribut(Definir("storage", AttributeValueType.Select, "128GB", "256GB")) };
        var valeurs = new Dictionary<string, string> { ["storage"] = "512GB" };

        var resultat = ValidationDesAttributs.Valider(schema, valeurs);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.attribute.value_not_allowed");
        resultat.Error.Message.Should().Contain("128GB", "le message doit rappeler les valeurs possibles");
    }

    /// <summary>
    /// LA BARRE VERTICALE, PAS LA VIRGULE.
    ///
    /// Les options sont des libellés saisis par un administrateur : « Noir, mat »
    /// est plausible. Découper sur la virgule en ferait deux valeurs dont aucune
    /// n'existe, et le vendeur verrait sa fiche refusée pour un choix correct.
    /// </summary>
    [Fact]
    public void Un_choix_multiple_se_separe_par_une_barre_verticale()
    {
        var schema = new[] { Attribut(Definir("options", AttributeValueType.MultiSelect, "5G", "NFC", "Noir, mat")) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string> { ["options"] = "5G|Noir, mat" })
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// CULTURE INVARIANTE : LE POINT, JAMAIS LA VIRGULE.
    ///
    /// La valeur voyage en JSON et se compare en SQL. Accepter « 6,3 » ferait
    /// entrer en base une chaîne sur laquelle un filtre « écran > 6 pouces »
    /// cesserait de fonctionner, sans erreur.
    /// </summary>
    [Theory]
    [InlineData("6.3", true)]
    [InlineData("6,3", false)]
    [InlineData("grand", false)]
    public void Un_decimal_sattend_a_un_point(string valeur, bool valide)
    {
        var schema = new[] { Attribut(Definir("screen_size", AttributeValueType.Decimal)) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string> { ["screen_size"] = valeur })
            .IsSuccess.Should().Be(valide);
    }

    [Theory]
    [InlineData("#1A2B3C", true)]
    [InlineData("#abc", true)]
    [InlineData("bleu", false)]
    public void Une_couleur_est_hexadecimale(string valeur, bool valide)
    {
        var schema = new[] { Attribut(Definir("teinte", AttributeValueType.Color)) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string> { ["teinte"] = valeur })
            .IsSuccess.Should().Be(valide);
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("12.5", false)]
    public void Un_entier_nest_pas_un_decimal(string valeur, bool valide)
    {
        var schema = new[] { Attribut(Definir("garantie_mois", AttributeValueType.Integer)) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string> { ["garantie_mois"] = valeur })
            .IsSuccess.Should().Be(valide);
    }

    /// <summary>
    /// Une valeur vide sur un attribut FACULTATIF ne déclenche pas le contrôle de
    /// type : un champ laissé blanc n'est pas une valeur fausse.
    /// </summary>
    [Fact]
    public void Une_valeur_vide_sur_un_attribut_facultatif_ne_declenche_pas_le_controle_de_type()
    {
        var schema = new[] { Attribut(Definir("screen_size", AttributeValueType.Decimal)) };

        ValidationDesAttributs.Valider(schema, new Dictionary<string, string> { ["screen_size"] = "   " })
            .IsSuccess.Should().BeTrue();
    }
}
