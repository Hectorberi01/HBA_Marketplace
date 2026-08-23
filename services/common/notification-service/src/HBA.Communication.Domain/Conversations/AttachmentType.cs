namespace HBA.Communication.Domain.Conversations;

/// <summary>
/// Type d'une pièce jointe de message. Il permet à l'app d'adapter le rendu (image
/// plein cadre, lecteur vidéo/audio, icône de document…) sans re-télécharger le
/// fichier pour deviner sa nature.
///
/// DÉDUIT DU TYPE MIME RÉEL, PLUS DE L'EXTENSION DE L'URL.
///
/// L'extension venait du nom de fichier choisi par l'expéditeur : un exécutable
/// nommé « photo.jpg » s'affichait comme une image. Le type MIME, lui, vient de
/// l'inspection des octets faite au dépôt.
/// </summary>
public enum AttachmentType
{
    Image = 1,
    Video = 2,
    Audio = 3,
    Document = 4,
    Archive = 5,
    Other = 6,
}

/// <summary>
/// Une pièce jointe telle qu'on la fournit à l'envoi : un média DÉJÀ DÉPOSÉ.
///
/// ON NE PASSE PLUS UNE URL.
///
/// Le message portait des chaînes d'URL choisies par le client. Deux défauts :
/// n'importe quelle adresse du web pouvait être jointe à une conversation, et le
/// fichier ainsi désigné n'appartenait à personne — donc ne s'effaçait jamais et
/// ne se contrôlait pas. L'appelant dépose d'abord, puis joint l'identifiant.
///
/// Le type MIME accompagne l'identifiant parce qu'il vient de l'inspection des
/// octets, faite au dépôt : c'est lui qui décide de l'icône affichée.
/// </summary>
public sealed record MessageAttachmentInput(Guid MediaId, string ContentType);
