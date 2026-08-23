import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../finance_data.dart';

class FinanceScreen extends ConsumerWidget {
  const FinanceScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final statement = ref.watch(statementProvider);
    final range = ref.watch(statementRangeProvider);
    final colors = AppColors.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.finTitle)),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(statementProvider);
          ref.invalidate(payoutsProvider);
        },
        child: ListView(
          padding: const EdgeInsets.only(bottom: 32),
          children: [
            SizedBox(
              height: 56,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                children: [
                  for (final d in const [30, 90, 365])
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: FilterChipPill(
                        label: d == 365 ? l.finRange12Months : l.finRangeDays(d),
                        selected: range == d,
                        onTap: () => ref.read(statementRangeProvider.notifier).state = d,
                      ),
                    ),
                ],
              ),
            ),
            statement.when(
              loading: () => const LoadingView(),
              error: (e, _) => ErrorView(
                message: e.toString(),
                onRetry: () => ref.invalidate(statementProvider),
              ),
              data: (s) => Column(
                children: [
                  CardSection(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        KeyValueRow(label: l.finGrossSales, value: Format.money(s.grossSales)),
                        KeyValueRow(
                            label: l.finPlatformCommission,
                            value: '− ${Format.money(s.commission)}',
                            color: colors.subtle),
                        KeyValueRow(
                            label: l.finPaymentFees,
                            value: '− ${Format.money(s.providerFee)}',
                            color: colors.subtle),
                        // LA LIGNE « REMBOURSEMENTS » A DISPARU.
                        //
                        // Le relevé ne porte pas ce total : `EarningStatus.Reversed`
                        // n'est assigné nulle part (tâche #189), donc aucun gain
                        // n'est jamais marqué contre-passé. Afficher « 0 »
                        // affirmerait qu'il n'y a eu aucun remboursement, ce qu'on
                        // ne sait pas — et un vendeur qui a remboursé un client y
                        // lirait que la plateforme ne l'a pas répercuté.
                        Divider(height: 22, color: colors.line),
                        KeyValueRow(
                          // LE NET VIENT DU SERVEUR, il n'est plus soustrait ici.
                          // `grossSales - commission - providerFee` doit l'égaler ;
                          // le recalculer côté écran ferait exister deux vérités, et
                          // masquerait l'écart le jour où l'une des deux dérive.
                          label: l.finNetForYou,
                          value: Format.money(s.net, s.currency),
                          strong: true,
                          color: AppTheme.brandGreen,
                        ),
                      ],
                    ),
                  ),
                  SectionHeader(title: l.finDetail),
                  if (s.lines.isEmpty)
                    EmptyView(message: l.finNoMovements, icon: Icons.timeline)
                  else
                    CardSection(
                      child: Column(
                        children: [
                          for (var i = 0; i < s.lines.length; i++) ...[
                            if (i > 0) Divider(height: 1, color: colors.line),
                            _LineTile(line: s.lines[i]),
                          ],
                        ],
                      ),
                    ),
                ],
              ),
            ),
            const _Payouts(),
          ],
        ),
      ),
    );
  }
}

/// Une ligne du relevé : un article vendu, avec ce qui en a été retiré.
///
/// L'ANCIENNE VERSION AFFICHAIT UN MONTANT SIGNÉ ET COLORÉ selon son sens,
/// parce que le contrat rendait des écritures TYPÉES (vente, puis commission deux
/// lignes plus loin). Le relevé réel rend un GAIN par article : le brut et le net
/// sur la même ligne, l'écart en dessous. Le vendeur n'a plus à rapprocher.
class _LineTile extends StatelessWidget {
  const _LineTile({required this.line});
  final StatementLine line;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final retenu = line.commission + line.providerFee;

    return ListTile(
      dense: true,
      title: Text(
        'Commande ${line.orderShort}',
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
      ),
      subtitle: Text(
        // Le brut et ce qui a été retiré : c'est l'explication de l'écart, et la
        // seule question que le vendeur se pose devant un relevé.
        retenu > 0
            ? '${Format.date(line.date)} · ${Format.money(line.gross, line.currency)} − ${Format.money(retenu, line.currency)}'
            : Format.date(line.date),
        style: TextStyle(fontSize: 12, color: colors.subtle),
      ),
      trailing: Text(
        Format.money(line.net, line.currency),
        style: const TextStyle(
          fontWeight: FontWeight.w800,
          fontSize: 13,
          color: AppTheme.brandGreen,
        ),
      ),
    );
  }
}

class _Payouts extends ConsumerWidget {
  const _Payouts();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final payouts = ref.watch(payoutsProvider);
    final colors = AppColors.of(context);

    return payouts.when(
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
      data: (list) {
        if (list.isEmpty) return const SizedBox.shrink();

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SectionHeader(title: l.finPayouts),
            CardSection(
              child: Column(
                children: [
                  for (var i = 0; i < list.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: colors.line),
                    ListTile(
                      dense: true,
                      title: Text(Format.money(list[i].amount, list[i].currency),
                          style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                      // RÉFÉRENCE D'OPÉRATION, PAS NOM D'OPÉRATEUR. Le contrat
                      // ne rend pas le second, et l'ancien modèle affichait « — »
                      // en permanence. La référence est ce qu'un vendeur cite à son
                      // opérateur mobile money quand il ne retrouve pas son argent.
                      //
                      // PAS DE DATE TANT QUE CE N'EST PAS PAYÉ : `PayoutSummary`
                      // ne porte que `PaidAtUtc`. Un versement en cours n'a aucune
                      // date à montrer, et en inventer une ferait croire à un
                      // paiement effectué.
                      subtitle: Text(
                          [
                            if (list[i].paidAt case final d?) Format.date(d),
                            if (list[i].providerRef case final r?) 'réf. $r',
                          ].join(' · '),
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                      trailing: StatusPill.withdrawal(l, list[i].status),
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
