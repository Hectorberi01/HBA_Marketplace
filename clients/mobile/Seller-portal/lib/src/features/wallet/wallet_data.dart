import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Soldes du portefeuille vendeur.
class Wallet {
  Wallet({
    required this.pendingBalance,
    required this.availableBalance,
    required this.pendingWithdrawal,
    required this.currency,
  });

  /// Gains en attente de livraison (escrow) : pas encore retirables.
  final double pendingBalance;

  /// Solde retirable.
  final double availableBalance;

  /// Retraits déjà engagés : en attente de validation admin OU versement en
  /// cours chez l'opérateur. Ces fonds ont quitté le solde principal — les
  /// masquer donnerait au vendeur l'impression que son argent s'est volatilisé.
  final double pendingWithdrawal;

  final String currency;

  factory Wallet.fromJson(Map d) => Wallet(
        pendingBalance: Json.asDouble(d['pendingBalance']),
        availableBalance: Json.asDouble(d['availableBalance']),
        pendingWithdrawal: Json.asDouble(d['pendingWithdrawal']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      );
}

/// Demande de retrait.
class Withdrawal {
  Withdrawal({
    required this.id,
    required this.amount,
    required this.currency,
    required this.status,
    required this.providerRef,
    required this.failureReason,
    required this.createdAt,
    required this.completedAt,
  });

  final String id;
  final double amount;
  final String currency;

  /// Requested | Processing | Completed | Failed | Rejected.
  ///
  /// « Processing » = le versement est PARTI chez l'opérateur mais n'est pas
  /// confirmé. Ce n'est ni un succès ni un échec : l'afficher comme l'un des
  /// deux serait mentir au vendeur sur l'endroit où se trouve son argent.
  final String status;

  final String? providerRef;
  final String? failureReason;
  final DateTime? createdAt;
  final DateTime? completedAt;

  bool get isProcessing => status.toLowerCase() == 'processing';
  bool get isFailed => status.toLowerCase() == 'failed';

  factory Withdrawal.fromJson(Map d) => Withdrawal(
        id: Json.str(d['id']),
        amount: Json.asDouble(d['amount']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        status: Json.str(d['status']),
        providerRef: (d['providerRef']?.toString().isNotEmpty ?? false) ? d['providerRef'].toString() : null,
        failureReason: _presentableFailure(d['failureReason']?.toString()),
        createdAt: Json.asDate(d['createdAtUtc']),
        completedAt: Json.asDate(d['completedAtUtc']),
      );

  /// Le vendeur ne doit JAMAIS lire la plomberie du prestataire.
  ///
  /// L'historique contient encore des motifs bruts recopiés de la réponse HTTP de
  /// FedaPay — « FedaPay — création refusée (403) : {"message":"Opération non
  /// autorisée","errors":{},"model":null} ». C'est illisible, ça n'indique aucune
  /// action possible, et ça expose notre intégration. Le serveur ne les produit
  /// plus, mais les lignes déjà en base subsistent : on filtre donc ici aussi.
  ///
  /// Deux garde-fous plutôt qu'un : le jour où une passerelle refera la même
  /// erreur, l'app tiendra bon même si le serveur cède.
  static String? _presentableFailure(String? raw) {
    final reason = raw?.trim() ?? '';
    if (reason.isEmpty) return null;

    final lower = reason.toLowerCase();
    final technical = reason.contains('{') ||
        reason.contains('}') ||
        lower.contains('fedapay') ||
        lower.contains('http') ||
        lower.contains('exception') ||
        lower.contains('null');

    return technical
        ? "Versement refusé par l'opérateur."
        : reason;
  }
}

/// Mouvement du grand livre.
class WalletTx {
  WalletTx({
    required this.id,
    required this.direction,
    required this.amount,
    required this.currency,
    required this.reason,
    required this.createdAt,
  });

  final String id;
  final String direction; // Credit | Debit
  final double amount;
  final String currency;
  final String reason;
  final DateTime? createdAt;

  bool get isCredit => direction.toLowerCase() == 'credit';

  /// Libellé métier (le code brut « withdrawal_refund » ne parle à personne).
  String get label {
    switch (reason) {
      case 'order_confirmed':
        return 'Vente confirmée';
      case 'delivery_release':
        return 'Livraison confirmée';
      case 'withdrawal':
        return 'Retrait';
      case 'withdrawal_request':
        return 'Demande de retrait';
      case 'withdrawal_refund':
        return 'Retrait recrédité';
      case 'withdrawal_reject':
        return 'Retrait refusé';
      default:
        return 'Mouvement';
    }
  }

