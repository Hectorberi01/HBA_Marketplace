namespace HBA.Shared.Infrastructure.Outbox;

/// <summary>
/// Ligne d'outbox : un IntegrationEvent sérialisé, écrit dans la MÊME
/// transaction que le changement d'état métier. Un processeur le publie ensuite
/// (in-process aujourd'hui, Kafka demain) — garantie « au moins une fois ».
/// Vit dans le schéma du module propriétaire.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nom de type stable « FullName, AssemblyName » (sans version).</summary>
    public string Type { get; init; } = default!;

    /// <summary>Charge utile JSON de l'event.</summary>
    public string Content { get; init; } = default!;

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>Message de la dernière erreur rencontrée. Null tant que rien n'a échoué.</summary>
    public string? Error { get; set; }

    // ═════════════════════════════════════════════════════════════════════════════
    // LES TROIS CHAMPS CI-DESSOUS N'EXISTAIENT PAS. SANS EUX, L'OUTBOX N'AVAIT NI
    // PLAFOND DE TENTATIVES, NI TEMPORISATION, NI ISSUE.
    //
    // Un message « empoisonné » — adresse e-mail invalide, domaine non vérifié chez le
    // fournisseur, JSON qu'aucun type ne sait plus désérialiser — restait
    // `ProcessedOnUtc == null` POUR TOUJOURS. Le processeur le relisait toutes les
    // 5 secondes, échouait, journalisait, recommençait. Éternellement.
    //
    // Le coût réel n'est pas le bruit dans les logs. C'est le BLOCAGE DE TÊTE DE FILE.
    // Le lot est trié par `OccurredOnUtc` et plafonné à 50 : chaque message mort occupe
    // définitivement une place. À 50 messages morts, PLUS AUCUN événement ne passe — les
    // commandes ne sont plus confirmées, les vendeurs ne sont plus crédités, les
    // notifications ne partent plus. Et rien ne le signale, sinon un log qui se répète.
    //
    // Une panne qui commence par un e-mail refusé, et finit par une plateforme muette.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Nombre de tentatives de publication déjà échouées.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Instant à partir duquel une nouvelle tentative est permise (backoff exponentiel).
    /// Null = éligible immédiatement.
    ///
    /// C'est ce champ qui SORT un message en échec du lot courant : il cesse d'occuper une
    /// place tant que son délai n'est pas écoulé, et les messages sains passent devant.
    /// Sans lui, le plafond de tentatives seul n'aurait pas suffi — le message aurait
    /// simplement épuisé ses essais en quelques secondes, au lieu de laisser à une panne
    /// passagère le temps de se résorber.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>
    /// Instant de mise en lettre morte. Non null = le message a épuisé ses tentatives et ne
    /// sera PLUS JAMAIS rejoué automatiquement.
    ///
    /// Une lettre morte est une perte métier réelle : un e-mail jamais envoyé, un gain
    /// vendeur jamais crédité, un stock jamais libéré. Elle DOIT être vue par un humain —
    /// d'où le log Critical et l'endpoint <c>GET /admin/outbox/dead-letters</c>. Enterrer un
    /// message en silence serait pire que de le rejouer sans fin : au moins, la boucle
    /// finissait par se remarquer.
    /// </summary>
    public DateTime? DeadLetteredOnUtc { get; set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CONTEXTE DE TRACE DE LA REQUÊTE QUI A PRODUIT CE MESSAGE (format W3C).
    ///
    /// SANS CETTE COLONNE, L'OUTBOX COUPE TOUTES LES TRACES. TOUJOURS.
    ///
    /// `KafkaIntegrationEventPublisher` pose l'en-tête `traceparent` à partir de
    /// `Activity.Current`. Cela marcherait si l'on publiait pendant la requête. Or
    /// c'est tout l'objet de l'outbox de NE PAS le faire : la ligne est écrite dans
    /// la transaction métier, et `OutboxProcessor` la publie plus tard, depuis un
    /// service d'arrière-plan où `Activity.Current` est nulle.
    ///
    /// Le résultat est le pire des deux mondes : le code de propagation existe, il
    /// a l'air correct, et il ne propage jamais rien. Chaque message part sans
    /// parent, et l'on ne s'en aperçoit qu'en constatant que les traces sont
    /// systématiquement orphelines — sans savoir si c'est la propagation ou le
    /// collecteur qui est en cause.
    ///
    /// Le contexte est donc capturé À L'ÉCRITURE, transporté en base, et restitué
    /// à la publication.
    ///
    /// NULLABLE, ET LES LIGNES D'AVANT CE LOT LE RESTENT.
    ///
    /// Une valeur absente n'est pas une anomalie : les messages écrits hors requête
    /// — travail planifié, reprise de données — n'en ont légitimement pas, et
    /// publient alors en racine.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? TraceParent { get; set; }

    /// <summary>
    /// L'identifiant de corrélation métier (`x-correlation-id`) de la requête qui a
    /// produit cet événement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE N'EST PAS UN DOUBLON DE `TraceParent`, ET LES DEUX SONT NÉCESSAIRES.
    ///
    /// `traceparent` relie les spans dans un outil d'observabilité : il sert à
    /// l'exploitant qui ouvre une trace. `x-correlation-id` est ce que l'utilisateur
    /// LIT — c'est le `meta.requestId` de chaque réponse, celui qu'il recopie dans un
    /// signalement au support. Les deux répondent à des questions différentes, et
    /// aucun ne remplace l'autre.
    ///
    /// IL ÉTAIT PERDU AU PASSAGE DE L'OUTBOX, ET C'EST TOUT LE DÉFAUT (§11 gRPC).
    ///
    /// La corrélation traversait bien HTTP et gRPC — l'intercepteur la recopie. Mais
    /// l'outbox est une frontière ASYNCHRONE : le message est publié plusieurs
    /// secondes plus tard, par un service d'arrière-plan, dans un autre contexte
    /// d'exécution. Tout ce qui n'est pas écrit en base est perdu là. `TraceParent`
    /// l'avait compris ; la corrélation, non.
    ///
    /// Le publieur retombait alors sur `Activity.Current.TraceId` — une valeur
    /// cohérente, propagée, et qui n'a AUCUN rapport avec le `requestId` que
    /// l'utilisateur a sous les yeux. Un incident traversant trois services n'était
    /// donc pas reconstituable à partir de ce que la personne pouvait citer.
    ///
    /// NULLABLE, comme `TraceParent` et pour la même raison : un message écrit
    /// hors requête — travail planifié, reprise de données — n'en a légitimement
    /// pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string? CorrelationId { get; set; }
}
