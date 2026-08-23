namespace HBA.Communication.Notifications.Infrastructure.Sms;

/// <summary>
/// Configuration de l'envoi de SMS. Section : « Notifications:Sms ».
/// Le secret (<see cref="ApiKey"/>) vient du vault, jamais du dépôt.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN FOURNISSEUR N'EST RETENU DANS CE DÉPÔT. C'EST UNE DÉCISION OUVERTE.
///
/// Ces trois champs sont le dénominateur commun de tous les agrégateurs SMS
/// (Twilio, Vonage, MTN/Moov en direct, un agrégateur régional) : une clé, une
/// identité d'expéditeur, une adresse d'API. Ils suffisent à écrire la garde de
/// démarrage et à décider quel adaptateur enregistrer.
///
/// Ils ne suffiront PAS à l'adaptateur réel : chaque fournisseur ajoute les
/// siens (compte, sous-compte, jeton de rappel, identifiant de campagne). C'est
/// à l'adaptateur de les porter, dans sa propre section — pas à celle-ci de les
/// deviner d'avance.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SmsOptions
{
    /// <summary>Clé ou jeton d'API du fournisseur. Vide = envoi désactivé.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Identité d'expéditeur affichée sur le téléphone, ex. « HBAExpress ».
    ///
    /// Au Bénin comme dans la plupart des pays, un expéditeur alphanumérique doit
    /// être HOMOLOGUÉ auprès des opérateurs avant de fonctionner. Un expéditeur non
    /// homologué est soit remplacé par un numéro court, soit purement bloqué — et
    /// l'échec se produit chez l'opérateur, après l'accusé de prise en charge du
    /// fournisseur. Le SMS est facturé et n'arrive pas.
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>Adresse de base de l'API du fournisseur retenu.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(SenderId)
        && !string.IsNullOrWhiteSpace(BaseUrl);
}
