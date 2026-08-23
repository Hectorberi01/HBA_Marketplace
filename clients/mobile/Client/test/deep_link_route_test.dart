import 'package:flutter_test/flutter_test.dart';

import 'package:client_mp_mobile/src/core/deeplink/deep_link_service.dart';

/// `DeepLinkService.routeForUri` : traduction d'un lien universel entrant en
/// route interne. Un bug ici = un lien produit partagé qui n'ouvre pas la fiche.
void main() {
  test('/p/{id} -> /product/{id}', () {
    expect(
      DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/p/abc123')),
      '/product/abc123',
    );
  });

  test('/s/{id} -> /shop/{id}', () {
    expect(
      DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/s/shop42')),
      '/shop/shop42',
    );
  });

  test('fonctionne quel que soit le domaine (prod ou staging)', () {
    expect(
      DeepLinkService.routeForUri(Uri.parse('https://m.marketplace-staging.hba-marketplace.fr/p/xyz')),
      '/product/xyz',
    );
    expect(
      DeepLinkService.routeForUri(Uri.parse('https://m.marketplace-staging.hba-marketplace.fr/s/xyz')),
      '/shop/xyz',
    );
  });

  test('ignore les chemins non reconnus', () {
    expect(DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/')), isNull);
    expect(DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/x/abc')), isNull);
    expect(DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/p/')), isNull);
    expect(DeepLinkService.routeForUri(Uri.parse('https://m.hba-express.org/s/')), isNull);
  });
}
