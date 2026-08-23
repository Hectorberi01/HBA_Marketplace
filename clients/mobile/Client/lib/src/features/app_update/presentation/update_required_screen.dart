import 'dart:io' show Platform;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../app_update_controller.dart';
import '../app_update_data.dart';

/// Écran de blocage « mise à jour requise ».
///
/// Volontairement SANS issue autre que la mise à jour : pas de barre d'onglets,
/// pas de bouton retour. Tant que le build est trop ancien, c'est le seul écran
/// atteignable (le routeur y renvoie toute autre destination).
class UpdateRequiredScreen extends ConsumerWidget {
  const UpdateRequiredScreen({super.key});

  static const _defaultBody =
      "Cette version de l'application n'est plus prise en charge. "
      'Mettez-la à jour pour continuer vos achats en toute sécurité.';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(appUpdateControllerProvider.notifier);
    final policy = controller.policy;
    final body = (policy?.message.isNotEmpty ?? false) ? policy!.message : _defaultBody;

    return Scaffold(
      backgroundColor: AppTheme.brandGreen,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(28),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.system_update, color: Colors.white, size: 64),
                const SizedBox(height: 24),
                const Text(
                  'Mise à jour requise',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 14),
                Text(
                  body,
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 15, height: 1.5),
                ),
                const SizedBox(height: 32),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    style: FilledButton.styleFrom(
                      backgroundColor: Colors.white,
                      foregroundColor: AppTheme.brandGreen,
                      padding: const EdgeInsets.symmetric(vertical: 16),
                    ),
                    onPressed: () => _openStore(context, policy),
                    child: const Text('Mettre à jour', style: TextStyle(fontWeight: FontWeight.w700)),
                  ),
                ),
                const SizedBox(height: 12),
                TextButton(
                  onPressed: () => controller.recheck(),
                  child: Text('J\'ai déjà mis à jour',
                      style: TextStyle(color: Colors.white.withValues(alpha: 0.9))),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _openStore(BuildContext context, AppVersionPolicy? policy) async {
    final android = policy?.updateUrlAndroid ?? '';
    final ios = policy?.updateUrlIos ?? '';
    final url = Platform.isIOS ? (ios.isNotEmpty ? ios : android) : (android.isNotEmpty ? android : ios);

    const fallbackMsg = 'Lien de mise à jour indisponible. Recherchez « HBA Express » dans votre store.';
    if (url.isEmpty) {
      if (context.mounted) AppNotify.error(context, fallbackMsg);
      return;
    }
    final ok = await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
    if (!ok && context.mounted) AppNotify.error(context, fallbackMsg);
  }
}
