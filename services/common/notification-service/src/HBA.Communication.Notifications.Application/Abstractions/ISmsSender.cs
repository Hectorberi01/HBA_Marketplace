namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>Un SMS transactionnel : numéro international, texte brut.</summary>
/// <param name="To">
/// Numéro au format international. <b>Au Bénin, `+229` suivi de DIX chiffres</b>
/// depuis la migration de 2024 — c'est ce que pose <c>BeninGeography.LocalPhoneLength</c>.
/// Un numéro à huit chiffres est un numéro d'avant la migration : il n'aboutit plus.
/// </param>
/// <param name="Text">
/// Corps du message. <b>Pas de HTML, pas de lien long.</b> Un SMS transactionnel qui
/// dépasse 160 caractères est découpé par l'opérateur en plusieurs messages facturés
/// séparément — et arrivant parfois dans le désordre, ce qui rend un code illisible.
/// </param>
public sealed record SmsMessage(string To, string Text);

/// <summary>
/// Port d'envoi de SMS transactionnels.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE PORT N'EXISTAIT PAS, ET `SMS` ÉTAIT LE CANAL PAR DÉFAUT DE L'OTP.
///
/// `MfaChannels.All` vaut `[SMS, EMAIL]`, et `IssueOtpChallengeCommand` retombe sur
/// `SMS` quand l'appelant ne précise rien. Le dépôt ne portait aucun fournisseur SMS
/// — seulement `IEmailSender`. Le canal le plus demandé était donc le seul à n'avoir
/// aucune implémentation, sur une plateforme mobile béninoise où c'est précisément
/// celui qui compte.
///
/// IL N'Y A PAS D'ADAPTATEUR DE PRODUCTION DANS CE DÉPÔT, ET C'EST DÉLIBÉRÉ.
///
/// Choisir un agrégateur SMS n'est pas une décision technique : c'est un contrat
/// commercial, un compte opérateur, une facturation au message et une identité
/// d'expéditeur à faire homologuer. Écrire un adaptateur pour un fournisseur
/// arbitraire aurait produit du code plausible, jamais exécuté, et impossible à
/// vérifier — exactement le genre de code que cet audit passe son temps à retirer.
///
/// Ce qui EST écrit : le port, l'adaptateur de développement, la garde qui refuse
/// de démarrer en production sans fournisseur, et tout le chemin en amont. Brancher
/// le fournisseur retenu, c'est une classe qui implémente cette interface et une
/// ligne dans `NotificationsModuleInstaller`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Envoie le SMS. Lève en cas d'échec.
    ///
    /// <b>Lever est le comportement voulu</b>, pour la même raison que
    /// <see cref="IEmailSender.SendAsync"/> : l'envoi se fait depuis l'OutboxProcessor,
    /// et une exception laisse le message non traité, donc rejoué, puis mis en lettre
    /// morte — visible et rejouable. Avaler l'erreur perdrait le code EN SILENCE, et
    /// l'utilisateur resterait devant un écran de saisie qui n'aboutira jamais.
    ///
    /// <b>Ce que le port ne dit pas</b> : si le message a été REÇU. Un agrégateur
    /// accuse la prise en charge, pas la remise ; un numéro éteint ou hors réseau ne
    /// produit aucune erreur ici. Le seul constat fiable de remise reste la
    /// vérification du code par l'utilisateur.
    /// </summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
