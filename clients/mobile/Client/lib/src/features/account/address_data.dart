import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Adresse du carnet utilisateur — modèle béninois.
///
/// Pas de code postal, pas de rue obligatoire : au Bénin on se repère à la commune,
/// au quartier et à un POINT DE REPÈRE (« en face de la pharmacie Sainte-Rita »).
/// C'est ce repère que le livreur utilise réellement.
class Address {
  Address({
    required this.id,
    required this.label,
    required this.recipient,
    required this.phone,
    required this.communeCode,
    required this.communeName,
    required this.departmentName,
    required this.quartier,
    required this.landmark,
    required this.line1,
    required this.latitude,
    required this.longitude,
    required this.isDefault,
    required this.isComplete,
  });

  final String id;
  final String label;
  final String recipient;
  final String phone;

  /// Code d'une des 77 communes. `null` sur les adresses créées avant la refonte.
  final String? communeCode;

  /// Libellé résolu PAR LE SERVEUR : l'application n'embarque pas les 77 communes
  /// pour afficher une adresse déjà enregistrée.
  final String communeName;
  final String departmentName;

  final String? quartier;
  final String? landmark;
  final String? line1;

  /// Position FACULTATIVE. `null` est le cas normal, pas une anomalie : elle
  /// complète le point de repère pour le coursier, elle ne le remplace pas.
  final double? latitude;
  final double? longitude;

  final bool isDefault;

  /// ─────────────────────────────────────────────────────────────────────────
  /// L'ADRESSE EST-ELLE LIVRABLE ?
  ///
  /// `false` pour les adresses saisies avant la refonte : ni commune normalisée,
  /// ni point de repère, parfois pas de téléphone. Le serveur REFUSE le paiement
  /// avec ces adresses — un coursier sans repère et sans numéro à appeler ne
  /// trouve pas la maison, et le colis revient.
  ///
  /// L'écran de carnet les signale et le checkout les bloque AVANT le paiement.
  /// ─────────────────────────────────────────────────────────────────────────
  final bool isComplete;

  /// Une ligne lisible, du plus précis au plus large. Le REPÈRE en premier :
  /// c'est ce que le livreur cherche, la commune il la connaît déjà.
  String get summary => [landmark, quartier, line1, communeName]
      .where((s) => s != null && s.isNotEmpty)
      .join(', ');

  factory Address.fromJson(Map d) => Address(
        id: Json.str(d['id']),
        label: Json.str(d['label'], 'Adresse'),
        recipient: Json.str(d['recipient']),
        phone: Json.str(d['phone']),
        communeCode: _orNull(d['communeCode']),
        communeName: Json.str(d['communeName']),
        departmentName: Json.str(d['departmentName']),
        quartier: _orNull(d['quartier']),
        landmark: _orNull(d['landmark']),
        line1: _orNull(d['line1']),
        latitude: (d['latitude'] as num?)?.toDouble(),
        longitude: (d['longitude'] as num?)?.toDouble(),
        isDefault: Json.asBool(d['isDefault']),
        isComplete: Json.asBool(d['isComplete']),
      );

  static String? _orNull(dynamic v) {
    final s = v?.toString().trim();
    return (s == null || s.isEmpty) ? null : s;
  }
}

class AddressApi {
  AddressApi(this._dio);
  final Dio _dio;
  static const _p = '${AppConfig.apiPrefix}/checkout/addresses';

  Future<List<Address>> list() => _wrap(() async {
        final resp = await _dio.get(_p);
        return Json.list(resp.data).map(Address.fromJson).toList();
      });

  Future<void> add({
    required String label,
    required String recipient,
    required String phone,
    required String communeCode,
    String? quartier,
    required String landmark,
    String? line1,
    double? latitude,
    double? longitude,
    required bool makeDefault,
  }) =>
      _wrap(() async {
        await _dio.post(_p, data: {
          'label': label,
          'recipient': recipient,
          'phone': phone,
          // Le serveur attend « commune » et résout indifféremment un code
          // (« abomey-calavi ») ou un libellé (« Abomey-Calavi »). On envoie le code.
          'commune': communeCode,
          'quartier': quartier,
          'landmark': landmark,
          'line1': line1,
          'latitude': latitude,
          'longitude': longitude,
          'makeDefault': makeDefault,
        });
      });

