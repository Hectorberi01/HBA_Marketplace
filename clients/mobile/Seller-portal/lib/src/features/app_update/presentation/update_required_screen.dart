import 'dart:io' show Platform;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../app_update_controller.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// MISE À JOUR REQUISE — LA PORTE DE BLOCAGE.
///
/// C'ÉTAIT UN BLOQUANT APP STORE 5.1.1(v), ET LA RAISON MÉRITE D'ÊTRE ÉCRITE.
///
/// La directive interdit une application qui devient inutilisable sans offrir de
/// sortie. L'écran précédent affichait « la vérification arrive bientôt » et rien
/// d'autre : si la porte s'était refermée, le vendeur se retrouvait devant un mur
/// sans savoir quelle version installer ni où la trouver. Un rejet certain.
///
/// Deux actions sont donc obligatoires ici, et non décoratives :
///
///   • OUVRIR LA FICHE STORE — le lien vient de la POLITIQUE, pas d'une constante
///     compilée. Changer d'identifiant d'application ne doit pas exiger une
///     livraison, précisément parce que les versions bloquées ne peuvent plus en
///     recevoir.
///
///   • « J'AI DÉJÀ MIS À JOUR » — relance la vérification. Sur Android, la mise à
///     jour peut s'installer pendant que l'application reste ouverte en arrière-
///     plan ; sans ce bouton, le vendeur devrait tuer le processus à la main, ce
///     que beaucoup ne savent pas faire.
///
/// AUCUN MOYEN DE PASSER OUTRE, ET C'EST LE POINT.
///
/// Pas de « plus tard », pas de retour arrière. Le routeur redirige tout vers ici
/// tant que l'état vaut `updateRequired` : un contournement viderait la porte de son sens.
/// C'est aussi pourquoi `AppUpdateController` ne bloque JAMAIS sur un échec de
/// vérification — une panne de passerelle mettrait sinon tout le parc dehors.
/// ═════════════════════════════════════════════════════════════════════════════
class UpdateRequiredScreen extends ConsumerWidget {
  const UpdateRequiredScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    // LU SUR LE CONTRÔLEUR, PAS RECHARGÉ. Un second appel réseau ici échouerait
    // exactement dans les cas où l'on a le plus besoin d'afficher quelque chose.
    final politique = ref.read(appUpdateControllerProvider.notifier).politique;

    final lien = Platform.isIOS ? politique?.updateUrlIos : politique?.updateUrlAndroid;

    return Scaffold(
      backgroundColor: colors.surface,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(Icons.system_update, size: 64, color: AppTheme.brandGreen),
              const SizedBox(height: 24),
              Text(
                'Mise à jour requise',
                textAlign: TextAlign.center,
                style: TextStyle(
                    fontSize: 22, fontWeight: FontWeight.w800, color: colors.ink),
              ),
              const SizedBox(height: 12),
              Text(
                // Le message du serveur d'abord : il peut expliquer POURQUOI cette
                // version est retirée, ce qu'un texte générique ne dira jamais.
                politique?.message ??
                    'Cette version de HBA Partner n\'est plus prise en charge. '
                        'Installez la dernière version pour continuer à gérer '
                        'votre boutique.',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 14, height: 1.5, color: colors.subtle),
              ),
              const SizedBox(height: 32),

              FilledButton(
                // DÉSACTIVÉ SI LA POLITIQUE N'A PAS DE LIEN, plutôt qu'un bouton
                // qui n'ouvre rien. Le cas ne devrait pas arriver — la
                // configuration en porte un pour les trois applications — mais un
                // bouton mort sur un écran sans issue serait précisément le motif
                // du rejet qu'on cherche à éviter.
                onPressed: lien == null ? null : () => _ouvrir(context, lien),
                style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight)),
                child: const Text('Mettre à jour'),
              ),
              const SizedBox(height: 12),

              OutlinedButton(
                // `ref.invalidate` RELANCE `build()` DU CONTRÔLEUR, donc la
                // vérification entière. Appeler une méthode publique du
                // contrôleur reviendrait au même en dupliquant le chemin.
                onPressed: () => ref.invalidate(appUpdateControllerProvider),
                style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight)),
                child: const Text('J\'ai déjà mis à jour'),
              ),

              if (lien == null) ...[
                const SizedBox(height: 16),
                const Text(
                  'Le lien de téléchargement est indisponible. Recherchez '
                  '« HBA Partner » dans votre magasin d\'applications.',
                  textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 12, color: AppTheme.promoOrange),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _ouvrir(BuildContext context, String lien) async {
    // `externalApplication` EST INDISPENSABLE. Le mode par défaut ouvre une vue
    // web intégrée, où la fiche store n'offre pas de bouton d'installation : le
    // vendeur verrait la page sans pouvoir agir dessus.
    final ok = await launchUrl(Uri.parse(lien), mode: LaunchMode.externalApplication);
    if (!ok && context.mounted) {
      AppNotify.error(context, 'Impossible d\'ouvrir le magasin d\'applications.');
    }
  }
}
