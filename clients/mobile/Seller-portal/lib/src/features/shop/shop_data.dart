import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/config/app_config.dart';
import '../../core/media/media_upload.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Compte de versement Mobile Money du vendeur.
///
/// L'opérateur détermine à lui seul le routage du versement (mode ET pays) chez
/// le prestataire. Un opérateur non supporté rend le retrait IMPOSSIBLE : mieux
/// vaut le dire ici que de laisser le vendeur demander un retrait qui échouera.
class PayoutAccount {
  PayoutAccount({required this.provider, required this.accountNumber, required this.accountName});

  final String provider;
  final String accountNumber;
  final String accountName;

  factory PayoutAccount.fromJson(Map d) => PayoutAccount(
        provider: Json.str(d['provider']),
        accountNumber: Json.str(d['accountNumber']),
        accountName: Json.str(d['accountName']),
      );
}

/// Opérateurs réellement routables pour un versement (alignés sur le backend).
/// Cette liste doit rester synchronisée avec `ResolveRoute` du gateway FedaPay :
/// y ajouter un opérateur non routé ferait échouer tous les retraits associés.
const kPayoutProviders = <({String value, String label})>[
  (value: 'MtnMomo', label: 'MTN Mobile Money (Bénin)'),
  (value: 'MoovMoney', label: 'Moov Money (Bénin)'),
  (value: 'Celtis', label: 'Celtis Cash (Bénin)'),
];

/// Libellé LOCALISÉ d'un opérateur (le nom de marque est conservé, seul le
/// parenthétique pays change de langue). Repli sur la valeur brute si inconnue.
String payoutProviderLabel(AppLocalizations l, String value) {
  switch (value) {
    case 'MtnMomo':
      return l.payoutMtn;
    case 'MoovMoney':
      return l.payoutMoov;
    case 'Celtis':
      return l.payoutCeltis;
  }
  return value;
}

/// Options localisées d'opérateurs (valeur technique + libellé traduit).
List<({String value, String label})> payoutProviderOptions(AppLocalizations l) =>
    [for (final p in kPayoutProviders) (value: p.value, label: payoutProviderLabel(l, p.value))];

/// Types de pièces acceptés — valeurs EXACTES de l'énumération serveur
/// (`KybDocumentType`). Envoyer un libellé français ferait échouer la validation.
const kKybTypes = <({String value, String label})>[
  (value: 'BusinessRegistry', label: 'Registre du commerce'),
  (value: 'IdCard', label: "Pièce d'identité"),
  (value: 'TaxId', label: 'Identifiant fiscal'),
  (value: 'ProofOfAddress', label: 'Justificatif de domicile'),
];

/// Libellé LOCALISÉ d'un type de pièce (repli sur le code brut si le serveur en
/// ajoute un). Les valeurs `value` restent les codes exacts attendus au serveur.
String kybTypeLabel(AppLocalizations l, String value) {
  switch (value) {
    case 'BusinessRegistry':
      return l.kybBusinessRegistry;
    case 'IdCard':
      return l.kybIdCard;
    case 'TaxId':
      return l.kybTaxId;
    case 'ProofOfAddress':
      return l.kybProofOfAddress;
  }
  return value.isEmpty ? l.kybFallbackDocument : value;
}

/// Options localisées de types de pièce (valeur technique + libellé traduit).
List<({String value, String label})> kybTypeOptions(AppLocalizations l) =>
    [for (final t in kKybTypes) (value: t.value, label: kybTypeLabel(l, t.value))];

/// Contraintes du serveur, reproduites ici pour échouer AVANT l'envoi : faire
/// téléverser 10 Mo sur un réseau mobile pour se voir refuser à l'arrivée est
/// la meilleure façon de faire abandonner un vendeur.
const kKybMaxBytes = 10 * 1024 * 1024; // 10 Mo
const kKybExtensions = <String>['pdf', 'jpg', 'jpeg', 'png', 'webp'];

class KybDocument {
  KybDocument({
    required this.id,
    required this.mediaId,
    required this.type,
    required this.status,
    required this.uploadedAt,
  });

  final String id;

  /// Identifiant du FICHIER dans media-service.
  ///
  /// UNE PIÈCE KYB EST PRIVÉE : SON URL N'EST PAS DIRECTEMENT LISIBLE.
  ///
  /// `SellerDocument` est une nature privée. Pour l'afficher, il faut demander
  /// une URL signée à `GET /api/media/{mediaId}/download-url` — pas réutiliser
  /// l'URL du dépôt, qui rendrait une image cassée.
  final String mediaId;

  final String type;
  final String status;
  final DateTime? uploadedAt;

  /// Une pièce vérifiée ne se supprime pas depuis l'app : elle fait foi.
  bool get isVerified => status.toLowerCase() == 'verified';

