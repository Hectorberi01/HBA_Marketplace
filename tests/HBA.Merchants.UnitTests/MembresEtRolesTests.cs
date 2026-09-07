using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Members.Events;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.UnitTests;

/// <summary>L'ÉQUIPE D'UN VENDEUR — CE QUI TIENT, ET CE QU'ON VIENT DE FERMER.</summary>
public sealed class MembresEtRolesTests
{
    private static readonly Guid Vendeur = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AutreVendeur = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CompteProprietaire = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid CompteGerant = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid CompteNouveau = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static IReadOnlyList<SellerRole> Catalogue => SystemSellerRoles.Catalogue;

    private static SellerRole Role(SellerRoleId id) => Catalogue.First(r => r.Id == id);

    // Le catalogue des permissions

    /// <summary>CE TEST NE VÉRIFIE PAS UN NOMBRE, IL DÉCLENCHE LE CONSTRUCTEUR STATIQUE.</summary>
    [Fact]
    public void Le_catalogue_decrit_toutes_les_permissions()
    {
        MerchantPermissions.All.Should().HaveCount(Enum.GetValues<MerchantPermission>().Length);

        MerchantPermissions.All
            .Select(p => p.ToCode())
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Un_code_public_se_relit_dans_les_deux_sens()
    {
        foreach (var permission in MerchantPermissions.All)
        {
            MerchantPermissions.Parse(permission.ToCode()).Should().Be(permission);
        }

        MerchantPermissions.Parse("CE_CODE_N_EXISTE_PAS").Should().BeNull();
        MerchantPermissions.Parse(null).Should().BeNull();
    }

    /// <summary>LES TROIS QUI DÉTOURNENT L'ARGENT SONT BIEN RÉSERVÉES.</summary>
    [Fact]
    public void Les_permissions_qui_touchent_a_l_argent_sont_reservees_au_proprietaire()
    {
        MerchantPermission.PayoutConfigure.IsOwnerOnly().Should().BeTrue();
        MerchantPermission.BankAccountUpdate.IsOwnerOnly().Should().BeTrue();
        MerchantPermission.OwnershipTransfer.IsOwnerOnly().Should().BeTrue();

        MerchantPermissions.Critical.Should().Contain(MerchantPermission.WithdrawalRequest,
            "une demande de retrait n'est pas réservée, mais elle exigera une réauthentification");
    }

    // Les rôles système

    [Fact]
    public void Le_proprietaire_porte_toutes_les_permissions()
        => Role(SystemSellerRoles.OwnerId).Permissions
            .Should().HaveCount(MerchantPermissions.All.Count);

    /// <summary>« ADMINISTRATION GÉNÉRALE HORS ACTIONS RÉSERVÉES » — LITTÉRALEMENT.</summary>
    [Fact]
    public void Un_administrateur_vendeur_n_a_aucune_permission_reservee()
        => Role(SystemSellerRoles.SellerAdminId).Permissions
            .Should().NotIntersectWith(MerchantPermissions.OwnerOnly);

    /// <summary>Le test §24 du cahier, écrit tel quel.</summary>
    [Fact]
    public void Un_gestionnaire_de_commandes_confirme_mais_n_ajuste_pas_le_stock()
    {
        var role = Role(SystemSellerRoles.OrderManagerId);

        role.Has(MerchantPermission.OrderConfirm).Should().BeTrue();
        role.Has(MerchantPermission.InventoryAdjust).Should().BeFalse();
        role.Has(MerchantPermission.PayoutConfigure).Should().BeFalse();
        role.Has(MerchantPermission.MemberInvite).Should().BeFalse();
    }

    [Fact]
    public void Un_gestionnaire_de_stock_ajuste_mais_ne_gere_pas_l_equipe()
    {
        var role = Role(SystemSellerRoles.InventoryManagerId);

        role.Has(MerchantPermission.InventoryAdjust).Should().BeTrue();
        role.Has(MerchantPermission.OrderConfirm).Should().BeFalse();
        role.Has(MerchantPermission.MemberInvite).Should().BeFalse();
    }

    /// <summary>Un rôle système ne se modifie pas, même par un propriétaire.</summary>
    [Fact]
    public void Un_role_systeme_ne_se_modifie_pas()
    {
        var resultat = Role(SystemSellerRoles.OrderManagerId).Update(
            "AUTRE", null, [MerchantPermission.PayoutConfigure], MerchantPermissions.All.ToHashSet());

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.role.system");
    }

    // Les rôles personnalisés (§18)

    [Fact]
    public void Un_vendeur_taille_son_propre_role()
    {
        var resultat = SellerRole.Custom(
            Vendeur, "Préparateur commandes", null, RoleScope.Store,
            acteurPermissions: MerchantPermissions.All.ToHashSet(),
            permissions:
            [
                MerchantPermission.OrderView, MerchantPermission.OrderMarkPreparing,
                MerchantPermission.OrderMarkReady, MerchantPermission.InventoryView
            ]);

        resultat.IsSuccess.Should().BeTrue();
        resultat.Value.IsSystemRole.Should().BeFalse();
        resultat.Value.SellerId.Should().Be(Vendeur);
        resultat.Value.Permissions.Should().HaveCount(4);
    }

    /// <summary>LE TEST QUI REMPLACE LA HIÉRARCHIE PAR ORDINAL.</summary>
    [Fact]
    public void On_ne_delegue_pas_une_permission_qu_on_n_a_pas()
    {
        var resultat = SellerRole.Custom(
            Vendeur, "Faux préparateur", null, RoleScope.Store,
            acteurPermissions: Role(SystemSellerRoles.OrderManagerId).Permissions,
            permissions: [MerchantPermission.OrderView, MerchantPermission.InventoryAdjust]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.Forbidden);
        resultat.Error.Code.Should().Be("sellers.role.cannot_delegate");
    }

    /// <summary>
    /// MÊME LE PROPRIÉTAIRE NE PEUT PAS METTRE UNE PERMISSION RÉSERVÉE DANS UN
    /// RÔLE.
    /// </summary>
    [Fact]
    public void Une_permission_reservee_n_entre_dans_aucun_role()
    {
        var resultat = SellerRole.Custom(
            Vendeur, "Trésorier", null, RoleScope.Seller,
            acteurPermissions: MerchantPermissions.All.ToHashSet(),
            permissions: [MerchantPermission.PayoutView, MerchantPermission.PayoutConfigure]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.role.owner_only");
    }

    [Fact]
    public void Un_role_encore_porte_ne_se_supprime_pas()
    {
        var role = RolePersonnalise([MerchantPermission.OrderView]);

        role.EnsureDeletable(membresPortantCeRole: 2).IsFailure.Should().BeTrue();
        role.EnsureDeletable(membresPortantCeRole: 0).IsSuccess.Should().BeTrue();
    }

    /// <summary>ON NE SUPPRIME PAS UN RÔLE QU'ON N'AURAIT PAS PU CRÉER (lot A3).</summary>
    [Fact]
    public void Un_role_portant_plus_que_l_acteur_ne_se_supprime_pas()
    {
        var role = RolePersonnalise([MerchantPermission.OrderView, MerchantPermission.FinanceView]);

        // Un gestionnaire de catalogue : il ne voit pas les finances.
        IReadOnlySet<MerchantPermission> catalogueur = new HashSet<MerchantPermission>
        {
            MerchantPermission.ProductView, MerchantPermission.ProductUpdate, MerchantPermission.OrderView
        };

        var refus = role.EnsureDeletable(membresPortantCeRole: 0, catalogueur);

        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("sellers.role.cannot_delegate");
        refus.Error.Message.Should().Contain("FINANCE_VIEW", "le refus doit nommer ce qui manque à l'acteur");
    }

    /// <summary>LE PIÈGE QUE CE TEST GARDE : `FirstOrDefault` SUR UNE ÉNUMÉRATION.</summary>
    [Fact]
    public void Un_acteur_qui_porte_tout_supprime_sans_refus()
    {
        var role = RolePersonnalise([MerchantPermission.OrderView, MerchantPermission.FinanceView]);

        role.EnsureDeletable(membresPortantCeRole: 0, MerchantPermissions.All.ToHashSet())
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>Le rôle encore porté est refusé AVANT qu'on regarde la délégation.</summary>
    [Fact]
    public void Le_role_encore_porte_prime_sur_la_delegation()
    {
        var role = RolePersonnalise([MerchantPermission.FinanceView]);

        var refus = role.EnsureDeletable(membresPortantCeRole: 3, new HashSet<MerchantPermission>());

        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("sellers.role.in_use");
    }

    // Les membres

    [Fact]
    public void Le_proprietaire_est_membre_de_son_propre_dossier()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);

        proprietaire.IsOwner.Should().BeTrue();
        proprietaire.CanAct.Should().BeTrue();
        proprietaire.Status.Should().Be(MemberStatus.Active);
        proprietaire.EffectivePermissions(Catalogue).Should().HaveCount(MerchantPermissions.All.Count);
    }

    [Fact]
    public void Le_proprietaire_recrute_un_gestionnaire_de_commandes()
    {
        var resultat = SellerMember.Join(
            ActeurProprietaire(), CompteNouveau, "David K.", "Responsable commandes",
            rolesVendeur: [Role(SystemSellerRoles.OrderManagerId)],
            affectations: []);

        resultat.IsSuccess.Should().BeTrue();

        var membre = resultat.Value;
        membre.DisplayName.Should().Be("David K.");
        membre.InvitedByUserId.Should().Be(CompteProprietaire);
        membre.IsOwner.Should().BeFalse();
        membre.EffectivePermissions(Catalogue).Should().Contain(MerchantPermission.OrderConfirm);
        membre.EffectivePermissions(Catalogue).Should().NotContain(MerchantPermission.InventoryAdjust);
    }

    /// <summary>UN MEMBRE SUSPENDU N'A PLUS AUCUNE PERMISSION — PAS « MOINS », AUCUNE.</summary>
    [Fact]
    public void Un_membre_suspendu_ne_peut_plus_rien()
    {
        var membre = Gerant();

        membre.Suspend(ActeurProprietaire(), estDernierProprietaire: false)
            .IsSuccess.Should().BeTrue();

        membre.Status.Should().Be(MemberStatus.Suspended);
        membre.CanAct.Should().BeFalse();
        membre.EffectivePermissions(Catalogue).Should().BeEmpty();
    }

    [Fact]
    public void Un_acces_revoque_ne_se_rouvre_pas_d_un_clic()
    {
        var membre = Gerant();
        membre.Revoke(ActeurProprietaire(), estDernierProprietaire: false, aUneAutreAppartenance: false);

        var resultat = membre.Reactivate(ActeurProprietaire());

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.not_reactivable");
    }

    /// <summary>L'ESCALADE LA PLUS COURTE DU MODULE, ET ELLE EST FERMÉE.</summary>
    [Fact]
    public void Un_administrateur_vendeur_ne_revoque_pas_le_proprietaire()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);
        var administrateur = ActeurDe(Role(SystemSellerRoles.SellerAdminId));

