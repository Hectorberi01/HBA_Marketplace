// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter_test/flutter_test.dart';

import 'package:hba_seller_mobile/main.dart';

void main() {
  testWidgets('HBAExpress Pro opens on seller dashboard', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(const HbaSellerApp());

    expect(find.text('Accueil'), findsWidgets);
    expect(find.text('Awa Électronique'), findsWidgets);
    expect(find.text('Boutique ouverte'), findsOneWidget);
    expect(find.text("Commandes aujourd'hui"), findsOneWidget);
  });

  testWidgets('seller can open catalog tab', (WidgetTester tester) async {
    await tester.pumpWidget(const HbaSellerApp());

    await tester.tap(find.text('Catalogue').last);
    await tester.pump();

    expect(find.text('Samsung Galaxy A35'), findsOneWidget);
    expect(find.text('Stock faible'), findsWidgets);
  });
}