  bool get isRejected => status.toLowerCase() == 'rejected';

  factory KybDocument.fromJson(Map d) => KybDocument(
        id: Json.str(d['id']),
        mediaId: Json.str(d['mediaId']),
        type: Json.str(d['type']),
        status: Json.str(d['status']),
        uploadedAt: Json.asDate(d['uploadedAtUtc']),
      );
}

/// Informations société déclarées (raison sociale, RCCM, IFU…). Toutes optionnelles.
class CompanyInfo {
  const CompanyInfo({
    this.legalName,
    this.rccm,
    this.ifu,
    this.address,
    this.commune,
    this.communeName,
    this.activity,
    this.managerName,
    this.phone,
  });

  final String? legalName;
  final String? rccm;
  final String? ifu;
  final String? address;
  /// CODE d'une des 77 communes (« abomey-calavi »), pas un libellé libre.
  /// Reste facultatif : c'est du déclaratif de dossier KYB, pas une adresse de
  /// livraison.
  final String? commune;

  /// Libellé accentué, résolu par le SERVEUR. En lecture seule uniquement : c'est
  /// `commune` (le code) qu'on renvoie à l'écriture.
  final String? communeName;
  final String? activity;
  final String? managerName;
  final String? phone;

  bool get isEmpty =>
      [legalName, rccm, ifu, address, commune, activity, managerName, phone]
          .every((v) => v == null || v.trim().isEmpty);

  static String? _s(dynamic v) =>
      (v == null || v.toString().trim().isEmpty) ? null : v.toString();

  factory CompanyInfo.fromJson(Map d) => CompanyInfo(
        legalName: _s(d['legalName']),
        rccm: _s(d['rccm']),
        ifu: _s(d['ifu']),
        address: _s(d['address']),
        commune: _s(d['commune']),
        communeName: _s(d['communeName']),
        activity: _s(d['activity']),
        managerName: _s(d['managerName']),
        phone: _s(d['phone']),
      );

  Map<String, dynamic> toJson() => {
        'legalName': legalName,
        'rccm': rccm,
        'ifu': ifu,
        'address': address,
        'commune': commune,
        'activity': activity,
        'managerName': managerName,
        'phone': phone,
      };
}

class Shop {
  Shop({
    required this.id,
    required this.shopName,
    required this.description,
    required this.logoUrl,
    required this.status,
    required this.kybStatus,
    required this.kybRejectionReason,
    required this.payout,
    required this.documents,
    required this.metadata,
    required this.platformDefaultCommissionRate,
  });

  final String id;
  final String shopName;
  final String description;
  final String? logoUrl;

  /// Statut commercial du compte : `Pending`, `Active`, `Suspended`, `Closed`,
  /// `PendingReactivation` — valeurs exactes de l'énumération `SellerStatus`.
  final String status;
  final String kybStatus;

  /// Motif du refus KYB. Sans lui, le vendeur lit « Rejeté » sans savoir quoi
  /// corriger, et redépose la même pièce.
  final String? kybRejectionReason;

  final PayoutAccount? payout;
  final List<KybDocument> documents;
  final CompanyInfo? metadata;

  /// ═══════════════════════════════════════════════════════════════════════════
  /// TROIS CHAMPS QUE `SellerSummary` PORTE ET QU'ON NE LIT PAS. C'EST VOULU.
  ///
  ///   • `rating` — merchant-service le déclare (`decimal`, `numeric(3,2)`) mais
  ///     RIEN NE L'ALIMENTE : `Seller.UpdateRating` n'a aucun appelant dans tout
  ///     le dépôt. Il vaut 0,00 pour tout le monde, en permanence. L'afficher
  ///     ferait croire à chaque vendeur qu'il est noté zéro. La vraie note est
  ///     calculée par engagement-service — voir `sellerRatingProvider` dans
  ///     `features/reviews/reviews_data.dart`, et c'est CELLE-LÀ que l'écran
  ///     Boutique affiche.
  ///
  ///   • `salesCount` — même histoire : `Seller.RecordSale` et `SetSalesCount`
  ///     n'ont aucun appelant. Toujours 0. Il n'existe AUCUNE autre source : le
  ///     nombre de ventes d'un vendeur n'est calculé nulle part sur la
  ///     plateforme. L'écran ne l'affiche donc pas du tout — un « 0 vente » sur
  ///     la boutique d'un vendeur qui en a fait cent est pire qu'une absence.
  ///
  ///   • `commissionRate` — il est bien rendu, mais c'est le DÉFAUT PLATEFORME
  ///     (`Pricing:PlatformCommissionRate`, 0,10), injecté par
  ///     `SellerMapper.ToSummary(seller, _pricing.CommissionRate)`. La colonne
  ///     `Seller.CommissionRate` est documentée « COLONNE MORTE, NE PAS LIRE »,
  ///     et le taux réellement appliqué vient du moteur de règles Billing, que
  ///     merchant-service n'interroge pas (tâche « Le vendeur voit encore le taux
  ///     par défaut, pas le sien »). Un vendeur à 5 % négociés lirait 10 %.
  ///     Il est donc exposé ci-dessous AVEC sa nature, pour que l'écran puisse le
  ///     présenter comme un taux INDICATIF et non comme « votre taux ».
  /// ═══════════════════════════════════════════════════════════════════════════
  final double platformDefaultCommissionRate;

