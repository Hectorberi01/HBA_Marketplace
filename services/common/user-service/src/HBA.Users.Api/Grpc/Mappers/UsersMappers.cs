using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using HBA.Users.Contracts;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;


// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Users.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 1 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Users.Api.Grpc.Mappers;

internal static class UsersGrpcMapping
{
    public static ProtoProfile ToProto(this ContractProfile profile)
    {
        var message = new ProtoProfile
        {
            UserId = profile.UserId.ToString(),
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            DisplayName = profile.DisplayName
        };

        if (profile.AvatarUrl is not null)
        {
            message.AvatarUrl = profile.AvatarUrl;
        }

        return message;
    }

    public static ContractProfile ToContract(this ProtoProfile message)
        => new(
            UserId: Guid.Parse(message.UserId),
            FirstName: message.FirstName,
            LastName: message.LastName,
            DisplayName: message.DisplayName,
            AvatarUrl: message.HasAvatarUrl ? message.AvatarUrl : null);
}
