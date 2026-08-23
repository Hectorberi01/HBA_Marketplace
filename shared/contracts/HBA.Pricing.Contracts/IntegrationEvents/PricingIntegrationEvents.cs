namespace HBA.Pricing.Contracts.IntegrationEvents;

// ═════════════════════════════════════════════════════════════════════════════
// CE FICHIER NE DÉCLARE PLUS RIEN, ET C'EST LE RÉSULTAT D'UNE COLLISION.
//
// Il portait un `PromotionCreatedIntegrationEvent` qui rendait exactement le même
// `eventType` que celui de `HBA.Promotions.Contracts.IntegrationEvents` :
// « promotion.created ». `KafkaEventNaming.EventType` ne regarde que le NOM DE
// CLASSE, et l'enveloppe Kafka ne transporte que ce nom — jamais l'espace de noms.
//
// CE QUE LA COLLISION PROVOQUAIT : `ResolveEventType` voyait deux types répondre
// au même nom et retenait LE PREMIER PAR ORDRE ALPHABÉTIQUE du nom complet — donc
// celui-ci, « HBA.Pricing… » précédant « HBA.Promotions… ». Un gestionnaire
// enregistré pour l'AUTRE type n'aurait JAMAIS été appelé : pas d'exception, pas
// d'échec de désérialisation, un avertissement noyé au démarrage, et l'offset
// committé juste après. L'effet métier attendu n'a simplement pas lieu, et on ne
// s'en aperçoit qu'en regardant une table vide.
//
// POURQUOI C'EST PROMOTIONS QUI EST RETENU : le sujet de l'événement est la
// CAMPAGNE, agrégat de promotion-service — pas le tarif. C'est aussi la seule des
// deux versions qui servait (celle d'ici n'était importée par aucun fichier) et
// la seule qui porte la forme complète du fait — Name, Scope, Value, fenêtre,
// budget, devise. Celle qui vivait ici n'en portait que trois champs, dont un
// `ScopeType` qui n'existe nulle part ailleurs dans le dépôt : c'est elle qui
// gagnait, et un consommateur qui serait apparu aurait donc reçu le MAUVAIS type,
// amputé de tout ce qui rend l'événement utile.
//
// NE PAS Y REDÉCLARER UN ÉVÉNEMENT DE PROMOTION. Le tarif CONSOMME les
// promotions (voir `IPricingModuleApi`), il ne les annonce pas. Tout fait public
// sur une campagne se déclare dans `HBA.Promotions.Contracts`.
// ═════════════════════════════════════════════════════════════════════════════
