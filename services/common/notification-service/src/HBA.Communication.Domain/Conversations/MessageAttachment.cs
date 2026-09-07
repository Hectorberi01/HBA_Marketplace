using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>Pièce jointe d'un message (URL object storage + type).</summary>
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

    /// <summary>Le fichier dans le service média.</summary>
    public Guid MediaId { get; private set; }

    /// <summary>TRANSITOIRE : l'URL PUBLIQUE d'avant la bascule.</summary>
    public string? LegacyUrl { get; private set; }

    public AttachmentType Type { get; private set; }

    /// <summary>Cette pièce jointe est-elle antérieure au service média ?</summary>
    public bool IsLegacy => MediaId == Guid.Empty;

    /// <summary>Déduit le type d'une pièce jointe de son type MIME RÉEL.</summary>
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

    /// <summary>Déduit le type d'une pièce jointe à partir de l'extension de son URL.</summary>
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
