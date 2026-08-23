/// Formatage des montants en francs CFA.
///
/// DUPLIQUÉ DEPUIS LE SELLER-PORTAL — à remonter avec le thème.
class Format {
  const Format._();

  static const String cfa = 'F CFA';

  /// « 15 500 » — espace insécable fine comme séparateur de milliers, jamais
  /// d'espace ordinaire : celui-ci autoriserait un retour à la ligne au milieu
  /// d'un montant.
  static String amount(num? value) {
    if (value == null) return '—';
    final negative = value < 0;
    final digits = value.abs().round().toString();
    final buffer = StringBuffer();
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) buffer.write(' ');
      buffer.write(digits[i]);
    }
    return negative ? '−${buffer.toString()}' : buffer.toString();
  }

  static String cfaAmount(num? value) => '${amount(value)} $cfa';

  /// « 1,8 KM » / « 6,2 km ».
  ///
  /// VIRGULE DÉCIMALE, PAS POINT. Le français écrit « 6,2 km » ; « 6.2 km »
  /// se lit comme une coquille et trahit une chaîne construite pour l'anglais.
  static String km(double value, {bool upper = true}) {
    final text = value.toStringAsFixed(1).replaceAll('.', ',');
    return upper ? '$text KM' : '$text km';
  }
}
