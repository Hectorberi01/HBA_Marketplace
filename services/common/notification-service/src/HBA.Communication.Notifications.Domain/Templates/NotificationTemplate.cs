using System.Text.RegularExpressions;
using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Communication.Notifications.Domain.Templates;

/// <summary>Gabarit transactionnel du §10.15, table <c>notification_templates</c>.</summary>
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

    /// <summary>Code métier stable, ex. `food.order.accepted`.</summary>
    public string Code { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>Locale du gabarit, ex. `fr-BJ`.</summary>
    public string Locale { get; private set; }

    /// <summary>Null pour le SMS et le push, qui n'ont pas de sujet.</summary>
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

    /// <summary>Remplace les `{placeholders}` par les valeurs fournies.</summary>
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

    /// <summary>
    /// Désactive le gabarit sans le supprimer — l'historique doit rester lisible.
    /// </summary>
    public void Deactivate() => IsActive = false;
}

/// <summary>Résultat d'un rendu, prêt à être envoyé.</summary>
public sealed record RenderedNotification(string? Subject, string Body, string TemplateCode, int TemplateVersion);

/// <summary>Accès aux gabarits.</summary>
public interface INotificationTemplateRepository
{
    /// <summary>Cherche le gabarit d'un code pour un canal et une locale.</summary>
    Task<NotificationTemplate?> FindAsync(
        string code, NotificationChannel channel, string locale, CancellationToken cancellationToken = default);

    Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
}
