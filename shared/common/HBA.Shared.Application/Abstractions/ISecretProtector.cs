namespace HBA.Shared.Application.Abstractions;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.
///
/// ÉCRIT PARCE QUE LES CODES DE RÉINITIALISATION CIRCULAIENT EN CLAIR.
///
/// `PasswordResetRequestedIntegrationEvent` et
/// `EmailVerificationRequestedIntegrationEvent` transportaient le code tel quel. Il
/// partait donc sur un topic Kafka — sept jours de rétention en production — ET il
/// était écrit en clair dans `identity.outbox_messages.Content`, table que rien ne
/// purgeait. Un accès en LECTURE suffisait à prendre n'importe quel compte : le
/// code EST le justificatif, la boîte mail n'est que le canal de livraison.
///
/// L'INTERFACE VIT DANS LA COUCHE APPLICATION, L'IMPLÉMENTATION DANS
/// L'INFRASTRUCTURE.
///
/// Ce sont des handlers de commande — couche Application — qui chiffrent, et des
/// handlers d'événement qui déchiffrent. Aucun des deux ne référence
/// `HBA.Shared.Infrastructure`, et il ne faut surtout pas les y contraindre pour
/// une seule interface : ce serait la première entorse à la direction des
/// dépendances, et elles ne viennent jamais seules.
///
/// Même découpage que `ICacheService` / `DistributedCacheService`, quelques
/// fichiers plus loin dans ce dossier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Chiffre un secret destiné à traverser l'outbox et Kafka.
    ///
    /// La sortie est une charge AES-GCM versionnée. Elle n'est PAS déterministe —
    /// chiffrer deux fois le même code donne deux valeurs différentes, parce que le
    /// nonce est tiré au hasard. Ne jamais s'en servir comme clé, ni la comparer.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Déchiffre.
    ///
    /// LÈVE si la charge est altérée, tronquée, ou chiffrée avec une autre clé.
    /// C'est voulu : un secret qu'on ne sait pas déchiffrer ne doit pas être
    /// remplacé par une valeur par défaut. L'exception remonte, le message part en
    /// lettre morte, et cela se voit.
    /// </summary>
    string Unprotect(string protectedValue);
}