  bool get hasPayoutAccount => payout != null && payout!.accountNumber.isNotEmpty;

  /// Compte fermé par le vendeur (suppression partielle), réactivation non encore demandée.
  bool get isClosed => status.toLowerCase() == 'closed';

  /// Réactivation demandée, en attente de validation admin.
  bool get isReactivationRequested => status.toLowerCase() == 'pendingreactivation';

  /// Mode restreint : compte fermé ou en attente de réactivation.
  bool get isRestricted => isClosed || isReactivationRequested;

  factory Shop.fromJson(Map d) => Shop(
        id: Json.str(d['id']),
        shopName: Json.str(d['shopName'], 'Ma boutique'),
        description: Json.str(d['description']),
        logoUrl: (d['logoUrl']?.toString().isNotEmpty ?? false) ? d['logoUrl'].toString() : null,
        status: Json.str(d['status'], 'Pending'),
        kybStatus: Json.str(d['kybStatus'], 'NotStarted'),
        kybRejectionReason: (d['kybRejectionReason']?.toString().isNotEmpty ?? false)
            ? d['kybRejectionReason'].toString()
            : null,
        payout: d['payout'] is Map ? PayoutAccount.fromJson(Json.map(d['payout'])) : null,
        documents: Json.list(d['kybDocuments']).map(KybDocument.fromJson).toList(),
        metadata: d['metadata'] is Map ? CompanyInfo.fromJson(Json.map(d['metadata'])) : null,
        platformDefaultCommissionRate: Json.asDouble(d['commissionRate']),
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// FICHE VENDEUR, KYB ET COMPTE DE REVERSEMENT — merchant-service.
///
/// LA PASSERELLE NE RÉÉCRIT PAS `/api/merchants` EN `/api/sellers`.
///
/// Elle l'a fait, c'était un reliquat du monolithe, et cela envoyait toute la
/// façade vendeur sur un chemin inexistant. Le chemin d'entrée EST celui du
/// service (routes « merchants-read » / « merchants-write », sans `Transforms`).
///
/// LA LECTURE PASSE PAR `/me`, LES ÉCRITURES PAR `/{sellerId}/…`.
///
/// Ce n'est pas une incohérence : `/me` résout le vendeur depuis le jeton, et les
/// routes d'écriture portent l'identifiant dans l'URL avec une garde de propriété
/// (`DenyUnlessOwnSellerAsync` → 403 si ce n'est pas le sien). L'identifiant vient
/// donc du socle (`requiredSellerIdProvider`), jamais d'un écran.
/// ═════════════════════════════════════════════════════════════════════════════
class ShopApi extends ApiBase {
  ShopApi(super.dio, this._media);

  final MediaApi _media;

  static const _p = AppConfig.merchants;

  /// La boutique du compte connecté.
  ///
  /// 404 SIGNIFIE « PAS ENCORE DE BOUTIQUE », PAS « PANNE ». Le socle
  /// d'identité le gère déjà (`SellerIdentityApi.me`) ; ici on laisse remonter,
  /// car un écran Boutique atteint sans boutique est un cas de navigation, pas un
  /// état à afficher.
  Future<Shop> shop() => guard(() async {
        final resp = await dio.get('$_p/me');
        return Shop.fromJson(Json.map(resp.data));
      });

  Future<void> updateProfile(
    String sellerId, {
    required String shopName,
    String? description,
    String? logoUrl,
  }) =>
      guard(() async {
        await dio.put('$_p/$sellerId/profile', data: {
          'shopName': shopName,
          'logoUrl': logoUrl,
          'description': description,
        });
      });

  /// Informations société.
  ///
  /// LE CORPS EST ENVELOPPÉ : `{ "metadata": { … } }`.
  ///
  /// `UpdateMetadataRequest(SellerCompanyInfo? Metadata)` : envoyer les champs à
  /// plat — ce que faisait `info.toJson()` seul — laissait `Metadata` à `null`,
  /// et le serveur EFFAÇAIT les informations société au lieu de les mettre à
  /// jour. Un vendeur qui corrigeait son IFU perdait son RCCM.
  ///
  /// `communeName` N'EST PAS RENVOYÉ. Le contrat d'entrée `SellerCompanyInfo`
  /// ne porte que le CODE de commune ; le libellé accentué est résolu par le
  /// serveur à la lecture. `CompanyInfo.toJson` l'omet déjà — ne pas l'ajouter.
  Future<void> updateMetadata(String sellerId, CompanyInfo info) => guard(() async {
        await dio.put('$_p/$sellerId/metadata', data: {'metadata': info.toJson()});
      });

  /// Remplace le logo de la boutique.
  ///
  /// DEUX APPELS, PARCE QU'IL N'Y A PLUS DE ROUTE `/shop/logo`.
  ///
  /// Le fichier va sur media-service (`Seller` / `StoreMedia`), qui rend une URL
  /// publique ; celle-ci est ensuite posée sur la fiche par
  /// `PUT /api/merchants/{sellerId}/profile`. Le nom de boutique DOIT être
  /// renvoyé au passage : `UpdateProfileRequest` déclare `ShopName` non
  /// nullable, et cette route remplace la fiche entière.
  Future<String> uploadLogo(
    String sellerId,
    File file, {
    required String shopName,
    String? description,
  }) =>
      guard(() async {
        final name = file.path.split(Platform.pathSeparator).last;
        final deposit = await _media.uploadBytes(
          bytes: await file.readAsBytes(),
          fileName: name,
          ownerType: MediaOwner.seller,
          ownerId: sellerId,
          mediaType: MediaKind.storeMedia,
        );

        await updateProfile(
          sellerId,
          shopName: shopName,
          description: description,
          logoUrl: deposit.url,
        );
        return deposit.url;
      });

  /// Destination des versements.
  ///
  /// LE SERVEUR ACCEPTE PLUS D'OPÉRATEURS QU'IL N'EN SAIT PAYER.
  ///
  /// `PayoutProvider` admet `MtnMomo`, `MoovMoney`, `Wave`, `BankAccount` et
  /// `Celtis` ; mais `WalletPayout.IsMobileMoney`, côté retrait, n'en route que
  /// trois — MTN, Moov, Celtis. Enregistrer Wave ou un compte bancaire réussit,
  /// puis chaque demande de retrait échoue en 400 `wallet.no_payout_account`,
  /// sans que rien ne relie les deux faits. D'où `kPayoutProviders`, qui n'en
  /// propose que trois.
  Future<void> setPayoutAccount(
    String sellerId, {
    required String provider,
    required String accountNumber,
    required String accountName,
  }) =>
      guard(() async {
        await dio.put('$_p/$sellerId/payout-account', data: {
          'provider': provider,
          'accountNumber': accountNumber,
          'accountName': accountName,
        });
      });

  /// Dépose une pièce KYB.
  ///
  /// EN DEUX TEMPS, ET LE SERVEUR NE VOIT JAMAIS LE FICHIER.
  ///
  /// `POST /seller/shop/kyb-documents/upload` n'existe pas. Le fichier va sur
  /// media-service en nature `SellerDocument` — PRIVÉE, donc non lisible par
  /// URL directe — puis merchant-service reçoit `AddKybDocumentRequest(Type,
  /// MediaId)`. C'est ce qui permet à la suppression d'une pièce d'effacer aussi
  /// le fichier (`KybDocumentRemovedIntegrationEvent`).
  ///
  /// `type` DOIT ÊTRE UNE VALEUR EXACTE DE `KybDocumentType` : `IdCard`,
  /// `BusinessRegistry`, `TaxId`, `ProofOfAddress`. Un libellé français fait
  /// échouer la validation — d'où `kKybTypes`, qui garde les codes et ne traduit
  /// que l'affichage.
  Future<void> uploadKybDocument(
    String sellerId, {
    required File file,
    required String type,
  }) =>
      guard(() async {
        final name = file.path.split(Platform.pathSeparator).last;
        final deposit = await _media.uploadBytes(
          bytes: await file.readAsBytes(),
          fileName: name,
          ownerType: MediaOwner.seller,
          ownerId: sellerId,
          mediaType: MediaKind.sellerDocument,
        );

        await dio.post('$_p/$sellerId/kyb/documents', data: {
          'type': type,
          'mediaId': deposit.mediaId,
        });
      });

  /// LE CHEMIN EST `/kyb/documents/…`, PAS `/kyb-documents/…`.
  Future<void> removeKybDocument(String sellerId, String documentId) => guard(() async {
        await dio.delete('$_p/$sellerId/kyb/documents/$documentId');
      });

  /// URL signée, de courte durée, pour consulter une pièce KYB déposée.
  Future<String> kybDocumentUrl(String mediaId) => _media.signedUrl(mediaId);
}

final shopApiProvider = Provider<ShopApi>(
    (ref) => ShopApi(ref.watch(dioProvider), ref.watch(mediaApiProvider)));

final shopProvider = FutureProvider<Shop>((ref) => ref.watch(shopApiProvider).shop());
