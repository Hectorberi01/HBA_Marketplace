using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Preferences;

namespace HBA.Users.Application.Preferences;

/// <summary>Préférences telles que rendues par l'API (§10.2).</summary>
public sealed record PreferencesDto(
    string Language,
    string Currency,
    bool PushEnabled,
    bool MarketingOptIn);

/// <summary>Lit les préférences, en les créant au passage si elles n'existent pas encore.</summary>
public sealed record GetPreferencesQuery(Guid UserId) : IQuery<PreferencesDto>;

internal sealed class GetPreferencesQueryHandler : IQueryHandler<GetPreferencesQuery, PreferencesDto>
{
    private readonly IUserPreferencesRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;

    public GetPreferencesQueryHandler(IUserPreferencesRepository repository, IUsersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PreferencesDto>> Handle(GetPreferencesQuery query, CancellationToken cancellationToken)
    {
        var preferences = await _repository.GetAsync(query.UserId, cancellationToken);

        if (preferences is null)
        {
            var created = UserPreferences.CreateDefault(query.UserId);

            if (created.IsFailure)
            {
                return Result.Failure<PreferencesDto>(created.Error);
            }

            preferences = created.Value;
            await _repository.AddAsync(preferences, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Map(preferences));
    }

    internal static PreferencesDto Map(UserPreferences p)
        => new(p.Language, p.Currency, p.PushEnabled, p.MarketingOptIn);
}

/// <summary>Met à jour les préférences.</summary>
public sealed record UpdatePreferencesCommand(
    Guid UserId,
    string? Language,
    string? Currency,
    bool? PushEnabled,
    bool? MarketingOptIn) : ICommand<PreferencesDto>;

internal sealed class UpdatePreferencesCommandHandler
    : ICommandHandler<UpdatePreferencesCommand, PreferencesDto>
{
    private readonly IUserPreferencesRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;

    public UpdatePreferencesCommandHandler(
        IUserPreferencesRepository repository, IUsersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PreferencesDto>> Handle(
        UpdatePreferencesCommand command, CancellationToken cancellationToken)
    {
        var preferences = await _repository.GetAsync(command.UserId, cancellationToken);

        if (preferences is null)
        {
            var created = UserPreferences.CreateDefault(command.UserId);

            if (created.IsFailure)
            {
                return Result.Failure<PreferencesDto>(created.Error);
            }

            preferences = created.Value;
            await _repository.AddAsync(preferences, cancellationToken);
        }

        var updated = preferences.Update(
            command.Language, command.Currency, command.PushEnabled, command.MarketingOptIn);

        if (updated.IsFailure)
        {
            return Result.Failure<PreferencesDto>(updated.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(GetPreferencesQueryHandler.Map(preferences));
    }
}
