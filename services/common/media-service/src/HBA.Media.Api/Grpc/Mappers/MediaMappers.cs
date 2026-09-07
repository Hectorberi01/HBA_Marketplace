using ContractMedia = HBA.Media.Contracts.MediaView;
using ContractVariant = HBA.Media.Contracts.MediaVariantView;
using Google.Protobuf.WellKnownTypes;

using HBA.Media.Contracts;
using HBA.Media.Grpc.V1;
using HBA.Media.Grpc.V1;

using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoMedia = HBA.Media.Grpc.V1.MediaView;
using ProtoVariant = HBA.Media.Grpc.V1.MediaVariantView;


// COPIE DEPUIS `HBA.Media.Contracts.Grpc` (lot D — dissolution des assemblages de
// contrats).

namespace HBA.Media.Api.Grpc.Mappers;

/// <summary>Traduction entre les enregistrements du contrat et les messages Protobuf.</summary>
internal static class MediaGrpcMapping
{
    public static ProtoMedia ToProto(this ContractMedia view)
    {
        var message = new ProtoMedia
        {
            Id = view.Id.ToString(),
            OwnerType = view.OwnerType,
            OwnerId = view.OwnerId.ToString(),
            MediaType = view.MediaType,
            OriginalFileName = view.OriginalFileName,
            ContentType = view.ContentType,
            SizeBytes = view.SizeBytes,
            Visibility = view.Visibility,
            Status = view.Status,

            // `SpecifyKind` EST OBLIGATOIRE : `Timestamp.FromDateTime` lève si le
            // `DateTime` n'est pas marqué UTC. EF Core rend des dates en
            // `Unspecified` depuis PostgreSQL — l'exception ne surviendrait donc
            // qu'au premier appel portant une date lue en base, pas en test.
            CreatedOnUtc = Timestamp.FromDateTime(
                DateTime.SpecifyKind(view.CreatedOnUtc, DateTimeKind.Utc)),

            // `Guid.Empty` se transporte en chaîne vide plutôt qu'en «
            // 00000000-0000-...
            CreatedByUserId = view.CreatedByUserId == Guid.Empty
                ? string.Empty
                : view.CreatedByUserId.ToString()
        };

        // N'AFFECTER QUE SI LA VALEUR EXISTE.
        if (view.Url is not null)
        {
            message.Url = view.Url;
        }

        if (view.Width.HasValue)
        {
            message.Width = view.Width.Value;
        }

        if (view.Height.HasValue)
        {
            message.Height = view.Height.Value;
        }

        foreach (var variant in view.Variants)
        {
            message.Variants.Add(new ProtoVariant
            {
                VariantType = variant.VariantType,
                Url = variant.Url,
                Width = variant.Width,
                Height = variant.Height,
                SizeBytes = variant.SizeBytes
            });
        }

        return message;
    }

    public static ContractMedia ToContract(this ProtoMedia message)
        => new(
            Id: Guid.Parse(message.Id),
            OwnerType: message.OwnerType,
            OwnerId: Guid.Parse(message.OwnerId),
            MediaType: message.MediaType,
            OriginalFileName: message.OriginalFileName,
            ContentType: message.ContentType,
            SizeBytes: message.SizeBytes,
            Visibility: message.Visibility,
            Status: message.Status,

            // `HasWidth` distingue « absent » de « zéro ».
            Width: message.HasWidth ? message.Width : null,
            Height: message.HasHeight ? message.Height : null,
            Url: message.HasUrl ? message.Url : null,

            Variants: message.Variants
                .Select(v => new ContractVariant(v.VariantType, v.Url, v.Width, v.Height, v.SizeBytes))
                .ToList(),

            CreatedOnUtc: message.CreatedOnUtc.ToDateTime(),

            // TOLÉRER LA CHAÎNE VIDE, ET SEULEMENT ELLE.
            CreatedByUserId: string.IsNullOrEmpty(message.CreatedByUserId)
                ? Guid.Empty
                : Guid.Parse(message.CreatedByUserId));
}
