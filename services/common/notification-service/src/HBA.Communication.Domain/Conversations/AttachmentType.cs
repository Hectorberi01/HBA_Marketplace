namespace HBA.Communication.Domain.Conversations;

/// <summary>Type d'une pièce jointe de message.</summary>
public enum AttachmentType
{
    Image = 1,
    Video = 2,
    Audio = 3,
    Document = 4,
    Archive = 5,
    Other = 6,
}

/// <summary>Une pièce jointe telle qu'on la fournit à l'envoi : un média DÉJÀ DÉPOSÉ.</summary>
public sealed record MessageAttachmentInput(Guid MediaId, string ContentType);
