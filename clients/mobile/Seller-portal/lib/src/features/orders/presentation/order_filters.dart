import '../orders_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// LES PUCES DE FILTRE DE L'ÉCRAN COMMANDES.
///
/// CE NE SONT PLUS LES HUIT DE LA MAQUETTE, ET C'EST IRRÉDUCTIBLE.
///
/// Elle listait « Toutes · Nouvelles · Acceptées · À préparer · Prêtes ·
/// En livraison · Livrées · Refusées ». AUCUN de ces sept statuts n'existe dans
/// `OrderStatus` côté order-service. « À préparer », « Prête » et
/// « En livraison » appartiennent au COLIS (module Shipping, jamais extrait du
/// monolithe) et à la COURSE (delivery-service) ; « Acceptée » et « Refusée »
/// n'existent que pour la restauration, sur le ticket de cuisine de
/// food-service, et pas sur la commande.
///
/// Les puces suivent donc [SellerOrderStatus.all], dans l'ordre du parcours
/// réel. Les afficher grisées aurait fait chercher la condition qui les
/// rouvrirait ; les afficher actives aurait rendu une liste vide qu'on aurait
/// lue comme « aucune commande à préparer ».
///
/// IL N'Y A PLUS DE LISTE PAR UNIVERS.
///
/// `forUniverse` rendait sept puces côté boutique et sept autres côté
/// restaurant. order-service applique le MÊME jeu de statuts aux deux : une
/// distinction par univers ne décrirait plus rien.
///
/// LE FILTRAGE EST LOCAL, PARCE QUE LE SERVEUR N'EN OFFRE AUCUN.
///
/// `GET /api/sellers/{sellerId}/orders` n'accepte ni `status`, ni période, ni
/// pagination (cf. `OrdersApi.orders`). La puce filtre donc la liste déjà
/// ramenée — ce qui est exact ici, puisque la route rend TOUT l'historique.
/// ═════════════════════════════════════════════════════════════════════════════
class OrderFilter {
  const OrderFilter._(this.label, this.status);

  /// « Toutes » — pas de statut associé.
  static const OrderFilter all = OrderFilter._('Toutes', null);

  final String label;

  /// Valeur brute de `OrderStatus`, ou `null` pour « Toutes ».
  final String? status;

  bool matches(SellerOrder o) => status == null || o.status == status;

  /// ÉGALITÉ PAR VALEUR — SANS ELLE, AUCUNE PUCE NE RESTERAIT SÉLECTIONNÉE.
  ///
  /// [forSeller] reconstruit la liste à chaque `build`. Avec l'égalité par
  /// défaut (identité), la puce mémorisée dans l'état ne serait jamais égale à
  /// celle qu'on vient de créer : le filtre s'appliquerait correctement, mais
  /// plus aucune puce ne s'afficherait comme active.
  ///
  /// Un défaut silencieux, qui n'aurait pas fait planter l'application et se
  /// serait vu au premier tap — c'est-à-dire trop tard.
  @override
  bool operator ==(Object other) => other is OrderFilter && other.status == status;

  @override
  int get hashCode => status.hashCode;

  /// « Toutes », puis un filtre par statut réellement émis par order-service.
  static List<OrderFilter> get forSeller => [
        all,
        for (final s in SellerOrderStatus.all) OrderFilter._(SellerOrderStatus.label(s), s),
      ];
}
