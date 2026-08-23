import 'package:intl/intl.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/config/app_config.dart';

/// Formatage des montants et des dates.
class Format {
  const Format._();

  /// Montant + devise. XOF/XAF n'ont PAS de décimales : les afficher inventerait
  /// une précision qui n'existe pas dans la monnaie.
  static String money(num? amount, [String? currency]) {
    final cur = (currency == null || currency.isEmpty) ? AppConfig.defaultCurrency : currency;
    final noDecimals = cur == 'XOF' || cur == 'XAF';
    final f = NumberFormat.currency(locale: 'fr', symbol: cur, decimalDigits: noDecimals ? 0 : 2);
    return f.format(amount ?? 0);
  }

  /// Unité monétaire telle qu'ÉCRITE dans la maquette.
  ///
  /// « F CFA » ET NON « XOF ». LA DIFFÉRENCE EST POUR L'UTILISATEUR.
  ///
  /// `NumberFormat.currency` rend le code ISO — « 420 000 XOF ». Personne
  /// n'écrit cela au Bénin : un commerçant lit « F CFA ». Le code ISO reste
  /// juste pour les échanges machine ; il n'a rien à faire sur un écran.
  static const String cfa = 'F CFA';

  /// Montant SEUL, groupé par milliers, sans unité.
  ///
  /// Séparé de [money] parce que la maquette compose les deux à des tailles
  /// différentes : « 420 000 » en grand, « F CFA » en petit à côté. Un formatage
  /// qui colle l'unité au nombre rendrait cette mise en page impossible sans
  /// redécouper la chaîne — et un redécoupage par recherche d'espace casse dès
  /// que le montant en contient.
  ///
  /// L'espace de groupement est une ESPACE INSÉCABLE ÉTROITE (U+202F) : sans
  /// elle, « 420 000 » peut se couper en fin de ligne et laisser « 000 F CFA »
  /// seul sur la suivante.
  static String amount(num? value) {
    final formatted = NumberFormat.decimalPattern('fr').format(value ?? 0);

    // `intl` en français produit une espace insécable ordinaire (U+00A0) ; on la
    // resserre pour coller au rendu de la maquette.
    return formatted.replaceAll(' ', ' ');
  }

  /// Montant suivi de son unité, en une seule chaîne (listes, lignes de compte).
  static String cfaAmount(num? value) => '${amount(value)} $cfa';

  /// Montant signé : « – 38 400 » pour une déduction.
  ///
  /// Le tiret est un TIRET DEMI-CADRATIN (U+2013) et non un moins ASCII, comme
  /// dans la maquette. Il est suivi d'une espace : « –38 400 » se lit mal à
  /// côté d'un montant positif dans une colonne alignée à droite.
  static String signedCfa(num value) =>
      value < 0 ? '– ${cfaAmount(value.abs())}' : cfaAmount(value);

  static String date(DateTime? d) =>
      d == null ? '—' : DateFormat('dd/MM/yyyy', 'fr').format(d.toLocal());

  static String dateTime(DateTime? d) =>
      d == null ? '—' : DateFormat('dd/MM/yyyy HH:mm', 'fr').format(d.toLocal());

  /// Heure seule si c'est aujourd'hui, sinon la date (listes de conversations).
  static String shortWhen(DateTime? d) {
    if (d == null) return '';
    final local = d.toLocal();
    final now = DateTime.now();
    final sameDay = local.year == now.year && local.month == now.month && local.day == now.day;
    return sameDay
        ? DateFormat('HH:mm', 'fr').format(local)
        : DateFormat('dd/MM', 'fr').format(local);
  }

  /// Ancienneté lisible (« il y a 3 h ») — utile pour repérer ce qui traîne.
  /// Localisée : exige `AppLocalizations` (le temps relatif suit la langue active).
  static String age(AppLocalizations l, DateTime? d) {
    if (d == null) return '—';
    final elapsed = DateTime.now().difference(d.toLocal());
    if (elapsed.inMinutes < 1) return l.ageJustNow;
    if (elapsed.inHours < 1) return l.ageMinutes(elapsed.inMinutes);
    if (elapsed.inDays < 1) return l.ageHours(elapsed.inHours);
    return l.ageDays(elapsed.inDays);
  }
}

/// Lecture TOLÉRANTE du JSON du BFF.
///
/// Le backend fait évoluer ses contrats (champs ajoutés, renommés) : un parsing
/// strict ferait planter l'app à chaque déploiement. Ici, un champ manquant vaut
/// sa valeur par défaut, et l'écran continue de s'afficher.
class Json {
  const Json._();

  static String str(dynamic v, [String fallback = '']) {
    final s = v?.toString();
    return (s == null || s.isEmpty) ? fallback : s;
  }

  static int asInt(dynamic v, [int fallback = 0]) {
    if (v is int) return v;
    if (v is num) return v.toInt();
    return int.tryParse(v?.toString() ?? '') ?? fallback;
  }

  static double asDouble(dynamic v, [double fallback = 0]) {
    if (v is num) return v.toDouble();
    return double.tryParse(v?.toString() ?? '') ?? fallback;
  }

  static bool asBool(dynamic v, [bool fallback = false]) {
    if (v is bool) return v;
    final s = v?.toString().toLowerCase();
    if (s == 'true') return true;
    if (s == 'false') return false;
    return fallback;
  }

  static DateTime? asDate(dynamic v) => v == null ? null : DateTime.tryParse(v.toString());

  static List<Map<String, dynamic>> list(dynamic v) {
    if (v is List) {
      return v.whereType<Map>().map((e) => e.cast<String, dynamic>()).toList();
    }
    // Le BFF renvoie parfois { items: [...] } : on accepte les deux formes.
    if (v is Map && v['items'] is List) return list(v['items']);
    return const [];
  }

  static Map<String, dynamic> map(dynamic v) =>
      v is Map ? v.cast<String, dynamic>() : <String, dynamic>{};
}

/// Calculs de tarification affichés au vendeur (aperçu en direct).
///
/// Le vendeur saisit son prix NET ; l'acheteur paie ce prix majoré de la
/// commission et des frais provider. Montrer les deux évite le malentendu
/// classique : « pourquoi mon produit est affiché plus cher que mon prix ? ».
class Pricing {
  const Pricing._();

  static double commission(double sellerPrice) => sellerPrice * AppConfig.commissionRate;
  static double providerFee(double sellerPrice) => sellerPrice * AppConfig.providerFeeRate;
  static double productPrice(double sellerPrice) => sellerPrice * AppConfig.priceMultiplier;
}
