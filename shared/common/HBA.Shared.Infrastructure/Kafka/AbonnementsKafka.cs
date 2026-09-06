namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>
/// Les sujets Kafka qu'un service déclare écouter.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `SubscribeTopics` EXISTAIT, ÉTAIT DOCUMENTÉ, ÉTAIT HONORÉ — ET AUCUN CODE NE
///     LE REMPLISSAIT.
///
/// `KafkaEventBusOptions.SubscribeTopics` porte un encadré expliquant qu'un
/// service peut n'écouter qu'une poignée de sujets. `KafkaIntegrationEventConsumer`
/// le lit et s'y conforme. La fabrique d'options, elle, construisait l'objet
/// sans jamais y toucher : la propriété restait vide, le consommateur retombait
/// sur `HbaTopics.Tous`, et CHAQUE service s'abonnait aux vingt sujets de la
/// plateforme.
///
/// Même famille que `ApproveUserCommand` et que les six routes de validation du
/// catalogue : un réglage écrit, testé, et injoignable.
///
/// CE QUE ÇA COÛTAIT, ET CE N'EST PAS QUE DU RÉSEAU.
///
/// user-service traite TROIS types d'événement et en écoutait vingt sujets.
/// media-service, cart-service et food-cart-service en traitent UN SEUL. Le
/// consommateur avertit une fois par type inconnu — « aucun type chargé ne lui
/// correspond » — un avertissement juste et parfaitement inutile quand on reçoit
/// délibérément les événements de douze domaines qui ne nous concernent pas. Le
/// signal se noie dans le bruit qu'on a créé.
///
/// EN CODE, PAS EN CONFIGURATION, ET C'EST UNE DÉCISION.
///
/// Une variable d'environnement l'aurait ouvert à la divergence : le jour où un
/// service ajoute un consommateur, il faudrait penser à modifier le compose, sur
/// une ligne qu'aucun compilateur ne relit. Déclarée ici, la liste vit À CÔTÉ des
/// gestionnaires qu'elle sert, dans le même module, sous la même revue.
///
/// NE PAS EN ENREGISTRER GARDE LE COMPORTEMENT ACTUEL — tous les sujets. La
/// migration se fait donc service par service, sans état intermédiaire cassé.
///
/// CE QUE CELA NE PROTÈGE PAS : déclarer un sujet et oublier le gestionnaire, ou
/// l'inverse. Un service qui écoute `service.order.v1` sans gestionnaire d'ordre
/// consomme pour rien ; un gestionnaire dont le sujet n'est pas déclaré ne sera
/// JAMAIS appelé, en silence. C'est le prix de la déclaration explicite, et la
/// raison pour laquelle les deux vivent dans le même fichier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Sujets">
/// Noms COMPLETS, préfixe et version compris — `service.identity.v1`. La fabrique
/// d'options refuse au démarrage un sujet qui ne commence pas par le préfixe de
/// publication : un abonnement à un préfixe étranger ne recevrait jamais rien.
/// </param>
public sealed record AbonnementsKafka(params string[] Sujets);