  factory WalletTx.fromJson(Map d) => WalletTx(
        id: Json.str(d['id']),
        direction: Json.str(d['direction']),
        amount: Json.asDouble(d['amount']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        reason: Json.str(d['reason']),
        createdAt: Json.asDate(d['createdAtUtc']),
      );
}

/// Portefeuille vendeur — financial-service.
///
/// LE CHEMIN PUBLIC N'EST PAS CELUI DU SERVICE : la passerelle réécrit
/// `/api/wallet/*` en `/api/financial/wallets/*`. On écrit donc le chemin
/// d'ENTRÉE (`AppConfig.wallet`) et jamais celui du service — le déduire du nom
/// du service produirait un 404 muet.
class WalletApi extends ApiBase {
  const WalletApi(super.dio);

  static const _p = AppConfig.wallet;

  /// Combien de mouvements on demande.
  ///
  /// `take` EST OBLIGATOIRE, ET SON ABSENCE NE PRODUIT PAS UN DÉFAUT MAIS UN 400.
  ///
  /// `ListSellerWalletTransactionsAsync(Guid sellerId, int take, …)` déclare un
  /// `int` NON nullable SANS valeur par défaut : en minimal API, cela en fait un
  /// paramètre de requête requis. Le `= 50` visible sur le record
  /// `ListSellerWalletTransactionsQuery` n'est jamais atteint par HTTP. L'appel
  /// sans `take` repartait donc en « Required parameter "int take" was not
  /// provided » — une erreur de liaison, illisible côté vendeur.
  static const int _transactionsPageSize = 50;

  Future<Wallet> wallet(String sellerId) => guard(() async {
        final resp = await dio.get('$_p/sellers/$sellerId');
        return Wallet.fromJson(Json.map(resp.data));
      });

  Future<List<Withdrawal>> withdrawals(String sellerId) => guard(() async {
        final resp = await dio.get('$_p/sellers/$sellerId/withdrawals');
        final items = Json.list(resp.data).map(Withdrawal.fromJson).toList();
        // Les plus récents d'abord : c'est ce que le vendeur vient vérifier.
        // Le serveur ne garantit aucun ordre sur cette route.
        items.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
        return items;
      });

  Future<List<WalletTx>> transactions(String sellerId) => guard(() async {
        final resp = await dio.get(
          '$_p/sellers/$sellerId/transactions',
          queryParameters: {'take': _transactionsPageSize},
        );
        return Json.list(resp.data).map(WalletTx.fromJson).toList();
      });

  /// Demande de retrait : les fonds sont RETENUS immédiatement, mais le
  /// versement n'a lieu qu'après validation par l'administrateur.
  ///
  /// LE CORPS NE PORTE QUE LE MONTANT, ET LA DESTINATION N'EST PAS NÉGOCIABLE.
  ///
  /// `AmountRequest(decimal Amount)` : un seul champ. La destination du versement
  /// est lue sur le compte du vendeur et FIGÉE dans la demande côté serveur —
  /// c'est ce qui empêche de changer de numéro Mobile Money entre la demande et
  /// la validation admin. Elle se règle ailleurs, par
  /// `PUT /api/merchants/{sellerId}/payout-account` (voir `shop_data.dart`).
  ///
  /// SANS COMPTE DE VERSEMENT, LE SERVEUR RÉPOND 400
  /// `wallet.no_payout_account` — et seuls MTN, Moov et Celtis sont réellement
  /// routables au versement, alors que la route de destination accepte aussi
  /// Wave et un compte bancaire. C'est pourquoi `kPayoutProviders` n'en propose
  /// que trois.
  Future<Withdrawal> requestWithdrawal(String sellerId, double amount) => guard(() async {
        final resp = await dio.post(
          '$_p/sellers/$sellerId/withdrawals',
          data: {'amount': amount},
        );
        return Withdrawal.fromJson(Json.map(resp.data));
      });

  /// UN VENDEUR NE PEUT PAS ANNULER SA DEMANDE DE RETRAIT.
  ///
  /// `approve` et `reject` vivent sous `MapAdminGroup`, et il n'existe aucune
  /// route d'annulation par le demandeur. Une demande partie est partie jusqu'à
  /// l'arbitrage. Ne pas offrir de bouton « Annuler » : il ne pourrait
  /// qu'échouer en 403.
}

final walletApiProvider = Provider<WalletApi>((ref) => WalletApi(ref.watch(dioProvider)));

/// LES TROIS FOURNISSEURS PASSENT PAR [requiredSellerIdProvider].
///
/// Ces routes portent le `sellerId` dans l'URL et le comparent au vendeur du
/// jeton : un identifiant vide ne produit pas une liste vide, il produit un 403
/// que le vendeur légitime ne comprend pas.
final walletProvider = FutureProvider<Wallet>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(walletApiProvider).wallet(sellerId);
});

final withdrawalsProvider = FutureProvider<List<Withdrawal>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(walletApiProvider).withdrawals(sellerId);
});

final walletTxProvider = FutureProvider<List<WalletTx>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(walletApiProvider).transactions(sellerId);
});
