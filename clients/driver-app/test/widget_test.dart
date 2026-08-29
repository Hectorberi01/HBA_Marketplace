import 'package:flutter_test/flutter_test.dart';

import 'package:hba_driver_app/src/app/driver_app.dart';

void main() {
  testWidgets('affiche le parcours de demarrage livreur', (tester) async {
    await tester.pumpWidget(const DriverApp());

    expect(find.text('HBA Driver'), findsOneWidget);
    expect(find.text('Commencer'), findsOneWidget);

    await tester.tap(find.text('Commencer'));
    await tester.pump();

    expect(find.text('Connexion livreur'), findsOneWidget);
    expect(find.text('Se connecter'), findsOneWidget);

    await tester.tap(find.text('Se connecter'));
    await tester.pump();

    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('Course en cours'), findsOneWidget);
  });
}
