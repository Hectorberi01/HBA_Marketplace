using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.AssignRole;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ATTRIBUE UN RÔLE DÉSIGNÉ PAR SON NOM.
///
/// POURQUOI CETTE VARIANTE EXISTE À CÔTÉ DE <c>AssignRoleCommand</c>
///
/// Celle-ci s'adresse aux appelants qui savent QUEL rôle attribuer mais pas son
/// identifiant : les adaptateurs du composition root, qui réagissent à un fait
/// métier — « ce livreur est vérifié » — et n'ont aucune raison de connaître les
/// clés primaires de la table des rôles.
///
/// L'alternative aurait été d'exposer une recherche de rôle par nom dans l'API
/// publique d'Identity. Elle donnerait à tous les modules le moyen de lire le
/// catalogue des rôles pour, en pratique, un seul usage — et le premier qui s'en
/// servirait écrirait la chaîne « Driver » dans son propre code.
///
/// LE NOM EST COMPARÉ TEL QUEL. Il vient d'une constante, jamais d'une saisie.
/// Un rôle introuvable est une ERREUR, pas un silence : le seul cas où cela
/// arrive est un semis incomplet, et l'avaler laisserait des livreurs vérifiés
/// sans rôle, donc bloqués par la première route qui l'exigera.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record AssignRoleByNameCommand(Guid UserId, string RoleName) : ICommand;

internal sealed class AssignRoleByNameCommandHandler : ICommandHandler<AssignRoleByNameCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AssignRoleByNameCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AssignRoleByNameCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound(
                "identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var role = await _roleRepository.GetByNameAsync(command.RoleName, cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound(
                "identity.role.not_found", $"Rôle « {command.RoleName} » introuvable."));
        }

        // AssignRole est IDEMPOTENTE côté agrégat : un rôle déjà porté n'est pas
        // ajouté deux fois et ne lève pas d'événement. Un rejeu de l'outbox — qui
        // livre au moins une fois — ne produit donc rien.
        var result = user.AssignRole(role.Id.Value);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
