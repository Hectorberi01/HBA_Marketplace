import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../shared/widgets/ui_kit.dart';
import '../../../core/theme/app_theme.dart';

/// Écran de démarrage affiché le temps de restaurer la session.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Scaffold(
      backgroundColor: AppTheme.brandGreen,
      body: SafeArea(
        // `SizedBox(width: double.infinity)` n'est pas décoratif : sans lui, tout
        // le bloc se collait au bord gauche.
        //
        // Une Column ne prend PAS la largeur de son parent : sur l'axe croisé
        // (ici l'horizontale) elle se réduit à la largeur de son plus large
        // enfant. Le plus large, c'était la phrase d'accroche — une petite
        // colonne de ~190 pt, plaquée en haut à gauche, qui centrait ses enfants
        // par rapport à ELLE-MÊME et non par rapport à l'écran. Le logo tombait
        // au quart de la largeur et l'accroche était rognée.
        //
        // Contraindre la largeur à l'infini la rend tight : la Column occupe
        // toute la place, et `CrossAxisAlignment.center` centre enfin sur l'écran.
        child: SizedBox(
          width: double.infinity,
          child: Padding(
            // Marge de sécurité : sur un petit écran, une accroche plus longue
            // (ou une police système agrandie) ne doit pas toucher les bords.
            padding: const EdgeInsets.symmetric(horizontal: 24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                // 3/2 plutôt que 1/1 : l'œil place le centre optique un peu
                // au-dessus du centre géométrique. À parts égales, le bloc
                // paraît tomber.
                const Spacer(flex: 3),

                // Le verrouillage porte déjà le nom : l'écrire en dessous le
                // dupliquerait.
                const BrandLockup(width: 220),
                const SizedBox(height: 12),
                Text(
                  l.authSplashTagline,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.85),
                    fontSize: 14,
                    height: 1.4,
                  ),
                ),

                const Spacer(flex: 2),

                const SizedBox(
                  width: 26,
                  height: 26,
                  child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                ),
                const SizedBox(height: 56),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
