using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using HBA.Users.Contracts;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;


// COPIE DEPUIS `HBA.Users.Contracts.Grpc` (lot D — dissolution des assemblages de
// contrats).

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
