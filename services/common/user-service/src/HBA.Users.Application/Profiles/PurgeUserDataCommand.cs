using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Profiles;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// EFFACE TOUT CE QUE CE MODULE SAIT D'UNE PERSONNE.
///
/// Appelée quand un compte est supprimé, depuis le composition root. Le module
/// User ignore ce qu'est une « suppression de compte » — il reçoit un identifiant
/// et efface ce qui s'y rattache.
///
/// ICI ON SUPPRIME VRAIMENT, ALORS QU'IDENTITY ANONYMISE.
///
/// L'écart est délibéré et vient d'une différence de nature. Identity garde la
/// ligne du compte parce que des COMMANDES la référencent : l'effacer casserait
/// des écritures comptables à conserver plusieurs années. Rien de tel ici. Une
/// adresse de livraison et un avatar ne portent aucune obligation de conservation,
/// et la commande passée a figé sa propre adresse de livraison au moment de
/// l'achat — supprimer le carnet ne réécrit aucun bon de livraison.
///
/// Anonymiser au lieu de supprimer produirait donc des lignes « Rue supprimée,
/// Cotonou » rattachées à personne, qu'il faudrait ensuite exclure de chaque
/// requête. On garde ce qui doit être gardé, on efface le reste.
///
/// IDEMPOTENTE. L'outbox livre au moins une fois, et un compte déjà purgé n'a
/// plus rien à purger — ce n'est pas une erreur, c'est l'état recherché.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record PurgeUserDataCommand(Guid UserId) : ICommand;

internal sealed class PurgeUserDataCommandHandler : ICommandHandler<PurgeUserDataCommand>
{
    private readonly IUserProfileRepository _profiles;
    private readonly IAddressRepository _addresses;
    private readonly IUsersUnitOfWork _unitOfWork;

    public PurgeUserDataCommandHandler(
        IUserProfileRepository profiles,
        IAddressRepository addresses,
        IUsersUnitOfWork unitOfWork)
    {
        _profiles = profiles;
        _addresses = addresses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PurgeUserDataCommand command, CancellationToken ct)
    {
        if (command.UserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "users.purge.user_required", "L'identifiant du compte est obligatoire."));
        }

        var carnet = await _addresses.ListByUserAsync(command.UserId, ct);
        foreach (var adresse in carnet)
        {
            _addresses.Remove(adresse);
        }

        var profile = await _profiles.GetByUserIdAsync(command.UserId, ct);
        if (profile is not null)
        {
            _profiles.Remove(profile);
        }

        // UN SEUL SaveChanges pour les deux. Enregistrer les adresses puis le
        // profil séparément laisserait, en cas d'échec au milieu, un compte dont
        // les adresses ont disparu mais dont le nom reste — une suppression à
        // moitié faite qui a l'air d'avoir réussi.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
