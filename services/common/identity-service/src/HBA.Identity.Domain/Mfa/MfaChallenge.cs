using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Identity.Domain.Mfa;

/// <summary>Canal par lequel un code à usage unique est transmis.</summary>
public static class MfaChannels
{
    public const string Sms = "SMS";
    public const string Email = "EMAIL";

    public static readonly string[] All = [Sms, Email];
}

/// <summary>Issue d'une tentative de vérification.</summary>
public enum MfaVerificationOutcome
{
    Verified = 0,
    WrongCode = 1,
    Expired = 2,
    TooManyAttempts = 3,
    AlreadyUsed = 4
}

/// <summary>
/// Défi à usage unique du §10.1, table <c>mfa_challenges</c>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// IL COMPLÈTE LE TOTP EXISTANT, IL NE LE REMPLACE PAS.
///
/// Le service porte déjà une authentification à deux facteurs par application
/// (`BeginMfaSetup`, `ConfirmMfa`, `DisableMfa`, via Otp.NET). Elle suppose que
/// l'utilisateur a installé un authentificateur — ce que la majorité des clients
/// d'une place de marché au Bénin n'ont pas.
///
/// Ce défi-ci couvre l'autre cas, celui que le §10.1 décrit avec
/// `POST /auth/verify-otp` : un code envoyé par SMS ou par e-mail. Les deux
/// mécanismes coexistent parce qu'ils répondent à deux populations distinctes.
///
/// TROIS PROTECTIONS, ET AUCUNE N'EST REDONDANTE.
///
///   • Le code est HACHÉ. Une fuite de la table ne donne pas les codes en cours.
///   • Il EXPIRE. Sans expiration, un code intercepté reste valable indéfiniment.
///   • Les tentatives sont COMPTÉES. Six chiffres, c'est un million de
///     combinaisons : sans plafond, un script les épuise en quelques minutes.
///     C'est cette troisième protection qui fait tout le travail, et c'est
///     précisément celle qu'on oublie le plus souvent.
///
/// `ConsumedAtUtc` EXISTE POUR QUE LE CODE SOIT VRAIMENT À USAGE UNIQUE.
///
/// Sans lui, un code valide et non expiré resterait rejouable jusqu'à sa date
/// limite. Un code lu dans une notification sur un écran verrouillé pourrait
/// alors servir deux fois — une fois par le titulaire, une fois par un tiers.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MfaChallenge : AggregateRoot<Guid>
{
    /// <summary>Au-delà, le défi est mort : il faut en demander un nouveau.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Durée de vie d'un code. Assez pour recevoir un SMS, trop court pour être utile à un tiers.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private MfaChallenge(Guid id, Guid userId, string channel, string codeHash, DateTime expiresAtUtc)
        : base(id)
    {
        UserId = userId;
        Channel = channel;
        CodeHash = codeHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedOnUtc = DateTime.UtcNow;
    }

    private MfaChallenge()
    {
        Channel = string.Empty;
        CodeHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Channel { get; private set; }

    /// <summary>Empreinte du code. Le code en clair n'existe qu'en mémoire, le temps de l'envoi.</summary>
    public string CodeHash { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public static Result<MfaChallenge> Issue(Guid userId, string? channel, string codeHash)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<MfaChallenge>(Error.Validation(
                "identity.mfa.user_required", "Un défi doit être rattaché à un compte."));
        }

        var normalized = (channel ?? string.Empty).Trim().ToUpperInvariant();

        if (!MfaChannels.All.Contains(normalized))
        {
            return Result.Failure<MfaChallenge>(Error.Validation(
                "identity.mfa.channel_unsupported", $"Canal non pris en charge : « {channel} »."));
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return Result.Failure<MfaChallenge>(Error.Validation(
                "identity.mfa.code_required", "Le code du défi est obligatoire."));
        }

        return new MfaChallenge(Guid.NewGuid(), userId, normalized, codeHash, DateTime.UtcNow.Add(Lifetime));
    }

    /// <summary>
    /// Vérifie un code. La comparaison est déléguée à l'appelant, qui détient le
    /// hacheur : le domaine ne connaît pas l'algorithme et n'a pas à le connaître.
    ///
    /// LA TENTATIVE EST COMPTÉE MÊME QUAND LE DÉFI EST DÉJÀ MORT.
    ///
    /// Ne compter que les tentatives sur défi vivant rendrait le compteur
    /// contournable : il suffirait d'attendre l'expiration pour repartir à zéro.
    /// </summary>
    public MfaVerificationOutcome Verify(Func<string, bool> codeMatches)
    {
        Attempts++;

        if (ConsumedAtUtc is not null)
        {
            return MfaVerificationOutcome.AlreadyUsed;
        }

        if (Attempts > MaxAttempts)
        {
            return MfaVerificationOutcome.TooManyAttempts;
        }

        if (DateTime.UtcNow > ExpiresAtUtc)
        {
            return MfaVerificationOutcome.Expired;
        }

        if (!codeMatches(CodeHash))
        {
            return MfaVerificationOutcome.WrongCode;
        }

        ConsumedAtUtc = DateTime.UtcNow;
        return MfaVerificationOutcome.Verified;
    }

    /// <summary>
    /// Invalide le défi sans le consommer, à l'émission d'un remplaçant.
    ///
    /// La date d'expiration est ramenée dans le passé plutôt que `ConsumedAtUtc`
    /// renseigné : l'audit doit pouvoir distinguer un code UTILISÉ d'un code
    /// REMPLACÉ. Le premier signale une connexion, le second une demande répétée —
    /// et une rafale de demandes est le signal d'un harcèlement par SMS.
    /// </summary>
    public void Expire() => ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
}

/// <summary>Accès aux défis à usage unique.</summary>
public interface IMfaChallengeRepository
{
    Task AddAsync(MfaChallenge challenge, CancellationToken cancellationToken = default);

    Task<MfaChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalide les défis encore vivants d'un utilisateur. Appelé à l'émission d'un
    /// nouveau : sans cela, cinq demandes successives laisseraient cinq codes
    /// valables en circulation, et le plafond de tentatives serait multiplié par cinq.
    /// </summary>
    Task<int> ConsumeActiveAsync(Guid userId, CancellationToken cancellationToken = default);
}
