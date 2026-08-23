using HBA.Shared.IntegrationEvents;

namespace HBA.Identity.Contracts.IntegrationEvents;

/// <summary>Un compte a été créé. Consommé par Notifications (bienvenue), Analytics…</summary>
[HbaEvent("identity", "user", "registered", Version = 1, AggregateType = "User")]
public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
}

/// <summary>
/// Une vérification d'e-mail est demandée. Porte le code CHIFFRÉ que Notifications
/// déchiffre puis insère dans l'e-mail.
///
/// IL PORTAIT LE CODE EN CLAIR, ET C'ÉTAIT UNE FUITE DE COMPTE.
///
/// L'événement traverse l'outbox — donc `identity.outbox_messages.Content`, en
/// clair, dans une table que rien ne purgeait — puis un topic Kafka retenu sept
/// jours en production. Un accès en LECTURE à l'un ou l'autre suffisait : le code
/// EST le justificatif, la boîte mail n'est que le canal de livraison.
///
/// Le champ porte désormais une charge AES-GCM produite par `ISecretProtector`.
/// Le nom dit ce qu'il contient : personne ne doit le lire en croyant y trouver un
/// code, ni le journaliser en pensant que c'est anodin.
/// </summary>
public sealed record EmailVerificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.</summary>
    public required string ProtectedVerificationToken { get; init; }
}

/// <summary>
/// Un code à usage unique vient d'être émis et doit être REMIS à son destinataire.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CET ÉVÉNEMENT N'EXISTAIT PAS, ET LE CODE ÉTAIT JETÉ (ISSUE-062).
///
/// `IssueOtpChallengeCommandHandler` générait le code, le hachait, le stockait,
/// appliquait le plafond de tentatives — puis finissait sur `_ = code;`. Le clair
/// partait avec la pile. Le commentaire juste au-dessus affirmait pourtant que
/// « le code EN CLAIR ne sort pas d'ici autrement que par le canal choisi » : il
/// n'existait aucun canal. La route rendait un défi, l'utilisateur attendait un
/// message qui ne venait jamais, et rien dans les journaux ne le disait.
///
/// LE CODE VOYAGE CHIFFRÉ (ISSUE-071). `ProtectedCode` traverse l'outbox puis
/// Kafka ; seul notification-service le déchiffre, au dernier moment, avec
/// `Security:SecretProtection:Key`. Un dump de base ou un consommateur du topic
/// n'y lit rien.
///
/// LES DEUX COORDONNÉES SONT PORTÉES, PAS SEULEMENT CELLE DU CANAL DEMANDÉ.
/// notification-service ne peut pas interroger identity — il consomme et il
/// envoie. Porter l'adresse ET le numéro lui évite un aller-retour, et permet à
/// un futur repli (SMS indisponible → e-mail) de se décider là où l'échec
/// d'envoi est constaté. Ce repli n'existe pas aujourd'hui : le canal demandé
/// est le seul essayé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[HbaEvent("identity", "otp", "issued", Version = 1, AggregateType = "User")]
public sealed record OtpChallengeIssuedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`SMS` ou `EMAIL` — voir `MfaChannels`.</summary>
    public required string Channel { get; init; }

    public required string Email { get; init; }

    /// <summary>Au format international, `+229` suivi de dix chiffres.</summary>
    public required string PhoneNumber { get; init; }

    public required string FirstName { get; init; }

    /// <summary>Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.</summary>
    public required string ProtectedCode { get; init; }

    /// <summary>
    /// Permet au message de dire « valable dix minutes » sans que le gabarit ait
    /// à connaître `MfaChallenge.Lifetime` — donc sans qu'un changement de durée
    /// laisse un message qui ment.
    /// </summary>
    public required DateTime ExpiresAtUtc { get; init; }
}

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
public sealed record UserEmailConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
}

/// <summary>
/// Le nom d'un compte a changé. Consommé par le composition root pour aligner le
/// profil du module User.
///
/// IL N'EST PAS LEVÉ QUAND SEUL LE TÉLÉPHONE CHANGE — voir le domain event.
///
/// Cet événement existe le TEMPS DE LA TRANSITION. Aujourd'hui Identity reste la
/// source de vérité du nom (dix-sept appelants lisent encore <c>UserSummary</c>)
/// et User en tient une copie. Quand la lecture aura basculé sur le profil, le
/// sens s'inversera : User écrira, Identity perdra ses colonnes, et cet événement
/// disparaîtra avec elles.
/// </summary>
public sealed record UserProfileUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}

