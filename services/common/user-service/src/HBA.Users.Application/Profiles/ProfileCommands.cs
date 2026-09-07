using FluentValidation;
using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Contracts.IntegrationEvents;

namespace HBA.Users.Application.Profiles;

/// <summary>CRÉE LE PROFIL D'UN COMPTE — IDEMPOTENT.</summary>
public sealed record CreateUserProfileCommand(Guid UserId, string? FirstName, string? LastName) : ICommand;

/// <summary>Met à jour le nom affiché.</summary>
public sealed record RenameUserProfileCommand(Guid UserId, string? FirstName, string? LastName) : ICommand;

/// <summary>Change ou retire l'avatar.</summary>
public sealed record SetUserAvatarCommand(Guid UserId, string? AvatarUrl) : ICommand;

internal sealed class CreateUserProfileCommandValidator : AbstractValidator<CreateUserProfileCommand>
{
    public CreateUserProfileCommandValidator()
    {
        // Validation de FORME. Les règles métier — prénom et nom obligatoires —
        // vivent dans l'agrégat, qui les applique quel que soit le chemin d'entrée.
        RuleFor(c => c.UserId).NotEmpty();
    }
}

internal sealed class ProfileCommandHandler
    : ICommandHandler<CreateUserProfileCommand>,
      ICommandHandler<RenameUserProfileCommand>,
      ICommandHandler<SetUserAvatarCommand>
{
    private readonly IUserProfileRepository _profiles;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public ProfileCommandHandler(
        IUserProfileRepository profiles,
        IUsersUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _profiles = profiles;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result> Handle(CreateUserProfileCommand command, CancellationToken ct)
    {
        var existant = await _profiles.GetByUserIdAsync(command.UserId, ct);
        if (existant is not null)
        {
            // Rejeu : le profil est déjà là.
            return Result.Success();
        }

        var profile = UserProfile.Create(command.UserId, command.FirstName, command.LastName);
        if (profile.IsFailure)
        {
            return Result.Failure(profile.Error);
        }

        await _profiles.AddAsync(profile.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task<Result> Handle(RenameUserProfileCommand c, CancellationToken ct)
        => MutateAsync(c.UserId, p => p.Rename(c.FirstName, c.LastName), ct);

    public Task<Result> Handle(SetUserAvatarCommand c, CancellationToken ct)
        => MutateAsync(c.UserId, p => p.SetAvatar(c.AvatarUrl), ct);

    private async Task<Result> MutateAsync(
        Guid userId, Func<UserProfile, Result> mutate, CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        if (profile is null)
        {
            return Result.Failure(Error.NotFound("users.profile.not_found", "Profil introuvable."));
        }

        var result = mutate(profile);
        if (result.IsFailure)
        {
            return result;
        }

        // PUBLIÉ DEPUIS LE POINT DE PASSAGE COMMUN, PAS DEPUIS CHAQUE COMMANDE.
        await _publisher.PublishAsync(
            new UserProfileChangedIntegrationEvent
            {
                UserId = profile.Id,
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                DisplayName = profile.DisplayName,
                AvatarUrl = profile.AvatarUrl
            },
            ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
