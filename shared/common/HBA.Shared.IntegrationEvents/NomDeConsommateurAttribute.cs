namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'IDENTITE D'UN CONSOMMATEUR DANS `consumer_inbox`, ECRITE PLUTOT QUE DEDUITE.
///
/// LE DEFAUT QUE CET ATTRIBUT FERME.
///
/// `IntegrationEventDispatcher` derivait la cle d'idempotence du NOM COMPLET du
/// type — espace de noms compris. Son propre commentaire l'avertissait :
/// « renommer une classe de handler change ce nom, et tous ses evenements passes
/// redeviennent jamais traites : au prochain rejeu, ils refont leur effet. Un
/// renommage de handler est donc un geste a traiter comme une migration ».
///
/// La migration des modules Kafka a fait exactement cela, QUATRE-VINGT-SEIZE
/// FOIS. Descendre les consommateurs de `Application/…/EventHandlers` vers
/// `Infrastructure/Messaging/Kafka/Consumers` a change leur espace de noms, donc
/// leur cle, donc a orpheline toutes leurs traces. Aucun test ne pouvait le voir :
/// la cle vit en BASE, pas dans le code.
///
/// CE QUE COUTAIT L'OUBLI. Au premier rejeu — remise a zero d'offsets,
/// rebalancement de partition, groupe recree — chaque evenement deja traite
/// repassait pour neuf. `CreditDriverOnDeliveryCompletedHandler` aurait credite
/// deux fois. `SendOtpCodeHandler` aurait renvoye un code a usage unique.
///
/// COMMENT S'EN SERVIR.
///
/// La valeur est une CLE DE BASE DE DONNEES, pas un nom de type : elle ne se
/// refactorise pas, elle ne se corrige pas, elle ne suit pas les renommages. Elle
/// est posee une fois et ne bouge plus. Les valeurs actuelles reproduisent le nom
/// complet que chaque handler portait AVANT le deplacement — c'est la seule forme
/// qui laisse les traces deja en base valides.
///
/// CE QU'IL NE COUVRE PAS. Il ne protege que les handlers qui le portent. Un
/// handler cree demain sans attribut retombe sur son nom de type, et le meme
/// piege se rearme au premier deplacement. Le controle qui fermerait cela est un
/// test d'architecture : « tout `IIntegrationEventHandler` porte un
/// `[NomDeConsommateur]` ». Il n'existe pas encore.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NomDeConsommateurAttribute : Attribute
{
    public NomDeConsommateurAttribute(string nom)
    {
        if (string.IsNullOrWhiteSpace(nom))
        {
            throw new ArgumentException(
                "Le nom de consommateur est une cle d'idempotence en base : il ne peut pas etre vide.",
                nameof(nom));
        }

        Nom = nom;
    }

    /// <summary>La cle telle qu'elle est — ou sera — dans `consumer_inbox.consumer_name`.</summary>
    public string Nom { get; }
}
