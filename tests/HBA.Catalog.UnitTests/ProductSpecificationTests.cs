using HBA.Catalog.Application.Products;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>LA FICHE TECHNIQUE (§12) ET SON EFFET SUR LES RÉVISIONS (§6).</summary>
public sealed class ProductSpecificationTests
{
    // Enregistrement et ordre

    [Fact]
    public void Les_groupes_et_leurs_lignes_sont_enregistres_dans_lordre_de_saisie()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        var groupes = produit.CurrentRevision.Specifications.OrderBy(g => g.DisplayOrder).ToList();

        groupes.Should().HaveCount(2);
        groupes[0].Name.Should().Be("Écran");
        groupes[1].Name.Should().Be("Batterie");

        var ecran = groupes[0].Items.OrderBy(i => i.DisplayOrder).ToList();
        ecran.Select(i => i.Name).Should().Equal("Type", "Taille");
        ecran.Select(i => i.Value).Should().Equal("Super Retina XDR OLED", "6,3 pouces");
    }

    /// <summary>SANS RANG EXPLICITE, C'EST LA POSITION DE SAISIE QUI FAIT FOI.</summary>
    [Fact]
    public void Un_groupe_sans_rang_explicite_prend_sa_position_de_saisie()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        var groupes = produit.CurrentRevision.Specifications.ToList();

        groupes.Single(g => g.Name == "Écran").DisplayOrder.Should().Be(0);
        groupes.Single(g => g.Name == "Batterie").DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void Un_rang_explicite_lemporte_sur_la_position_de_saisie()
    {
        var contenu = UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("Connectivité", new List<SpecificationSaisie> { new("Wi-Fi", "6E") }, DisplayOrder: 9),
            new("Écran", new List<SpecificationSaisie> { new("Taille", "6,3 pouces") }, DisplayOrder: 3),
        });

        var produit = UnProduit.Brouillon(contenu);

        produit.CurrentRevision.Specifications
            .OrderBy(g => g.DisplayOrder)
            .Select(g => g.Name)
            .Should().Equal("Écran", "Connectivité");
    }

    /// <summary>Les rattachements que `AttacherA` pose.</summary>
    [Fact]
    public void Chaque_groupe_est_rattache_a_sa_revision_et_chaque_ligne_a_son_groupe()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));
        var revision = produit.CurrentRevision;

        foreach (var groupe in revision.Specifications)
        {
            groupe.RevisionId.Should().Be(revision.Id);
            groupe.Items.Should().OnlyContain(i => i.GroupId == groupe.Id);
        }
    }

    [Fact]
    public void Une_fiche_sans_specifications_est_acceptee()
    {
        var produit = UnProduit.Brouillon();

        produit.CurrentRevision.Specifications.Should().BeEmpty();
    }

    // Refus de saisie

    [Fact]
    public void Un_groupe_sans_nom_est_refuse()
    {
        var contenu = UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("   ", new List<SpecificationSaisie> { new("Taille", "6,3 pouces") }),
        });

        var creation = Product.Create(UnProduit.Vendeur, UnProduit.Boutique, contenu);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("catalog.specification.group_name_required");
    }

    /// <summary>Un intertitre suivi de rien : la fiche a l'air tronquée sans l'être.</summary>
    [Fact]
    public void Un_groupe_vide_est_refuse()
    {
        var contenu = UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("Écran", new List<SpecificationSaisie>()),
        });

        var creation = Product.Create(UnProduit.Vendeur, UnProduit.Boutique, contenu);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("catalog.specification.group_empty");
    }

    [Theory]
    [InlineData("", "6,3 pouces")]
    [InlineData("Taille", "")]
    [InlineData("   ", "   ")]
    public void Une_ligne_sans_nom_ou_sans_valeur_est_refusee(string nom, string valeur)
    {
        var contenu = UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("Écran", new List<SpecificationSaisie> { new(nom, valeur) }),
        });

        var creation = Product.Create(UnProduit.Vendeur, UnProduit.Boutique, contenu);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("catalog.specification.item_incomplete");
    }

    /// <summary>UN REFUS DOIT LAISSER LA RÉVISION INTACTE, PAS À MOITIÉ RÉÉCRITE.</summary>
    [Fact]
    public void Une_specification_invalide_nabime_pas_la_revision_existante()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        var echec = produit.UpdateContenu(UnProduit.Contenu(
            name: "Nom qui ne doit pas être retenu",
            specifications: new List<GroupeDeSpecifications>
            {
                new("Écran", new List<SpecificationSaisie>()),
            }));

        echec.IsFailure.Should().BeTrue();
        produit.CurrentRevision.Name.Should().Be("iPhone 16 Pro");
        produit.CurrentRevision.Specifications.Should().HaveCount(2);
    }

    // Remplacement en bloc

    /// <summary>
    /// Le formulaire envoie la fiche technique ENTIÈRE. Fusionner ligne à ligne
    /// rendrait impossible de supprimer une caractéristique : elle resterait faute
    /// d'être mentionnée, et le vendeur verrait réapparaître la ligne qu'il vient
    /// d'effacer.
    /// </summary>
    [Fact]
    public void Reenregistrer_remplace_la_fiche_technique_au_lieu_de_la_completer()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        produit.UpdateContenu(UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("Écran", new List<SpecificationSaisie> { new("Type", "Super Retina XDR OLED") }),
        })).IsSuccess.Should().BeTrue();

        produit.CurrentRevision.Specifications.Should().HaveCount(1);
        produit.CurrentRevision.Specifications.Single().Items.Should().HaveCount(1);
    }

    [Fact]
    public void Une_fiche_technique_peut_etre_entierement_retiree()
    {
        var produit = UnProduit.Brouillon(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        produit.UpdateContenu(UnProduit.Contenu(specifications: null)).IsSuccess.Should().BeTrue();

        produit.CurrentRevision.Specifications.Should().BeEmpty();
    }

    // Empreinte : ce qui ouvre une révision et ce qui n'en ouvre pas (§6)

    /// <summary>LE TEST QUI TIENT LES DEUX CALCULS D'EMPREINTE EN PHASE.</summary>
    [Fact]
    public void Reenregistrer_la_meme_fiche_technique_nouvre_pas_de_revision()
    {
        var produit = UnProduit.Publie(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        produit.UpdateContenu(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()))
            .IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(1, "rien n'a changé, donc rien à faire relire");
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Published);
    }

    /// <summary>« Caractéristiques essentielles » est dans la liste limitative du §6.</summary>
    [Fact]
    public void Changer_une_valeur_de_la_fiche_technique_ouvre_une_nouvelle_revision()
    {
        var produit = UnProduit.Publie(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));
        var publiee = produit.PublishedRevisionId;

        produit.UpdateContenu(UnProduit.Contenu(specifications: UnProduit.FicheTechnique("5000 mAh")))
            .IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(2);
        produit.CurrentRevision.Version.Should().Be(2);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Draft);

        // L'acheteur continue de voir l'ancienne capacité tant que la nouvelle
        // version n'est pas relue.
        produit.PublishedRevisionId.Should().Be(publiee);
        produit.PublishedRevision!.Specifications
            .Single(g => g.Name == "Batterie").Items.Single().Value
            .Should().Be("4400 mAh");
    }

    [Fact]
    public void Retirer_un_groupe_dune_fiche_publiee_ouvre_une_nouvelle_revision()
    {
        var produit = UnProduit.Publie(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        produit.UpdateContenu(UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("Écran", new List<SpecificationSaisie>
            {
                new("Type", "Super Retina XDR OLED"),
                new("Taille", "6,3 pouces"),
            }),
        })).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(2);
    }

    /// <summary>RÉORDONNER EST UNE MODIFICATION, MÊME SI AUCUNE VALEUR NE CHANGE.</summary>
    [Fact]
    public void Reordonner_les_groupes_ouvre_une_nouvelle_revision()
    {
        var produit = UnProduit.Publie(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        var inversee = UnProduit.FicheTechnique().Reverse().ToList();

        produit.UpdateContenu(UnProduit.Contenu(specifications: inversee)).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(2);
    }

    /// <summary>
    /// Les espaces autour d'une valeur ne sont pas une modification : la saisie est
    /// nettoyée à la construction, l'empreinte doit l'être aussi.
    /// </summary>
    [Fact]
    public void Des_espaces_superflus_ne_sont_pas_une_modification_critique()
    {
        var produit = UnProduit.Publie(UnProduit.Contenu(specifications: UnProduit.FicheTechnique()));

        produit.UpdateContenu(UnProduit.Contenu(specifications: new List<GroupeDeSpecifications>
        {
            new("  Écran  ", new List<SpecificationSaisie>
            {
                new(" Type ", " Super Retina XDR OLED "),
                new("Taille", "6,3 pouces "),
            }),
            new("Batterie", new List<SpecificationSaisie> { new("Capacité ", " 4400 mAh") }),
        })).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(1);
    }

    // La traduction depuis le transport (§12, §14)

    /// <summary>
    /// `GroupeSpecSaisi` est un miroir de `GroupeDeSpecifications` : deux types de
    /// même forme, recopiés à la main.
    /// </summary>
    [Fact]
    public void La_fabrique_traduit_les_specifications_du_transport_vers_le_domaine()
    {
        var contenu = ContenuProduitFactory.Construire(
            name: "iPhone 16 Pro",
            description: "Smartphone Apple 256 Go.",
            categoryId: UnProduit.Categorie,
            tarification: new TarificationSaisie(850_000),
            specifications: new List<GroupeSpecSaisi>
            {
                new("Écran", new List<LigneSpecSaisie>
                {
                    new("Type", "Super Retina XDR OLED"),
                    new("Taille", "6,3 pouces"),
                }, DisplayOrder: 2),
            });

        contenu.IsSuccess.Should().BeTrue();

        var groupe = contenu.Value.Specifications.Should().ContainSingle().Subject;
        groupe.Name.Should().Be("Écran");
        groupe.DisplayOrder.Should().Be(2);
        groupe.Items.Select(i => i.Name).Should().Equal("Type", "Taille");
        groupe.Items.Select(i => i.Value).Should().Equal("Super Retina XDR OLED", "6,3 pouces");
    }

    [Fact]
    public void La_fabrique_accepte_labsence_de_specifications()
    {
        var contenu = ContenuProduitFactory.Construire(
            name: "iPhone 16 Pro",
            description: "Smartphone Apple 256 Go.",
            categoryId: UnProduit.Categorie,
            tarification: new TarificationSaisie(850_000));

        contenu.IsSuccess.Should().BeTrue();
        contenu.Value.Specifications.Should().BeEmpty();
    }
}
