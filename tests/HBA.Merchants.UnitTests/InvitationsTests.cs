using HBA.Merchants.Domain.Members;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.UnitTests;

/// <summary>LES INVITATIONS — CE QUI SÉPARE UN LIEN D'UN PASSE-PARTOUT.</summary>
public sealed class InvitationsTests
{
    private static readonly Guid Vendeur = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AutreVendeur = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CompteProprietaire = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid CompteInvite = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private const string Adresse = "david@example.com";
    private const string Empreinte = "0000000000000000000000000000000000000000000000000000000000000001";

    private static IReadOnlyList<SellerRole> Catalogue => SystemSellerRoles.Catalogue;

    private static SellerRole Role(SellerRoleId id) => Catalogue.First(r => r.Id == id);

    // L'émission

    [Fact]
    public void Le_proprietaire_invite_un_gestionnaire_de_commandes()
    {
        var invitation = Emettre();

        invitation.IsSuccess.Should().BeTrue();
        invitation.Value.Status.Should().Be(InvitationStatus.Pending);
        invitation.Value.Email.Should().Be(Adresse);
        invitation.Value.InvitedByUserId.Should().Be(CompteProprietaire);
        invitation.Value.SellerRoleIds.Should().ContainSingle()
            .Which.Should().Be(SystemSellerRoles.OrderManagerId);
    }

    /// <summary>L'ADRESSE EST NORMALISÉE À L'ÉMISSION, PAS À LA COMPARAISON.</summary>
    [Fact]
    public void L_adresse_est_normalisee_des_l_emission()
    {
        var invitation = Emettre(email: "  DAVID@Example.COM ");

        invitation.IsSuccess.Should().BeTrue();
        invitation.Value.Email.Should().Be("david@example.com");
    }

    /// <summary>UNE INVITATION SANS RÔLE PRODUIRAIT LE PIRE DES ÉTATS.</summary>
    [Fact]
    public void Une_invitation_sans_role_est_refusee()
    {
        var invitation = SellerInvitation.Create(
            ActeurProprietaire(), Adresse, null, null,
            rolesVendeur: [], affectations: [], Empreinte, DansUneSemaine());

        invitation.IsFailure.Should().BeTrue();
        invitation.Error.Code.Should().Be("sellers.invitation.roles_required");
    }

