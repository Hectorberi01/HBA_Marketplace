import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../auth/application/auth_controller.dart';
import '../account_data.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final me = ref.watch(meProvider);
    final colors = AppColors.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.profTitle)),
      body: me.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(meProvider)),
        data: (m) => ListView(
          padding: const EdgeInsets.only(bottom: 32),
          children: [
            const SizedBox(height: 16),
            CardSection(
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  KeyValueRow(label: l.profName, value: m.fullName),
                  KeyValueRow(label: l.profEmail, value: m.email),
                  KeyValueRow(label: l.profPhone, value: m.phoneNumber.isEmpty ? '—' : m.phoneNumber),

                  // LE NOM DE BOUTIQUE NE VIENT PAS DU COMPTE.
                  //
                  // `AccountMe` le portait, lu sur `GET /seller/account/me` du
                  // BFF du monolithe. `UserSummary` d'identity-service est un
                  // contrat de COMPTE : il ne sait rien des boutiques, et la
                  // valeur retombait toujours sur « Ma boutique ». La seule
                  // source est `GET /api/merchants/me` — d'où `sellerNameProvider`,
                  // qui suit en prime un renommage sans attendre une reconnexion.
                  KeyValueRow(
                    label: l.profShop,
                    value: ref.watch(sellerNameProvider).valueOrNull ?? '—',
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            CardSection(
              child: Column(
                children: [
                  SettingsTile(
                    icon: Icons.edit_outlined,
                    label: l.profEditInfo,
                    onTap: () => _editProfile(context, ref, m),
                  ),
                  Divider(height: 1, color: colors.line),
                  SettingsTile(
                    icon: Icons.lock_outline,
                    label: l.profChangePassword,
                    onTap: () => _changePassword(context, ref),
                  ),
                  Divider(height: 1, color: colors.line),
                  SettingsTile(
                    icon: Icons.shield_outlined,
                    label: l.profMfa,
                    subtitle: m.mfaEnabled ? l.profEnabled : l.profDisabled,
                    iconColor: m.mfaEnabled ? AppTheme.brandGreen : colors.subtle,
                    onTap: () => m.mfaEnabled ? _disableMfa(context, ref) : _enableMfa(context, ref),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _editProfile(BuildContext context, WidgetRef ref, AccountMe me) {
    final colors = AppColors.of(context);
    final first = TextEditingController(text: me.firstName);
    final last = TextEditingController(text: me.lastName);
    final phone = TextEditingController(text: me.phoneNumber);

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        return Padding(
        padding: sheetPadding(sheetContext),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.profMyInfo,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 18),
            TextField(controller: first, decoration: InputDecoration(labelText: l.profFirstName)),
            const SizedBox(height: 14),
            TextField(controller: last, decoration: InputDecoration(labelText: l.profName)),
            const SizedBox(height: 14),
            TextField(
              controller: phone,
              keyboardType: TextInputType.phone,
              decoration: InputDecoration(labelText: l.profPhone),
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: () async {
                Navigator.pop(sheetContext);
                try {
                  await ref.read(accountApiProvider).updateProfile(
                        firstName: first.text.trim(),
                        lastName: last.text.trim(),
                        phoneNumber: phone.text.trim(),
                      );
                  ref.invalidate(meProvider);
                  if (context.mounted) AppNotify.success(context, l.profUpdated);
                } catch (e) {
                  if (context.mounted) AppNotify.error(context, e.toString());
                }
              },
              child: Text(l.profSave),
            ),
          ],
        ),
        );
      },
    );
  }

  void _changePassword(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final current = TextEditingController();
    final next = TextEditingController();

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        return Padding(
        padding: sheetPadding(sheetContext),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.profChangePassword,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 18),
            TextField(
              controller: current,
              obscureText: true,
              decoration: InputDecoration(labelText: l.profCurrentPassword),
            ),
            const SizedBox(height: 14),
            TextField(
              controller: next,
              obscureText: true,
              decoration: InputDecoration(labelText: l.profNewPassword),
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: () async {
                if (next.text.length < 8) {
                  AppNotify.error(context, l.profPasswordMinLength);
                  return;
                }
                Navigator.pop(sheetContext);
                try {
                  await ref.read(accountApiProvider).changePassword(
                        currentPassword: current.text,
                        newPassword: next.text,
                      );
                  if (context.mounted) AppNotify.success(context, l.profPasswordChanged);
                } catch (e) {
                  if (context.mounted) AppNotify.error(context, e.toString());
                }
              },
              child: Text(l.profModify),
            ),
          ],
        ),
        );
      },
    );
  }

  /// Activation MFA : on récupère le secret TOTP, on l'affiche à copier dans une
  /// app d'authentification, puis on confirme avec un code.
  Future<void> _enableMfa(BuildContext context, WidgetRef ref) async {
    final MfaSetup setup;
    try {
      setup = await ref.read(accountApiProvider).mfaSetup();
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
      return;
    }
    if (!context.mounted) return;

    final colors = AppColors.of(context);
    final code = TextEditingController();
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        return Padding(
        padding: sheetPadding(sheetContext),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SheetHandle(),
              Text(l.profEnableMfa,
                  style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
              const SizedBox(height: 14),
              Text(
                l.profMfaStep1,
                style: TextStyle(fontSize: 13, color: colors.ink, height: 1.5),
              ),
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                decoration: BoxDecoration(
                  color: colors.softGreen,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: SelectableText(
                        setup.secret,
                        style: TextStyle(
                          fontFamily: 'monospace',
                          fontSize: 15,
                          fontWeight: FontWeight.w700,
                          letterSpacing: 1.5,
                          color: colors.ink,
                        ),
                      ),
                    ),
                    IconButton(
                      icon: const Icon(Icons.copy, size: 18, color: AppTheme.brandGreen),
                      onPressed: () {
                        Clipboard.setData(ClipboardData(text: setup.secret));
                        AppNotify.info(context, l.profKeyCopied);
                      },
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
              Text(l.profMfaStep3,
                  style: TextStyle(fontSize: 13, color: colors.ink, height: 1.5)),
              const SizedBox(height: 10),
              TextField(
                controller: code,
                keyboardType: TextInputType.number,
                maxLength: 6,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                decoration: const InputDecoration(counterText: '', hintText: '••••••'),
              ),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: () async {
                  if (code.text.trim().length < 6) {
                    AppNotify.error(context, l.profCodeRequired);
                    return;
                  }
                  Navigator.pop(sheetContext);
                  try {
                    await ref.read(accountApiProvider).mfaConfirm(code.text.trim());
                    ref.invalidate(meProvider);
                    if (context.mounted) AppNotify.success(context, l.profMfaEnabled);
                  } catch (e) {
                    if (context.mounted) AppNotify.error(context, e.toString());
                  }
                },
                child: Text(l.profEnable),
              ),
            ],
          ),
        ),
        );
      },
    );
  }

  /// Désactivation MFA : exige un code valide de l'app d'authentification.
  void _disableMfa(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final code = TextEditingController();
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        return Padding(
        padding: sheetPadding(sheetContext),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.profDisableMfa,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 8),
            Text(l.profDisableMfaBody,
                style: TextStyle(fontSize: 13, color: colors.subtle, height: 1.4)),
            const SizedBox(height: 16),
            TextField(
              controller: code,
              keyboardType: TextInputType.number,
              maxLength: 6,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              decoration: const InputDecoration(counterText: '', hintText: '••••••'),
            ),
            const SizedBox(height: 12),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
              onPressed: () async {
                if (code.text.trim().length < 6) {
                  AppNotify.error(context, l.profCodeRequired);
                  return;
                }
                Navigator.pop(sheetContext);
                try {
                  await ref.read(accountApiProvider).mfaDisable(code.text.trim());
                  ref.invalidate(meProvider);
                  if (context.mounted) AppNotify.success(context, l.profMfaDisabled);
                } catch (e) {
                  if (context.mounted) AppNotify.error(context, e.toString());
                }
              },
              child: Text(l.profDisable),
            ),
          ],
        ),
        );
      },
    );
  }
}
