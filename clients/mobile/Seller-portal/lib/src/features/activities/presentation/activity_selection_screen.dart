import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../activities_data.dart';
import '../selected_activity.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// SÉLECTION DE L'ACTIVITÉ — maquette HBA Partner.
///
/// Affiché après connexion UNIQUEMENT si le compte possède plus d'une activité.
///
/// CET ÉCRAN N'EST PAS UN MENU. C'EST UN AIGUILLAGE.
///
/// Il ne s'insère pas dans la barre du bas et n'a pas de retour : à ce moment-là,
/// le partenaire est connecté mais l'application ne sait pas encore quel métier
/// afficher. Boutique et restaurant n'ont ni les mêmes écrans, ni le même
/// vocabulaire — « à préparer » contre « en cuisine ». Choisir d'abord évite une
/// interface qui essaierait de servir les deux à la fois et n'en servirait aucun.
///
/// POURQUOI PAS DE `bottomNavigationBar` ICI.
///
/// La maquette l'écrit : « Pas de bottom nav ». La barre suppose un contexte
/// choisi — « Commandes » de quelle activité ? L'afficher avant le choix
/// proposerait des onglets qui ne peuvent rien montrer.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivitySelectionScreen extends ConsumerWidget {
  const ActivitySelectionScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final async = ref.watch(activitiesProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: async.when(
          loading: () => const LoadingView(),

          // 403 N'EST PAS UNE PANNE ICI, ET LE MESSAGE DOIT LE DIRE.
          //
          // La route exige le rôle `Seller`. Un compte qui vient de créer sa
          // boutique et dont le jeton n'a pas encore été repris tombe ici — de
          // même qu'un partenaire uniquement restaurateur. « Erreur serveur »
          // l'enverrait chercher une panne inexistante.
          error: (e, _) => ErrorView(
            message: e.toString(),
            onRetry: () => ref.invalidate(activitiesProvider),
          ),
          data: (result) {
            final activities = result.data;

            if (activities.isEmpty) {
              return const EmptyView(
                message: 'Aucune activité n\'est rattachée à ce compte.\n'
                    'Créez votre boutique pour commencer à vendre.',
                icon: Icons.storefront_outlined,
              );
            }

            return ListView(
              padding: const EdgeInsets.fromLTRB(20, 24, 20, 32),
              children: [
                // PLUS DE « BONJOUR <PRÉNOM> » : LE CONTRAT NE PORTE PAS DE
                //    PRÉNOM SUR CET ÉCRAN.
                //
                // Le salut affichait `PartnerMockData.greetingName`, c'est-à-dire
                // « Hector », à tout le monde. Le prénom se lit sur
                // `GET /api/identity/account/me` — un appel de plus sur le tout
                // premier écran après connexion, pour une politesse. On garde la
                // question, on retire le faux prénom.
                Text(
                  'Quelle activité souhaitez-vous gérer ?',
                  style: TextStyle(
                    fontSize: 26,
                    fontWeight: FontWeight.w800,
                    height: 1.2,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 24),

                // Rendu partiel : une partie des activités manque à l'appel.
                // Le taire présenterait une liste amputée comme complète — et un
                // partenaire qui ne voit pas son restaurant conclurait qu'il a
                // été supprimé.
                if (result.isPartial) ...[
                  for (final warning in result.warnings)
                    _PartialBanner(message: warning.message),
                  const SizedBox(height: 12),
                ],

                for (final activity in activities) ...[
                  _ActivityCard(activity: activity),
                  const SizedBox(height: 16),
                ],

                // CE BOUTON N'EST PAS UN « AUTRE » — C'EST UN REPLI ASSUMÉ.
                //
                // Un partenaire à trois activités ne veut pas toujours en choisir
                // une : il veut son chiffre consolidé. Sans cette sortie, il
                // devrait entrer dans une activité au hasard puis en ressortir.
                //
                // LA VUE CONSOLIDÉE N'A TOUJOURS PAS D'AMONT (module
                // `merchantConsolidated`) : `bffMerchant/activities` rend la
                // LISTE des activités, pas leurs totaux. Le bouton mène donc à un
                // écran qui l'annonce. On le garde parce que c'est la seule sortie
                // de cet aiguillage — et parce qu'un vendeur doit pouvoir
                // apprendre que le consolidé arrive, plutôt que de le chercher.
                OutlinedButton(
                  // On remet l'état à `null` : c'est CE QUI choisit la vue
                  // consolidée. Naviguer sans l'écrire afficherait le tableau de
                  // bord de la dernière activité consultée.
                  onPressed: () {
                    ref.read(selectedActivityIdProvider.notifier).choisir(null);
                    context.go('/home');
                  },
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                    backgroundColor: colors.surface,
                    side: BorderSide(color: colors.line),
                    foregroundColor: colors.ink,
                    textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
                  ),
                  child: const Text('Toutes mes activités'),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

/// Bandeau d'un rendu incomplet du BFF.
class _PartialBanner extends StatelessWidget {
  const _PartialBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(
        color: AppTheme.foodAmberSoft,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          const Icon(Icons.cloud_off_outlined, size: 18, color: AppTheme.foodAmber),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: const TextStyle(fontSize: 12.5, color: AppTheme.foodAmber),
            ),
          ),
        ],
      ),
    );
  }
}

class _ActivityCard extends ConsumerWidget {
  const _ActivityCard({required this.activity});

  final SellerActivity activity;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _InitialsAvatar(activity: activity),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      activity.name,
                      style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w800,
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(height: 6),
                    _UniverseBadge(universe: activity.universe),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),

          // ═══════════════════════════════════════════════════════════════════
          // LES DEUX TUILES CHIFFRÉES DE LA MAQUETTE ONT ÉTÉ RETIRÉES.
          //
          // Elles affichaient « 12 commandes aujourd'hui » et « 3 commandes à
          // préparer ». `MerchantActivityDto` ne porte QUE
          // `type / id / name / logoUrl / role / status / isOpenNow` : ni
          // compteur de commandes, ni file d'attente, ni chiffre d'affaires.
          //
          // Les obtenir demanderait un appel au tableau de bord PAR ACTIVITÉ,
          // c'est-à-dire N requêtes sur le tout premier écran après connexion,
          // avant même que le partenaire ait choisi quoi que ce soit.
          //
          // Les remplir de zéros était l'autre option, et c'est la pire : « 0
          // commande aujourd'hui » sur une boutique qui en a douze détourne le
          // partenaire de l'activité qu'il devait justement ouvrir.
          //
          // Le tableau de bord, lui, les donne — une fois l'activité choisie.
          // À rebrancher ici le jour où `bffMerchant/activities` portera un
          // résumé par activité.
          // ═══════════════════════════════════════════════════════════════════

          // Ce que l'amont sait dire : l'activité prend-elle des commandes ?
          // `null` signifie « le service n'a pas su répondre » — et ce n'est PAS
          // « fermé » : on n'affiche alors rien plutôt qu'un état inventé.
          if (activity.isOpenNow != null) ...[
            _OpenPill(isOpen: activity.isOpenNow!, universe: activity.universe),
            const SizedBox(height: 16),
          ],

          FilledButton(
            // ON FIXE L'ACTIVITÉ AVANT DE NAVIGUER.
            //
            // `/home` aiguille sur l'univers de l'activité COURANTE. Naviguer
            // sans avoir écrit l'état afficherait le tableau de bord de
            // l'activité précédente — celle qu'on vient justement de quitter.
            onPressed: () {
              ref.read(selectedActivityIdProvider.notifier).choisir(activity.id);
              context.go('/home');
            },
            style: FilledButton.styleFrom(
              minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
            ),
            // Le libellé nomme le MÉTIER, pas l'action générique : « Gérer la
            // boutique » / « Gérer le restaurant ». Un « Gérer » unique
            // économiserait une ligne de code et coûterait la seule indication
            // qui dit au partenaire où il va atterrir.
            child: Text(
              activity.universe == HbaUniverse.food
                  ? 'Gérer le restaurant'
                  : 'Gérer la boutique',
            ),
          ),
        ],
      ),
    );
  }
}

