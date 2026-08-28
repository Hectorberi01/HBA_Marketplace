namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>
/// Le catalogue des sujets Kafka de la plateforme — UNE seule table, lue par le
/// producteur ET par le consommateur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-001 : IL Y AVAIT DEUX SOURCES DE VÉRITÉ, ET ELLES SE CONTREDISAIENT.
///
/// Le producteur dérivait son sujet de `SERVICE_NAME` : seller-service publiait sur
/// `service.seller.v1`. Le consommateur, lui, s'abonnait à une liste de treize
/// sujets ÉCRITE EN DUR dans `KafkaEventBusOptions`, qui disait
/// `service.merchant.v1`. Six domaines ne se croisaient donc jamais :
///
///   seller → merchant · cart → commerce · payment → financial
///   review → engagement · notification → communication · restaurant → food
///
/// Vingt-trois sujets étaient publiés que personne n'écoutait ; six étaient écoutés
/// que personne ne publiait. Aucune erreur, aucun avertissement : un message part,
/// il est acquitté, et il n'arrive nulle part.
///
/// CE DÉFAUT EN MASQUAIT UN AUTRE, ET C'EST POUR CELA QU'IL PASSE APRÈS 2.1.
///
/// Tant que les messages n'arrivaient pas, l'absence de garde d'idempotence sur
/// quatre-vingt-dix gestionnaires (ISSUE-008) ne se voyait pas. La corriger ici,
/// avant l'inbox, aurait fait apparaître les doublons le jour même.
///
/// LE NOM DU CONTENEUR N'EST PAS LE NOM DU DOMAINE, ET C'EST ASSUMÉ.
///
/// Le dossier s'appelle `seller-service`, l'espace de noms `HBA.Merchants.*`, la
/// clé de la passerelle `Services:Merchant`. Le sujet est un CONTRAT entre
/// services : il porte le domaine, pas le nom du processus qui l'héberge — sans
/// quoi renommer un conteneur casserait le bus en silence. La table ci-dessous rend
/// cette traduction explicite au lieu de la laisser à une convention de nommage.
///
/// TOUS LES SERVICES S'ABONNENT À TOUS LES SUJETS, ET C'EST UN COÛT CONNU.
///
/// Un consommateur qui ne s'intéresse qu'aux commandes reçoit aussi les paniers et
/// les avis, et les jette après désérialisation. La rétention et le
/// partitionnement se règlent alors par producteur, pas par agrégat : impossible de
/// garder les paiements trente jours et les positions GPS une heure. C'est
/// exactement ce que le §19.2 corrige avec un sujet par AGRÉGAT — voir
/// `HbaEventNaming`, écrit pour cela et pas encore branché. Ce fichier-ci fait
/// marcher l'existant ; il ne prétend pas être la cible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class HbaTopics
{
    /// <summary>
    /// Le domaine de chaque service qui publie. La clé est le nom du conteneur —
    /// c'est-à-dire `SERVICE_NAME`, ou `Kafka:Producer` quand il est renseigné.
    /// </summary>
    /// <remarks>
    /// LES QUATRE SQUELETTES RETIRÉS PAR D30 N'Y SONT PAS : `menu`,
    /// `availability`, `kitchen-prep` et `food/review`. Leur donner un sujet ferait
    /// provisionner en production des topics pour du code qui va disparaître.
    ///
    /// wallet ET billing N'ONT PAS D'ENTRÉE, et ce n'est pas un oubli : ils n'ont
    /// pas d'hôte à eux. `HBA.Financial.Api` compose payments, wallet et billing
    /// dans un seul processus, dont le `SERVICE_NAME` est `payment-service`. Leurs
    /// événements partent donc sur `financial`, ce qui est correct — c'est bien le
    /// même producteur au sens de Kafka.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> DomaineParService =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Les six traductions qui manquaient ────────────────────────────
            ["seller-service"] = "merchant",
            ["cart-service"] = "commerce",
            ["payment-service"] = "financial",
            ["review-service"] = "engagement",
            ["notification-service"] = "communication",
            ["restaurant-service"] = "food",

            // ── Les services dont le nom coïncide déjà avec le domaine ────────
            ["identity-service"] = "identity",
            ["user-service"] = "user",
            ["catalog-service"] = "catalog",
            ["inventory-service"] = "inventory",
            ["order-service"] = "order",
            ["delivery-service"] = "delivery",
            ["media-service"] = "media",
            ["promotion-service"] = "promotion",
            ["food-cart-service"] = "food-cart",
            ["food-order-service"] = "food-order",
            ["return-refund-service"] = "return-refund",
            ["delivery-pricing-service"] = "delivery-pricing",
            ["driver-service"] = "driver",
            ["route-service"] = "route",

            // ═════════════════════════════════════════════════════════════════
            // LES NOMS DE KUBERNETES, QUI NE SONT PAS CEUX DU COMPOSE.
            //
            // Les déploiements nomment leurs conteneurs par DOMAINE —
            // `merchant-service`, `commerce-service`, `financial-service` — là où
            // `docker-compose.dev.yml` les nomme par dépôt : `seller-service`,
            // `cart-service`, `payment-service`. Deux vocabulaires pour une même
            // chose, et c'est précisément ce qui a produit ISSUE-001.
            //
            // Le repli de `Domaine` tombe juste pour ceux-là — `merchant-service`
            // donne bien `merchant`. Mais il tombe juste par accident, et le
            // producteur AVERTIT à chaque démarrage qu'il n'est pas inscrit : un
            // avertissement qui crie à tort finit par être ignoré, y compris le
            // jour où il a raison.
            //
            // Ces alias ne créent aucun sujet supplémentaire — `Tous` déduplique
            // par domaine. Ils disent seulement que ces deux noms désignent le même
            // domaine, ce qui est vrai.
            // ═════════════════════════════════════════════════════════════════
            ["merchant-service"] = "merchant",
            ["commerce-service"] = "commerce",
            ["financial-service"] = "financial",
            ["communication-service"] = "communication",
            ["engagement-service"] = "engagement",
            ["food-service"] = "food"
        };

    /// <summary>Le service est-il inscrit au catalogue ?</summary>
    public static bool EstConnu(string? serviceOuProducteur)
        => !string.IsNullOrWhiteSpace(serviceOuProducteur)
           && DomaineParService.ContainsKey(serviceOuProducteur);

    /// <summary>
    /// Le domaine d'un service.
    /// </summary>
    /// <remarks>
    /// LE REPLI DÉRIVE, IL NE LÈVE PAS — MAIS L'APPELANT DOIT LE SIGNALER.
    ///
    /// Lever ici ferait qu'un conteneur hors catalogue — une passerelle, un BFF, un
    /// service neuf — refuserait de démarrer alors qu'il ne publie peut-être rien.
    /// Le repli reproduit donc l'ancienne dérivation, et c'est
    /// `KafkaIntegrationEventPublisher` qui avertit : voir son appel à
    /// <see cref="EstConnu"/>. Un service absent de la table publie sur un sujet que
    /// personne n'écoute — exactement le défaut qu'on ferme ici.
    /// </remarks>
    public static string Domaine(string serviceOuProducteur)
        => DomaineParService.TryGetValue(serviceOuProducteur, out var domaine)
            ? domaine
            : serviceOuProducteur.Replace("-service", string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Le sujet d'un service : <c>{prefixe}.{domaine}.{version}</c>.</summary>
    public static string Pour(KafkaEventBusOptions options, string serviceOuProducteur)
        => $"{options.TopicPrefix}.{Domaine(serviceOuProducteur)}.{options.TopicVersion}";

    /// <summary>
    /// Tous les sujets de la plateforme, sans doublon et triés.
    /// </summary>
    /// <remarks>
    /// C'EST CETTE MÉTHODE QUI REMPLACE LA LISTE ÉCRITE EN DUR.
    ///
    /// Ajouter un service au catalogue l'abonne partout et le rend joignable par
    /// tous : il n'y a plus de seconde liste à penser à mettre à jour. C'était tout
    /// le défaut — la liste existait, elle était juste, et elle avait cessé de
    /// correspondre aux `SERVICE_NAME` sans que rien ne le dise.
    /// </remarks>
    public static IReadOnlyList<string> Tous(KafkaEventBusOptions options)
        => [.. DomaineParService.Values
            .Distinct(StringComparer.Ordinal)
            .Select(domaine => $"{options.TopicPrefix}.{domaine}.{options.TopicVersion}")
            .OrderBy(sujet => sujet, StringComparer.Ordinal)];
}