        var resultat = proprietaire.Revoke(administrateur, estDernierProprietaire: false, aUneAutreAppartenance: false);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_protected");
    }

    [Fact]
    public void Le_dernier_proprietaire_ne_part_pas()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);

        var resultat = proprietaire.Leave(estDernierProprietaire: true, aUneAutreAppartenance: false);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.last_owner");

        proprietaire.Leave(estDernierProprietaire: false, aUneAutreAppartenance: false).IsSuccess.Should().BeTrue();
    }

    /// <summary>LE DRAPEAU QUI EMPÊCHE D'ENFERMER QUELQU'UN DEHORS.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void La_sortie_annonce_s_il_reste_une_autre_appartenance(bool autreAppartenance)
    {
        var membre = Gerant();

        membre.Revoke(ActeurProprietaire(), estDernierProprietaire: false, autreAppartenance)
            .IsSuccess.Should().BeTrue();

        membre.DomainEvents.OfType<SellerMemberRevokedDomainEvent>().Should().ContainSingle()
            .Which.HasOtherSellerMembership.Should().Be(autreAppartenance);
    }

    /// <summary>
    /// L'ACTEUR A ICI TOUTES LES PERMISSIONS — SANS QUOI LE TEST NE PROUVERAIT
    /// RIEN.
    /// </summary>
    [Fact]
    public void On_ne_modifie_pas_ses_propres_droits()
    {
        var membre = Gerant();

        var luiMeme = new MemberActor(
            membre.Id, Vendeur, CompteGerant,
            IsOwner: false, CanAct: true, MerchantPermissions.All.ToHashSet());

        var resultat = membre.SetSellerRoles(luiMeme, [Role(SystemSellerRoles.SellerAdminId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.self");
    }

    /// <summary>« INTROUVABLE » ET NON « INTERDIT », ET C'EST DÉLIBÉRÉ.</summary>
    [Fact]
    public void Un_membre_d_un_autre_vendeur_est_introuvable()
    {
        var membre = Gerant();
        var etranger = new MemberActor(
            SellerMemberId.New(), AutreVendeur, Guid.NewGuid(),
            IsOwner: true, CanAct: true, MerchantPermissions.All.ToHashSet());

        var resultat = membre.Revoke(etranger, estDernierProprietaire: false, aUneAutreAppartenance: false);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>LE RÔLE DE PROPRIÉTAIRE NE S'ATTRIBUE PAS, IL SE TRANSFÈRE.</summary>
    [Fact]
    public void Le_role_de_proprietaire_ne_s_attribue_pas_par_la_liste_des_roles()
    {
        var membre = Gerant();

        var resultat = membre.SetSellerRoles(
            ActeurProprietaire(), [Role(SystemSellerRoles.OwnerId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    /// <summary>NI LUI-MÊME, NI UN AUTRE PROPRIÉTAIRE.</summary>
    [Fact]
    public void Un_autre_proprietaire_ne_depouille_pas_le_proprietaire_de_son_role()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);
        var autreProprietaire = new MemberActor(
            SellerMemberId.New(), Vendeur, Guid.NewGuid(),
            IsOwner: true, CanAct: true, MerchantPermissions.All.ToHashSet());

        var resultat = proprietaire.SetSellerRoles(
            autreProprietaire, [Role(SystemSellerRoles.FinanceManagerId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    /// <summary>ON N'ATTRIBUE PAS UN RÔLE QUI PORTE PLUS QUE SOI.</summary>
    [Fact]
    public void On_n_attribue_pas_un_role_qui_depasse_ses_propres_droits()
    {
        var acteur = new MemberActor(
            SellerMemberId.New(), Vendeur, CompteGerant,
            IsOwner: false, CanAct: true,
            Role(SystemSellerRoles.InventoryManagerId).Permissions
                .Append(MerchantPermission.MemberInvite)
                .ToHashSet());

        var resultat = SellerMember.Join(
            acteur, CompteNouveau, null, null,
            rolesVendeur: [Role(SystemSellerRoles.OrderManagerId)],
            affectations: []);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.cannot_delegate");
    }

    [Fact]
    public void Un_role_d_un_autre_vendeur_est_introuvable()
    {
        var roleDuConcurrent = SellerRole.Custom(
            AutreVendeur, "Espion", null, RoleScope.Seller,
            MerchantPermissions.All.ToHashSet(), [MerchantPermission.OrderView]).Value;

        var resultat = SellerMember.Join(
            ActeurProprietaire(), CompteNouveau, null, null,
            rolesVendeur: [roleDuConcurrent],
            affectations: []);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // La Phase 1, telle qu'elle est

    /// <summary>CE TEST DOCUMENTE UNE BRÈCHE PLUTÔT QU'IL NE LA FERME. C'EST VOULU.</summary>
    [Fact]
    public void En_phase_1_un_role_de_boutique_vaut_pour_tout_le_vendeur()
    {
        var resultat = SellerMember.Join(
            ActeurProprietaire(), CompteNouveau, null, null,
            rolesVendeur: [],
            affectations: [(Guid.NewGuid(), new[] { Role(SystemSellerRoles.OrderManagerId) })]);

        resultat.IsSuccess.Should().BeTrue();

        var membre = resultat.Value;
        membre.StoreMemberships.Should().ContainSingle()
            .Which.Enforcement.Should().Be(StoreEnforcement.Prepared);

        membre.EffectivePermissions(Catalogue).Should().Contain(
            MerchantPermission.OrderConfirm,
            "aucune commande ne connaît sa boutique : la permission vaut pour tout le vendeur");
    }

    // Outillage

    private static MemberActor ActeurProprietaire()
        => MemberAccess.For(SellerMember.Owner(Vendeur, CompteProprietaire), Catalogue);

    private static MemberActor ActeurDe(SellerRole role)
        => new(SellerMemberId.New(), Vendeur, CompteGerant,
            IsOwner: false, CanAct: true, role.Permissions);

    private static SellerMember Gerant()
        => SellerMember.Join(
            ActeurProprietaire(), CompteGerant, "Sophie A.", "Gestionnaire stock",
            rolesVendeur: [Role(SystemSellerRoles.InventoryManagerId)],
            affectations: []).Value;

    // LE CADRAGE PAR BOUTIQUE (lot F)

    private static readonly Guid BoutiqueA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid BoutiqueB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid BoutiqueC = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    /// <summary>LE TROU QUE LE LOT F FERME, ÉNONCÉ EN UN TEST.</summary>
    [Fact]
    public void Un_responsable_de_la_boutique_A_n_agit_pas_sur_la_boutique_B()
    {
        var acteur = MemberAccess.For(MembreDeLaBoutiqueA(), Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.ProductUnpublish).Should().BeTrue();
        acteur.HasInStore(BoutiqueB, MerchantPermission.ProductUnpublish).Should().BeFalse(
            "il n'est affecté qu'à la boutique A");

        // ET L'UNION, ELLE, DIT TOUJOURS OUI — c'est bien pour cela qu'on ne
        // pouvait pas s'en contenter.
        acteur.Has(MerchantPermission.ProductUnpublish).Should().BeTrue();
    }

    /// <summary>UNE BOUTIQUE OÙ LE MEMBRE N'EST PAS AFFECTÉ RETOMBE SUR LE SOCLE.</summary>
    [Fact]
    public void Une_boutique_inconnue_ne_donne_que_le_socle_vendeur()
    {
        var acteur = MemberAccess.For(MembreDeLaBoutiqueA(), Catalogue);

        acteur.HasInStore(BoutiqueC, MerchantPermission.ProductUnpublish).Should().BeFalse();
        acteur.HasInStore(BoutiqueC, MerchantPermission.OrderView).Should().BeTrue(
            "ORDER_VIEW vient du rôle EMPLOYEE attribué au niveau du VENDEUR, donc il vaut partout");
    }

    /// <summary>LE PIÈGE QUE CE TEST GARDE : RECONSTITUER LE SOCLE PAR INTERSECTION.</summary>
    [Fact]
    public void Deux_boutiques_ne_font_pas_un_socle()
    {
        var membre = SellerMember.Join(
            ActeurProprietaire(), CompteNouveau, "Fatou D.", "Responsable réseau",
            rolesVendeur: [],
            affectations:
            [
                (BoutiqueA, new[] { Role(SystemSellerRoles.StoreAdminId) }),
                (BoutiqueB, new[] { Role(SystemSellerRoles.StoreAdminId) })
            ]).Value;

        var acteur = MemberAccess.For(membre, Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.StoreUpdate).Should().BeTrue();
        acteur.HasInStore(BoutiqueB, MerchantPermission.StoreUpdate).Should().BeTrue();
        acteur.HasInStore(BoutiqueC, MerchantPermission.StoreUpdate).Should().BeFalse(
            "STORE_UPDATE vient de ses DEUX affectations, pas d'un rôle au niveau du vendeur");
    }

    /// <summary>
    /// Le propriétaire n'a aucune affectation, et n'en a pas besoin : ses rôles
    /// sont au niveau du vendeur, donc dans le socle, donc dans chaque périmètre.
    /// </summary>
    [Fact]
    public void Le_proprietaire_agit_dans_toute_boutique_sans_y_etre_affecte()
    {
        var acteur = MemberAccess.For(SellerMember.Owner(Vendeur, CompteProprietaire), Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.StoreUpdate).Should().BeTrue();
        acteur.HasInStore(Guid.NewGuid(), MerchantPermission.ProductPublish).Should().BeTrue();
    }

    /// <summary>RETIRER L'AFFECTATION RETIRE LES DROITS, IMMÉDIATEMENT.</summary>
    [Fact]
    public void Retirer_une_affectation_retire_les_droits_de_cette_boutique()
    {
        var membre = MembreDeLaBoutiqueA();
        membre.UnassignStore(ActeurProprietaire(), BoutiqueA).IsSuccess.Should().BeTrue();

        var acteur = MemberAccess.For(membre, Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.ProductUnpublish).Should().BeFalse();
        acteur.HasInStore(BoutiqueA, MerchantPermission.OrderView).Should().BeTrue("le socle reste");
    }

    /// <summary>CE QUE `StoreScoped` DÉCLARE DOIT CORRESPONDRE À CE QUE LE CODE FAIT.</summary>
    [Fact]
    public void Aucune_permission_de_stock_ni_de_commande_n_est_declaree_cadrable()
    {
        MerchantPermissions.StoreScoped.Should().NotContain(
        [
            MerchantPermission.InventoryAdjust,
            MerchantPermission.InventoryView,
            MerchantPermission.StockLocationManage,
            MerchantPermission.OrderConfirm,
            MerchantPermission.OrderCancel,
            MerchantPermission.FinanceView,
            MerchantPermission.MemberInvite
        ]);

        MerchantPermissions.StoreScoped.Should().Contain(
        [
            MerchantPermission.ProductUpdate,
            MerchantPermission.OfferPriceUpdate,
            MerchantPermission.StoreOpenClose
        ]);
    }

    // LES QUATRE RÉGRESSIONS DE L'AUDIT — chacune tenue par un test

    /// <summary>LE BLANCHIMENT D'UNE PERMISSION DE BOUTIQUE VERS LE SOCLE.</summary>
    [Fact]
    public void Un_responsable_de_boutique_ne_recrute_pas_au_niveau_vendeur_avec_ses_droits_de_boutique()
    {
        var responsableDeA = RecruteurDeLaBoutiqueA();

        // Il tient STORE_UPDATE de la boutique A, et de nulle part ailleurs.
        responsableDeA.Has(MerchantPermission.StoreUpdate).Should().BeTrue();
        responsableDeA.SellerLevelPermissions.Should().NotContain(MerchantPermission.StoreUpdate);

        var resultat = SellerMember.Join(
            responsableDeA, CompteRecrue, "Complice", null,
            rolesVendeur: [Role(SystemSellerRoles.StoreAdminId)],
            affectations: []);

        resultat.IsFailure.Should().BeTrue("il ne peut pas donner AU VENDEUR ce qu'il ne tient que de A");
        resultat.Error.Code.Should().Be("sellers.member.cannot_delegate");
    }

    /// <summary>Le pendant : recruter POUR SA PROPRE BOUTIQUE reste permis.</summary>
    [Fact]
    public void Un_responsable_de_boutique_recrute_pour_sa_boutique()
    {
        SellerMember.Join(
            RecruteurDeLaBoutiqueA(), CompteRecrue, "Renfort", null,
            rolesVendeur: [],
            affectations: [(BoutiqueA, new[] { Role(SystemSellerRoles.StoreAdminId) })])
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>Et pas pour une AUTRE boutique, où il n'a que son socle.</summary>
    [Fact]
    public void Un_responsable_de_boutique_ne_recrute_pas_pour_la_boutique_d_a_cote()
    {
        var resultat = SellerMember.Join(
            RecruteurDeLaBoutiqueA(), CompteRecrue, "Renfort", null,
            rolesVendeur: [],
            affectations: [(BoutiqueB, new[] { Role(SystemSellerRoles.StoreAdminId) })]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.cannot_delegate");
    }

    /// <summary>LES DEUX GARDES DU PROPRIÉTAIRE S'ANNULAIENT.</summary>
    [Fact]
    public void Le_proprietaire_peut_recevoir_un_role_de_plus()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);

        var resultat = proprietaire.SetSellerRoles(
            ActeurProprietaire(),
            [Role(SystemSellerRoles.OwnerId), Role(SystemSellerRoles.FinanceManagerId)]);

        resultat.IsSuccess.Should().BeTrue();
        proprietaire.SellerRoleIds.Should().HaveCount(2);
    }

    /// <summary>Et il ne peut toujours pas se dépouiller d'OWNER par ce chemin.</summary>
    [Fact]
    public void Le_proprietaire_ne_se_depouille_pas_de_son_role()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);

        var resultat = proprietaire.SetSellerRoles(
            ActeurProprietaire(), [Role(SystemSellerRoles.FinanceManagerId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    /// <summary>Un membre ORDINAIRE ne reçoit pas OWNER pour autant.</summary>
    [Fact]
    public void Un_membre_ordinaire_ne_recoit_pas_le_role_de_proprietaire()
    {
        var membre = Gerant();

        var resultat = membre.SetSellerRoles(
            ActeurProprietaire(), [Role(SystemSellerRoles.OwnerId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    /// <summary>`Leave` N'AVAIT AUCUN APPELANT — LE DÉPART VOLONTAIRE ÉTAIT IMPOSSIBLE.</summary>
    [Fact]
    public void Un_membre_quitte_l_equipe_de_lui_meme()
    {
        var membre = Gerant();

        membre.Leave(estDernierProprietaire: false, aUneAutreAppartenance: false)
            .IsSuccess.Should().BeTrue();

        membre.Status.Should().Be(MemberStatus.Left);
        membre.CanAct.Should().BeFalse();
    }

    /// <summary>Et le dernier propriétaire ne part pas : il transfère d'abord.</summary>
    [Fact]
    public void Le_dernier_proprietaire_ne_quitte_pas_son_dossier()
    {
        var proprietaire = SellerMember.Owner(Vendeur, CompteProprietaire);

        var depart = proprietaire.Leave(estDernierProprietaire: true, aUneAutreAppartenance: false);

        depart.IsFailure.Should().BeTrue();
        depart.Error.Code.Should().Be("sellers.member.last_owner");
        proprietaire.CanAct.Should().BeTrue();
    }

    /// <summary>`UpdateProfile` NE VÉRIFIAIT NI L'ACTIVITÉ NI LE VENDEUR SUR SOI-MÊME.</summary>
    [Fact]
    public void Un_membre_parti_ne_modifie_plus_sa_fiche()
    {
        var membre = Gerant();
        membre.Leave(estDernierProprietaire: false, aUneAutreAppartenance: false);

        var acteur = new MemberActor(
            membre.Id, Vendeur, CompteGerant,
            IsOwner: false, CanAct: false, MerchantPermissions.All.ToHashSet());

        var resultat = membre.UpdateProfile(acteur, "Nouveau nom", null);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.not_active");
    }

    private static readonly Guid CompteRecrue = Guid.Parse("66666666-6666-4666-8666-000000000006");

    /// <summary>
    /// Un acteur qui peut RECRUTER, et dont STORE_UPDATE ne vient que de la
    /// boutique A.
    /// </summary>
    private static MemberActor RecruteurDeLaBoutiqueA()
    {
        var recruteur = RolePersonnalise([MerchantPermission.MemberInvite, MerchantPermission.MemberView]);

        var membre = SellerMember.Join(
            ActeurProprietaire(), CompteGerant, "Awa K.", "Responsable boutique A",
            rolesVendeur: [recruteur],
            affectations: [(BoutiqueA, new[] { Role(SystemSellerRoles.StoreAdminId) })]).Value;

        return MemberAccess.For(membre, [.. Catalogue, recruteur]);
    }

    /// <summary>
    /// Un membre affecté à la seule boutique A, plus des rôles au niveau vendeur.
    /// </summary>
    private static SellerMember MembreDeLaBoutiqueA()
        => SellerMember.Join(
            ActeurProprietaire(), CompteNouveau, "Yao B.", "Responsable boutique A",
            rolesVendeur: [Role(SystemSellerRoles.EmployeeId)],
            affectations: [(BoutiqueA, new[] { Role(SystemSellerRoles.StoreAdminId) })]).Value;

    private static SellerRole RolePersonnalise(IReadOnlyCollection<MerchantPermission> permissions)
        => SellerRole.Custom(
            Vendeur, "Rôle d'essai", null, RoleScope.Seller,
            MerchantPermissions.All.ToHashSet(), permissions).Value;
}
