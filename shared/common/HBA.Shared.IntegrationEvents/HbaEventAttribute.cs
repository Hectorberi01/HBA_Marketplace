namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// Déclare le nom métier d'un événement d'intégration selon le §19.2 :
/// <c>&lt;domaine&gt;.&lt;agrégat&gt;.&lt;action passée&gt;</c>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN ATTRIBUT PLUTÔT QUE LA DÉRIVATION DEPUIS LE NOM DE CLASSE.
///
/// `KafkaEventNaming.EventType()` calcule aujourd'hui le nom depuis le type .NET :
/// `OrderPlacedIntegrationEvent` devient `order.placed`. Ça marche tant que le nom
/// de classe suffit — et il ne suffit pas. La spec veut TROIS segments, dont un
/// domaine que le nom de classe ne porte pas : `marketplace.order.created` et
/// `food.order.created` sont deux événements distincts que la dérivation
/// écraserait tous les deux en `order.created`.
///
/// Pire, la dérivation lie un CONTRAT PUBLIC à un nom de classe interne : renommer
/// `OrderPlaced` en `OrderCreated` pour la lisibilité du code change silencieusement
/// le topic de production. Aucune compilation n'échoue, aucun test unitaire ne
/// bronche, et les consumers cessent simplement de recevoir.
///
/// L'attribut coupe ce lien : le nom métier est écrit une fois, à côté de
/// l'événement, et le refactoring du code ne l'atteint plus.
///
/// IL VIT DANS `HBA.Shared.IntegrationEvents` ET NON DANS `Infrastructure`.
///
/// Il avait d'abord été écrit à côté de `HbaEventNaming`, dans Kafka. Le défaut
/// n'est apparu qu'à la première utilisation réelle : les événements d'intégration
/// vivent dans les projets `*.Contracts`, qui ne référencent que
/// `HBA.Shared.IntegrationEvents`. Annoter un événement aurait obligé chaque
/// Contracts à dépendre de l'Infrastructure — donc d'EF Core, de Confluent.Kafka et
/// du reste — pour un simple attribut de nommage. Un contrat public ne doit rien
/// savoir du transport qui le véhicule.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HbaEventAttribute : Attribute
{
    /// <param name="domain">Domaine métier : `identity`, `marketplace`, `food`, `delivery`, `payment`…</param>
    /// <param name="aggregate">Agrégat : `order`, `cart`, `product`, `delivery`…</param>
    /// <param name="action">Action au passé : `created`, `accepted`, `succeeded`, `cancelled`…</param>
    public HbaEventAttribute(string domain, string aggregate, string action)
    {
        Domain = domain;
        Aggregate = aggregate;
        Action = action;
        EventType = $"{domain}.{aggregate}.{action}";
    }

    /// <summary>
    /// Forme littérale, pour les événements que le cahier des charges nomme en DEUX
    /// segments.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DOCUMENT SE CONTREDIT, ET C'EST SA LISTE PAR SERVICE QUI FAIT FOI.
    ///
    /// Le §19.2 pose la règle `&lt;domaine&gt;.&lt;agrégat&gt;.&lt;action&gt;` — trois segments —
    /// et donne pourtant `payment.succeeded` en exemple, qui n'en a que deux. Le
    /// §10.12 confirme : `payment.created`, `payment.succeeded`, `payment.failed`,
    /// `payment.refunded`. Et surtout, les services qui les CONSOMMENT les nomment
    /// pareil — wallet, notification et les deux services de commande attendent
    /// tous `payment.succeeded`.
    ///
    /// Appliquer la règle des trois segments donnerait `payment.payment.succeeded`,
    /// que personne n'écoute. Entre une règle et l'usage que quatre services
    /// partagent, c'est l'usage qui est le contrat.
    ///
    /// Le domaine et l'agrégat restent déduits pour le topic : `payment.succeeded`
    /// publie sur `hba.&lt;env&gt;.payment.payment.v1` — le topic, lui, a besoin des deux
    /// niveaux pour rester partitionnable par agrégat.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public HbaEventAttribute(string eventType)
    {
        var segments = eventType.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new ArgumentException(
                $"Nom d'événement invalide : « {eventType} ». Au moins deux segments sont attendus.",
                nameof(eventType));
        }

        Domain = segments[0];
        Aggregate = segments.Length >= 3 ? segments[1] : segments[0];
        Action = segments[^1];
        EventType = eventType;
    }

    public string Domain { get; }

    public string Aggregate { get; }

    public string Action { get; }

    /// <summary>
    /// Version majeure du contrat. Elle apparaît dans le topic (`...v1`) et dans
    /// `eventVersion`. Le §19.7 impose de l'incrémenter pour tout renommage,
    /// suppression ou changement de sémantique d'un champ ; ajouter un champ
    /// optionnel reste compatible et ne la change pas.
    /// </summary>
    /// <summary>
    /// Version majeure du contrat. Vaut 1, et devrait le rester.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CONVENTION EST ADDITIVE (décision D32). LIRE AVANT D'INCRÉMENTER.
    ///
    /// On n'ajoute à un événement que des champs OPTIONNELS. Une rupture — champ
    /// renommé, champ retiré, champ obligatoire ajouté — crée un NOUVEAU type
    /// d'événement, par exemple `OrderConfirmedV2`, et jamais une version 2 du
    /// même.
    ///
    /// La raison est le décalage dans le temps : un événement est écrit dans
    /// l'outbox, publié, puis relu par des services déployés à des dates
    /// différentes. Deux formes d'un même type circulent alors en même temps, et
    /// `JsonSerializer` ne s'en plaint pas — il lit ce qu'il reconnaît, ignore le
    /// reste, et rend des champs à `null`. Un nouveau TYPE, lui, est visible :
    /// l'ancien continue d'être servi, le nouveau est adopté service par service.
    ///
    /// CE QUE FAIT LE CONSOMMATEUR SI VOUS INCRÉMENTEZ QUAND MÊME.
    ///
    /// `KafkaIntegrationEventConsumer` compare la version reçue à celle qu'il
    /// connaît. Au-dessus, il REFUSE : journal `Critical`, effet métier annulé,
    /// message acquitté. C'est un filet contre une entorse à la règle, pas un
    /// mécanisme de migration.
    ///
    /// le contrôle `event-contracts` tient l'instantané des contrats et
    /// échoue sur toute rupture. Il ne l'interdit pas — il la rend visible en revue.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Type d'agrégat tel qu'il apparaît dans `aggregate.type` de l'enveloppe, en
    /// PascalCase : `FoodOrder`, `MarketplaceOrder`, `Payment`. Vide, il est déduit
    /// de <see cref="Aggregate"/>, ce qui suffit quand agrégat et type coïncident.
    /// </summary>
    public string? AggregateType { get; init; }

    /// <summary>Nom métier complet, ex. `food.order.accepted` ou `payment.succeeded`.</summary>
    public string EventType { get; }
}