  Future<void> update({
    required String id,
    required String label,
    required String recipient,
    required String phone,
    required String communeCode,
    String? quartier,
    required String landmark,
    String? line1,
    double? latitude,
    double? longitude,
    required bool makeDefault,
  }) =>
      _wrap(() async {
        await _dio.put('$_p/$id', data: {
          'label': label,
          'recipient': recipient,
          'phone': phone,
          // Le serveur attend « commune » et résout indifféremment un code
          // (« abomey-calavi ») ou un libellé (« Abomey-Calavi »). On envoie le code.
          'commune': communeCode,
          'quartier': quartier,
          'landmark': landmark,
          'line1': line1,
          'latitude': latitude,
          'longitude': longitude,
          'makeDefault': makeDefault,
        });
      });

  Future<void> remove(String id) => _wrap(() async => _dio.delete('$_p/$id'));

  Future<void> setDefault(String id) => _wrap(() async => _dio.put('$_p/$id/default'));

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final addressApiProvider = Provider<AddressApi>((ref) => AddressApi(ref.watch(dioProvider)));

class AddressController extends AsyncNotifier<List<Address>> {
  @override
  Future<List<Address>> build() => ref.watch(addressApiProvider).list();

  Future<void> add({
    required String label,
    required String recipient,
    required String phone,
    required String communeCode,
    String? quartier,
    required String landmark,
    String? line1,
    double? latitude,
    double? longitude,
    required bool makeDefault,
  }) async {
    await ref.read(addressApiProvider).add(
        label: label, recipient: recipient, phone: phone,
        communeCode: communeCode, quartier: quartier, landmark: landmark,
        line1: line1, latitude: latitude, longitude: longitude, makeDefault: makeDefault);
    ref.invalidateSelf();
    await future;
  }

  // NB : nommé « edit » et pas « update » car AsyncNotifier expose déjà update().
  Future<void> edit({
    required String id,
    required String label,
    required String recipient,
    required String phone,
    required String communeCode,
    String? quartier,
    required String landmark,
    String? line1,
    double? latitude,
    double? longitude,
    required bool makeDefault,
  }) async {
    await ref.read(addressApiProvider).update(
        id: id, label: label, recipient: recipient, phone: phone,
        communeCode: communeCode, quartier: quartier, landmark: landmark,
        line1: line1, latitude: latitude, longitude: longitude, makeDefault: makeDefault);
    ref.invalidateSelf();
    await future;
  }

  Future<void> remove(String id) async {
    await ref.read(addressApiProvider).remove(id);
    ref.invalidateSelf();
    await future;
  }

  Future<void> setDefault(String id) async {
    await ref.read(addressApiProvider).setDefault(id);
    ref.invalidateSelf();
    await future;
  }
}

final addressControllerProvider =
    AsyncNotifierProvider<AddressController, List<Address>>(AddressController.new);

/// Adresse par défaut (ou la première), pour le checkout.
final defaultAddressProvider = Provider<Address?>((ref) {
  final list = ref.watch(addressControllerProvider).valueOrNull;
  if (list == null || list.isEmpty) return null;
  return list.firstWhere((a) => a.isDefault, orElse: () => list.first);
});

/// Id de l'adresse choisie au checkout (null = utiliser l'adresse par défaut).
final selectedCheckoutAddressIdProvider = StateProvider<String?>((ref) => null);

/// Adresse effectivement retenue au checkout : la sélection explicite, sinon
/// l'adresse par défaut.
final checkoutAddressProvider = Provider<Address?>((ref) {
  final list = ref.watch(addressControllerProvider).valueOrNull ?? const [];
  final selectedId = ref.watch(selectedCheckoutAddressIdProvider);
  if (selectedId != null) {
    for (final a in list) {
      if (a.id == selectedId) return a;
    }
  }
  return ref.watch(defaultAddressProvider);
});