class _InitialsAvatar extends StatelessWidget {
  const _InitialsAvatar({required this.activity});

  final SellerActivity activity;

  @override
  Widget build(BuildContext context) => Container(
        width: 44,
        height: 44,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: activity.universe.soft,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Text(
          activity.initials,
          style: TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w800,
            color: activity.universe.accent,
          ),
        ),
      );
}

class _UniverseBadge extends StatelessWidget {
  const _UniverseBadge({required this.universe});

  final HbaUniverse universe;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
        decoration: BoxDecoration(
          color: universe.soft,
          borderRadius: BorderRadius.circular(6),
        ),
        child: Text(
          universe.badge,
          style: TextStyle(
            fontSize: 10,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.6,
            color: universe.accent,
          ),
        ),
      );
}

/// Pastille « ouvert / fermé ».
///
/// L'ACCORD SUIT LE MÉTIER : « Boutique ouverte », « Restaurant ouvert ».
/// Un « Ouvert » générique laisserait le partenaire se demander de quoi on parle
/// sur un écran qui liste les deux.
class _OpenPill extends StatelessWidget {
  const _OpenPill({required this.isOpen, required this.universe});

  final bool isOpen;
  final HbaUniverse universe;

  String get _label => switch ((universe, isOpen)) {
        (HbaUniverse.food, true) => 'Restaurant ouvert',
        (HbaUniverse.food, false) => 'Restaurant fermé',
        (HbaUniverse.express, true) => 'Boutique ouverte',
        (HbaUniverse.express, false) => 'Boutique fermée',
      };

  @override
  Widget build(BuildContext context) {
    final color = isOpen ? AppTheme.brandGreen : AppTheme.slate;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 8,
          height: 8,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 7),
        Text(
          _label,
          style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: color),
        ),
      ],
    );
  }
}
