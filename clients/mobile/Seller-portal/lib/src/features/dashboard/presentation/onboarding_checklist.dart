import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../catalog/catalog_data.dart';
import '../../shop/shop_data.dart';

/// Mémorise si le vendeur a masqué la checklist de démarrage (une fois pour toutes).
const _kDismissedKey = 'onboarding_checklist_dismissed';

class _ChecklistDismissed extends Notifier<bool> {
  final FlutterSecureStorage _storage = const FlutterSecureStorage();

  @override
  bool build() {
    _load();
    return false;
  }

  Future<void> _load() async {
    if (await _storage.read(key: _kDismissedKey) == '1' && !state) state = true;
  }

  Future<void> dismiss() async {
    state = true;
    await _storage.write(key: _kDismissedKey, value: '1');
  }
}

final _checklistDismissedProvider =
    NotifierProvider<_ChecklistDismissed, bool>(_ChecklistDismissed.new);

class _Step {
  const _Step(this.title, this.subtitle, this.done, this.route, this.icon);
  final String title;
  final String subtitle;
  final bool done;
  final String route;
  final IconData icon;
}

/// Checklist « Bien démarrer » : suit la progression réelle du vendeur et renvoie
/// vers chaque action. S'efface quand tout est fait, ou si le vendeur la masque.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE WIDGET ÉTAIT ORPHELIN, ET IL EST RÉDUIT À DEUX ÉTAPES SUR QUATRE.
///
/// VEN2 a supprimé `home_screen.dart`, son seul appelant : plus rien ne
/// l'affichait. Deux issues étaient possibles — le supprimer, ou le rebrancher.
/// Il est rebranché sur le tableau de bord d'une boutique
/// (`ExpressDashboardScreen`), parce que c'est exactement là qu'un vendeur qui
/// démarre a besoin de savoir ce qui lui manque.
///
/// MAIS DEUX DE SES QUATRE ÉTAPES N'ONT PLUS D'AMONT, ET LES GARDER RENDAIT LA
///    CHECKLIST INFINIE.
///
///   • « Ajouter un lieu d'expédition » — la LECTURE existe
///     (`GET /api/inventory/owners/{id}/locations`), mais la CRÉATION est
///     réservée à l'administration (`MapAdminGroup`). L'étape renvoyait vers
///     `/locations`, écran neutralisé en VEN2, où le vendeur ne pouvait rien
///     faire. Une case à cocher qu'on ne peut pas cocher.
///
///   • « Publier une offre » — le module Products/Offers vit encore dans le
///     monolithe. `offersProvider` lève désormais, `valueOrNull` rendait une
///     liste vide, l'étape restait DÉFINITIVEMENT incomplète… et la checklist ne
///     disparaissait donc jamais, puisqu'elle ne s'efface qu'à 4/4.
///
/// Les deux étapes sont retirées, pas grisées : une étape barrée d'avance
/// pousserait le vendeur à chercher comment la franchir. Elles reviendront avec
/// leurs routes — les clés `l10n` correspondantes (`homeStepAddLocation*`,
/// `homeStepPublishOffer*`) sont conservées dans les fichiers ARB pour ça.
/// ═════════════════════════════════════════════════════════════════════════════
class StartupChecklist extends ConsumerWidget {
  const StartupChecklist({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    if (ref.watch(_checklistDismissedProvider)) return const SizedBox.shrink();

    final shop = ref.watch(shopProvider).valueOrNull;
    if (shop == null) return const SizedBox.shrink(); // on attend la boutique

    final products = ref.watch(productsProvider).valueOrNull ?? const [];

    final steps = <_Step>[
      // `kybStatus` VAUT « Verified », PAS « verified ». La comparaison est
      // faite en minuscules des deux côtés : l'énumération serveur est en
      // PascalCase (`NotStarted`, `InReview`, `Verified`, `Rejected`), et une
      // comparaison stricte n'aurait jamais coché cette étape.
      _Step(l.homeStepCompleteShopTitle, l.homeStepCompleteShopSubtitle,
          shop.kybStatus.toLowerCase() == 'verified' && shop.hasPayoutAccount, '/shop', Icons.storefront_outlined),
      // `/activities` ET NON `/products` : CETTE ROUTE N'EXISTE PLUS.
      //
      // `ProductsScreen` a été supprimée avec les trois autres écrans d'avant le
      // modèle multi-activités. Le catalogue se lit désormais SOUS une activité,
      // par le 3ᵉ onglet — qui aiguille vers `PartnerProductsScreen` quand
      // l'activité courante est une boutique.
      //
      // CE WIDGET N'EST IMPORTÉ PAR AUCUN FICHIER. Son commentaire de tête
      // affirme le contraire ; il a cessé d'être vrai sans que rien ne le
      // signale. Ce n'est donc pas du code exécuté aujourd'hui — mais le laisser
      // pointer sur une route effacée garantirait qu'il casse le jour où on le
      // rebranche, et pour une raison sans rapport avec ce qu'on cherchera
      // alors. Voir la tâche VEN9.
      _Step(l.homeStepCreateProductTitle, l.homeStepCreateProductSubtitle,
          products.isNotEmpty, '/activities', Icons.inventory_2_outlined),
    ];

    final done = steps.where((s) => s.done).length;
    if (done == steps.length) return const SizedBox.shrink(); // tout est fait

    final colors = AppColors.of(context);

    return Container(
      margin: const EdgeInsets.fromLTRB(16, 4, 16, 12),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: colors.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 14, 8, 6),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(l.homeChecklistTitle,
                          style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16, color: colors.ink)),
                      const SizedBox(height: 2),
                      Text(l.homeChecklistProgress(done, steps.length),
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                    ],
                  ),
                ),
                IconButton(
                  icon: Icon(Icons.close, size: 18, color: colors.subtle),
                  tooltip: l.homeChecklistDismiss,
                  onPressed: () => ref.read(_checklistDismissedProvider.notifier).dismiss(),
                ),
              ],
            ),
          ),
          ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: LinearProgressIndicator(
              value: done / steps.length,
              minHeight: 4,
              backgroundColor: colors.line,
              valueColor: const AlwaysStoppedAnimation(AppTheme.brandGreen),
            ),
          ),
          const SizedBox(height: 4),
          for (var i = 0; i < steps.length; i++) _StepTile(step: steps[i], colors: colors),
          const SizedBox(height: 6),
        ],
      ),
    );
  }
}

class _StepTile extends StatelessWidget {
  const _StepTile({required this.step, required this.colors});
  final _Step step;
  final AppColors colors;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      onTap: step.done ? null : () => context.push(step.route),
      leading: Icon(
        step.done ? Icons.check_circle : Icons.radio_button_unchecked,
        color: step.done ? AppTheme.brandGreen : colors.subtle,
      ),
      title: Text(
        step.title,
        style: TextStyle(
          fontWeight: FontWeight.w600,
          fontSize: 14,
          color: step.done ? colors.subtle : colors.ink,
          decoration: step.done ? TextDecoration.lineThrough : null,
        ),
      ),
      subtitle: Text(step.subtitle, style: TextStyle(fontSize: 12, color: colors.subtle)),
      trailing: step.done
          ? null
          : Icon(step.icon, color: AppTheme.brandGreen, size: 20),
      dense: true,
    );
  }
}