/// <summary>
/// Un compte a été anonymisé à la demande de son titulaire. Consommé par le
/// composition root, qui purge le profil et le carnet d'adresses côté User.
///
/// NE PORTE QUE L'IDENTIFIANT, ET C'EST VOLONTAIRE. Un événement qui annonce
/// l'effacement de données personnelles n'a aucune raison d'en transporter — il
/// resterait dans la table d'outbox, lisible, après que l'original a disparu.
/// </summary>
public sealed record UserAnonymizedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
}

/// <summary>
/// Une réinitialisation de mot de passe est demandée. Porte le jeton EN CLAIR (usage
/// unique, 1 h) que Notifications insère dans le lien de l'e-mail.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CET ÉVÉNEMENT N'EXISTAIT PAS — ET C'EST POUR ÇA QUE LE JETON SORTAIT EN HTTP.
///
/// Faute de canal, `RequestPasswordResetCommand` RENVOYAIT le jeton en clair, et
/// l'endpoint anonyme `/mobile/auth/password/forgot` le recopiait dans sa réponse.
/// N'importe qui pouvait prendre le contrôle de n'importe quel compte, admin compris.
///
/// Le jeton a désormais un chemin légitime : il traverse l'outbox jusqu'à Notifications,
/// qui l'envoie par e-mail à son destinataire — et à personne d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// CET ÉVÉNEMENT PORTAIT UN SECRET EN CLAIR — CE N'EST PLUS LE CAS.
///
/// L'ancien commentaire jugeait acceptable que le jeton soit écrit en clair dans
/// l'outbox, au motif qu'il expire en une heure et « ne vaut rien sans l'accès à la
/// boîte mail ». Ce raisonnement était faux : un code de réinitialisation ne vaut
/// pas MOINS que la boîte mail, il vaut AUTANT — c'est lui le justificatif, et la
/// boîte mail n'est que le chemin par lequel on le reçoit. Quiconque lisait la
/// table pendant l'heure de validité prenait le compte, sans jamais toucher à une
/// messagerie.
///
/// Le champ est désormais chiffré (AES-GCM, `ISecretProtector`). L'avertissement
/// tient tout de même : <b>ne pas journaliser ce record</b>, ne pas lui ajouter de
/// `ToString()`. Un chiffré recopié dans les journaux d'un service qui détient la
/// clé redevient un secret en clair pour qui lit les deux.
/// </summary>
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.</summary>
    public required string ProtectedResetToken { get; init; }
}


// ═════════════════════════════════════════════════════════════════════════════
// LES DEUX ÉVÉNEMENTS MANQUANTS DU §10.1.
//
// Le contrat en annonce trois — `identity.user.registered`, `identity.user.logged_in`,
// `identity.token.revoked` — et seul le premier existait. Les deux autres portent la
// piste d'audit de l'authentification : sans eux, une plateforme de paiement n'a
// aucune trace exploitable des connexions ni des révocations.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Une connexion a réussi.
///
/// AUCUN SECRET, ET PAS D'ADRESSE IP EN CLAIR.
///
/// L'événement sert la détection d'anomalie et l'audit, pas la reconstitution de
/// session. Ni jeton, ni empreinte de mot de passe, ni identifiant d'appareil
/// complet n'y ont leur place : il se pose sur un topic conservé plusieurs jours et
/// se retrouve dans les journaux de chaque consommateur.
///
/// L'adresse IP est une donnée personnelle au sens du RGPD. Elle n'est pas transmise
/// ici ; le service qui en a besoin pour de la détection de fraude la lit à la
/// source, avec les contrôles d'accès et la durée de rétention qui vont avec.
/// </summary>
[HbaEvent("identity", "user", "logged_in", Version = 1, AggregateType = "User")]
public sealed record UserLoggedInIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`PASSWORD`, `REFRESH`, `OTP` — comment la session a été obtenue.</summary>
    public required string Method { get; init; }

    /// <summary>Identifiant d'appareil fourni par le client, ou null.</summary>
    public string? DeviceId { get; init; }
}

/// <summary>
/// Des sessions ont été révoquées.
///
/// L'événement est publié aussi bien pour une déconnexion volontaire que pour une
/// révocation administrative : les consommateurs — notification de sécurité,
/// invalidation de caches de session — réagissent de la même façon, et le champ
/// `Reason` leur permet de nuancer le message envoyé à l'utilisateur.
/// </summary>
[HbaEvent("identity", "token", "revoked", Version = 1, AggregateType = "User")]
public sealed record TokenRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`LOGOUT`, `ADMIN_REVOKE`, `PASSWORD_CHANGED`, `SUSPENDED`.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Nombre de jetons de rafraîchissement révoqués. Zéro est un cas normal :
    /// une déconnexion depuis un appareil dont la session avait déjà expiré.
    /// </summary>
    public required int RevokedCount { get; init; }
}
