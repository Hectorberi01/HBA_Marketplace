import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/application/auth_controller.dart';
import '../../shared/widgets/app_notify.dart';

/// Exige une session pour une action rattachée au compte (panier, favoris,
/// messagerie…), depuis un écran par ailleurs PUBLIC.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// Depuis l'ouverture du catalogue aux visiteurs (App Store 5.1.1(v)), ces écrans
/// sont atteignables sans compte. Leurs actions, elles, ne le sont pas : sans ce
/// garde, elles partiraient au serveur pour revenir en 401, et l'utilisateur ne
/// verrait qu'une erreur technique sans comprendre qu'il lui suffit de se connecter.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// Renvoie `true` si l'action peut se poursuivre ; sinon invite à se connecter et
/// renvoie `false`.
bool requireAuth(BuildContext context, WidgetRef ref, {String? action}) {
  if (ref.read(authControllerProvider) == AuthStatus.authenticated) {
    return true;
  }

  AppNotify.info(
    context,
    action == null
        ? 'Connectez-vous pour continuer.'
        : 'Connectez-vous pour $action.',
    actionLabel: 'Se connecter',
    onAction: () => context.push('/login'),
  );
  return false;
}
