using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Infrastructure.Persistence.Outbox;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Retry;

/// <summary>
/// Politique de réessai de l'outbox : combien de fois, à quel rythme, et quand
/// abandonner.
/// </summary>
public sealed class OutboxRetryPolicy
{
    /// <summary>Nombre de tentatives avant mise en lettre morte.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Délai de base (première temporisation).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Plafond du délai : au-delà, on n'espace plus davantage.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Prochaine tentative après <paramref name="attemptCount"/> échecs.</summary>
    public TimeSpan DelayFor(int attemptCount, Random random)
    {
        // 2^(n-1) × base, saturé au plafond.
        var exponent = Math.Min(attemptCount - 1, 20);
        var seconds = BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, exponent));
        seconds = Math.Min(seconds, MaxDelay.TotalSeconds);

        var jitter = 1.0 + ((random.NextDouble() - 0.5) * 0.4); // ±20 %
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    /// <summary>
    /// Applique un échec au message : incrémente le compteur, puis TEMPORISE le
    /// message ou l'ENTERRE selon qu'il reste ou non des tentatives.
    /// </summary>
    /// <returns><c>true</c> si le message vient d'être mis en LETTRE MORTE.</returns>
    public bool RegisterFailure(OutboxMessage message, string error, Random random, DateTime nowUtc)
    {
        message.AttemptCount++;
        message.Error = error.Length <= 2000 ? error : error[..2000];

        if (message.AttemptCount >= MaxAttempts)
        {
            message.DeadLetteredOnUtc = nowUtc;

            // On efface la temporisation : un message enterré n'est plus « en
            // attente d'une prochaine tentative », il n'en aura plus.
            message.NextAttemptAtUtc = null;
            return true;
        }

        // C'EST CETTE LIGNE QUI SUPPRIME LE BLOCAGE DE TÊTE DE FILE. Tant que cette
        // date n'est pas atteinte, le message sort du lot — et les messages sains
        // passent devant.
        message.NextAttemptAtUtc = nowUtc.Add(DelayFor(message.AttemptCount, random));
        return false;
    }
}
