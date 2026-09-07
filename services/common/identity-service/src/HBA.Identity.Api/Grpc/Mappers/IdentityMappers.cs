using ContractUser = HBA.Identity.Contracts.UserSummary;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HBA.Identity.Contracts;
using HBA.Identity.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoUser = HBA.Identity.Grpc.V1.UserSummary;


// COPIE DEPUIS `HBA.Identity.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Identity.Api.Grpc.Mappers;

internal static class IdentityGrpcMapping
{
    public static ProtoUser ToProto(this ContractUser user)
    {
        var message = new ProtoUser
        {
            Id = user.Id.ToString(),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Status = user.Status,
            EmailVerified = user.EmailVerified,
            MfaEnabled = user.MfaEnabled
        };

        message.RoleIds.AddRange(user.RoleIds.Select(id => id.ToString()));

        // N'affecter que si présent : `""` marquerait le champ comme renseigné et
        // vide, ce qui ferait croire à une version de CGU acceptée alors qu'aucune
        // ne l'a été.
        if (user.AcceptedTermsVersion is not null)
        {
            message.AcceptedTermsVersion = user.AcceptedTermsVersion;
        }

        if (user.AcceptedTermsOnUtc.HasValue)
        {
            message.AcceptedTermsOnUtc = ToTimestamp(user.AcceptedTermsOnUtc.Value);
        }

        if (user.EmailVerifiedByAdminOnUtc.HasValue)
        {
            message.EmailVerifiedByAdminOnUtc = ToTimestamp(user.EmailVerifiedByAdminOnUtc.Value);
        }

        return message;
    }

    public static ContractUser ToContract(this ProtoUser message)
        => new(
            Id: Guid.Parse(message.Id),
            FirstName: message.FirstName,
            LastName: message.LastName,
            Email: message.Email,
            PhoneNumber: message.PhoneNumber,
            Status: message.Status,
            EmailVerified: message.EmailVerified,
            MfaEnabled: message.MfaEnabled,
            RoleIds: message.RoleIds.Select(Guid.Parse).ToList(),
            AcceptedTermsVersion: message.HasAcceptedTermsVersion ? message.AcceptedTermsVersion : null,
            AcceptedTermsOnUtc: message.AcceptedTermsOnUtc?.ToDateTime(),
            EmailVerifiedByAdminOnUtc: message.EmailVerifiedByAdminOnUtc?.ToDateTime());

    // `Timestamp.FromDateTime` lève si le DateTime n'est pas marqué UTC. EF Core
    // rend des dates en `Unspecified` depuis PostgreSQL : sans ce marquage,
    // l'exception ne surviendrait qu'au premier appel portant une date lue en base.
    private static Timestamp ToTimestamp(DateTime value)
        => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
