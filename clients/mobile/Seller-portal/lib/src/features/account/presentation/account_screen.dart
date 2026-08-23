import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../auth/application/auth_controller.dart';
import '../../shop/shop_data.dart';
import '../account_data.dart';

/// Onglet « Plus » : le compte, et l'accès à tout ce qui n'est pas quotidien.
class AccountScreen extends ConsumerWidget {
  const AccountScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    final me = ref.watch(meProvider);
    // `AsyncValue<int>`, PAS `int` : le compteur vient de
    // `GET /api/notifications/unread-count`. Pas de badge tant qu'il n'a pas
    // répondu — un « 0 » qui devient « 12 » une seconde plus tard fait douter
    // des deux.
    final unread = ref.watch(unreadNotificationsProvider).valueOrNull ?? 0;
    final shop = ref.watch(shopProvider).valueOrNull;

    return Scaffold(
      appBar: AppBar(title: Text(l.accTitle)),
      body: ListView(
        padding: const EdgeInsets.only(bottom: 32),
        children: [
          const SizedBox(height: 12),
          if (shop != null && shop.isRestricted) _RestrictedBanner(shop: shop),
          CardSection(
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                CircleAvatar(
                  radius: 26,
                  backgroundColor: colors.softGreen,
                  child: Text(
                    me.valueOrNull?.initials ?? '?',
                    style: const TextStyle(
                        color: AppTheme.brandGreen, fontWeight: FontWeight.w800, fontSize: 18),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(me.valueOrNull?.fullName ?? l.accLoading,
                          style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800)),
                      const SizedBox(height: 2),
                      Text(me.valueOrNull?.email ?? '',
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                    ],
                  ),
                ),
              ],
            ),
          ),

