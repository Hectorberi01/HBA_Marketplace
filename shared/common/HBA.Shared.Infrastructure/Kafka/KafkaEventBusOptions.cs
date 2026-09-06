namespace HBA.Shared.Infrastructure.Kafka;

public sealed class KafkaEventBusOptions
{
    public const string SectionName = "Kafka";

    public string? BootstrapServers { get; init; }

    public string TopicPrefix { get; init; } = "service";

    public string TopicVersion { get; init; } = "v1";

    public string? Producer { get; init; }

    public string? ProducerVersion { get; init; }

    public bool Enabled { get; init; } = true;

    public string? ConsumerGroup { get; init; }

    /// <summary>
    /// Les sujets auxquels ce service s'abonne. VIDE par défaut : le catalogue
    /// <see cref="HbaTopics"/> fait foi.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE PROPRIÉTÉ PORTAIT UNE LISTE DE TREIZE SUJETS ÉCRITE EN DUR, ET
    ///    C'ÉTAIT LA SECONDE SOURCE DE VÉRITÉ D'ISSUE-001.
    ///
    /// Elle disait `service.merchant.v1`, `service.commerce.v1`,
    /// `service.financial.v1`… pendant que les producteurs dérivaient leur sujet de
    /// `SERVICE_NAME` et publiaient sur `service.seller.v1`, `service.cart.v1`,
    /// `service.payment.v1`. Les deux listes étaient justes chacune de son côté ;
    /// elles avaient cessé de se correspondre, et rien ne pouvait le signaler.
    ///
    /// Vide, le consommateur prend `HbaTopics.Tous(options)` — la même table que
    /// celle qui décide du sujet de publication. Une seule dérivation, appelée des
    /// deux côtés.
    ///
    /// LA RENSEIGNER RESTE POSSIBLE, et reste un choix DÉLIBÉRÉ : un service qui
    /// n'écoute qu'une poignée de sujets consomme moins. Mais il n'entendra plus
    /// jamais un domaine ajouté au catalogue, et c'est à lui de le savoir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string[] SubscribeTopics { get; init; } = [];

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « LISTE VIDE » ET « PAS DE LISTE » NE VEULENT PAS DIRE LA MEME CHOSE.
    ///
    /// Le consommateur traitait les deux pareil : liste vide, donc on s'abonne a
    /// TOUT. C'etait le bon repli tant qu'aucun service ne declarait ses sujets —
    /// mais depuis que les vingt-cinq modules le font, sept services qui ne
    /// consomment RIEN se retrouvaient abonnes aux vingt sujets de la plateforme.
    /// Ils deserialisaient l'integralite du trafic du bus pour tout jeter.
    ///
    /// Ce drapeau porte la difference : il vaut vrai des qu'un `AbonnementsKafka`
    /// est enregistre, meme avec une liste vide. Un service qui declare « je
    /// n'ecoute rien » ne demarre alors pas de consommateur du tout.
    ///
    /// CE QU'IL NE FAUT PAS EN FAIRE. Le poser a la main dans une configuration :
    /// il est deduit de la presence du service, pas lu. Un service qui n'a pas
    /// encore de module Kafka garde l'ancien comportement — c'est ce qui rend la
    /// migration progressive possible.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public bool AbonnementsDeclares { get; init; }
}
