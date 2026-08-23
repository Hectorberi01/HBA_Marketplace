import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// La politique de version publiée par la passerelle.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// ELLE VIENT DE LA PASSERELLE, PAS D'UN SERVICE — ET C'EST DÉLIBÉRÉ.
///
/// `AppVersionController` la lit dans `IConfiguration` : aucun domaine, aucune
/// table, aucune migration. C'est une décision d'exploitation, qu'on ajuste le
/// jour d'une livraison. La loger dans un des treize services l'aurait dotée d'une
/// base de données dont elle n'a aucun besoin — et surtout, aurait fait dépendre
/// le DÉMARRAGE de toutes les applications de la santé de ce service-là.
///
/// L'APPEL EST ANONYME, ET C'EST OBLIGATOIRE.
///
/// Cette requête est la toute première de l'application, sur l'écran de démarrage,
/// AVANT la connexion. L'exiger authentifiée rendrait impossible le blocage d'une
/// version dont le parcours de connexion est justement cassé — le cas où l'on en a
/// le plus besoin.
/// ═══════════════════════════════════════════════════════════════════════════════
class AppVersionPolicy {
  const AppVersionPolicy({
    required this.minSupportedBuild,
    required this.latestBuild,
    required this.updateUrlAndroid,
    required this.updateUrlIos,
    required this.message,
  });

  /// En dessous, l'application se bloque.
  ///
  /// `0` SIGNIFIE « AUCUNE POLITIQUE EN VIGUEUR », pas « tout est bloqué ».
  /// C'est le repli que rend la passerelle pour une application non configurée :
  /// aucun build installé ne peut être inférieur à zéro, donc personne n'est
  /// bloqué. Un 404 aurait obligé chaque application à distinguer « je ne suis pas
  /// configurée » de « le serveur est en panne ».
  final int minSupportedBuild;

  /// Dernier build publié. Sert à PROPOSER une mise à jour, jamais à bloquer.
  final int latestBuild;

  final String? updateUrlAndroid;
  final String? updateUrlIos;

  /// Message de blocage. Nul = l'application emploie le sien.
  final String? message;

  factory AppVersionPolicy.fromJson(Map d) => AppVersionPolicy(
        minSupportedBuild: Json.asInt(d['minSupportedBuild']),
        latestBuild: Json.asInt(d['latestBuild']),
        updateUrlAndroid: (d['updateUrlAndroid']?.toString().isNotEmpty ?? false)
            ? d['updateUrlAndroid'].toString()
            : null,
        updateUrlIos: (d['updateUrlIos']?.toString().isNotEmpty ?? false)
            ? d['updateUrlIos'].toString()
            : null,
        message: (d['message']?.toString().isNotEmpty ?? false)
            ? d['message'].toString()
            : null,
      );
}

class AppVersionApi extends ApiBase {
  const AppVersionApi(super.dio);

  /// « seller » EST LA CLÉ DE CETTE APPLICATION dans `AppVersions`. Les trois
  /// applications partagent la route et se distinguent par ce segment : une clé
  /// erronée ne produit pas d'erreur mais la politique permissive, donc un
  /// blocage qui ne se déclenche jamais. À ne pas changer sans changer la config.
  Future<AppVersionPolicy> policy() => guard(() async {
        final resp = await dio.get('/api/app/seller/version');
        return AppVersionPolicy.fromJson(Json.map(resp.data));
      });
}

final appVersionApiProvider =
    Provider<AppVersionApi>((ref) => AppVersionApi(ref.watch(dioProvider)));

/// Le numéro de build INSTALLÉ, lu sur l'appareil.
///
/// C'EST `buildNumber`, PAS `version`. `version` est la chaîne marketing
/// (« 1.4.2 »), que l'on ne peut pas comparer numériquement — « 1.10 » est
/// inférieur à « 1.9 » en ordre lexicographique. `buildNumber` est le
/// `versionCode` Android / `CFBundleVersion` iOS : un entier qui croît à chaque
/// livraison, et le seul comparable.
///
/// REND `0` SI LA LECTURE ÉCHOUE, ce qui NE BLOQUE PAS : `0 >= 0` reste vrai
/// face à la politique permissive, et face à une politique réelle le blocage
/// serait déclenché par une panne de plugin plutôt que par une version périmée.
/// Voir le repli d'`AppUpdateController`, qui traite ce cas comme « on n'a pas pu
/// savoir ».
final installedBuildProvider = FutureProvider<int>((ref) async {
  final info = await PackageInfo.fromPlatform();
  return int.tryParse(info.buildNumber) ?? 0;
});
