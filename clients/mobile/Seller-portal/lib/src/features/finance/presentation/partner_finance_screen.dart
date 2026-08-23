import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../activities/selected_activity.dart';
import '../../wallet/wallet_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// FINANCES — LE PORTEFEUILLE, ET RIEN QUE LUI.
///
/// LE RELEVÉ DU JOUR A DISPARU. IL N'A AUCUN AMONT PUBLIC.
///
/// L'écran montrait cinq lignes — « Revenus bruts, Commissions HBA, Frais de
/// livraison, Remboursements, Net à percevoir » — toutes écrites dans le fichier
/// de maquette. financial-service SAIT les produire
/// (`/api/financial/settlements/sellers/{id}/statement`), mais AUCUNE entrée
/// `ReverseProxy:Routes` n'y mène : depuis un téléphone, ces chemins repartent
/// en 404. Et le contrat diffère de celui que l'écran attendait — il faudra
/// réécrire la projection en plus d'ouvrir la route (module `sellerStatement`,
/// cf. `features/finance/finance_data.dart`).
///
/// Ce qui reste est VRAI : les trois soldes du portefeuille. C'est moins que la
/// maquette, mais un vendeur qui vient voir « combien j'ai gagné » y trouve
/// l'essentiel. Ce qui manque est la DÉCOMPOSITION, et il doit l'apprendre
/// plutôt que lire des totaux fabriqués.
///
/// LE PORTEFEUILLE N'EST PAS VENTILÉ PAR ACTIVITÉ, ET IL NE PEUT PAS L'ÊTRE.
///
/// `GET /api/wallet/sellers/{sellerId}` est scopée par le compte MARCHAND :
/// il n'existe qu'un portefeuille par vendeur, quel que soit son nombre de
/// boutiques. Le « solde par activité » de la maquette — et la section « Par
/// activité » qui le déclinait — répartissait un total au jugé. Les deux sont
/// retirés, et l'écran affiche le même solde quelle que soit l'activité
/// choisie : la phrase sous le montant le dit, sans quoi le vendeur croirait
/// lire celui de sa boutique.
///
/// Un restaurant, lui, n'a de finances que via le VENDEUR de reversement qui lui
/// est rattaché (`payoutSellerId`) — encore le même portefeuille.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerFinanceScreen extends ConsumerWidget {
  const PartnerFinanceScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final activity = ref.watch(selectedActivityProvider);
    final async = ref.watch(walletProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(walletProvider),
          child: async.when(
            loading: () => const LoadingView(),

            // « PAS DE BOUTIQUE » PASSE PAR ICI : `walletProvider` traverse
            // `requiredSellerIdProvider`, qui lève une erreur NOMMÉE plutôt que
            // d'appeler `/api/wallet/sellers/null/…`.
            error: (e, _) => ErrorView(
              message: e.toString(),
              onRetry: () => ref.invalidate(walletProvider),
            ),
            data: (wallet) => ListView(
              padding: const EdgeInsets.fromLTRB(16, 10, 16, 24),
              children: [
                if (activity == null)
                  _GlobalHeader(colors: colors)
                else
                  PartnerScreenHeader(title: 'Finances', activity: activity),
                const SizedBox(height: 16),

                _BalanceCard(
                  amount: wallet.availableBalance,
                  currency: wallet.currency,
                  // Le même compte, quelle que soit l'activité affichée : le
                  // dire évite qu'il soit pris pour celui d'une seule boutique.
                  caption: 'Solde disponible · tout le compte',
                ),
                const SizedBox(height: 16),

                // L'ENTRÉE VERS LE RELEVÉ — sans elle, l'écran branché en #228b
                // serait inatteignable, exactement le défaut reproché à
                // `/analytics`.
                //
                // DEUX QUESTIONS DISTINCTES, DEUX ÉCRANS. Ici : « combien
                // j'ai », soldes à l'instant. Là-bas : « d'où ça vient », brut,
                // commission, frais et net sur une période. Les fondre donnerait un
                // écran où le vendeur ne trouve ni l'un ni l'autre.
                PartnerCard(
                  child: ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: const Icon(Icons.receipt_long_outlined, color: AppTheme.brandGreen),
                    title: const Text('Relevé détaillé',
                        style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14.5)),
                    subtitle: Text(
                      'Ventes, commission, frais et net, article par article.',
                      style: TextStyle(fontSize: 12.5, color: colors.subtle),
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push('/statement'),
                  ),
                ),
                const SizedBox(height: 24),

                const PartnerSectionTitle('Détail des soldes'),
                const SizedBox(height: 10),
                PartnerCard(
                  child: Column(
                    children: [
                      _AmountRow(
                        label: 'En attente de livraison',
                        hint: 'Encaissé, retirable une fois la commande livrée.',
                        amount: wallet.pendingBalance,
                        currency: wallet.currency,
                      ),
                      Divider(height: 1, color: colors.line),
                      _AmountRow(
                        label: 'Retrait en cours',
                        // NE PAS MASQUER CETTE LIGNE QUAND ELLE VAUT ZÉRO.
                        //
                        // Ces fonds ont quitté le solde disponible sans être
                        // encore versés. La ligne absente donnerait au vendeur
                        // l'impression que son argent s'est volatilisé entre sa
                        // demande et le virement.
                        hint: 'Demandé, en attente de validation ou de versement.',
                        amount: wallet.pendingWithdrawal,
                        currency: wallet.currency,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),

                OutlinedButton(
                  onPressed: () => context.push('/wallet'),
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                    backgroundColor: colors.surface,
                    side: BorderSide(color: colors.line),
                    foregroundColor: colors.ink,
                    textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
                  ),
                  child: const Text('Mouvements et retraits'),
                ),
                const SizedBox(height: 24),

                _MissingStatementNote(colors: colors),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _GlobalHeader extends StatelessWidget {
  const _GlobalHeader({required this.colors});

  final AppColors colors;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Finances',
            style: TextStyle(
              fontSize: 25,
              fontWeight: FontWeight.w800,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            'TOUT MON COMPTE',
            style: TextStyle(
              fontSize: 11,
              fontWeight: FontWeight.w800,
              letterSpacing: 0.7,
              color: colors.subtle,
            ),
          ),
        ],
      );
}

class _BalanceCard extends StatelessWidget {
  const _BalanceCard({
    required this.amount,
    required this.currency,
    required this.caption,
  });

  final double amount;
  final String currency;
  final String caption;

  @override
  Widget build(BuildContext context) => Container(
        width: double.infinity,
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(
          color: AppTheme.brandGreen,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              caption.toUpperCase(),
              style: const TextStyle(
                fontSize: 11,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.9,
                color: Color(0xFFBFE0D2),
              ),
            ),
            const SizedBox(height: 8),
            Row(
              crossAxisAlignment: CrossAxisAlignment.baseline,
              textBaseline: TextBaseline.alphabetic,
              children: [
                Text(
                  Format.amount(amount),
                  style: const TextStyle(
                    fontSize: 30,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(width: 7),
                Text(
                  // La devise vient du portefeuille, pas d'une constante :
                  // afficher « F CFA » sur un solde en une autre monnaie serait
                  // un faux montant.
                  currency == 'XOF' ? Format.cfa : currency,
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: Color(0xFFBFE0D2),
                  ),
                ),
              ],
            ),
          ],
        ),
      );
}

class _AmountRow extends StatelessWidget {
  const _AmountRow({
    required this.label,
    required this.hint,
    required this.amount,
    required this.currency,
  });

  final String label;
  final String hint;
  final double amount;
  final String currency;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(hint, style: TextStyle(fontSize: 11.5, color: colors.subtle)),
              ],
            ),
          ),
          const SizedBox(width: 10),
          Text(
            Format.money(amount, currency),
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w800, color: colors.ink),
          ),
        ],
      ),
    );
  }
}

/// Dit ce qui manque, à un vendeur — pas à un développeur.
///
/// CE N'EST PAS UN ÉTAT D'ERREUR, ET LE TON DOIT LE MONTRER.
///
/// Un bandeau rouge ferait chercher une panne. Ici rien n'est cassé : la
/// décomposition n'a simplement pas encore de chemin public.
class _MissingStatementNote extends StatelessWidget {
  const _MissingStatementNote({required this.colors});

  final AppColors colors;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(color: colors.line),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(Icons.hourglass_empty_rounded, size: 18, color: colors.subtle),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                'Le détail de vos revenus — ventes, commissions, frais de '
                'livraison, remboursements — arrive bientôt. En attendant, vos '
                'mouvements se lisent un par un dans « Mouvements et retraits ».',
                style: TextStyle(fontSize: 12.5, height: 1.45, color: colors.subtle),
              ),
            ),
          ],
        ),
      );
}
