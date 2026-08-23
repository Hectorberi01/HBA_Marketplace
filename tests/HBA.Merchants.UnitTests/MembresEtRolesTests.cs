using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Members.Events;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉQUIPE D'UN VENDEUR — CE QUI TIENT, ET CE QU'ON VIENT DE FERMER.
///
/// CES RÈGLES N'ONT BESOIN NI DE BASE, NI DE SERVEUR, POUR ÊTRE FAUSSES.
///
/// Un RBAC se casse par ses cas limites : celui qui s'auto-promeut, celui qui
/// donne ce qu'il n'a pas, le dernier propriétaire qui s'en va. Aucun de ces
/// scénarios n'a besoin de PostgreSQL — et les éprouver à travers l'API
/// demanderait Testcontainers et une minute par exécution, donc en pratique de ne
/// pas les éprouver.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MembresEtRolesTests
{
    private static readonly Guid Vendeur = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AutreVendeur = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CompteProprietaire = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid CompteGerant = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid CompteNouveau = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static IReadOnlyList<SellerRole> Catalogue => SystemSellerRoles.Catalogue;

    private static SellerRole Role(SellerRoleId id) => Catalogue.First(r => r.Id == id);

    // ═════════════════════════════════════════════════════════════════════════
    // Le catalogue des permissions
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CE TEST NE VÉRIFIE PAS UN NOMBRE, IL DÉCLENCHE LE CONSTRUCTEUR STATIQUE.
    ///
    /// `MerchantPermissions` lève au chargement si une valeur de l'énumération n'a
    /// pas sa ligne dans le catalogue, ou si deux permissions partagent un code.
    /// Toucher à ce simple appel suffit donc à faire échouer la suite le jour où
    /// quelqu'un ajoute une permission sans la décrire.
    /// </summary>
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

    /// <summary>
    /// LES TROIS QUI DÉTOURNENT L'ARGENT SONT BIEN RÉSERVÉES.
    ///
    /// `PUT /{sellerId}/payout-account` fixe le numéro Mobile Money où partent les
    /// gains. Si cette permission cessait d'être réservée, un rôle personnalisé
    /// pourrait la porter — et tout ce que les gardes de propriété ont fermé
    /// s'ouvrirait par la porte de côté.
    /// </summary>
    [Fact]
    public void Les_permissions_qui_touchent_a_l_argent_sont_reservees_au_proprietaire()
    {
        MerchantPermission.PayoutConfigure.IsOwnerOnly().Should().BeTrue();
        MerchantPermission.BankAccountUpdate.IsOwnerOnly().Should().BeTrue();
        MerchantPermission.OwnershipTransfer.IsOwnerOnly().Should().BeTrue();

        MerchantPermissions.Critical.Should().Contain(MerchantPermission.WithdrawalRequest,
            "une demande de retrait n'est pas réservée, mais elle exigera une réauthentification");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Les rôles système
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Le_proprietaire_porte_toutes_les_permissions()
        => Role(SystemSellerRoles.OwnerId).Permissions
            .Should().HaveCount(MerchantPermissions.All.Count);

    /// <summary>
    /// « ADMINISTRATION GÉNÉRALE HORS ACTIONS RÉSERVÉES » — LITTÉRALEMENT.
    ///
    /// C'est le rôle le plus dangereux du catalogue : il a tout, sauf ce qui
    /// détourne l'argent. Une permission réservée qui s'y glisserait rendrait la
    /// réserve inopérante sans qu'aucune garde ne soit touchée.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Les rôles personnalisés (§18)
    // ═════════════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// LE TEST QUI REMPLACE LA HIÉRARCHIE PAR ORDINAL.
    ///
    /// L'acteur est un gestionnaire de commandes parfaitement légitime. Il tente de
    /// créer un rôle portant l'ajustement de stock — qu'il n'a pas. Un modèle à
    /// rangs laisserait passer si le rôle créé était « plus bas » que lui ; une
    /// inclusion d'ensembles ne peut pas.
    /// </summary>
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
    /// MÊME LE PROPRIÉTAIRE NE PEUT PAS METTRE UNE PERMISSION RÉSERVÉE DANS UN RÔLE.
    ///
    /// Il la possède pourtant : `MerchantPermissions.All` la contient. La réserve
    /// ne porte pas sur qui la détient, mais sur le fait qu'elle ne se DÉLÈGUE pas
    /// — sinon « réservé au propriétaire » ne voudrait rien dire, il suffirait
    /// d'un rôle pour la contourner.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ON NE SUPPRIME PAS UN RÔLE QU'ON N'AURAIT PAS PU CRÉER (lot A3).
    ///
    /// C'est le pendant de la règle de délégation, et il ne va pas de soi : on ne
    /// GAGNE rien à supprimer un rôle, donc ce n'est pas une escalade. C'est un
    /// dégât — un gestionnaire de catalogue qui porte `ROLE_DELETE` pourrait
    /// effacer le rôle du comptable, et celui-ci ne se recrée qu'en le retapant
    /// permission par permission, en espérant n'en oublier aucune.
    ///
    /// L'autorité d'un acteur sur un rôle se mesure aux permissions que ce rôle
    /// PORTE, jamais au verbe HTTP employé pour l'atteindre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// LE PIÈGE QUE CE TEST GARDE : `FirstOrDefault` SUR UNE ÉNUMÉRATION.
    ///
    /// Sans le `Cast&lt;MerchantPermission?&gt;`, l'absence de permission hors portée
    /// rend `0` — c'est-à-dire `PRODUCT_VIEW`, une valeur parfaitement légitime. Le
    /// refus tomberait alors exactement à l'envers : bloqué quand tout va bien,
    /// passant quand l'acteur est trop faible. Un acteur qui porte TOUT doit
    /// pouvoir supprimer.
    /// </summary>
    [Fact]
    public void Un_acteur_qui_porte_tout_supprime_sans_refus()
    {
        var role = RolePersonnalise([MerchantPermission.OrderView, MerchantPermission.FinanceView]);

        role.EnsureDeletable(membresPortantCeRole: 0, MerchantPermissions.All.ToHashSet())
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>Le rôle encore porté est refusé AVANT qu'on regarde la délégation.</summary>
    /// <remarks>
    /// L'ordre compte pour le message : un acteur tout-puissant qui supprime un rôle
    /// encore attribué doit lire « il est porté par 3 membres », pas un refus de
    /// délégation qui ne s'applique pas à lui.
    /// </remarks>
    [Fact]
    public void Le_role_encore_porte_prime_sur_la_delegation()
    {
        var role = RolePersonnalise([MerchantPermission.FinanceView]);

        var refus = role.EnsureDeletable(membresPortantCeRole: 3, new HashSet<MerchantPermission>());

        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("sellers.role.in_use");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Les membres
    // ═════════════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// UN MEMBRE SUSPENDU N'A PLUS AUCUNE PERMISSION — PAS « MOINS », AUCUNE.
    ///
    /// C'est le §5 : la suspension conserve l'historique et les affectations mais
    /// interdit immédiatement l'accès. Rendre l'ensemble amputé plutôt que vide
    /// laisserait un membre suspendu conserver ses droits de lecture, ce que
    /// personne n'a décidé.
    /// </summary>
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

    /// <summary>
    /// L'ESCALADE LA PLUS COURTE DU MODULE, ET ELLE EST FERMÉE.
    ///
    /// SELLER_ADMIN porte `MEMBER_REVOKE`. Sans cette garde, il révoquerait le
    /// propriétaire du dossier et resterait seul aux commandes — d'un vendeur dont
    /// il ne peut plus, lui, changer le compte de reversement. Un dossier
    /// définitivement bloqué, en un appel.
    /// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DRAPEAU QUI EMPÊCHE D'ENFERMER QUELQU'UN DEHORS.
    ///
    /// Le rôle `Seller` du jeton a deux causes possibles : être vendeur soi-même,
    /// ou appartenir à une équipe. L'événement de sortie porte donc « en
    /// reste-t-il une autre ? », et identity ne retire le rôle que si la réponse
    /// est non.
    ///
    /// Sans ce champ, révoquer un comptable chez un commerçant lui ferait perdre
    /// l'accès à SON PROPRE dossier vendeur, sur lequel personne n'aurait rien
    /// fait — la cause serait ailleurs que le symptôme.
    ///
    /// Il vient de l'appelant et n'est jamais recalculé en aval : les événements
    /// de domaine sont dépêchés AVANT l'enregistrement, si bien qu'une lecture
    /// faite plus loin verrait le membre encore actif et répondrait
    /// invariablement « oui ».
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// L'ACTEUR A ICI TOUTES LES PERMISSIONS — SANS QUOI LE TEST NE PROUVERAIT RIEN.
    ///
    /// Un acteur à qui il manquerait `MEMBER_ASSIGN_ROLE` serait refusé par
    /// l'habilitation, qui passe AVANT, et le test serait vert sans avoir jamais
    /// éprouvé la règle qu'il prétend éprouver. C'est le piège classique du test
    /// d'autorisation : vérifier un refus sans savoir lequel.
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

    /// <summary>
    /// « INTROUVABLE » ET NON « INTERDIT », ET C'EST DÉLIBÉRÉ.
    ///
    /// Les identifiants de membres circulent dans l'écran d'équipe. Un 403
    /// confirmerait à qui en essaie lesquels existent chez le concurrent d'à côté.
    /// </summary>
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

    /// <summary>
    /// LE RÔLE DE PROPRIÉTAIRE NE S'ATTRIBUE PAS, IL SE TRANSFÈRE.
    ///
    /// Sans cette garde, un propriétaire fabriquerait un second propriétaire par
    /// une simple modification de rôles — sans que rien ne le distingue d'un
    /// changement d'intitulé, et sans l'événement qu'un transfert doit produire.
    /// </summary>
    [Fact]
    public void Le_role_de_proprietaire_ne_s_attribue_pas_par_la_liste_des_roles()
    {
        var membre = Gerant();

        var resultat = membre.SetSellerRoles(
            ActeurProprietaire(), [Role(SystemSellerRoles.OwnerId)]);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    /// <summary>
    /// NI LUI-MÊME, NI UN AUTRE PROPRIÉTAIRE.
    ///
    /// Le jumeau plus bas — <see cref="Le_proprietaire_ne_se_depouille_pas_de_son_role"/>
    /// — fait porter le geste au propriétaire LUI-MÊME. Celui-ci le fait porter
    /// par un SECOND propriétaire, qui détient pourtant toutes les permissions.
    ///
    /// La distinction n'est pas cosmétique : la garde de délégation compare les
    /// droits de l'acteur à ceux qu'il attribue, et un propriétaire les a tous.
    /// Rien dans ce chemin-là ne l'arrêterait ; c'est le verrou sur OWNER qui le
    /// fait, et c'est ce verrou-ci que ce test vérifie. Les deux portaient le
    /// même nom, ce qui a fini par un CS0111 — deux méthodes, une seule
    /// signature.
    /// </summary>
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

    /// <summary>
    /// ON N'ATTRIBUE PAS UN RÔLE QUI PORTE PLUS QUE SOI.
    ///
    /// Le pendant, pour les membres, du test sur la création de rôles. L'acteur
    /// est un gestionnaire de stock qui a le droit d'inviter ; il tente de recruter
    /// quelqu'un avec un rôle de commandes qu'il ne détient pas lui-même.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // La Phase 1, telle qu'elle est
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CE TEST DOCUMENTE UNE BRÈCHE PLUTÔT QU'IL NE LA FERME. C'EST VOULU.
    ///
    /// Un rôle affecté à UNE boutique donne aujourd'hui ses permissions sur le
    /// VENDEUR ENTIER — parce que ni `OrderLine` ni `InventoryItem` ne connaît la
    /// boutique. Le champ `Enforcement` vaut donc `Prepared`, et le garde-fou est
    /// ailleurs : la couche Application refuse d'attribuer un rôle à vocation
    /// boutique dès que le vendeur en a plus d'une (D27).
    ///
    /// Le jour du lot G, cette assertion devra être inversée. C'est exactement ce
    /// qu'on attend d'elle : qu'elle échoue bruyamment quand le comportement
    /// change, plutôt que de laisser le changement passer inaperçu.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

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

    // ═════════════════════════════════════════════════════════════════════════
    // LE CADRAGE PAR BOUTIQUE (lot F)
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly Guid BoutiqueA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid BoutiqueB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid BoutiqueC = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TROU QUE LE LOT F FERME, ÉNONCÉ EN UN TEST.
    ///
    /// Un responsable de la boutique A dépubliait les fiches de la boutique B du
    /// même vendeur. Rien ne le signalait : `EffectivePermissions` rend l'UNION de
    /// tout ce que le membre porte, boutiques confondues, et la garde du catalogue
    /// comparait cette union sans jamais nommer de boutique.
    ///
    /// C'est précisément ce que la décision D27 compensait, en INTERDISANT au
    /// vendeur d'ouvrir un second magasin tant qu'un membre portait un rôle de
    /// vocation boutique. Le cadrage rend cet interdit inutile pour les
    /// permissions qu'il couvre — voir `MerchantPermissions.StoreScoped`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// UNE BOUTIQUE OÙ LE MEMBRE N'EST PAS AFFECTÉ RETOMBE SUR LE SOCLE.
    ///
    /// Pas sur l'union — ce serait le trou reconstitué — et pas sur « rien » non
    /// plus : le comptable rattaché au vendeur lit les finances partout, y compris
    /// dans une boutique qui ne le connaît pas.
    /// </summary>
    [Fact]
    public void Une_boutique_inconnue_ne_donne_que_le_socle_vendeur()
    {
        var acteur = MemberAccess.For(MembreDeLaBoutiqueA(), Catalogue);

        acteur.HasInStore(BoutiqueC, MerchantPermission.ProductUnpublish).Should().BeFalse();
        acteur.HasInStore(BoutiqueC, MerchantPermission.OrderView).Should().BeTrue(
            "ORDER_VIEW vient du rôle EMPLOYEE attribué au niveau du VENDEUR, donc il vaut partout");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PIÈGE QUE CE TEST GARDE : RECONSTITUER LE SOCLE PAR INTERSECTION.
    ///
    /// Chaque entrée de `PermissionsByStore` contient le socle plus les rôles de sa
    /// boutique, ce qui donne très envie de retrouver le socle en intersectant les
    /// entrées — un champ de moins à transporter jusque dans le contrat gRPC.
    ///
    /// Ça ne marche pas. Un membre STORE_ADMIN sur A ET sur B porte `STORE_UPDATE`
    /// dans les deux entrées, donc dans leur intersection : une boutique C où il
    /// n'est pas affecté hériterait du droit. La version intersectée de ce code a
    /// existé une heure ; ce test est ce qui la rend impossible à réintroduire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// Aucun cas particulier n'est écrit pour lui — c'est la structure qui le porte.
    /// </summary>
    [Fact]
    public void Le_proprietaire_agit_dans_toute_boutique_sans_y_etre_affecte()
    {
        var acteur = MemberAccess.For(SellerMember.Owner(Vendeur, CompteProprietaire), Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.StoreUpdate).Should().BeTrue();
        acteur.HasInStore(Guid.NewGuid(), MerchantPermission.ProductPublish).Should().BeTrue();
    }

    /// <summary>
    /// RETIRER L'AFFECTATION RETIRE LES DROITS, IMMÉDIATEMENT.
    ///
    /// `UnassignStore` supprime la ligne : la boutique disparaît du dictionnaire, et
    /// la garde retombe sur le socle — ce que le membre porte au niveau du vendeur,
    /// et rien de la boutique dont on vient de le retirer.
    ///
    /// C'est le geste qu'un vendeur fait quand un employé change de magasin, et il
    /// doit mordre avant qu'il n'ait fini de le dire. La purge du cache d'accès, à
    /// l'autre bout, est ce qui le rend vrai en production.
    /// </summary>
    [Fact]
    public void Retirer_une_affectation_retire_les_droits_de_cette_boutique()
    {
        var membre = MembreDeLaBoutiqueA();
        membre.UnassignStore(ActeurProprietaire(), BoutiqueA).IsSuccess.Should().BeTrue();

        var acteur = MemberAccess.For(membre, Catalogue);

        acteur.HasInStore(BoutiqueA, MerchantPermission.ProductUnpublish).Should().BeFalse();
        acteur.HasInStore(BoutiqueA, MerchantPermission.OrderView).Should().BeTrue("le socle reste");
    }

    /// <summary>
    /// CE QUE `StoreScoped` DÉCLARE DOIT CORRESPONDRE À CE QUE LE CODE FAIT.
    ///
    /// Ce test ne peut pas vérifier les appels à `CanInStore` dans catalog — ils
    /// vivent dans un autre assemblage. Il verrouille l'autre moitié : que les
    /// familles qu'on SAIT non cadrables n'y entrent pas par distraction. Y ajouter
    /// `INVENTORY_ADJUST` ne casserait rien de visible — cela AUTORISERAIT
    /// simplement une attribution que plus rien ne cadre, ce qui est le défaut le
    /// moins détectable du module.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // LES QUATRE RÉGRESSIONS DE L'AUDIT — chacune tenue par un test
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE BLANCHIMENT D'UNE PERMISSION DE BOUTIQUE VERS LE SOCLE.
    ///
    /// M est responsable de la seule boutique A. Son UNION contient donc
    /// STORE_UPDATE — mais son socle, non. Tant que la délégation se mesurait à
    /// l'union, il pouvait recruter N EN LUI DONNANT CE RÔLE AU NIVEAU DU VENDEUR :
    /// STORE_UPDATE entrait alors dans le socle de N, donc dans TOUTES les
    /// boutiques, B comprise.
    ///
    /// M fabriquait ainsi un compte capable d'administrer une boutique sur laquelle
    /// il n'avait lui-même aucun droit. Le lot F avait fermé la porte de l'ACTION
    /// et laissé ouverte celle de l'ATTRIBUTION.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// Le pendant : recruter POUR SA PROPRE BOUTIQUE reste permis. La règle borne le
    /// périmètre, elle ne ferme pas la délégation.
    /// </summary>
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

    /// <summary>
    /// LES DEUX GARDES DU PROPRIÉTAIRE S'ANNULAIENT.
    ///
    /// `SetSellerRoles` exige OWNER dans la liste quand la cible est propriétaire ;
    /// `EnsureCanAssign` refusait OWNER dans la liste. Aucune liste ne passait : les
    /// rôles du propriétaire étaient FIGÉS À JAMAIS, et le message parlait d'un
    /// transfert de propriété que l'appelant n'avait pas demandé.
    /// </summary>
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

    /// <summary>
    /// `Leave` N'AVAIT AUCUN APPELANT — LE DÉPART VOLONTAIRE ÉTAIT IMPOSSIBLE.
    ///
    /// La méthode était écrite, testée, protégée contre le départ du dernier
    /// propriétaire, et injoignable : ni commande, ni route. Pour quitter une
    /// équipe, il fallait demander à quelqu'un d'autre de vous révoquer. Le statut
    /// `Left` n'était donc jamais atteignable en production.
    /// </summary>
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

    /// <summary>
    /// `UpdateProfile` NE VÉRIFIAIT NI L'ACTIVITÉ NI LE VENDEUR SUR SOI-MÊME.
    ///
    /// Sans route exposée, ce n'était pas exploitable — la première route qui
    /// l'exposera hériterait du trou. Un membre révoqué éditait sa fiche, et un
    /// acteur d'un autre vendeur dont l'identifiant de membre coïnciderait aussi.
    /// </summary>
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
    /// Un acteur qui peut RECRUTER, et dont STORE_UPDATE ne vient que de la boutique A.
    /// </summary>
    /// <remarks>
    /// MEMBER_INVITE EST AU NIVEAU DU VENDEUR, PAS DANS LA BOUTIQUE.
    ///
    /// C'est ce qui rend le scénario réaliste plutôt que théorique : un vendeur
    /// délègue volontiers le recrutement à un responsable de magasin, sans pour
    /// autant lui donner la main sur les autres magasins. La faille était là.
    /// </remarks>
    private static MemberActor RecruteurDeLaBoutiqueA()
    {
        var recruteur = RolePersonnalise([MerchantPermission.MemberInvite, MerchantPermission.MemberView]);

        var membre = SellerMember.Join(
            ActeurProprietaire(), CompteGerant, "Awa K.", "Responsable boutique A",
            rolesVendeur: [recruteur],
            affectations: [(BoutiqueA, new[] { Role(SystemSellerRoles.StoreAdminId) })]).Value;

        return MemberAccess.For(membre, [.. Catalogue, recruteur]);
    }

    /// <summary>Un membre affecté à la seule boutique A, plus des rôles au niveau vendeur.</summary>
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
