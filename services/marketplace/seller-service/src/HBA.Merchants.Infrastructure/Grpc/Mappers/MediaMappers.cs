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


// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Media.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
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
// CE QUE ÇA COUTE : cette traduction existe en 4 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Merchants.Infrastructure.Grpc.Mappers;

/// <summary>
/// Traduction entre les enregistrements du contrat et les messages Protobuf.
/// </summary>
/// <remarks>
/// Serveur et client l'utilisent tous les deux. Si chacun écrivait la sienne,
/// une divergence — un champ oublié d'un côté — produirait une valeur nulle
/// silencieuse chez l'appelant, sans erreur ni journal.
/// </remarks>
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

            // `SpecifyKind` EST OBLIGATOIRE : `Timestamp.FromDateTime` lève si
            // le `DateTime` n'est pas marqué UTC. EF Core rend des dates en
            // `Unspecified` depuis PostgreSQL — l'exception ne surviendrait donc
            // qu'au premier appel portant une date lue en base, pas en test.
            CreatedOnUtc = Timestamp.FromDateTime(
                DateTime.SpecifyKind(view.CreatedOnUtc, DateTimeKind.Utc)),

            // `Guid.Empty` se transporte en chaîne vide plutôt qu'en
            // « 00000000-0000-... » : le lecteur d'un vidage réseau voit
            // immédiatement « inconnu » au lieu d'un identifiant d'apparence
            // valide.
            CreatedByUserId = view.CreatedByUserId == Guid.Empty
                ? string.Empty
                : view.CreatedByUserId.ToString()
        };

        // N'AFFECTER QUE SI LA VALEUR EXISTE.
        //
        // Affecter `""` marquerait le champ comme PRÉSENT et vide. C'est le
        // piège : un fichier privé — dont l'URL doit être absente par
        // construction (§10) — arriverait chez l'appelant avec une URL présente
        // et vide, qu'un `if (url is not null)` prendrait pour une URL publique.
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

            // `HasWidth` distingue « absent » de « zéro ». Sans ce test, une image
            // dont la largeur n'est pas encore calculée arriverait à 0 px et
            // serait traitée comme une image de dimension nulle, non comme une
            // dimension inconnue.
            Width: message.HasWidth ? message.Width : null,
            Height: message.HasHeight ? message.Height : null,
            Url: message.HasUrl ? message.Url : null,

            Variants: message.Variants
                .Select(v => new ContractVariant(v.VariantType, v.Url, v.Width, v.Height, v.SizeBytes))
                .ToList(),

            CreatedOnUtc: message.CreatedOnUtc.ToDateTime(),

            // TOLÉRER LA CHAÎNE VIDE, ET SEULEMENT ELLE.
            //
            // Une instance de media-service antérieure à ce champ répond une
            // chaîne vide : `Guid.Parse` lèverait, et un service par ailleurs
            // sain tomberait en erreur pendant un déploiement progressif. Une
            // chaîne NON vide et malformée, elle, doit lever — c'est un défaut
            // de sérialisation, pas une version en retard.
            CreatedByUserId: string.IsNullOrEmpty(message.CreatedByUserId)
                ? Guid.Empty
                : Guid.Parse(message.CreatedByUserId));
}
