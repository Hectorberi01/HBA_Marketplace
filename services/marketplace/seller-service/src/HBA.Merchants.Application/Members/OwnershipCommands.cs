using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>
/// Transférer la propriété du dossier à un autre membre de l'équipe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `OWNERSHIP_TRANSFER` ÉTAIT DÉCLARÉE, CRITIQUE, RÉSERVÉE AU PROPRIÉTAIRE —
/// ET NE GARDAIT RIEN (ISSUE-040).
///
/// Aucune route ne l'exigeait, aucun handler ne la lisait. Trois gardes du domaine
/// renvoyaient pourtant l'utilisateur vers « un transfert de propriété » qui
/// n'existait nulle part.
///
/// Ce que cela coûtait : un dossier dont le propriétaire disparaît devient
/// DÉFINITIVEMENT inadministrable. `SELLER_CLOSE`, `PAYOUT_CONFIGURE` et
/// `SELLER_REACTIVATE` ne sont portés par aucun autre rôle — `SELLER_ADMIN` est
/// semé avec `All.Where(p => !p.IsOwnerOnly())` — et `EnsureCanAdminister` interdit
/// à quiconque n'est pas propriétaire de toucher un propriétaire. Le commerçant ne
/// peut donc ni fermer son dossier, ni changer son compte de reversement, ni
/// reprendre la main.
///
/// LE BÉNÉFICIAIRE EST DÉSIGNÉ PAR SON IDENTIFIANT DE MEMBRE, PAS PAR SON
/// COMPTE.
///
/// Un `UserId` obligerait à chercher l'appartenance, et l'échec de cette recherche
/// serait indistinguable d'un compte inexistant. Le `MemberId` vient de la liste
/// d'équipe que l'appelant vient de lire : il désigne quelqu'un dont il sait déjà
/// qu'il est là.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record TransferSellerOwnershipCommand(
    Guid SellerId, Guid ActorUserId, Guid NewOwnerMemberId) : ICommand;

internal sealed class TransferSellerOwnershipCommandHandler
    : ICommandHandler<TransferSellerOwnershipCommand>
{
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRepository _sellers;
    private readonly MemberAccessResolver _acces;
    private readonly ISellerUnitOfWork _unitOfWork;

    public TransferSellerOwnershipCommandHandler(
        ISellerMemberRepository members,
        ISellerRepository sellers,
        MemberAccessResolver acces,
        ISellerUnitOfWork unitOfWork)
    {
        _members = members;
        _sellers = sellers;
        _acces = acces;
        _unitOfWork = unitOfWork;
    }

    // ═════════════════════════════════════════════════════════════════════
    // LE VERROU CONSULTATIF, PRIS AVANT TOUTE LECTURE.
    //
    // C'est le même que celui de `MuterAsync`, et pour la même raison : il n'y
    // a pas de ligne commune à verrouiller entre deux membres. Sans lui, deux
    // transferts simultanés depuis le même propriétaire liraient chacun « je
    // suis propriétaire » et écriraient DEUX LIGNES DIFFÉRENTES — aucun
    // conflit `xmin`, les deux réussissent, et le dossier se retrouve avec deux
    // propriétaires alors que tout ce module suppose qu'il n'y en a qu'un.
    //
    // Il porte sur le VENDEUR, tenu jusqu'au `COMMIT`, et ne sérialise que les
    // mutations d'équipe d'un même commerçant.
    //
    // LE VERROU ENVELOPPE L'OPÉRATION, IL N'EST PLUS PRIS EN PASSANT.
    //
    // L'écriture précédente appelait `LockSellerAsync` au fil du handler. Elle
    // ne verrouillait RIEN : `pg_advisory_xact_lock` se relâche à la fin de la
    // transaction, et il n'y en avait aucune d'ouverte — EF n'ouvre la sienne
    // qu'au `SaveChangesAsync`. Le verrou était pris, validé et relâché avant la
    // première lecture. Ce lot a été écrit sur cette illusion.
    //
    // ET LA RÉSOLUTION DE L'ACTEUR EST PASSÉE DEDANS. Elle était faite AVANT,
    // dans `Handle` : on décidait qui agit sur une lecture hors verrou, puis on
    // verrouillait pour lire le reste. Un verrou ne vaut que si TOUTES les
    // lectures dont dépend la décision sont sous sa protection.
    // ═════════════════════════════════════════════════════════════════════
    public Task<Result> Handle(
        TransferSellerOwnershipCommand command, CancellationToken cancellationToken)
        => _unitOfWork.ExecuteUnderSellerLockAsync(
            command.SellerId,
            ct => TransfererAsync(command, ct),
            cancellationToken);

    /// <summary>
    /// Le transfert proprement dit, mené sous verrou et dans une transaction —
    /// toutes deux tenues par <c>ExecuteUnderSellerLockAsync</c>.
    /// </summary>
    private async Task<Result> TransfererAsync(
        TransferSellerOwnershipCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(
            command.SellerId, command.ActorUserId, cancellationToken);

        if (acteur.IsFailure)
        {
            return Result.Failure(acteur.Error);
        }

        var cedant = await _members.GetMembershipAsync(
            command.SellerId, command.ActorUserId, cancellationToken);

        var beneficiaire = await _members.GetByIdAsync(
            new SellerMemberId(command.NewOwnerMemberId), cancellationToken);

        // Même réponse pour « n'existe pas » et « appartient à un autre vendeur » :
        // distinguer les deux dirait à qui essaie des identifiants lesquels existent.
        if (cedant is null || beneficiaire is null || beneficiaire.SellerId != command.SellerId)
        {
            return Result.Failure(Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        var vendeur = await _sellers.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (vendeur is null)
        {
            return Result.Failure(Error.NotFound("sellers.not_found", "Vendeur introuvable."));
        }

        // UN COMPTE NE POSSÈDE QU'UN DOSSIER — `IX_sellers_UserId` EST UNIQUE.
        //
        // Sans cette question, la violation d'unicité sortirait au `SaveChanges`
        // sous la forme d'un 409 « doublon » qui ne dit pas lequel, ni pourquoi. On
        // la pose avant, et on rend un refus qui s'explique.
        //
        // La course résiduelle — deux dossiers transférés au même compte en même
        // temps — reste fermée par l'index : le second `SaveChanges` échoue, et
        // c'est le bon comportement.
        if (await _sellers.ExistsForUserAsync(beneficiaire.UserId, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.recipient_owns_another",
                "Ce compte possède déjà un dossier vendeur : un compte n'en possède qu'un."));
        }

        // Le rôle système d'abord — c'est lui qui porte toutes les gardes.
        var deplacement = SellerMember.TransferOwnership(cedant, beneficiaire, acteur.Value);
        if (deplacement.IsFailure)
        {
            return deplacement;
        }

        // ET LE DOSSIER DANS LA MÊME TRANSACTION.
        //
        // `Seller.UserId` est la clé de `GetByUserIdAsync`, par laquelle TOUTES les
        // routes vendeur résolvent « quel dossier ce jeton administre-t-il ». Le
        // laisser derrière donnerait un dossier dont le porteur du rôle OWNER et le
        // `UserId` désignent deux personnes différentes — deux sources de vérité
        // qui se contredisent sur la seule question qui compte ici.
        var dossier = vendeur.TransferOwnership(beneficiaire.UserId);
        if (dossier.IsFailure)
        {
            return dossier;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