    /// <summary>LA RELANCE RESSUSCITAIT UNE DÉLÉGATION QU'ON N'AURAIT PAS PU CRÉER.</summary>
    [Fact]
    public void Une_relance_ne_ressuscite_pas_ce_que_le_relanceur_ne_peut_pas_accorder()
    {
        var invitation = SellerInvitation.Create(
            ActeurProprietaire(), Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.SellerAdminId)],
            affectations: [], Empreinte, DansUneSemaine()).Value;

        var recruteur = new MemberActor(
            SellerMemberId.New(), Vendeur, Guid.NewGuid(),
            IsOwner: false, CanAct: true,
            new HashSet<MerchantPermission> { MerchantPermission.MemberInvite });

        var relance = invitation.Refresh(
            recruteur, "empreinte-neuve", DansUneSemaine(), Promis(SystemSellerRoles.SellerAdminId));

        relance.IsFailure.Should().BeTrue();
        relance.Error.Code.Should().Be("sellers.member.cannot_delegate");
    }

    /// <summary>Le propriétaire, lui, relance sans obstacle.</summary>
    [Fact]
    public void Le_proprietaire_relance_son_invitation()
    {
        var invitation = SellerInvitation.Create(
            ActeurProprietaire(), Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.SellerAdminId)],
            affectations: [], Empreinte, DansUneSemaine()).Value;

        invitation.Refresh(
            ActeurProprietaire(), "empreinte-neuve", DansUneSemaine(),
            Promis(SystemSellerRoles.SellerAdminId))
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>UN RÔLE SUPPRIMÉ DEPUIS L'ÉMISSION ARRÊTE LA RELANCE.</summary>
    [Fact]
    public void Une_relance_s_arrete_si_un_role_promis_a_disparu()
    {
        var invitation = SellerInvitation.Create(
            ActeurProprietaire(), Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.SellerAdminId)],
            affectations: [], Empreinte, DansUneSemaine()).Value;

        var relance = invitation.Refresh(
            ActeurProprietaire(), "empreinte-neuve", DansUneSemaine(),
            new Dictionary<SellerRoleId, SellerRole>());

        relance.IsFailure.Should().BeTrue();
        relance.Error.Code.Should().Be("sellers.invitation.role_missing");
    }

    private static IReadOnlyDictionary<SellerRoleId, SellerRole> Promis(params SellerRoleId[] ids)
        => ids.ToDictionary(id => id, Role);

    [Fact]
    public void Un_compte_sans_droit_d_inviter_n_invite_pas()
    {
        var acteur = new MemberActor(
            SellerMemberId.New(), Vendeur, Guid.NewGuid(),
            IsOwner: false, CanAct: true, Role(SystemSellerRoles.OrderManagerId).Permissions);

        var invitation = SellerInvitation.Create(
            acteur, Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.OrderManagerId)],
            affectations: [], Empreinte, DansUneSemaine());

        invitation.IsFailure.Should().BeTrue();
        invitation.Error.Code.Should().Be("sellers.member.permission_denied");
    }

    /// <summary>ON N'INVITE PAS QUELQU'UN DE PLUS PUISSANT QUE SOI.</summary>
    [Fact]
    public void On_n_invite_pas_avec_un_role_qu_on_ne_detient_pas()
    {
        var acteur = new MemberActor(
            SellerMemberId.New(), Vendeur, Guid.NewGuid(),
            IsOwner: false, CanAct: true,
            Role(SystemSellerRoles.OrderManagerId).Permissions
                .Append(MerchantPermission.MemberInvite)
                .ToHashSet());

        var invitation = SellerInvitation.Create(
            acteur, Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.InventoryManagerId)],
            affectations: [], Empreinte, DansUneSemaine());

        invitation.IsFailure.Should().BeTrue();
        invitation.Error.Code.Should().Be("sellers.member.cannot_delegate");
    }

    [Fact]
    public void Le_role_de_proprietaire_ne_s_invite_pas()
    {
        var invitation = SellerInvitation.Create(
            ActeurProprietaire(), Adresse, null, null,
            rolesVendeur: [Role(SystemSellerRoles.OwnerId)],
            affectations: [], Empreinte, DansUneSemaine());

        invitation.IsFailure.Should().BeTrue();
        invitation.Error.Code.Should().Be("sellers.member.owner_role_locked");
    }

    // L'acceptation

    [Fact]
    public void L_invite_accepte_et_devient_membre()
    {
        var invitation = Emettre().Value;

        invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        invitation.Status.Should().Be(InvitationStatus.Accepted);
        invitation.AcceptedByUserId.Should().Be(CompteInvite);

        var membre = SellerMember.FromInvitation(
            invitation, [Role(SystemSellerRoles.OrderManagerId)], []);

        membre.IsSuccess.Should().BeTrue();
        membre.Value.UserId.Should().Be(CompteInvite);
        membre.Value.InvitedByUserId.Should().Be(CompteProprietaire);
        membre.Value.Status.Should().Be(MemberStatus.Active);
        membre.Value.EffectivePermissions(Catalogue).Should().Contain(MerchantPermission.OrderConfirm);
    }

    /// <summary>LE LIEN TRANSFÉRÉ N'OUVRE RIEN.</summary>
    [Fact]
    public void Une_autre_adresse_n_accepte_pas()
    {
        var invitation = Emettre().Value;

        var resultat = invitation.Accept(CompteInvite, "quelqun.dautre@example.com", DateTime.UtcNow);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.Forbidden);
        resultat.Error.Code.Should().Be("sellers.invitation.email_mismatch");
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    /// <summary>LE STATUT EST POSÉ AU PASSAGE, ET C'EST LA MOITIÉ DE L'INTÉRÊT.</summary>
    [Fact]
    public void Une_invitation_expiree_est_refusee_et_marquee()
    {
        var invitation = Emettre(echeance: DateTime.UtcNow.AddDays(-1)).Value;

        var resultat = invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.invitation.expired");
        invitation.Status.Should().Be(InvitationStatus.Expired);
    }

    /// <summary>L'usage unique, éprouvé littéralement.</summary>
    [Fact]
    public void Une_invitation_acceptee_ne_se_rejoue_pas()
    {
        var invitation = Emettre().Value;
        invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow);

        var second = invitation.Accept(Guid.NewGuid(), Adresse, DateTime.UtcNow);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("sellers.invitation.already_accepted");
        invitation.AcceptedByUserId.Should().Be(CompteInvite, "le premier acceptant reste le seul");
    }

    [Fact]
    public void Une_invitation_revoquee_ne_s_accepte_pas()
    {
        var invitation = Emettre().Value;
        invitation.Revoke(ActeurProprietaire()).IsSuccess.Should().BeTrue();

        var resultat = invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.invitation.revoked");
    }

    [Fact]
    public void Un_membre_ne_nait_pas_d_une_invitation_non_acceptee()
    {
        var invitation = Emettre().Value;

        var membre = SellerMember.FromInvitation(
            invitation, [Role(SystemSellerRoles.OrderManagerId)], []);

        membre.IsFailure.Should().BeTrue();
        membre.Error.Code.Should().Be("sellers.invitation.not_accepted");
    }

    // Le renvoi et la révocation

    /// <summary>LE RENVOI REMPLACE L'EMPREINTE : IL N'Y A JAMAIS DEUX LIENS VIVANTS.</summary>
    [Fact]
    public void Un_renvoi_remplace_l_empreinte_et_rouvre_une_invitation_expiree()
    {
        var invitation = Emettre(echeance: DateTime.UtcNow.AddDays(-1)).Value;
        invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow);
        invitation.Status.Should().Be(InvitationStatus.Expired);

        const string nouvelle = "00000000000000000000000000000000000000000000000000000000000000ff";

        // LES RÔLES PROMIS SE PASSENT À LA RELANCE — voir `Promis` plus haut, et
        // les deux tests de délégation qui l'accompagnent.
        var relance = invitation.Refresh(
            ActeurProprietaire(), nouvelle, DansUneSemaine(),
            Promis(SystemSellerRoles.OrderManagerId));

        relance.IsSuccess.Should().BeTrue();
        invitation.Status.Should().Be(InvitationStatus.Pending);
        invitation.TokenHash.Should().Be(nouvelle);
        invitation.ResolvedOnUtc.Should().BeNull();
    }

    [Fact]
    public void Une_invitation_acceptee_ne_se_renvoie_pas()
    {
        var invitation = Emettre().Value;
        invitation.Accept(CompteInvite, Adresse, DateTime.UtcNow);

        var relance = invitation.Refresh(
            ActeurProprietaire(), Empreinte, DansUneSemaine(),
            Promis(SystemSellerRoles.OrderManagerId));

        relance.IsFailure.Should().BeTrue();
        relance.Error.Code.Should().Be("sellers.invitation.not_pending");
    }

    /// <summary>« Introuvable » et non « interdit » : même raison que partout ailleurs.</summary>
    [Fact]
    public void Une_invitation_d_un_autre_vendeur_est_introuvable()
    {
        var invitation = Emettre().Value;

        var etranger = new MemberActor(
            SellerMemberId.New(), AutreVendeur, Guid.NewGuid(),
            IsOwner: true, CanAct: true, MerchantPermissions.All.ToHashSet());

        var resultat = invitation.Revoke(etranger);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // Outillage

    private static DateTime DansUneSemaine() => DateTime.UtcNow.Add(SellerInvitation.DureeParDefaut);

    private static MemberActor ActeurProprietaire()
        => MemberAccess.For(SellerMember.Owner(Vendeur, CompteProprietaire), Catalogue);

    private static Result<SellerInvitation> Emettre(string? email = null, DateTime? echeance = null)
        => SellerInvitation.Create(
            ActeurProprietaire(), email ?? Adresse, "David K.", "Responsable commandes",
            rolesVendeur: [Role(SystemSellerRoles.OrderManagerId)],
            affectations: [],
            Empreinte,
            echeance ?? DansUneSemaine());
}
