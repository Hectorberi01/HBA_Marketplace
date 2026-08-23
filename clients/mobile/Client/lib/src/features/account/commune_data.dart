import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Une des 77 communes du Bénin.
class Commune {
  const Commune({
    required this.code,
    required this.name,
    required this.departmentName,
  });

  /// Identifiant STABLE (« abomey-calavi »). C'est lui qu'on envoie au serveur.
  final String code;

  /// Libellé accentué (« Abomey-Calavi »), pour l'affichage seulement.
  final String name;

  final String departmentName;

  factory Commune.fromJson(Map d) => Commune(
        code: Json.str(d['code']),
        name: Json.str(d['name']),
        departmentName: Json.str(d['departmentName']),
      );

  /// Clé de recherche : minuscules, sans accent, ponctuation aplatie. Permet de
  /// trouver « Sèmè-Podji » en tapant « seme podji », ce que fait un utilisateur
  /// sur un clavier de téléphone.
  String get searchKey => fold('$name $code $departmentName');

  /// Replie une chaîne pour la recherche : minuscules, sans accent, ponctuation
  /// aplatie. Publique parce que le sélecteur, dans un autre fichier, doit replier
  /// la requête de l'utilisateur EXACTEMENT comme les clés — en Dart, `_fold`
  /// serait invisible d'une autre bibliothèque.
  static String fold(String v) {
    const from = 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿ';
    const to = 'aaaaaaceeeeiiiinooooouuuuyy';
    final buffer = StringBuffer();
    for (final ch in v.toLowerCase().split('')) {
      final i = from.indexOf(ch);
      buffer.write(i >= 0 ? to[i] : ch);
    }
    return buffer.toString().replaceAll(RegExp(r'[^a-z0-9]+'), ' ').trim();
  }
}

/// ─────────────────────────────────────────────────────────────────────────────
/// LE RÉFÉRENTIEL VIENT DU SERVEUR, PAS D'UNE LISTE EMBARQUÉE.
///
/// Quatre surfaces ont besoin des 77 communes. Dupliquée quatre fois, la liste
/// diverge à la première correction d'orthographe — et une commune que
/// l'application propose mais que le serveur refuse produit un message d'erreur
/// incompréhensible. Le serveur reste l'unique autorité : ce qu'il liste est
/// exactement ce qu'il accepte.
///
/// La route est ANONYME et mise en cache 24 h côté serveur : elle peut être
/// appelée avant même la connexion, sur l'écran d'inscription.
/// ─────────────────────────────────────────────────────────────────────────────
class CommuneApi {
  CommuneApi(this._dio);
  final Dio _dio;

  Future<List<Commune>> list() async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/geo/communes');
      final map = Json.map(resp.data);
      return Json.list(map['communes']).map(Commune.fromJson).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final communeApiProvider = Provider<CommuneApi>((ref) => CommuneApi(ref.watch(dioProvider)));

/// Liste des communes. `keepAlive` : le découpage administratif n'a pas bougé
/// depuis 1999 — la recharger à chaque ouverture d'écran serait du gaspillage.
final communesProvider = FutureProvider<List<Commune>>((ref) {
  ref.keepAlive();
  return ref.watch(communeApiProvider).list();
});
