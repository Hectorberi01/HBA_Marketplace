using HBA.Shared.Application.Messaging;

namespace HBA.Financial.Payments.Application.Payments.Commands.InitiatePayment;

/// <summary>
/// Initie un paiement pour une commande en attente. <paramref name="Provider"/>
/// = « Stripe » / « PayPal ». <paramref name="Flow"/> = « HostedCheckout »
/// (redirection) ou « PaymentIntent » (confirmation côté client).
/// </summary>
public sealed record InitiatePaymentCommand(
    Guid OrderId,
    string Method,
    string Provider,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'UNIVERS DE LA COMMANDE : « Marketplace » ou « Food ».
    ///
    /// SANS LUI, UNE COMMANDE DE REPAS NE POUVAIT PAS ÊTRE PAYÉE (ISSUE-059).
    ///
    /// Le handler lisait toujours la commande chez Ordering et figeait
    /// `PaymentOrderType.Marketplace`. Une commande de repas répondait
    /// « introuvable », et `ConfirmMealOrderOnPaymentCapturedHandler` — qui
    /// existe, est enregistré, et filtre sur `OrderType == "FOOD"` — ne voyait
    /// jamais passer un seul paiement.
    ///
    /// DÉFAUT « Marketplace », PARCE QUE C'ÉTAIT LE COMPORTEMENT.
    ///
    /// Toutes les applications déjà déployées envoient ce corps SANS ce champ.
    /// Le rendre obligatoire les casserait toutes le jour du déploiement, pour un
    /// champ dont la valeur va de soi dans leur cas. C'est la même règle que les
    /// ajouts additifs de contrat d'événement (D32).
    ///
    /// CE CHAMP VIENT DU CLIENT, ET CE N'EST PAS UNE FAILLE.
    ///
    /// Il ne fait que CHOISIR À QUI on demande la commande. La réponse, elle,
    /// doit ensuite passer les mêmes gardes qu'avant : l'acheteur du jeton doit
    /// être celui de la commande, et la commande doit attendre un paiement. Un
    /// univers annoncé à tort ne donne donc rien — au mieux « commande
    /// introuvable ». Ce qu'il ne faut SURTOUT pas faire, c'est chercher dans les
    /// deux univers à la suite : un identifiant absent d'un côté serait alors
    /// payé de l'autre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    string OrderType = "Marketplace",

    string Flow = "HostedCheckout",
    string? ReturnUrl = null,
    string? CancelUrl = null,
    string? PayerPhone = null,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'APPELANT, IMPOSÉ PAR L'ENDPOINT — JAMAIS LU DANS LE CORPS.
    ///
    /// SANS LUI, N'IMPORTE QUI BLOQUAIT LA COMMANDE DE N'IMPORTE QUI.
    ///
    /// La route ne vérifiait aucune propriété : il suffisait d'un identifiant de
    /// commande pour créer un paiement `Pending` dessus. Le vrai acheteur se
    /// heurtait ensuite à `payments.already_exists` — commande impayable, et rien
    /// dans l'interface pour l'expliquer. Un déni de service à une requête, sans
    /// aucun privilège.
    ///
    /// CE CHAMP EST DANS UN OBJET LIÉ DEPUIS LE CORPS DE LA REQUÊTE : un client
    /// peut donc l'envoyer. L'endpoint l'ÉCRASE avec l'identité du jeton
    /// (`command with { RequestedByUserId = … }`) avant d'envoyer la commande. Ne
    /// jamais faire confiance à la valeur reçue — c'est exactement le §36 : un
    /// identifiant venant du client ne constitue jamais une preuve.
    ///
    /// Nullable parce que la lecture du jeton peut échouer ; le handler refuse
    /// alors, plutôt que de comparer à un `Guid.Empty` qui appartiendra un jour à
    /// quelqu'un.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Guid? RequestedByUserId = null) : ICommand<InitiatePaymentResult>;

/// <summary>
/// Résultat d'initiation : selon le flux, <see cref="RedirectUrl"/> (checkout
/// hébergé) ou <see cref="ClientSecret"/> (intention) est renseigné.
/// </summary>
public sealed record InitiatePaymentResult(
    Guid PaymentId,
    string Provider,
    string Flow,
    string ProviderReference,
    string? RedirectUrl,
    string? ClientSecret);
