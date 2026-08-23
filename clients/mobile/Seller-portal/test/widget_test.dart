import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:hba_express_pro/src/app.dart';

/// Test de fumée : l'application se construit et affiche l'écran de démarrage.
///
/// Le fichier généré par `flutter create` testait un compteur inexistant et
/// référençait une classe `MyApp` absente du projet : `flutter test` ne
/// compilait même pas. Un test qui ne compile pas est pire que pas de test —
/// il donne l'illusion d'une couverture.
///
/// On s'arrête volontairement au splash : au-delà, l'app restaure la session
/// depuis le stockage sécurisé puis appelle le réseau. Les tests suivants
/// devront simuler ces dépendances via `ProviderScope(overrides: …)`.
void main() {
  testWidgets("L'application démarre sur l'écran de lancement", (tester) async {
    await tester.pumpWidget(const ProviderScope(child: HbaExpressProApp()));
    await tester.pump();

    expect(find.text('Votre boutique, dans votre poche'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsWidgets);
  });
}
