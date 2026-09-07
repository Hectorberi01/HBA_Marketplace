using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>Transférer la propriété du dossier à un autre membre de l'équipe.</summary>
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

    // LE VERROU CONSULTATIF, PRIS AVANT TOUTE LECTURE.
    public Task<Result> Handle(
        TransferSellerOwnershipCommand command, CancellationToken cancellationToken)
        => _unitOfWork.ExecuteUnderSellerLockAsync(
            command.SellerId,
            ct => TransfererAsync(command, ct),
            cancellationToken);

    /// <summary>
    /// Le transfert proprement dit, mené sous verrou et dans une transaction —
    /// toutes deux tenues par <c> ExecuteUnderSellerLockAsync</c>.
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
        // distinguer les deux dirait à qui essaie des identifiants lesquels
        // existent.
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
        var dossier = vendeur.TransferOwnership(beneficiaire.UserId);
        if (dossier.IsFailure)
        {
            return dossier;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
