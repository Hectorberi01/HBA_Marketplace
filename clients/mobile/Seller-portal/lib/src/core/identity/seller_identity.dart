import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../shared/utils/formatters.dart';
import '../config/app_config.dart';
import '../network/api_base.dart';
import '../network/api_exception.dart';
import '../providers/core_providers.dart';

/// Identité VENDEUR du compte connecté, telle que merchant-service la connaît.
///
/// Reprend les champs de `SellerSummary` dont l'application a besoin. Les autres
/// (compte de retrait, pièces KYB, informations société) sont laissés aux écrans
/// qui les affichent : le socle n'a pas à porter le RIB du vendeur en mémoire
/// pendant toute la session.
class SellerIdentity {
  const SellerIdentity({
    required this.sellerId,
    required this.userId,
    required this.shopName,
    required this.status,
    required this.kybStatus,
    required this.commissionRate,
    this.logoUrl,
    this.kybRejectionReason,
  });

  final String sellerId;
  final String userId;
  final String shopName;

  /// Statut du vendeur (`Pending`, `Active`, `Suspended`, `Closed`…) : ce qui
  /// autorise à VENDRE, indépendamment du rôle porté par le jeton.
  final String status;

  /// Avancement du dossier KYB (`Pending`, `Approved`, `Rejected`).
  final String kybStatus;

  /// LE TAUX RÉELLEMENT APPLIQUÉ À CE VENDEUR, ET NON UNE CONSTANTE.
  ///
  /// `AppConfig.commissionRate` est une valeur d'affichage par défaut ; le
  /// serveur peut appliquer un taux négocié. Afficher la constante à un vendeur
  /// qui n'a pas ce taux-là lui fait calculer de faux revenus nets.
  final double commissionRate;

  final String? logoUrl;

  /// Motif du refus KYB : sans lui, le vendeur voit « Rejeté » sans savoir quoi
  /// corriger, et redépose la même pièce.
  final String? kybRejectionReason;

  factory SellerIdentity.fromJson(Map<String, dynamic> d) => SellerIdentity(
        sellerId: Json.str(d['id']),
        userId: Json.str(d['userId']),
        shopName: Json.str(d['shopName'], 'Ma boutique'),
        status: Json.str(d['status']),
        kybStatus: Json.str(d['kybStatus']),
        commissionRate: Json.asDouble(d['commissionRate']),
        logoUrl: d['logoUrl']?.toString(),
        kybRejectionReason: d['kybRejectionReason']?.toString(),
      );
}

/// Accès à l'identité vendeur (merchant-service, via la passerelle).
class SellerIdentityApi extends ApiBase {
  const SellerIdentityApi(super.dio);

  static const _base = AppConfig.merchants;

  /// Le vendeur du compte connecté, ou `null` s'il n'a pas encore de boutique.
  ///
  /// 404 N'EST PAS UNE PANNE ICI. `GET /api/merchants/me` répond « Aucune
  /// boutique pour ce compte » à un compte qui vient de s'inscrire et n'a pas
  /// franchi l'étape de création. Le traiter comme une erreur afficherait un
  /// écran d'échec là où il faut proposer de créer la boutique.
  Future<SellerIdentity?> me() => guard(() async {
        try {
          final resp = await dio.get('$_base/me');
          return SellerIdentity.fromJson(Json.map(resp.data));
        } on DioException catch (e) {
          if (e.response?.statusCode == 404) return null;
          rethrow;
        }
      });

  /// Crée la boutique du compte connecté et renvoie son `sellerId`.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// LE JETON EN MAIN NE PORTE PAS ENCORE LE RÔLE `Seller` AU RETOUR.
  ///
  /// L'inscription du vendeur émet `SellerRegisteredIntegrationEvent`, et c'est
  /// identity-service qui, en le consommant, attribue le rôle. Le jeton obtenu à
  /// la connexion est ANTÉRIEUR : il ne contient pas ce rôle, et il ne le
  /// contiendra jamais — un JWT ne se met pas à jour tout seul.
  ///
  /// Conséquence concrète si l'on n'y prend garde : le vendeur crée sa boutique,
  /// l'application l'envoie sur `bffMerchant/activities`, et la passerelle
  /// répond 403 (`MerchantOnly`). Tout semble avoir réussi, et plus rien ne
  /// fonctionne — jusqu'à une reconnexion manuelle, que personne ne devine.
  ///
  /// L'appelant DOIT donc rafraîchir la session juste après (voir
  /// `AuthController.registerShop`).
  ///
  /// `commissionRate` N'EST PAS ENVOYÉ, ET C'EST VOLONTAIRE. Le champ existe
  /// dans le contrat et vaut 0,10 par défaut côté serveur ; laisser une
  /// application cliente le proposer reviendrait à laisser un vendeur négocier
  /// sa propre commission depuis son téléphone.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String> registerShop({
    required String shopName,
    Map<String, dynamic>? metadata,
  }) =>
      guard(() async {
        final resp = await dio.post(_base, data: {
          'shopName': shopName,
          if (metadata != null) 'metadata': metadata,
        });
        final data = Json.map(resp.data);
        final id = Json.str(data['id']);
        if (id.isEmpty) {
          throw ApiException('Boutique créée, mais son identifiant est absent de la réponse.');
        }
        return id;
      });
}

