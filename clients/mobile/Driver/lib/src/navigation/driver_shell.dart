import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/theme/app_theme.dart';

/// Coquille à cinq onglets : Accueil · Missions · Revenus · Historique · Compte.
///
/// BARRE MAISON, PAS UNE `NavigationBar` MATERIAL 3.
///
/// Material 3 impose une pastille arrondie derrière l'onglet actif et une
/// hauteur de 80 px, que la maquette ne montre pas. La rendre transparente
/// laisse l'animation et l'espacement. Cinq colonnes égales coûtent moins de
/// code que les contournements qu'il aurait fallu empiler.
class DriverShell extends StatelessWidget {
  const DriverShell({super.key, required this.child, required this.location});

  final Widget child;
  final String location;

  static const tabs = ['/home', '/missions', '/earnings', '/history', '/account'];

  static const _labels = ['Accueil', 'Missions', 'Revenus', 'Historique', 'Compte'];

  static const _icons = [
    Icons.home_outlined,
    Icons.description_outlined,
    Icons.credit_card_outlined,
    Icons.access_time,
    Icons.person_outline,
  ];

  int get _index {
    final i = tabs.indexWhere((t) => location.startsWith(t));
    return i < 0 ? 0 : i;
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      body: child,
      bottomNavigationBar: Container(
        decoration: BoxDecoration(
          color: colors.surface,
          border: Border(top: BorderSide(color: colors.line)),
        ),
        child: SafeArea(
          top: false,
          child: SizedBox(
            height: 62,
            child: Row(
              children: [
                for (var i = 0; i < tabs.length; i++)
                  Expanded(
                    child: _NavItem(
                      icon: _icons[i],
                      label: _labels[i],
                      selected: i == _index,
                      onTap: () => context.go(tabs[i]),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    required this.icon,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    // L'ONGLET ACTIF EST VERT, PAS SEULEMENT PLUS SOMBRE.
    //
    // Le portail vendeur distingue l'onglet actif par le seul contraste de gris.
    // Ici la maquette met du vert — et c'est justifié : cette application se
    // consulte dehors, en plein soleil, où un écart de gris disparaît.
    final tint = selected ? AppTheme.brandGreen : colors.subtle;

    return InkWell(
      onTap: onTap,
      splashColor: Colors.transparent,
      highlightColor: Colors.transparent,
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 22, color: tint),
          const SizedBox(height: 4),
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 11,
              fontWeight: selected ? FontWeight.w800 : FontWeight.w500,
              color: tint,
            ),
          ),
        ],
      ),
    );
  }
}
