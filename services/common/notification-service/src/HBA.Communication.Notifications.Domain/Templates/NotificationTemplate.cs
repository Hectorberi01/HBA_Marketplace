using System.Text.RegularExpressions;
using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Communication.Notifications.Domain.Templates;

/// <summary>
/// Gabarit transactionnel du §10.15, table <c>notification_templates</c>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LES GABARITS CHANGENT, ET POURQUOI CE N'EST PAS COSMÉTIQUE.
///
/// Aujourd'hui chaque notification porte son sujet et son corps DÉJÀ RENDUS. La
/// phrase « Chez Awa prépare votre commande. » est donc écrite dans le code du
/// service qui l'émet. Trois conséquences :
///
///   • corriger une faute d'orthographe demande un déploiement ;
///   • deux services qui annoncent le même fait le formulent différemment ;
///   • la traduction est impossible — le texte est figé à l'émission, avant même
///     de savoir dans quelle langue le destinataire lit.
///
/// Le gabarit déplace le texte hors du code et le rend adressable par un CODE
/// stable, que le producteur cite au lieu de rédiger.
///
/// LA CLÉ MÉTIER EST (code, canal, locale), PAS LE CODE SEUL.
///
/// Le même fait s'annonce autrement selon le canal — un SMS n'a pas de sujet et
/// coûte au caractère, un e-mail peut développer — et selon la langue. Trois
/// dimensions, donc, et une seule ligne par combinaison.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class NotificationTemplate : AggregateRoot<Guid>
{
    /// <summary>Reconnaît `{nom}` et rien d'autre : pas d'expression, pas d'appel.</summary>
    private static readonly Regex Placeholder = new(@"\{([a-zA-Z][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

    private NotificationTemplate(
        Guid id, string code, NotificationChannel channel, string locale,
        string? subjectTemplate, string bodyTemplate, int version)
        : base(id)
    {
        Code = code;
        Channel = channel;
        Locale = locale;
        SubjectTemplate = subjectTemplate;
        BodyTemplate = bodyTemplate;
        Version = version;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private NotificationTemplate()
    {
        Code = string.Empty;
        Locale = string.Empty;
        BodyTemplate = string.Empty;
    }

    /// <summary>Code métier stable, ex. `food.order.accepted`. C'est lui que le producteur cite.</summary>
    public string Code { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>Locale du gabarit, ex. `fr-BJ`.</summary>
    public string Locale { get; private set; }

    /// <summary>
    /// Null pour le SMS et le push, qui n'ont pas de sujet. Le rendre obligatoire
    /// aurait forcé à inventer un sujet que personne n'affiche.
    /// </summary>
    public string? SubjectTemplate { get; private set; }

    public string BodyTemplate { get; private set; }

    /// <summary>
    /// Version du gabarit. Elle est reportée sur la notification produite : sans
    /// elle, on ne peut pas savoir quel texte a réellement été envoyé à quelqu'un
    /// qui se plaint six mois plus tard — le gabarit a changé depuis.
    /// </summary>
    public int Version { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public static Result<NotificationTemplate> Create(
        string? code, NotificationChannel channel, string? locale,
        string? subjectTemplate, string? bodyTemplate, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<NotificationTemplate>(Error.Validation(
                "notifications.template.code_required", "Le code du gabarit est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(bodyTemplate))
        {
            return Result.Failure<NotificationTemplate>(Error.Validation(
                "notifications.template.body_required", "Le corps du gabarit est obligatoire."));
        }

        if (version < 1)
        {
            return Result.Failure<NotificationTemplate>(Error.Validation(
                "notifications.template.version_invalid", "La version doit être supérieure à zéro."));
        }

        return new NotificationTemplate(
            Guid.NewGuid(),
            code.Trim(),
            channel,
            string.IsNullOrWhiteSpace(locale) ? "fr-BJ" : locale.Trim(),
            string.IsNullOrWhiteSpace(subjectTemplate) ? null : subjectTemplate.Trim(),
            bodyTemplate.Trim(),
            version);
    }

    /// <summary>
    /// Remplace les `{placeholders}` par les valeurs fournies.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN PLACEHOLDER MANQUANT EST UNE ERREUR, PAS UN TROU.
    ///
    /// Trois comportements étaient possibles :
    ///
    ///   • laisser `{firstName}` tel quel — l'utilisateur reçoit « Bonjour
    ///     {firstName} », et le défaut se voit chez lui, jamais chez nous ;
    ///   • remplacer par une chaîne vide — « Bonjour , votre commande… », un
    ///     message qui a l'air correct et ne l'est pas ;
    ///   • refuser le rendu.
    ///
    /// On refuse. Une notification non envoyée déclenche `notification.failed`,
    /// se retrouve dans les journaux avec le nom du placeholder manquant, et se
    /// répare. Les deux autres options produisent un message parti, illisible, et
    /// silencieux.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result<RenderedNotification> Render(IReadOnlyDictionary<string, string> values)
    {
        var manquants = new List<string>();

        string Substituer(string gabarit) => Placeholder.Replace(gabarit, correspondance =>
        {
            var nom = correspondance.Groups[1].Value;

            if (values.TryGetValue(nom, out var valeur) && !string.IsNullOrEmpty(valeur))
            {
                return valeur;
            }

            manquants.Add(nom);
            return correspondance.Value;
        });

        var sujet = SubjectTemplate is null ? null : Substituer(SubjectTemplate);
        var corps = Substituer(BodyTemplate);

        if (manquants.Count > 0)
        {
            return Result.Failure<RenderedNotification>(Error.Validation(
                "notifications.template.placeholder_missing",
                $"Gabarit « {Code} » : valeur absente pour {string.Join(", ", manquants.Distinct())}."));
        }

        return Result.Success(new RenderedNotification(sujet, corps, Code, Version));
    }

    /// <summary>Désactive le gabarit sans le supprimer — l'historique doit rester lisible.</summary>
    public void Deactivate() => IsActive = false;
}

/// <summary>Résultat d'un rendu, prêt à être envoyé.</summary>
public sealed record RenderedNotification(string? Subject, string Body, string TemplateCode, int TemplateVersion);

/// <summary>Accès aux gabarits.</summary>
public interface INotificationTemplateRepository
{
    /// <summary>
    /// Cherche le gabarit d'un code pour un canal et une locale.
    ///
    /// REPLI SUR LA LOCALE PAR DÉFAUT, JAMAIS SUR UN AUTRE CANAL.
    ///
    /// Un texte dans la mauvaise langue reste lisible ; un corps d'e-mail envoyé
    /// par SMS coûte dix messages et arrive tronqué.
    /// </summary>
    Task<NotificationTemplate?> FindAsync(
        string code, NotificationChannel channel, string locale, CancellationToken cancellationToken = default);

    Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
}
