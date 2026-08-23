import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../shop/shop_data.dart';
import '../wallet_data.dart';

class WalletScreen extends ConsumerWidget {
  const WalletScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final wallet = ref.watch(walletProvider);
    final withdrawals = ref.watch(withdrawalsProvider);
    final colors = AppColors.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.walTitle)),
      body: wallet.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(walletProvider)),
        data: (w) => RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(walletProvider);
            ref.invalidate(withdrawalsProvider);
          },
          child: ListView(
            padding: EdgeInsets.only(bottom: bottomSafePadding(context, extra: 32)),
            children: [
              _Balances(wallet: w),
              _WithdrawCard(wallet: w),
              SectionHeader(title: l.walWithdrawalHistory),
              withdrawals.when(
                loading: () => const LoadingView(),
                error: (e, _) => ErrorView(
                  message: e.toString(),
                  onRetry: () => ref.invalidate(withdrawalsProvider),
                ),
                data: (list) => list.isEmpty
                    ? Padding(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        child: EmptyView(
                          message: l.walNoWithdrawals,
                          icon: Icons.account_balance_outlined,
                        ),
                      )
                    : CardSection(
                        child: Column(
                          children: [
                            for (var i = 0; i < list.length; i++) ...[
                              if (i > 0) Divider(height: 1, color: colors.line),
                              _WithdrawalTile(withdrawal: list[i]),
                            ],
                          ],
                        ),
                      ),
              ),
              const _Ledger(),
            ],
          ),
        ),
      ),
    );
  }
}

/// Grand livre : d'où vient chaque franc. C'est la réponse à « pourquoi mon
/// solde a-t-il changé ? » — sans ça, le portefeuille reste une boîte noire.
class _Ledger extends ConsumerWidget {
  const _Ledger();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final transactions = ref.watch(walletTxProvider);
    final colors = AppColors.of(context);

    return transactions.when(
      loading: () => const SizedBox.shrink(),

      // ───────────────────────────────────────────────────────────────────────
      // UNE PANNE NE DOIT PAS RESSEMBLER À UN PORTEFEUILLE VIDE.
      //
      // La section disparaissait purement et simplement en cas d'erreur. Le
      // vendeur en concluait qu'il n'avait aucun mouvement — sur un écran
      // financier, c'est l'interprétation la plus naturelle et la plus fausse.
      //
      // On affiche donc l'erreur, avec un bouton pour réessayer.
      // ───────────────────────────────────────────────────────────────────────
      error: (e, __) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeader(title: l.walMovements),
          CardSection(
            child: ErrorView(
              message: e.toString(),
              onRetry: () => ref.invalidate(walletTxProvider),
            ),
          ),
        ],
      ),

      data: (list) {
        if (list.isEmpty) return const SizedBox.shrink();
        final recent = list.take(15).toList();

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SectionHeader(title: l.walMovements),
            CardSection(
              child: Column(
                children: [
                  for (var i = 0; i < recent.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: colors.line),
                    ListTile(
                      dense: true,
                      title: Text(recent[i].label,
                          style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                      subtitle: Text(Format.dateTime(recent[i].createdAt),
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                      trailing: Text(
                        // Le signe fait le sens : « + » entre, « − » sort.
                        '${recent[i].isCredit ? '+' : '−'} ${Format.money(recent[i].amount, recent[i].currency)}',
                        style: TextStyle(
                          fontWeight: FontWeight.w800,
                          fontSize: 13,
                          color: recent[i].isCredit ? AppTheme.brandGreen : AppTheme.danger,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ],
        );
      },
    );
  }
}

class _Balances extends StatelessWidget {
  const _Balances({required this.wallet});
  final Wallet wallet;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    return Column(
      children: [
        Container(
          margin: const EdgeInsets.fromLTRB(16, 16, 16, 12),
          padding: const EdgeInsets.all(20),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(18),
            gradient: const LinearGradient(
              colors: [AppTheme.brandGreenDark, AppTheme.brandGreen],
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(l.walAvailableBalance,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 13)),
              const SizedBox(height: 6),
              Text(Format.money(wallet.availableBalance, wallet.currency),
                  style: const TextStyle(color: Colors.white, fontSize: 30, fontWeight: FontWeight.w800)),
              const SizedBox(height: 6),
              Text(l.walPendingDelivery(Format.money(wallet.pendingBalance, wallet.currency)),
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 12)),
            ],
          ),
        ),

        // Les fonds déjà engagés : ni dans le solde, ni encore chez le vendeur.
        // Sans cette ligne, l'argent semblerait avoir disparu pendant le versement.
        if (wallet.pendingWithdrawal > 0)
          CardSection(
            padding: const EdgeInsets.all(14),
            child: Row(
              children: [
                Container(
                  width: 38,
                  height: 38,
                  decoration: BoxDecoration(
                    color: AppTheme.promoOrange.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: const Icon(Icons.sync, size: 18, color: AppTheme.promoOrange),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(l.walPendingWithdrawals,
                      style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14, color: colors.ink)),
                ),
                Text(Format.money(wallet.pendingWithdrawal, wallet.currency),
                    style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15)),
              ],
            ),
          ),
      ],
    );
  }
}

class _WithdrawCard extends ConsumerStatefulWidget {
  const _WithdrawCard({required this.wallet});
  final Wallet wallet;

  @override
  ConsumerState<_WithdrawCard> createState() => _WithdrawCardState();
}

