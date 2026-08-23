using HBA.Shared.Domain.Events;

namespace HBA.Identity.Domain.Users.Events;

/// <summary>Un compte vient d'être créé (en attente de vérification).</summary>
public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string FirstName) : DomainEvent;

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
public sealed record UserEmailConfirmedDomainEvent(Guid UserId, string Email) : DomainEvent;

/// <summary>Le mot de passe d'un compte a changé (sessions invalidées).</summary>
public sealed record UserPasswordChangedDomainEvent(Guid UserId) : DomainEvent;

/// <summary>Un rôle a été assigné à un compte.</summary>
public sealed record UserRoleAssignedDomainEvent(Guid UserId, Guid RoleId) : DomainEvent;

/// <summary>
/// Le prénom ou le nom d'un compte a changé.
///
/// N'EST PAS LEVÉ SI SEUL LE TÉLÉPHONE CHANGE.
///
/// <c>UpdateProfile</c> touche trois champs, mais cet événement n'existe que pour
/// tenir le profil du module User aligné — et User ne stocke pas le téléphone.
/// Le lever à chaque appel produirait un renommage inutile, donc une écriture et
/// une date de modification qui bougent sans que le nom ait bougé. La date
/// répondrait alors à « depuis quand ce profil a-t-il été touché ? » au lieu de
/// « depuis quand ce nom est-il celui-là ? ».
/// </summary>
public sealed record UserProfileUpdatedDomainEvent(Guid UserId, string FirstName, string LastName) : DomainEvent;

/// <summary>
/// Un compte a été anonymisé à la demande de son titulaire.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CET ÉVÉNEMENT MANQUAIT, ET LA SÉPARATION IDENTITY/USER LE RENDAIT URGENT.
///
/// <c>Anonymize</c> nettoie soigneusement <c>identity.users</c> — nom, e-mail,
/// téléphone, secret MFA. Mais les données personnelles ne vivent plus seulement
/// là : le profil et le CARNET D'ADRESSES sont désormais dans le schéma
/// <c>users</c>, que l'anonymisation n'atteint pas.
///
/// Le résultat, sans cet événement : un compte « supprimé » dont le nom réel et
/// les adresses de livraison restent en base indéfiniment. La suppression aurait
/// eu l'air d'avoir fonctionné — c'est le pire des deux mondes.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record UserAnonymizedDomainEvent(Guid UserId) : DomainEvent;
