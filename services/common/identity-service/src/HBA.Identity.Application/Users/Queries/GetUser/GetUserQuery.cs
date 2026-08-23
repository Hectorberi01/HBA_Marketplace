using HBA.Shared.Application.Messaging;
using HBA.Identity.Contracts;

namespace HBA.Identity.Application.Users.Queries.GetUser;

/// <summary>Récupère le profil public d'un compte.</summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserSummary>;
