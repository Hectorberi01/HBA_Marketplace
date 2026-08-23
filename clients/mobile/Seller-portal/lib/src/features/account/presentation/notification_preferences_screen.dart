import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/push/push_service.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../account_data.dart';

/// Réglage des notifications PUSH par catégorie. Couper une catégorie n'efface pas
/// la notification in-app correspondante (elle reste dans la boîte de réception) —
/// seul le push est silencié.
class NotificationPreferencesScreen extends ConsumerStatefulWidget {
  const NotificationPreferencesScreen({super.key});

  @override
  ConsumerState<NotificationPreferencesScreen> createState() => _NotificationPreferencesScreenState();
}

class _NotificationPreferencesScreenState extends ConsumerState<NotificationPreferencesScreen> {
  /// Libellés et descriptions par catégorie (clés alignées sur le backend).
  Map<String, (String, String, IconData)> _labels(AppLocalizations l) => {
        'orders': (l.notifprefOrdersTitle, l.notifprefOrdersDesc, Icons.shopping_bag_outlined),
        'returns': (l.notifprefReturnsTitle, l.notifprefReturnsDesc, Icons.assignment_return_outlined),
        'reviews': (l.notifprefReviewsTitle, l.notifprefReviewsDesc, Icons.star_border),
        'messages': (l.notifprefMessagesTitle, l.notifprefMessagesDesc, Icons.chat_bubble_outline),
        'account': (l.notifprefAccountTitle, l.notifprefAccountDesc, Icons.account_balance_wallet_outlined),
      };

  /// État local (clé → activé). Semé une fois depuis le serveur.
  Map<String, bool>? _state;
  bool _saving = false;

  /// Activation manuelle des push sur CET appareil en cours.
  bool _enabling = false;

  /// Active/réenregistre explicitement l'appareil, et informe précisément
  /// l'utilisateur du résultat (permission, jeton APNs, réseau).
  Future<void> _enableOnThisDevice() async {
    setState(() => _enabling = true);
    final outcome = await ref.read(pushServiceProvider).enableOnThisDevice();
    if (!mounted) return;
    setState(() => _enabling = false);

    final l = AppLocalizations.of(context);
    switch (outcome) {
      case PushEnableOutcome.registered:
        AppNotify.success(context, l.notifprefDeviceRegistered);
      case PushEnableOutcome.permissionDenied:
        AppNotify.error(context, l.notifprefDevicePermissionDenied);
      case PushEnableOutcome.apnsUnavailable:
      case PushEnableOutcome.noToken:
        AppNotify.error(context, l.notifprefDeviceApnsUnavailable);
      case PushEnableOutcome.error:
        AppNotify.error(context, l.notifprefDeviceError);
    }
  }

  Future<void> _toggle(String key, bool value) async {
    final previous = Map<String, bool>.from(_state!);
    setState(() {
      _state![key] = value;
      _saving = true;
    });

    final muted = _state!.entries.where((e) => !e.value).map((e) => e.key).toList();
    try {
      await ref.read(accountApiProvider).updateNotificationPreferences(muted);
    } catch (e) {
      // Échec : on revient à l'état précédent pour ne pas mentir sur le réglage.
      if (mounted) {
        setState(() => _state = previous);
        AppNotify.error(context, e.toString());
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final labels = _labels(l);
    final prefs = ref.watch(notificationPreferencesProvider);
    final colors = AppColors.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.notifprefTitle)),
      body: prefs.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(notificationPreferencesProvider),
        ),
        data: (list) {
          _state ??= {for (final p in list) p.key: p.enabled};

          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
            children: [
              // Activation/diagnostic de CET appareil : réenregistre le jeton push
              // quand l'enregistrement automatique au login a échoué (typiquement
              // iOS, jeton APNs pas encore prêt).
              Container(
                margin: const EdgeInsets.only(bottom: 16),
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: colors.surface,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: colors.line),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        const Icon(Icons.notifications_active_outlined,
                            color: AppTheme.brandGreen, size: 20),
                        const SizedBox(width: 8),
                        Text(l.notifprefDeviceTitle,
                            style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15)),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(l.notifprefDeviceHint,
                        style: TextStyle(fontSize: 12.5, color: colors.subtle, height: 1.4)),
                    const SizedBox(height: 12),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton.icon(
                        onPressed: _enabling ? null : _enableOnThisDevice,
                        icon: _enabling
                            ? const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                              )
                            : const Icon(Icons.phonelink_ring_outlined, size: 18),
                        label: Text(_enabling ? l.notifprefDeviceEnabling : l.notifprefDeviceButton),
                      ),
                    ),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                child: Text(
                  l.notifprefIntro,
                  style: TextStyle(fontSize: 13, color: colors.subtle, height: 1.4),
                ),
              ),
              Container(
                decoration: BoxDecoration(
                  color: colors.surface,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: colors.line),
                ),
                child: Column(
                  children: [
                    for (final entry in labels.entries)
                      if (_state!.containsKey(entry.key)) ...[
                        if (entry.key != labels.keys.first)
                          Divider(height: 1, color: colors.line),
                        SwitchListTile(
                          value: _state![entry.key]!,
                          onChanged: _saving ? null : (v) => _toggle(entry.key, v),
                          activeThumbColor: AppTheme.brandGreen,
                          secondary: CircleAvatar(
                            backgroundColor: colors.softGreen,
                            child: Icon(entry.value.$3, color: AppTheme.brandGreen, size: 20),
                          ),
                          title: Text(entry.value.$1,
                              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                          subtitle: Text(entry.value.$2,
                              style: TextStyle(fontSize: 12, color: colors.subtle)),
                        ),
                      ],
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}
