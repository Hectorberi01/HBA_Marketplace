import 'package:flutter/material.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

/// Écran de démarrage affiché le temps de restaurer la session.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppTheme.brandGreen,
      body: SafeArea(
        // Une Column ne prend PAS la largeur de son parent : sur l'axe croisé,
        // elle se réduit à la largeur de son plus large enfant, puis se cale à
        // gauche. Le centrage tombait donc juste par accident — parce que la
        // ligne « PAIEMENT MOBILE MONEY SÉCURISÉ » était presque aussi large que
        // l'écran. Raccourcissez cette phrase et tout le bloc dérive.
        // `width: double.infinity` rend la contrainte tight : la colonne occupe
        // toute la largeur et centre par rapport à l'ÉCRAN.
        child: SizedBox(
          width: double.infinity,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                // Le centre optique se situe légèrement au-dessus du centre
                // géométrique : à parts égales, le bloc paraît tomber.
                const Spacer(flex: 3),

                const BrandLogo(size: 92, onGreen: true),
                const SizedBox(height: 20),
                const Text(
                  'HbaExpress',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white, fontSize: 26, fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 6),
                Text(
                  'Achat rapide & sécurisé',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 14),
                ),

                const Spacer(flex: 2),

                const SizedBox(
                  width: 26,
                  height: 26,
                  child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                ),

                const Spacer(flex: 2),

                // Wrap plutôt que Row : à 24 pt de marge et avec une police
                // système agrandie (accessibilité), cette ligne dépasse et une
                // Row afficherait la bande jaune d'overflow. Le Wrap replie.
                Wrap(
                  alignment: WrapAlignment.center,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 6,
                  children: [
                    Icon(Icons.lock_outline, size: 13, color: Colors.white.withValues(alpha: 0.8)),
                    Text(
                      'PAIEMENT MOBILE MONEY SÉCURISÉ',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.8),
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        letterSpacing: 0.5,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 24),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
