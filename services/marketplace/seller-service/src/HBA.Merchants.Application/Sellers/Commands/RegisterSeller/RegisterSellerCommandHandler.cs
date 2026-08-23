using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.RegisterSeller;

/// <summary>
/// Onboarde un vendeur. Vérifie d'abord, via un appel IN-PROCESS au module
/// Identity (ses Contracts), que le compte existe et que l'e-mail est confirmé —
/// jamais d'accès direct à la base d'Identity. Refuse les doublons (un compte =
/// un vendeur, nom de boutique unique).
/// </summary>
internal sealed class RegisterSellerCommandHandler : ICommandHandler<RegisterSellerCommand, Guid>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerMemberRepository _memberRepository;
    private readonly IIdentityModuleApi _identityModuleApi;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RegisterSellerCommandHandler(
        ISellerRepository sellerRepository,
        ISellerMemberRepository memberRepository,
        IIdentityModuleApi identityModuleApi,
        ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _memberRepository = memberRepository;
        _identityModuleApi = identityModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterSellerCommand command, CancellationToken cancellationToken)
    {
        var user = await _identityModuleApi.GetUserAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("sellers.seller.user_not_found", "Compte utilisateur introuvable.");
        }

        if (!user.EmailVerified)
        {
            return Error.Forbidden("sellers.seller.email_unverified", "L'e-mail du compte doit être vérifié avant l'onboarding vendeur.");
        }

        if (await _sellerRepository.ExistsForUserAsync(command.UserId, cancellationToken))
        {
            return Error.Conflict("sellers.seller.already_seller", "Ce compte est déjà rattaché à une boutique.");
        }

        if (await _sellerRepository.ShopNameExistsAsync(command.ShopName.Trim(), cancellationToken))
        {
            return Error.Conflict("sellers.seller.shop_name_taken", $"Le nom de boutique « {command.ShopName} » est déjà pris.");
        }

        var result = Seller.Register(command.UserId, command.ShopName, command.CommissionRate, command.Metadata);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _sellerRepository.AddAsync(result.Value, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // LE VENDEUR DEVIENT MEMBRE DE SON PROPRE DOSSIER, DANS LA MÊME
        //    TRANSACTION.
        //
        // Sans cette ligne, tout vendeur inscrit APRÈS la reprise du 19 août n'a
        // aucune appartenance — et l'appartenance EST la garde de toutes les
        // routes d'équipe. Il ne pourrait ni voir son équipe, ni inviter
        // personne : le propriétaire serait le seul compte de la plateforme à
        // n'avoir aucun droit sur sa propre boutique.
        //
        // La panne serait de surcroît invisible en recette, où l'on éprouve avec
        // les comptes existants — ceux que la migration a rattachés.
        // ═════════════════════════════════════════════════════════════════════
        await _memberRepository.AddAsync(
            SellerMember.Owner(result.Value.Id.Value, command.UserId), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}
