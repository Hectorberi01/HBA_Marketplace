import 'package:flutter_test/flutter_test.dart';
import 'package:hba_express_pro/src/shared/utils/formatters.dart';

/// Tests de la logique de prix (net vendeur → prix acheteur) et du parsing JSON
/// tolérant. Pure logique, sans réseau ni locale.
void main() {
  group('Pricing (net vendeur → prix affiché)', () {
    // Taux plateforme : commission 10 %, frais paiement 5 % → ×1,15.
    test('décomposition d\'un prix net de 10 000', () {
      const net = 10000.0;
      // closeTo : évite toute fragilité liée aux arrondis flottants.
      expect(Pricing.commission(net), closeTo(1000.0, 0.001));
      expect(Pricing.providerFee(net), closeTo(500.0, 0.001));
      expect(Pricing.productPrice(net), closeTo(11500.0, 0.001));
    });

    test('le prix affiché est toujours > au net perçu', () {
      for (final net in [1.0, 250.0, 99999.0]) {
        expect(Pricing.productPrice(net), greaterThan(net));
      }
    });

    test('net + commission + frais == prix affiché (cohérence)', () {
      const net = 7000.0;
      final reconstructed = net + Pricing.commission(net) + Pricing.providerFee(net);
      expect(reconstructed, closeTo(Pricing.productPrice(net), 0.0001));
    });
  });

  group('Json (lecture tolérante du BFF)', () {
    test('asInt : int, num, chaîne, défaut', () {
      expect(Json.asInt(3), 3);
      expect(Json.asInt(3.9), 3);
      expect(Json.asInt('12'), 12);
      expect(Json.asInt(null), 0);
      expect(Json.asInt('abc', 7), 7);
    });

    test('asDouble : num, chaîne, défaut', () {
      expect(Json.asDouble(2), 2.0);
      expect(Json.asDouble('3.5'), 3.5);
      expect(Json.asDouble(null), 0.0);
      expect(Json.asDouble('x', 1.5), 1.5);
    });

    test('str : null/vide → repli', () {
      expect(Json.str('ok'), 'ok');
      expect(Json.str(null), '');
      expect(Json.str('', 'repli'), 'repli');
      expect(Json.str(42), '42');
    });

    test('map : non-map → map vide', () {
      expect(Json.map({'a': 1}), {'a': 1});
      expect(Json.map(null), <String, dynamic>{});
      expect(Json.map('nope'), <String, dynamic>{});
    });

    test('list : liste directe OU enveloppe {items: [...]}', () {
      expect(Json.list([{'a': 1}]).length, 1);
      expect(Json.list({'items': [{'a': 1}, {'b': 2}]}).length, 2);
      expect(Json.list(null), isEmpty);
    });
  });
}