          SectionLabel(l.accSectionShop),
          CardSection(
            child: Column(
              children: [
                SettingsTile(
                  icon: Icons.storefront_outlined,
                  label: l.accShopLabel,
                  subtitle: l.accShopSubtitle,
                  onTap: () => context.push('/shop'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.sell_outlined,
                  label: l.accOffersLabel,
                  subtitle: l.accOffersSubtitle,
                  onTap: () => context.push('/offers'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.local_shipping_outlined,
                  label: l.accShipmentsLabel,
                  onTap: () => context.push('/shipments'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.location_on_outlined,
                  label: l.accLocationsLabel,
                  subtitle: l.accLocationsSubtitle,
                  onTap: () => context.push('/locations'),
                ),
                Divider(height: 1, color: colors.line),
                // LE BADGE « RETOURS EN ATTENTE » A ÉTÉ RETIRÉ, PAS MASQUÉ.
                //
                // Il comptait `pendingReturnsProvider`, alimenté par
                // `returns_data.dart` — supprimé avec l'écran Retours, dont le
                // module n'a jamais été extrait du monolithe. Un compteur rouge
                // est une injonction à agir : le laisser afficher un nombre
                // fabriqué enverrait le vendeur traiter des demandes qui
                // n'existent pas. Il reviendra avec le module.
                SettingsTile(
                  icon: Icons.assignment_return_outlined,
                  label: l.accReturnsLabel,
                  subtitle: l.accReturnsSubtitle,
                  onTap: () => context.push('/returns'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.star_border,
                  label: l.accReviewsLabel,
                  iconColor: AppTheme.star,
                  iconBackground: AppTheme.star.withValues(alpha: 0.14),
                  onTap: () => context.push('/reviews'),
                ),
                Divider(height: 1, color: colors.line),

                // ═══════════════════════════════════════════════════════════
                // LA MESSAGERIE N'AVAIT AUCUNE ENTRÉE DANS L'INTERFACE.
                //
                // `/messages` est déclarée dans la coquille et entièrement
                // câblée — `GET /api/notifications/messaging/conversations`
                // répond, le fil de discussion fonctionne, les pièces jointes
                // aussi. Mais elle ne figure dans aucun des cinq onglets
                // (`main_shell.dart`), et aucun bouton n'y menait.
                //
                // Le seul chemin d'accès était une NOTIFICATION PUSH de type
                // `message` (`push_service.dart`). Autrement dit : un vendeur
                // qui n'avait pas autorisé les notifications, ou qui avait
                // balayé l'alerte, ne pouvait plus jamais rouvrir la
                // conversation d'un client — sans qu'aucun écran ne le lui
                // dise.
                //
                // Ici plutôt qu'un sixième onglet : la barre est celle de la
                // maquette, à cinq colonnes, et « Messages » a été
                // délibérément fondu dans « Compte » lors du passage au modèle
                // multi-activités.
                // ═══════════════════════════════════════════════════════════
                SettingsTile(
                  icon: Icons.forum_outlined,
                  label: 'Messages',
                  subtitle: 'Vos conversations avec les clients',
                  onTap: () => context.push('/messages'),
                ),
              ],
            ),
          ),

          SectionLabel(l.accSectionMoney),
          CardSection(
            child: Column(
              children: [
                SettingsTile(
                  icon: Icons.account_balance_wallet_outlined,
                  label: l.accWalletLabel,
                  subtitle: l.accWalletSubtitle,
                  onTap: () => context.push('/wallet'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.insert_chart_outlined,
                  label: l.accFinanceLabel,
                  subtitle: l.accFinanceSubtitle,
                  onTap: () => context.push('/finance'),
                ),
              ],
            ),
          ),

          SectionLabel(l.accSectionAccount),
          CardSection(
            child: Column(
              children: [
                SettingsTile(
                  icon: Icons.notifications_none,
                  label: l.accNotificationsLabel,
                  badge: unread > 0 ? '$unread' : null,
                  onTap: () => context.push('/notifications'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.person_outline,
                  label: l.accProfileLabel,
                  onTap: () => context.push('/profile'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.palette_outlined,
                  label: l.accAppearanceLabel,
                  subtitle: l.accAppearanceSubtitle,
                  onTap: () => context.push('/settings'),
                ),
                Divider(height: 1, color: colors.line),
                // Exigée par l'App Store et le Play Store : une politique de
                // confidentialité doit être accessible DEPUIS l'application, pas
                // seulement au moment de l'inscription.
                SettingsTile(
                  icon: Icons.lock_outline,
                  label: l.accPrivacyLabel,
                  subtitle: l.accPrivacySubtitle,
                  onTap: () => context.push('/privacy'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.help_outline,
                  label: l.accHelpLabel,
                  subtitle: l.accHelpSubtitle,
                  onTap: () => context.push('/help'),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.gavel_outlined,
                  label: l.accTermsLabel,
                  subtitle: l.accTermsSubtitle,
                  onTap: () => context.push('/terms'),
                ),
                Divider(height: 1, color: colors.line),
                // « Fermer mon compte » n'a de sens que si le compte est encore actif :
                // masqué en mode restreint (où la bannière propose la réactivation).
                if (shop != null && !shop.isRestricted) ...[
                  SettingsTile(
                    icon: Icons.no_accounts_outlined,
                    label: l.accCloseLabel,
                    subtitle: l.accCloseSubtitle,
                    iconColor: AppTheme.promoOrange,
                    iconBackground: AppTheme.promoOrange.withValues(alpha: 0.14),
                    onTap: () => _confirmClose(context, ref),
                  ),
                  Divider(height: 1, color: colors.line),
                ],
                // Suppression DÉFINITIVE en libre-service (exigence App Store 5.1.1(v)) :
                // toujours accessible, y compris pour un compte déjà fermé.
                SettingsTile(
                  icon: Icons.delete_forever_outlined,
                  label: l.accDeleteLabel,
                  subtitle: l.accDeleteSubtitle,
                  iconColor: AppTheme.danger,
                  iconBackground: AppTheme.danger.withValues(alpha: 0.12),
                  onTap: () => _confirmDelete(context, ref),
                ),
                Divider(height: 1, color: colors.line),
                SettingsTile(
                  icon: Icons.logout,
                  label: l.accLogout,
                  iconColor: AppTheme.danger,
                  iconBackground: AppTheme.danger.withValues(alpha: 0.12),
                  onTap: () => _logout(context, ref),
                  trailing: const SizedBox.shrink(),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _logout(BuildContext context, WidgetRef ref) async {
    final l = AppLocalizations.of(context);
    // Le `builder` reçoit le contexte DE LA BOÎTE DE DIALOGUE (`dialogContext`),
    // et c'est LUI qu'il faut dépiler. Ignorer ce paramètre pour réutiliser le
    // `context` de l'écran — ce que faisait ce code — fait remonter le `pop`
    // jusqu'au Navigator du routeur : on ne fermait pas la boîte, on dépilait la
    // dernière PAGE. Plus aucune page à afficher, écran noir.
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(l.accLogoutConfirmTitle),
        content: Text(l.accLogoutConfirmBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: Text(l.accCancel),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: Text(l.accLogout),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    // `logout()` ne lève plus : elle purge et bascule l'état quoi qu'il arrive.
    // Le routeur, qui écoute la session, redirige seul vers la connexion.
    await ref.read(authControllerProvider.notifier).logout();
  }

  Future<void> _confirmClose(BuildContext context, WidgetRef ref) async {
    final l = AppLocalizations.of(context);
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(l.accCloseConfirmTitle),
        content: Text(l.accCloseConfirmBody),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: Text(l.accCancel)),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: Text(l.accCloseConfirmAction),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    try {
      // ═══════════════════════════════════════════════════════════════════════
      // « FERMER MON COMPTE » FERME LA BOUTIQUE. `closeAccount()` N'EXISTE
      //    PLUS, ET CE N'ÉTAIT PAS UN RENOMMAGE.
      //
      // L'appel visait `POST /seller/account/me/close`, une route du BFF du
      // monolithe qui mêlait identité et boutique. Le geste réel vit dans
      // merchant-service : `POST /api/merchants/{sellerId}/close` retire les
      // produits de la vente et LAISSE le compte ouvert — ce que dit déjà le
      // libellé de la tuile (« Suspend la vente — réversible »). La suppression
      // définitive, elle, est ailleurs (`_confirmDelete`) et n'a pas d'amont.
      //
      // L'identifiant vient du socle : la route le compare au vendeur du jeton,
      // et un `sellerId` fabriqué par l'écran ne fuit rien — il produit un 403
      // que le vendeur légitime ne comprend pas.
      // ═══════════════════════════════════════════════════════════════════════
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(accountApiProvider).closeShop(sellerId);
      ref.invalidate(shopProvider);
      if (context.mounted) {
        AppNotify.success(context, l.accCloseSuccess);
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  /// Suppression DÉFINITIVE : ouvre une feuille qui exige le mot de passe, effectue
  /// la suppression, puis déconnecte (le routeur renvoie alors vers la connexion).
  Future<void> _confirmDelete(BuildContext context, WidgetRef ref) async {
    final l = AppLocalizations.of(context);
    final done = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => const _DeleteAccountSheet(),
    );

    if (done == true && context.mounted) {
      // Compte anonymisé côté serveur : on purge la session locale. Le routeur,
      // qui écoute la session, redirige seul vers la connexion.
      await ref.read(authControllerProvider.notifier).logout();
      if (context.mounted) {
        AppNotify.success(context, l.accDeleteSuccess);
      }
    }
  }
}

/// Feuille de suppression DÉFINITIVE. Deux frictions volontaires : l'utilisateur
/// doit cocher qu'il comprend l'irréversibilité, ET saisir son mot de passe. Apple
/// exige que la suppression soit possible ; le bon sens exige qu'elle soit voulue.
class _DeleteAccountSheet extends ConsumerStatefulWidget {
  const _DeleteAccountSheet();

  @override
  ConsumerState<_DeleteAccountSheet> createState() => _DeleteAccountSheetState();
}

class _DeleteAccountSheetState extends ConsumerState<_DeleteAccountSheet> {
  final _password = TextEditingController();
  bool _understood = false;
  bool _obscure = true;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_understood || _password.text.isEmpty || _busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(accountApiProvider).deleteAccount(_password.text);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.toString();
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    final canSubmit = _understood && _password.text.isNotEmpty && !_busy;

    return SafeArea(
      top: false,
      child: Padding(
        // `SafeArea` juste au-dessus gère déjà la barre système : ne pas la compter deux fois.
        padding: sheetPadding(context, top: 14, bottom: 20, systemInset: false),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Row(
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: BoxDecoration(
                    color: AppTheme.danger.withValues(alpha: 0.12),
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(Icons.delete_forever_outlined, color: AppTheme.danger),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(l.accDeleteSheetTitle,
                      style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
                ),
              ],
            ),
            const SizedBox(height: 14),
            Text(
              l.accDeleteSheetBody,
              style: TextStyle(fontSize: 13, height: 1.45, color: colors.subtle),
            ),
            const SizedBox(height: 16),
            InkWell(
              borderRadius: BorderRadius.circular(10),
              onTap: () => setState(() => _understood = !_understood),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 4),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Checkbox(
                      value: _understood,
                      onChanged: (v) => setState(() => _understood = v ?? false),
                      activeColor: AppTheme.danger,
                      materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.only(top: 10),
                        child: Text(l.accDeleteSheetUnderstand,
                            style: TextStyle(fontSize: 13, color: colors.ink)),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _password,
              obscureText: _obscure,
              enabled: !_busy,
              onChanged: (_) => setState(() {}),
              decoration: InputDecoration(
                labelText: l.accPasswordLabel,
                hintText: l.accPasswordHint,
                suffixIcon: IconButton(
                  icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined,
                      color: colors.subtle),
                  onPressed: () => setState(() => _obscure = !_obscure),
                ),
              ),
              onSubmitted: (_) => _submit(),
            ),
            if (_error != null) ...[
              const SizedBox(height: 10),
              Text(_error!,
                  style: const TextStyle(fontSize: 12.5, color: AppTheme.danger, height: 1.4)),
            ],
            const SizedBox(height: 18),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: _busy ? null : () => Navigator.pop(context, false),
                    child: Text(l.accCancel),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  flex: 2,
                  child: FilledButton(
                    style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
                    onPressed: canSubmit ? _submit : null,
                    child: _busy
                        ? const SizedBox(
                            width: 22,
                            height: 22,
                            child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white),
                          )
                        : Text(l.accDeleteSheetAction),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Bannière affichée quand le compte est en mode restreint (fermé ou en attente de
/// réactivation). Propose au vendeur de demander la réactivation, ou lui indique que
/// sa demande est en cours d'examen par un administrateur.
class _RestrictedBanner extends ConsumerWidget {
  const _RestrictedBanner({required this.shop});
  final Shop shop;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    final requested = shop.isReactivationRequested;

    return Container(
      margin: const EdgeInsets.fromLTRB(16, 4, 16, 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.star.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.star.withValues(alpha: 0.4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.pause_circle_outline, color: AppTheme.star),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  requested ? l.accReactivationPendingTitle : l.accClosedTitle,
                  style: TextStyle(fontWeight: FontWeight.w800, fontSize: 15, color: colors.ink),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            requested ? l.accReactivationPendingBody : l.accClosedBody,
            style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
          ),
          if (!requested) ...[
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: () => _requestReactivation(context, ref),
                child: Text(l.accRequestReactivation),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Future<void> _requestReactivation(BuildContext context, WidgetRef ref) async {
    final l = AppLocalizations.of(context);
    try {
      // Même garde de propriété que la fermeture : la route porte l'identifiant
      // (`POST /api/merchants/{sellerId}/reactivation`) et le compare au vendeur
      // du jeton. `shop.id` serait la même valeur, mais la faire transiter par
      // l'écran rouvrirait la porte que le socle a fermée.
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(accountApiProvider).requestReactivation(sellerId);
      ref.invalidate(shopProvider);
      if (context.mounted) {
        AppNotify.success(context, l.accReactivationSent);
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}
