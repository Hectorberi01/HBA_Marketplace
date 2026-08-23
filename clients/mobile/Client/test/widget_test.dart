import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

// `MarketplaceApp` vit dans src/app.dart — `main.dart` ne fait que l'amorcer.
// Importer main.dart ne suffit donc pas : la classe n'y est pas déclarée.
import 'package:client_mp_mobile/src/app.dart';

/// Test de fumée.
///
/// Le fichier généré par `flutter create` testait un compteur qui n'a jamais
/// existé et référençait une classe `MyApp` absente : il ne compilait pas, donc
/// `flutter test` échouait en bloc — aucun test ne tournait. Un test cassé est
/// pire que pas de test : il laisse croire que la suite est verte alors qu'elle
/// n'a jamais démarré.
///
/// On s'arrête volontairement à l'écran de démarrage : au-delà, l'app lit le
/// coffre-fort sécurisé puis appelle le réseau. Les tests suivants devront simuler
/// ces dépendances via `ProviderScope(overrides: …)`.
void main() {
  testWidgets('L’application démarre sans exception', (tester) async {
    await tester.pumpWidget(const ProviderScope(child: MarketplaceApp()));
    await tester.pump();

    expect(tester.takeException(), isNull);
  });
}
