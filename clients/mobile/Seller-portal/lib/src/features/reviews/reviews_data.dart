import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/network/api_base.dart';
import '../../core/network/not_migrated.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Avis laissé par un acheteur sur un produit de ce vendeur.
///
/// Miroir de `ReviewSummary` (engagement-service). Les noms de champs sont ceux
/// du contrat, à la casse près : le service ne configure aucun
/// `JsonSerializerOptions`, donc camelCase par défaut.
class Review {
  Review({
    required this.id,
    required this.productId,
    required this.buyerId,
    required this.rating,
    required this.title,
    required this.body,
    required this.isVerifiedPurchase,
    required this.status,
    required this.createdAt,
    required this.sellerReply,
    required this.sellerRepliedAt,
  });

  final String id;

  /// Le produit noté. Le contrat ne porte PAS son nom : afficher « sur quel
  /// produit » demanderait une seconde requête au catalogue par avis.
  final String productId;

  /// IL N'Y A PAS DE NOM D'AUTEUR, ET IL NE FAUT PAS EN FABRIQUER UN.
  ///
  /// Le modèle portait `author`, lu sur un champ `author` que `ReviewSummary`
  /// n'a jamais eu — la valeur retombait donc TOUJOURS sur le défaut « Client »,
  /// pour tous les avis, sans que rien ne le dise. Le contrat ne rend que
  /// `BuyerId`, un GUID.
  ///
  /// Résoudre le nom supposerait un appel à user-service PAR AVIS, sur des
  /// profils qu'un vendeur n'a aucune raison de lire. L'écran affiche donc un
  /// libellé générique assumé, et non un faux nom.
  final String buyerId;

  /// 1..5.
  final int rating;

  /// Titre de l'avis. Distinct du corps dans le contrat ; l'ancien modèle le
  /// perdait entièrement.
  final String title;

  final String body;

  /// Achat vérifié : l'acheteur a réellement commandé ce produit. C'est ce qui
  /// distingue un avis d'un commentaire, et le vendeur doit pouvoir le voir.
  final bool isVerifiedPurchase;

  /// `Published` | `Flagged` | `Rejected`.
  ///
  /// LES TROIS SORTENT DE LA ROUTE, SANS FILTRE.
  ///
  /// `ListReviewsBySellerQuery` ne filtre pas par statut : un avis signalé ou
  /// rejeté par la modération arrive avec les autres. Les confondre montrerait
  /// au vendeur, comme publics, des avis que les acheteurs ne voient plus.
  final String status;

  final DateTime? createdAt;
  final String? sellerReply;
  final DateTime? sellerRepliedAt;

  bool get hasReply => sellerReply != null && sellerReply!.isNotEmpty;

  /// Retiré de la vitrine par la modération.
  bool get isRejected => status.toLowerCase() == 'rejected';

  /// Signalé, en cours d'arbitrage.
  bool get isFlagged => status.toLowerCase() == 'flagged';

  factory Review.fromJson(Map d) => Review(
        id: Json.str(d['id']),
        productId: Json.str(d['productId']),
        buyerId: Json.str(d['buyerId']),
        rating: Json.asInt(d['rating']),
        title: Json.str(d['title']),
        body: Json.str(d['body']),
        isVerifiedPurchase: Json.asBool(d['isVerifiedPurchase']),
        status: Json.str(d['status'], 'Published'),
        createdAt: Json.asDate(d['createdAtUtc']),
        sellerReply: (d['sellerReply']?.toString().isNotEmpty ?? false)
            ? d['sellerReply'].toString()
            : null,
        sellerRepliedAt: Json.asDate(d['sellerRepliedAtUtc']),
      );
}

/// Note moyenne et volume d'avis d'un vendeur (`SellerRatingSummary`).
///
/// CE N'EST PAS LA MÊME CHOSE QUE `SellerSummary.Rating`.
///
/// merchant-service porte bien un champ `rating` sur le vendeur, mais RIEN NE
/// L'ALIMENTE (`Seller.UpdateRating` n'a aucun appelant dans tout le dépôt) : il
/// vaut 0 en permanence. La seule note réelle est celle-ci, calculée par
/// engagement-service à partir des avis. Voir `features/shop/shop_data.dart`.
class SellerRating {
  const SellerRating({required this.average, required this.count});

  final double average;
  final int count;

  bool get hasReviews => count > 0;

