import 'package:intl/intl.dart';

import '../../core/config/app_config.dart';

/// Helpers de formatage (montants, dates).
class Format {
  const Format._();

  /// Formate un montant avec sa devise. XOF/XAF sans décimales.
  static String money(num? amount, [String? currency]) {
    final cur = (currency == null || currency.isEmpty)
        ? AppConfig.defaultCurrency
        : currency;
    final noDecimals = cur == 'XOF' || cur == 'XAF';
    final f = NumberFormat.currency(
      locale: 'fr',
      symbol: cur,
      decimalDigits: noDecimals ? 0 : 2,
    );
    return f.format(amount ?? 0);
  }

  static String date(DateTime? d) {
    if (d == null) return '—';
    return DateFormat('dd/MM/yyyy', 'fr').format(d.toLocal());
  }

  static String dateTime(DateTime? d) {
    if (d == null) return '—';
    return DateFormat('dd/MM/yyyy HH:mm', 'fr').format(d.toLocal());
  }
}

/// Parsing tolérant des valeurs JSON renvoyées par le BFF.
class Json {
  const Json._();

  static String str(dynamic v, [String fallback = '']) => v?.toString() ?? fallback;

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

  static DateTime? asDate(dynamic v) {
    if (v == null) return null;
    return DateTime.tryParse(v.toString());
  }

  /// Objet JSON, ou map vide si la valeur n'en est pas un.
  ///
  /// Ajoutée pour le référentiel des communes (`{ communes: [...] }`). Elle existait
  /// déjà côté application vendeur : deux `Json` divergents dans deux applications du
  /// même produit, c'est exactement le genre d'écart qui coûte une compilation.
  static Map<String, dynamic> map(dynamic v) =>
      v is Map ? v.cast<String, dynamic>() : <String, dynamic>{};

  static List<Map<String, dynamic>> list(dynamic v) {
    if (v is List) {
      return v.whereType<Map>().map((e) => e.cast<String, dynamic>()).toList();
    }
    return const [];
  }
}
