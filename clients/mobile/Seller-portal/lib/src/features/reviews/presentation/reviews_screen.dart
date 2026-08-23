import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../reviews_data.dart';

class ReviewsScreen extends ConsumerWidget {
  const ReviewsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final reviews = ref.watch(reviewsProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l.revTitle)),
      body: reviews.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(reviewsProvider)),
        data: (list) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(reviewsProvider),
          child: list.isEmpty
              ? EmptyView(message: l.revEmpty, icon: Icons.star_border)
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                  itemCount: list.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (_, i) => _ReviewCard(review: list[i]),
                ),
        ),
      ),
    );
  }
}

class _ReviewCard extends ConsumerWidget {
  const _ReviewCard({required this.review});
  final Review review;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              // « CLIENT » EST UN LIBELLÉ ASSUMÉ, PAS UN NOM MANQUANT.
              //
              // `ReviewSummary` ne rend que `BuyerId`, un GUID. Le modèle lisait
              // un champ `author` qui n'a jamais existé : la valeur retombait
              // TOUJOURS sur son défaut, pour tous les avis. Résoudre le nom
              // supposerait un appel à user-service PAR AVIS, sur des profils
              // qu'un vendeur n'a aucune raison de lire.
              //
              // « Achat vérifié », lui, est réel — et c'est ce qui distingue un
              // avis d'un commentaire.
              Expanded(
                child: Text(
                  review.isVerifiedPurchase ? 'Client · achat vérifié' : 'Client',
                  style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
                ),
              ),
              Row(
                children: [
                  for (var i = 1; i <= 5; i++)
                    Icon(
                      i <= review.rating ? Icons.star : Icons.star_border,
                      size: 15,
                      color: AppTheme.star,
                    ),
                ],
              ),
            ],
          ),

          // Le titre est distinct du corps dans le contrat ; l'ancien modèle le
          // perdait entièrement.
          if (review.title.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text(
              review.title,
              style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: colors.ink),
            ),
          ],
          const SizedBox(height: 6),
          Text(review.body, style: TextStyle(fontSize: 13, height: 1.4, color: colors.ink)),
          const SizedBox(height: 6),
          Row(
            children: [
              Text(Format.date(review.createdAt),
                  style: TextStyle(fontSize: 11, color: colors.subtle)),

              // LES AVIS SIGNALÉS ET REJETÉS ARRIVENT AVEC LES AUTRES.
              //
              // `ListReviewsBySellerQuery` ne filtre pas par statut. Les
              // confondre montrerait au vendeur, comme publics, des avis que les
              // acheteurs ne voient plus.
              if (review.isRejected || review.isFlagged) ...[
                const SizedBox(width: 8),
                Text(
                  review.isRejected ? 'Retiré par la modération' : 'En cours d\'arbitrage',
                  style: const TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    color: AppTheme.danger,
                  ),
                ),
              ],
            ],
          ),

          if (review.hasReply) ...[
            const SizedBox(height: 12),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(color: colors.softGreen, borderRadius: BorderRadius.circular(12)),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l.revYourReply,
                      style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w800, color: AppTheme.brandGreen)),
                  const SizedBox(height: 4),
                  Text(review.sellerReply!, style: const TextStyle(fontSize: 13, height: 1.4)),
                ],
              ),
            ),
          ] else ...[
            const SizedBox(height: 12),

            // ═══════════════════════════════════════════════════════════════
            // LE BOUTON « SIGNALER » A ÉTÉ RETIRÉ. C'EST UNE DÉCISION, PAS
            //    UN TRAVAIL À FINIR.
            //
            // `POST /api/reviews/{id}/flag` existe, mais sous
            // `MapAdminGroup` : `RequireRole("Admin", "Moderator")`. Un vendeur
            // reçoit 403, et il n'existe aucune route `/report`. Le raisonnement
            // du serveur est le bon — laisser la partie mise en cause déclencher
            // le retrait d'un avis négatif lui donnerait prise sur sa propre
            // réputation.
            //
            // Retiré, et non grisé : un bouton désactivé fait chercher la
            // condition qui le rouvrirait. Le jour où un canal de contestation
            // existera (support, messagerie), il ne passera pas par
            // engagement-service.
            // ═══════════════════════════════════════════════════════════════
            OutlinedButton.icon(
              onPressed: () => _openReply(context, ref),
              icon: const Icon(Icons.reply, size: 18),
              label: Text(l.revReply),
            ),
          ],
        ],
      ),
    );
  }

  void _openReply(BuildContext context, WidgetRef ref) {
    final controller = TextEditingController();

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        final colors = AppColors.of(sheetContext);
        return Padding(
        padding: sheetPadding(sheetContext),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.revReplyTitle,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(l.revReplyPublicNote,
                style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4)),
            const SizedBox(height: 16),
            TextField(
              controller: controller,
              maxLines: 4,
              autofocus: true,
              decoration: InputDecoration(hintText: l.revReplyHint),
            ),
            const SizedBox(height: 18),
            FilledButton(
              onPressed: () async {
                final body = controller.text.trim();
                if (body.isEmpty) return;
                Navigator.pop(sheetContext);
                try {
                  await ref.read(reviewsApiProvider).reply(review.id, body);
                  ref.invalidate(reviewsProvider);
                  if (context.mounted) AppNotify.success(context, l.revReplyPublished);
                } catch (e) {
                  if (context.mounted) AppNotify.error(context, e.toString());
                }
              },
              child: Text(l.revPublish),
            ),
          ],
        ),
        );
      },
    );
  }

  // `_flag` A ÉTÉ SUPPRIMÉE AVEC SON BOUTON. Les clés `l.revFlag*` restent
  // dans les fichiers ARB : elles serviront si un canal de contestation est
  // ouvert un jour, et les retirer maintenant obligerait à les réécrire.
}