  factory SellerRating.fromJson(Map d) => SellerRating(
        average: Json.asDouble(d['average']),
        count: Json.asInt(d['count']),
      );
}

/// Avis clients — engagement-service, via la route publique `/api/reviews`
/// (réécrite en `/api/engagement/reviews` par la passerelle).
class ReviewsApi extends ApiBase {
  const ReviewsApi(super.dio);

  static const _p = AppConfig.reviews;

  /// Les avis reçus par ce vendeur.
  ///
  /// NI PAGINATION, NI FILTRE, NI TRI CÔTÉ SERVEUR.
  ///
  /// `GET /api/reviews/seller/{sellerId}` n'accepte AUCUN paramètre de requête et
  /// rend tout l'historique d'un coup. Le tri antichronologique est donc fait
  /// ici. Sur un vendeur à plusieurs milliers d'avis, cette route deviendra un
  /// problème — c'est une limite du service, pas de l'écran.
  Future<List<Review>> reviews(String sellerId) => guard(() async {
        final resp = await dio.get('$_p/seller/$sellerId');
        final items = Json.list(resp.data).map(Review.fromJson).toList();
        items.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
        return items;
      });

  /// Note moyenne agrégée. Répond toujours 200, jamais 404 : `0.0 / 0` quand il
  /// n'y a aucun avis.
  Future<SellerRating> rating(String sellerId) => guard(() async {
        final resp = await dio.get('$_p/seller/$sellerId/rating');
        return SellerRating.fromJson(Json.map(resp.data));
      });

  /// Réponse publique du vendeur à un avis.
  ///
  /// 204 SANS CORPS : IL FAUT RELIRE LA LISTE POUR VOIR SA RÉPONSE.
  ///
  /// La route ne renvoie pas l'avis mis à jour. L'appelant doit invalider
  /// [reviewsProvider] — sinon le vendeur écrit sa réponse et l'écran continue
  /// d'afficher « aucune réponse ».
  ///
  /// UNE SECONDE RÉPONSE ÉCRASE LA PREMIÈRE. `Review.Reply` affecte, il
  /// n'empile pas : il n'y a qu'UNE réponse vendeur par avis, et rien à l'écran
  /// ne doit suggérer une conversation.
  Future<void> reply(String reviewId, String body) => guard(() async {
        await dio.post('$_p/$reviewId/reply', data: {'body': body});
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// UN VENDEUR NE PEUT PAS SIGNALER UN AVIS. C'EST UNE DÉCISION, PAS UN TROU.
  ///
  /// Cette méthode appelait `POST /api/reviews/{id}/flag`. La route existe — mais
  /// elle est déclarée sous `app.MapAdminGroup("/api/engagement/reviews")`,
  /// c'est-à-dire `RequireRole("Admin", "Moderator")`. Un vendeur reçoit 403.
  /// Il n'existe par ailleurs AUCUNE route `/report` dans engagement-service.
  ///
  /// Le raisonnement est le bon : arbitrer un contenu, c'est modérer. Laisser la
  /// partie mise en cause déclencher elle-même le retrait d'un avis négatif
  /// reviendrait à lui donner une prise sur sa propre réputation.
  ///
  /// Le bouton « Signaler » de l'écran a donc été retiré, et non désactivé : un
  /// bouton grisé fait chercher la condition qui le rouvrirait. Le jour où un
  /// canal de contestation existera (support, messagerie), il ne passera pas par
  /// engagement-service.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<void> flag(String reviewId, String reason) async =>
      NotMigrated.call('reviewReport', screen: 'Avis · signaler');
}

final reviewsApiProvider = Provider<ReviewsApi>((ref) => ReviewsApi(ref.watch(dioProvider)));

/// Les avis du vendeur connecté.
///
/// LE `sellerId` VIENT DU SOCLE D'IDENTITÉ, PAS DE L'ÉCRAN.
///
/// L'ancien appel visait `/seller/reviews/` — un chemin sans identifiant, où le
/// BFF du monolithe déduisait le vendeur du jeton. Les routes HBA le portent dans
/// l'URL et vérifient la correspondance. Il vient donc de
/// `GET /api/merchants/me`, résolu une fois par session.
final reviewsProvider = FutureProvider<List<Review>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(reviewsApiProvider).reviews(sellerId);
});

/// Note moyenne du vendeur connecté.
final sellerRatingProvider = FutureProvider<SellerRating>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(reviewsApiProvider).rating(sellerId);
});