class _WithdrawCardState extends ConsumerState<_WithdrawCard> {
  final _amount = TextEditingController();
  bool _busy = false;

  double get _value => double.tryParse(_amount.text.replaceAll(',', '.')) ?? 0;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  Future<void> _withdraw() async {
    final l = AppLocalizations.of(context);
    final amount = _value;
    if (amount <= 0 || amount > widget.wallet.availableBalance) {
      AppNotify.error(context, l.walInvalidAmount);
      return;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONFIRMATION AVANT D'ENGAGER DE L'ARGENT.
    //
    // Un seul appui suffisait à envoyer la demande : les fonds étaient retenus
    // immédiatement, et l'annulation dépend ensuite d'un administrateur. Une
    // faute de frappe sur le montant — un zéro de trop — n'avait aucun filet.
    //
    // Le récapitulatif reprend le montant FORMATÉ, tel qu'il sera débité. C'est
    // le seul moment où le vendeur voit ce qu'il engage, plutôt que ce qu'il a
    // tapé : « 150000 » saisi devient « 150 000 F CFA », et l'erreur saute aux
    // yeux là où la suite de chiffres ne disait rien.
    // ─────────────────────────────────────────────────────────────────────────
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        final dl = AppLocalizations.of(dialogContext);
        return AlertDialog(
          title: Text(dl.walRequestWithdrawal),
          content: Text(
            '${Format.money(amount, widget.wallet.currency)}\n\n'
            '${dl.walFundsHeldNotice}',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: Text(dl.commonCancel),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext, true),
              child: Text(dl.commonConfirm),
            ),
          ],
        );
      },
    );

    if (confirmed != true || !mounted) return;

    setState(() => _busy = true);
    try {
      // LA ROUTE PORTE LE `sellerId` DANS L'URL, ET LE VÉRIFIE.
      //
      // L'appel ne passait que le montant : vestige du BFF du monolithe, qui
      // déduisait le vendeur du jeton. financial-service sert
      // `POST /api/wallet/sellers/{sellerId}/withdrawals` et compare
      // l'identifiant au vendeur du jeton. L'identifiant vient donc du socle —
      // il est déjà résolu, puisque `walletProvider` en dépend pour afficher le
      // solde de cet écran.
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(walletApiProvider).requestWithdrawal(sellerId, amount);
      ref.invalidate(walletProvider);
      ref.invalidate(withdrawalsProvider);
      _amount.clear();
      if (mounted) {
        AppNotify.success(context, l.walRequestSaved);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    final shop = ref.watch(shopProvider).valueOrNull;

    // Sans compte de versement, la demande échouera côté serveur. Autant le dire
    // AVANT que le vendeur ne bloque ses fonds dans une demande impossible à payer.
    final missingAccount = shop != null && !shop.hasPayoutAccount;

    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: CardSection(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(l.walRequestWithdrawal,
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 12),

            if (missingAccount) ...[
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppTheme.promoOrange.withValues(alpha: 0.10),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(
                  l.walNoMobileMoneyAccount,
                  style: TextStyle(fontSize: 12, color: colors.ink, height: 1.4),
                ),
              ),
              const SizedBox(height: 12),
            ],

            TextField(
              controller: _amount,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              enabled: !missingAccount,
              decoration: InputDecoration(
                labelText: l.walAmount,
                suffixText: widget.wallet.currency,
                helperText: l.walAvailableAmount(
                    Format.money(widget.wallet.availableBalance, widget.wallet.currency)),
              ),
              onChanged: (_) => setState(() {}),
            ),
            const SizedBox(height: 14),
            FilledButton(
              onPressed: (_busy || missingAccount || _value <= 0 || _value > widget.wallet.availableBalance)
                  ? null
                  : _withdraw,
              child: _busy
                  ? const SizedBox(
                      width: 22,
                      height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                  : Text(l.walRequestWithdrawalButton),
            ),
            const SizedBox(height: 8),
            Text(
              l.walFundsHeldNotice,
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 11, color: colors.subtle, height: 1.4),
            ),
          ],
        ),
      ),
    );
  }
}

class _WithdrawalTile extends StatelessWidget {
  const _WithdrawalTile({required this.withdrawal});
  final Withdrawal withdrawal;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(Format.money(withdrawal.amount, withdrawal.currency),
                    style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15)),
              ),
              StatusPill.withdrawal(l, withdrawal.status),
            ],
          ),
          const SizedBox(height: 4),
          Text(Format.dateTime(withdrawal.createdAt),
              style: TextStyle(fontSize: 12, color: colors.subtle)),

          // « En cours » n'est ni un succès ni un échec : on explique où en est
          // l'argent, plutôt que de laisser le vendeur l'interpréter seul.
          if (withdrawal.isProcessing) ...[
            const SizedBox(height: 8),
            Text(
              l.walWithdrawalProcessing,
              style: const TextStyle(fontSize: 12, color: AppTheme.promoOrange, height: 1.4),
            ),
          ],

          if (withdrawal.isFailed && withdrawal.failureReason != null) ...[
            const SizedBox(height: 8),
            Text(withdrawal.failureReason!,
                style: const TextStyle(fontSize: 12, color: AppTheme.danger, height: 1.4)),
            const SizedBox(height: 4),
            Text(l.walFundsRecredited,
                style: TextStyle(fontSize: 12, color: colors.subtle)),
          ],
        ],
      ),
    );
  }
}
