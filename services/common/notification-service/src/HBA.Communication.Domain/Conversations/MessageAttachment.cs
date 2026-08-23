using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>
/// Pièce jointe d'un message (URL object storage + type). Entité ENFANT de Message,
/// exactement comme <see cref="MessageReaction"/>.
///
/// Pourquoi une table enfant plutôt qu'une colonne tableau/JSON : EF Core 8 traite
/// un <c>List&lt;string&gt;</c> mappé comme une « collection primitive » qu'il persiste
/// mais relit VIDE (bug observé ici). Le pattern « entité enfant » est, lui, éprouvé —
/// c'est celui des réactions, qui fonctionnent.
/// </summary>
public sealed class MessageAttachment : Entity<Guid>
{
    private MessageAttachment()
    {
    }

    internal MessageAttachment(Guid id, Guid mediaId, string? legacyUrl, AttachmentType type)
        : base(id)
    {
        MediaId = mediaId;
        LegacyUrl = legacyUrl;
        Type = type;
    }

    /// <summary>Le fichier dans le service média. Zéro pour une pièce d'avant la bascule.</summary>
    public Guid MediaId { get; private set; }

    /// <summary>
    /// TRANSITOIRE : l'URL PUBLIQUE d'avant la bascule.
    ///
    /// C'est elle le problème que cette bascule corrige. Les pièces jointes de
    /// discussion étaient déposées dans un bucket PUBLIC, et leur adresse
    /// permanente recopiée dans le message : la photo d'un colis abîmé, d'une
    /// facture, parfois d'une pièce d'identité envoyée à un vendeur, se lisait
    /// sans compte par quiconque connaissait l'URL — et une URL circule.
    ///
    /// Les pièces déposées depuis la bascule n'ont plus d'adresse permanente :
    /// elles se lisent par URL signée, après vérification que le demandeur est
    /// partie à la conversation.
    /// </summary>
    public string? LegacyUrl { get; private set; }

    public AttachmentType Type { get; private set; }

    /// <summary>Cette pièce jointe est-elle antérieure au service média ?</summary>
    public bool IsLegacy => MediaId == Guid.Empty;

    /// <summary>
    /// Déduit le type d'une pièce jointe de son type MIME RÉEL.
    ///
    /// PRÉFÉRER CECI À `InferType`, QUI LIT UNE EXTENSION.
    ///
    /// Le type MIME vient de l'inspection des octets à l'entrée ; l'extension,
    /// elle, vient du nom de fichier choisi par l'expéditeur. Un « .jpg » qui est
    /// un exécutable s'afficherait comme une image et se téléchargerait comme
    /// autre chose.
    /// </summary>
    public static AttachmentType InferTypeFromContentType(string contentType)
    {
        var type = contentType.ToLowerInvariant();

        if (type.StartsWith("image/", StringComparison.Ordinal)) return AttachmentType.Image;
        if (type.StartsWith("video/", StringComparison.Ordinal)) return AttachmentType.Video;
        if (type.StartsWith("audio/", StringComparison.Ordinal)) return AttachmentType.Audio;

        if (type is "application/pdf" or "text/plain" or "text/csv"
            || type.StartsWith("application/vnd.openxmlformats-officedocument", StringComparison.Ordinal)
            || type.StartsWith("application/vnd.ms-", StringComparison.Ordinal)
            || type is "application/msword")
        {
            return AttachmentType.Document;
        }

        if (type is "application/zip" or "application/x-7z-compressed"
            or "application/vnd.rar" or "application/x-tar" or "application/gzip")
        {
            return AttachmentType.Archive;
        }

        return AttachmentType.Other;
    }

    /// <summary>
    /// Déduit le type d'une pièce jointe à partir de l'extension de son URL.
    ///
    /// NE SERT PLUS QU'AUX PIÈCES HÉRITÉES, qui n'ont qu'une URL.
    /// </summary>
    public static AttachmentType InferType(string url)
    {
        var u = url.ToLowerInvariant();
        var query = u.IndexOf('?');
        if (query >= 0)
        {
            u = u[..query];
        }

        if (EndsWithAny(u, ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".bmp"))
        {
            return AttachmentType.Image;
        }
        if (EndsWithAny(u, ".mp4", ".mov", ".avi", ".webm", ".mkv"))
        {
            return AttachmentType.Video;
        }
        if (EndsWithAny(u, ".mp3", ".wav", ".ogg", ".m4a", ".aac"))
        {
            return AttachmentType.Audio;
        }
        if (EndsWithAny(u, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"))
        {
            return AttachmentType.Document;
        }
        if (EndsWithAny(u, ".zip", ".rar", ".7z", ".tar", ".gz"))
        {
            return AttachmentType.Archive;
        }
        return AttachmentType.Other;
    }

    private static bool EndsWithAny(string value, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
