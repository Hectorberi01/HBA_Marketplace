import 'package:flutter/material.dart';

import '../../core/theme/app_theme.dart';

/// Notifications légères (toasts) en bas d'écran.
///
/// On efface systématiquement la précédente avant d'en afficher une nouvelle :
/// sinon elles s'empilent et restent bloquées à l'écran lors des navigations.
class AppNotify {
  const AppNotify._();

  static void success(BuildContext context, String message) => _show(
        context,
        message: message,
        icon: Icons.check_circle,
        iconColor: AppTheme.brandGreen,
      );

  static void error(BuildContext context, String message) => _show(
        context,
        message: message,
        icon: Icons.error_outline,
        iconColor: AppTheme.danger,
        duration: const Duration(seconds: 5),
      );

  static void info(BuildContext context, String message) => _show(
        context,
        message: message,
        icon: Icons.info_outline,
        iconColor: Colors.white,
      );

  static void _show(
    BuildContext context, {
    required String message,
    required IconData icon,
    required Color iconColor,
    Duration duration = const Duration(seconds: 3),
  }) {
    final messenger = ScaffoldMessenger.of(context);
    messenger.clearSnackBars();
    messenger.showSnackBar(
      SnackBar(
        behavior: SnackBarBehavior.floating,
        // Toujours SOMBRE (les deux modes) : le texte est blanc. Utiliser une
        // couleur adaptative (ink) rendait le fond quasi-blanc en mode sombre →
        // texte blanc illisible. Un toast se veut un bandeau sombre constant.
        backgroundColor: const Color(0xFF23282A),
        duration: duration,
        elevation: 6,
        margin: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        content: Row(
          children: [
            Icon(icon, color: iconColor, size: 20),
            const SizedBox(width: 12),
            Expanded(
              child: Text(message, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600)),
            ),
          ],
        ),
      ),
    );
  }
}