final sellerIdentityApiProvider =
    Provider<SellerIdentityApi>((ref) => SellerIdentityApi(ref.watch(dioProvider)));

/// ═══════════════════════════════════════════════════════════════════════════════
/// L'IDENTITÉ VENDEUR, RÉSOLUE UNE FOIS — ET JAMAIS DEVINÉE.
///
/// AUCUN ÉCRAN NE DOIT FABRIQUER NI CONSERVER UN `sellerId`.
///
/// `GET /api/sellers/{sellerId}/orders` et les routes `/api/merchants/{sellerId}/…`
/// exigent désormais que l'appelant PROUVE qu'il est ce vendeur : le service
/// compare l'identifiant de l'URL à celui résolu depuis le jeton et répond 403
/// sinon. Avant ce durcissement, un identifiant ramassé dans une fiche produit
/// suffisait à renommer la boutique d'un concurrent ou à détourner ses virements.
///
/// Un `sellerId` deviné, recopié d'un ancien écran ou stocké en dur ne produit
/// donc plus une fuite : il produit un 403 que le vendeur légitime ne comprend
/// pas. La seule source est `GET /api/merchants/me`, et elle est ici.
///
/// Un seul appel par session : le provider est mis en cache par Riverpod, et
/// recalculé quand l'état d'authentification change — connexion, déconnexion,
/// création de boutique. Laisser chaque écran interroger `/me` multiplierait la
/// requête par le nombre d'onglets ouverts au démarrage.
/// ═══════════════════════════════════════════════════════════════════════════════
final sellerIdentityProvider = FutureProvider<SellerIdentity?>((ref) async {
  // Recalcule à chaque bascule de session : un jeton neuf peut désigner un autre
  // vendeur (téléphone partagé), et l'identité précédente serait alors fausse.
  ref.watch(sessionExpiredProvider);

  final hasSession = await ref.watch(tokenStorageProvider).hasSession;
  if (!hasSession) return null;

  return ref.watch(sellerIdentityApiProvider).me();
});

/// Raccourci pour les écrans qui n'ont besoin que de l'identifiant.
///
/// Rend `null` tant que l'identité n'est pas résolue, ou si le compte n'a pas de
/// boutique : c'est à l'appelant de ne PAS appeler une route scopée vendeur dans
/// ce cas, plutôt que d'envoyer une chaîne vide et de récolter un 403.
final sellerIdProvider = Provider<String?>((ref) {
  return ref.watch(sellerIdentityProvider).valueOrNull?.sellerId;
});

/// Le `sellerId` de la session, ou une ERREUR NOMMÉE — jamais une chaîne vide.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// C'EST PAR ICI QUE PASSENT TOUTES LES ROUTES SCOPÉES VENDEUR.
///
/// `/api/sellers/{sellerId}/orders`, `/api/wallet/sellers/{sellerId}`,
/// `/api/reviews/seller/{sellerId}`, `/api/catalog/sellers/{sellerId}/products`,
/// `/api/merchants/{sellerId}/…` : toutes portent l'identifiant DANS L'URL, et
/// toutes vérifient qu'il correspond au vendeur du jeton.
///
/// Le [sellerIdProvider] voisin rend `null` pendant la résolution ET pour un
/// compte sans boutique — deux situations que l'appelant confondait, avec le même
/// résultat : une URL du genre `/api/wallet/sellers/null/…`, un 404 ou un 403, et
/// un écran d'erreur générique qui ne dit rien de la cause.
///
/// Ce fournisseur-ci distingue les trois : il ATTEND la résolution (état de
/// chargement), rend l'identifiant, ou lève une erreur qui NOMME le manque. Les
/// écrans le consomment en `AsyncValue` et affichent naturellement le bon état.
///
/// « PAS DE BOUTIQUE » N'EST PAS UNE PANNE. Un compte fraîchement inscrit dont
/// la création de boutique a échoué est dans ce cas : il faut lui proposer de la
/// créer, pas lui montrer « erreur serveur ».
/// ═══════════════════════════════════════════════════════════════════════════════
final requiredSellerIdProvider = FutureProvider<String>((ref) async {
  final seller = await ref.watch(sellerIdentityProvider.future);
  if (seller == null || seller.sellerId.isEmpty) {
    throw ApiException(
      "Aucune boutique n'est rattachée à ce compte. Créez votre boutique pour "
      'accéder à cet écran.',
      code: 'seller.no_shop',
    );
  }
  return seller.sellerId;
});
