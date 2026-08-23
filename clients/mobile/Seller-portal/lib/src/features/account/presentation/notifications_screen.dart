import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../account_data.dart';

class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final notifications = ref.watch(notificationsProvider);

    // LE COMPTEUR EST UN `AsyncValue`, PAS UN `int`.
    //
    // Il vient de `GET /api/notifications/unread-count` — un appel réseau — et
    // non plus d'un filtrage de la liste locale, qui ne rendait que les 50
    // dernières et plafonnait le badge à 50. Tant qu'il n'a pas répondu, on
    // considère qu'il n'y a rien à marquer comme lu : proposer l'action puis la
    // voir échouer est pire que l'afficher une seconde plus tard.
    final unread = ref.watch(unreadNotificationsProvider).valueOrNull ?? 0;

    return Scaffold(
      appBar: AppBar(
        title: Text(l.notifTitle),
        actions: [
          IconButton(
            icon: const Icon(Icons.tune),
            tooltip: l.notifPreferences,
            onPressed: () => context.push('/notification-preferences'),
          ),
          if (unread > 0)
            TextButton(
              onPressed: () async {
                try {
                  await ref.read(accountApiProvider).markAllRead();
                  ref.invalidate(notificationsProvider);
                  // Le compteur est servi par une AUTRE route : sans cette
                  // seconde invalidation, le bouton « Tout marquer comme lu »
                  // reste affiché après avoir tout marqué comme lu.
                  ref.invalidate(unreadNotificationsProvider);
                } catch (e) {
                  if (context.mounted) AppNotify.error(context, e.toString());
                }
              },
              child: Text(l.notifMarkAllRead),
            ),
        ],
      ),
      body: notifications.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(notificationsProvider),
        ),
        data: (list) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(notificationsProvider),
          child: list.isEmpty
              ? EmptyView(message: l.notifEmpty, icon: Icons.notifications_none)
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                  itemCount: list.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (_, i) => _NotificationCard(notification: list[i]),
                ),
        ),
      ),
    );
  }
}

class _NotificationCard extends ConsumerWidget {
  const _NotificationCard({required this.notification});
  final SellerNotification notification;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final unread = !notification.isRead;
    final colors = AppColors.of(context);

    return InkWell(
      borderRadius: BorderRadius.circular(16),
      onTap: unread
          ? () async {
              try {
                await ref.read(accountApiProvider).markRead(notification.id);
                ref.invalidate(notificationsProvider);
              } catch (e) {
                if (context.mounted) AppNotify.error(context, e.toString());
              }
            }
          : null,
      child: CardSection(
        margin: EdgeInsets.zero,
        padding: const EdgeInsets.all(14),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Une pastille discrète suffit à distinguer le non-lu : pas besoin
            // de colorer toute la carte, ce qui rendrait la liste illisible.
            Container(
              width: 8,
              height: 8,
              margin: const EdgeInsets.only(top: 6, right: 10),
              decoration: BoxDecoration(
                color: unread ? AppTheme.brandGreen : Colors.transparent,
                shape: BoxShape.circle,
              ),
            ),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    notification.title,
                    style: TextStyle(
                      fontWeight: unread ? FontWeight.w800 : FontWeight.w600,
                      fontSize: 14,
                      color: colors.ink,
                    ),
                  ),
                  if (notification.message.isNotEmpty) ...[
                    const SizedBox(height: 4),
                    Text(notification.message,
                        style: TextStyle(fontSize: 13, color: colors.subtle, height: 1.4)),
                  ],
                  const SizedBox(height: 6),
                  Text(Format.age(l, notification.createdAt),
                      style: TextStyle(fontSize: 11, color: colors.subtle)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
