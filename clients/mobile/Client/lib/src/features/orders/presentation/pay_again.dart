import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../checkout/checkout_data.dart';
import '../../checkout/presentation/payment_webview_screen.dart';
import '../orders_data.dart';

/// ─────────────────────────────────────────────────────────────────────────────
/// REPRENDRE LE PAIEMENT D'UNE COMMANDE RESTÉE IMPAYÉE.
///
/// POURQUOI CET ÉCRAN EXISTE
///
/// Le paiement peut échouer pour des raisons parfaitement banales, et au Bénin
/// elles sont fréquentes : solde Mobile Money insuffisant, mauvais numéro saisi,
/// code de confirmation expiré, réseau opérateur qui lâche au milieu. Jusqu'ici,
/// la commande restait alors définitivement impayable : `/checkout/pay` part du
/// PANIER, or le panier a été vidé à la création de la commande. Le seul recours
/// était d'écrire au support — pour un problème que l'acheteur pouvait résoudre
/// lui-même en trente secondes.
///
/// POURQUOI ON NE REDEMANDE PAS LE MOYEN DE PAIEMENT ICI
///
/// L'application n'a pas de sélecteur de moyen de paiement : elle ouvre la page
/// hébergée FedaPay, où l'acheteur choisit MTN, Moov ou la carte, et saisit son
/// numéro. Reproduire ce choix dans l'application ajouterait un écran de plus
/// avant celui qui décide vraiment — et deux endroits où se tromper. Réessayer
/// rouvre donc cette page, et le numéro y est ressaisi : c'est précisément ce
/// qu'il faut, la cause la plus courante d'un échec étant un mauvais numéro.
///
/// CE QUI GARANTIT L'ABSENCE DE DOUBLE PAIEMENT
///
/// Ce n'est pas cet écran, c'est le serveur. Avant d'ouvrir une nouvelle session,
/// il interroge FedaPay sur le sort de la précédente. Si elle a abouti, la reprise
/// est refusée. On ne se fie donc pas au statut affiché ici, qui peut avoir
/// plusieurs minutes de retard.
/// ─────────────────────────────────────────────────────────────────────────────
Future<void> payOrderAgain(BuildContext context, WidgetRef ref, OrderItem order) async {
  final confirmed = await showModalBottomSheet<bool>(
    context: context,
    useSafeArea: true,
    isScrollControlled: true,
    backgroundColor: AppTheme.surface,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
    ),
    builder: (sheetCtx) => SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Reprendre le paiement',
                style: TextStyle(fontWeight: FontWeight.w800, fontSize: 18)),
            const SizedBox(height: 6),
            Text(
              'Vous allez régler la commande ${order.reference}.',
              style: TextStyle(color: AppTheme.subtle, fontSize: 13),
            ),
            const SizedBox(height: 16),

            // Le montant est RAPPELÉ, et il est identique au premier essai :
            // articles, adresse et livraison sont figés sur la commande. Le dire
            // évite la crainte, légitime, de payer deux fois ou de payer plus.
            Container(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: AppTheme.softGreen,
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text('Montant à payer', style: TextStyle(fontWeight: FontWeight.w600)),
                  Text(
                    Format.money(order.total, order.currency),
                    style: const TextStyle(
                        fontWeight: FontWeight.w800, fontSize: 17, color: AppTheme.brandGreen),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 14),
            Text(
              'La page de paiement sécurisée va s\'ouvrir. Choisissez MTN Mobile Money, '
              'Moov Money ou votre carte, puis saisissez votre numéro.',
              style: TextStyle(color: AppTheme.subtle, fontSize: 13, height: 1.45),
            ),
            const SizedBox(height: 18),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                style: FilledButton.styleFrom(minimumSize: const Size(0, 50)),
                onPressed: () => Navigator.pop(sheetCtx, true),
                child: const Text('Payer maintenant'),
              ),
            ),
            const SizedBox(height: 8),
            SizedBox(
              width: double.infinity,
              child: TextButton(
                onPressed: () => Navigator.pop(sheetCtx, false),
                child: Text('Plus tard', style: TextStyle(color: AppTheme.subtle)),
              ),
            ),
          ],
        ),
      ),
    ),
  );

  if (confirmed != true || !context.mounted) return;

  final api = ref.read(checkoutApiProvider);

  // Indicateur bloquant : l'ouverture d'une session FedaPay prend quelques
  // secondes sur un réseau mobile, et sans retour visuel l'acheteur réappuie.
  //
  // Il est fermé dans un bloc À PART, avant la suite. Le fermer dans le `catch`
  // d'un try englobant l'ensemble ferait dépiler une route de trop si l'erreur
  // survenait APRÈS la fermeture — l'acheteur se retrouverait éjecté de sa
  // commande au moment précis où il a besoin de la voir.
  showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (_) => const Center(child: CircularProgressIndicator()),
  );

  final PaymentResult result;
  try {
    result = await api.payOrder(
      orderId: order.id,
      method: PayMethod.fedapay,
      returnUrl: 'marketplace://payment/return',
      cancelUrl: 'marketplace://payment/cancel',
    );
  } catch (e) {
    if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
    if (context.mounted) AppNotify.error(context, e.toString());
    return;
  }

  if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
  if (!context.mounted) return;

  try {
    if (result.requiresAction && (result.redirectUrl?.isNotEmpty ?? false)) {
      // La WebView se ferme d'elle-même dès que le SERVEUR voit le paiement
      // terminé. On ne se fie jamais à la page affichée : FedaPay peut montrer
      // « merci » avant que la transaction soit réellement encaissée.
      await openPaymentWebView(
        context,
        result.redirectUrl!,
        onPoll: result.paymentId.isEmpty
            ? null
            : () async {
                try {
                  return isTerminalPaymentStatus(await api.status(result.paymentId));
                } catch (_) {
                  return false; // une coupure réseau ne doit pas fermer la page
                }
              },
      );

      if (result.paymentId.isNotEmpty) {
        await api.confirmRedirect(result.paymentId);
      }
    }

    // On attend le statut définitif AVANT de relire la commande. Invalider
    // d'abord ferait recharger une commande encore « en attente de paiement »,
    // et l'écran afficherait l'ancien état sous un message de succès.
    final status = result.paymentId.isEmpty ? '' : await _finalStatus(api, result.paymentId);

    // C'est le serveur qui dit si la commande est payée, pas le chemin qu'a pris
    // l'acheteur dans la page hébergée.
    ref.invalidate(orderDetailProvider(order.id));

    if (!context.mounted) return;

    if (isSuccessfulPaymentStatus(status)) {
      AppNotify.success(context, 'Paiement reçu. Votre commande est confirmée.');
    } else if (status.isEmpty) {
      AppNotify.info(context, 'Paiement en cours de vérification. Actualisez dans un instant.');
    } else {
      AppNotify.error(context, 'Le paiement n\'a pas abouti. Vous pouvez réessayer.');
    }
  } catch (e) {
    // Plus aucun dialogue à fermer ici : la session a été ouverte, l'échec porte
    // sur la suite (WebView, sondage). On informe, et la commande reste payable.
    if (context.mounted) AppNotify.error(context, e.toString());
  }
}

/// Interroge le statut jusqu'à ce qu'il soit définitif, sans dépasser ~12 s.
///
/// Le webhook FedaPay arrive en général en une seconde ou deux, mais rien ne le
/// garantit. On borne l'attente : au-delà, mieux vaut rendre la main avec un
/// message honnête que de bloquer l'écran sur une réponse qui viendra peut-être.
Future<String> _finalStatus(CheckoutApi api, String paymentId) async {
  var status = '';
  for (var i = 0; i < 6; i++) {
    try {
      status = await api.status(paymentId);
    } catch (_) {
      return status;
    }
    if (isTerminalPaymentStatus(status)) return status;
    await Future<void>.delayed(const Duration(seconds: 2));
  }
  return status;
}
