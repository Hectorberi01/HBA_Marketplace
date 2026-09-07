using HBA.Shared.Domain.Events;

namespace HBA.Identity.Domain.Users.Events;

/// <summary>Un compte vient d'être créé (en attente de vérification).</summary>
/// <summary>LE NOM DE FAMILLE A ÉTÉ AJOUTÉ, ET CE N'EST PAS UN CONFORT.</summary>
public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string FirstName, string LastName) : DomainEvent;

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
public sealed record UserEmailConfirmedDomainEvent(Guid UserId, string Email) : DomainEvent;

/// <summary>Le mot de passe d'un compte a changé (sessions invalidées).</summary>
public sealed record UserPasswordChangedDomainEvent(Guid UserId) : DomainEvent;

/// <summary>Un rôle a été assigné à un compte.</summary>
public sealed record UserRoleAssignedDomainEvent(Guid UserId, Guid RoleId) : DomainEvent;

/// <summary>Le prénom ou le nom d'un compte a changé.</summary>
public sealed record UserProfileUpdatedDomainEvent(Guid UserId, string FirstName, string LastName) : DomainEvent;

/// <summary>Un compte a été anonymisé à la demande de son titulaire.</summary>
public sealed record UserAnonymizedDomainEvent(Guid UserId) : DomainEvent;
