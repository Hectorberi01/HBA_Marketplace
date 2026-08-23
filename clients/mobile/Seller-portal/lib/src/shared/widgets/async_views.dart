import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/theme/app_theme.dart';

/// Chargement centré.
class LoadingView extends StatelessWidget {
  const LoadingView({super.key});

  @override
  Widget build(BuildContext context) =>
      const Center(child: Padding(padding: EdgeInsets.all(32), child: CircularProgressIndicator()));
}

/// Erreur avec bouton « Réessayer ».
///
/// Un 404 signifie ici « le serveur ne connaît pas encore cette route » (backend
/// pas redéployé) : on le dit clairement plutôt que d'afficher « introuvable »,
/// qui laisserait croire à une donnée manquante.
class ErrorView extends StatelessWidget {
  const ErrorView({super.key, required this.message, this.onRetry, this.isNotFound = false});

  final String message;
  final VoidCallback? onRetry;
  final bool isNotFound;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final text = isNotFound ? l.commonFeatureUnavailable : message;

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(isNotFound ? Icons.cloud_off_outlined : Icons.error_outline,
                size: 48, color: isNotFound ? colors.subtle : AppTheme.danger),
            const SizedBox(height: 12),
            Text(text, textAlign: TextAlign.center),
            if (onRetry != null) ...[
              const SizedBox(height: 16),
              OutlinedButton.icon(
                onPressed: onRetry,
                icon: const Icon(Icons.refresh),
                label: Text(l.commonRetry),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// État vide.
class EmptyView extends StatelessWidget {
  const EmptyView({super.key, required this.message, this.icon = Icons.inbox_outlined, this.action});

  final String message;
  final IconData icon;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 48, color: colors.subtle),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center, style: TextStyle(color: colors.subtle)),
            if (action != null) ...[const SizedBox(height: 16), action!],
          ],
        ),
      ),
    );
  }
}
