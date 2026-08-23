using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Communication.Notifications.Domain.Preferences;

namespace HBA.Communication.Notifications.Application.Notifications.Preferences;

internal sealed class UpdateNotificationPreferencesCommandHandler
    : ICommandHandler<UpdateNotificationPreferencesCommand>
{
    private readonly INotificationPreferenceRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public UpdateNotificationPreferencesCommandHandler(
        INotificationPreferenceRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        var pref = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (pref is null)
        {
            pref = NotificationPreference.Create(command.UserId);
            await _repository.AddAsync(pref, cancellationToken);
        }

        // On ne conserve que des catégories connues : un client qui envoie n'importe
        // quoi ne peut pas polluer la table.
        var muted = command.MutedCategories
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(NotificationCategories.IsKnown)
            .ToList();

        pref.Replace(muted);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
