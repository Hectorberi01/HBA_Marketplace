import 'package:flutter_test/flutter_test.dart';

import 'package:client_mp_mobile/src/features/catalog/catalog_data.dart';

/// `categorySubtreeIds` : résolution du sous-arbre d'un rayon depuis l'arbre plat.
/// C'est le cœur du parcours « une catégorie parente montre AUSSI les produits de
/// ses sous-catégories ». Un bug ici = des rayons qui paraissent vides.
void main() {
  // Arbre : Électronique > { Informatique > { Portables }, Téléphones }
  //         Beauté (autre racine, ne doit jamais remonter avec Électronique)
  final cats = [
    Category(id: 'elec', name: 'Électronique', slug: 'elec'),
    Category(id: 'info', name: 'Informatique', slug: 'info', parentId: 'elec'),
    Category(id: 'port', name: 'Portables', slug: 'port', parentId: 'info'),
    Category(id: 'tel', name: 'Téléphones', slug: 'tel', parentId: 'elec'),
    Category(id: 'beaute', name: 'Beauté', slug: 'beaute'),
  ];

  test('inclut la racine ET tout son sous-arbre (récursif)', () {
    expect(categorySubtreeIds('elec', cats).toSet(), {'elec', 'info', 'port', 'tel'});
  });

  test('une feuille ne renvoie qu\'elle-même', () {
    expect(categorySubtreeIds('port', cats), ['port']);
  });

  test('un nœud intermédiaire remonte ses descendants', () {
    expect(categorySubtreeIds('info', cats).toSet(), {'info', 'port'});
  });

  test('n\'inclut jamais une autre racine', () {
    expect(categorySubtreeIds('elec', cats), isNot(contains('beaute')));
  });

  test('identifiant inconnu -> juste cet identifiant', () {
    expect(categorySubtreeIds('inconnu', cats), ['inconnu']);
  });
}
