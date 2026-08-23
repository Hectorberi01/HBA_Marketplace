import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Ligne du relevé (une vente, une commission, un remboursement…).
/// Une ligne du relevé : UN GAIN, c'est-à-dire un article vendu.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// UNE LIGNE PORTE LES QUATRE MONTANTS, ELLE N'EST PAS TYPÉE.
///
/// L'ancien modèle avait un champ `type` — sale | commission | refund | payout —
/// hérité du BFF du monolithe, qui rendait une écriture par nature. Le relevé de
/// financial-service est bâti sur `SellerEarning` : une ligne par ARTICLE VENDU,
/// portant son brut, sa commission, ses frais et son net.
///
/// C'est plus utile ainsi : le vendeur voit sur une même ligne ce qu'a rapporté
/// l'article et ce qui en a été retiré. Avec des écritures typées, il devait
/// rapprocher lui-même une vente et sa commission, deux lignes plus loin.
/// ═══════════════════════════════════════════════════════════════════════════════
class StatementLine {
  StatementLine({
    required this.earningId,
    required this.orderId,
    required this.date,
    required this.gross,
    required this.commission,
    required this.providerFee,
    required this.net,
    required this.currency,
    required this.status,
  });

  final String earningId;
  final String orderId;
  final DateTime? date;
  final double gross;
  final double commission;
  final double providerFee;
  final double net;
  final String currency;

  /// Accrued | Released | Settled — l'état du gain.
  ///
  /// `Reversed` EXISTE DANS L'ÉNUMÉRATION ET N'EST ASSIGNÉ NULLE PART
  /// (tâche #189). Un gain contre-passé ne se distingue donc pas encore d'un gain
  /// normal. On affiche l'état tel qu'il vient plutôt que de le traduire, pour ne
  /// pas donner un nom rassurant à une valeur qui n'arrive jamais.
  final String status;

  /// Les six derniers caractères de la commande — ce que le vendeur peut
  /// rapprocher de sa liste de commandes sans lire un GUID entier.
  String get orderShort =>
      orderId.length <= 6 ? orderId : orderId.substring(orderId.length - 6).toUpperCase();

  factory StatementLine.fromJson(Map d) => StatementLine(
        earningId: Json.str(d['earningId']),
        orderId: Json.str(d['orderId']),
        date: Json.asDate(d['createdAtUtc']),
        gross: Json.asDouble(d['grossAmount']),
        commission: Json.asDouble(d['commissionAmount']),
        providerFee: Json.asDouble(d['providerFeeAmount']),
        net: Json.asDouble(d['netAmount']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        status: Json.str(d['status']),
      );
}

/// Relevé d'un vendeur sur une période.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// `refunds` A DISPARU DE CE MODÈLE, ET C'EST DÉLIBÉRÉ.
///
/// Le relevé de financial-service ne porte pas de total de remboursements. Il
/// pourrait : `EarningStatus` a une valeur `Reversed`. Mais elle N'EST ASSIGNÉE
/// NULLE PART dans le code (tâche #189) — aucun gain n'est jamais marqué comme
/// contre-passé.
///
/// Afficher « remboursements : 0 » affirmerait donc qu'il n'y en a eu aucun, ce
/// qu'on ne sait pas. Un vendeur qui a remboursé un client et lit ce zéro conclut
/// que la plateforme ne l'a pas répercuté — et ouvre une réclamation. Le champ
/// revient quand `Reversed` sera assigné, pas avant.
///
/// `grossSales - commission - providerFee == net`, ET C'EST VÉRIFIABLE.
///
/// Le serveur rendait le brut, la commission et le net, mais PAS les frais : le
/// résumé ne s'équilibrait pas et rien n'expliquait l'écart. `ProviderFees` a été
/// ajouté à l'agrégation (`SellerStatement`), où il était déjà présent sur chaque
/// gain.
/// ═══════════════════════════════════════════════════════════════════════════════
class Statement {
  Statement({
    required this.from,
    required this.to,
    required this.grossSales,
    required this.commission,
    required this.providerFee,
    required this.net,
    required this.currency,
    required this.lineCount,
    required this.lines,
  });

  final DateTime? from;
  final DateTime? to;
  final double grossSales;
  final double commission;
  final double providerFee;

  /// Ce que le vendeur perçoit réellement.
  final double net;
  final String currency;

  /// Nombre de gains sur la période, rendu par le serveur.
  ///
  /// IL PEUT DÉPASSER `lines.length` : le résumé et les lignes sont DEUX
  /// requêtes. Si la seconde échoue, on garde le résumé — des totaux justes sans
  /// détail valent mieux qu'un écran en erreur — et l'écart se voit.
  final int lineCount;

  final List<StatementLine> lines;

  /// LE RÉSUMÉ ET LES LIGNES VIENNENT DE DEUX ROUTES. Cette fabrique assemble
  /// les deux réponses ; elle ne lit pas un seul document.
  factory Statement.assemble({
    required Map resume,
    required List<StatementLine> lignes,
    required DateTime from,
    required DateTime to,
  }) =>
      Statement(
        from: from,
        to: to,
        grossSales: Json.asDouble(resume['grossSales']),
        commission: Json.asDouble(resume['commissions']),
        providerFee: Json.asDouble(resume['providerFees']),
        net: Json.asDouble(resume['netPayout']),
        currency: Json.str(resume['currency'], AppConfig.defaultCurrency),
        lineCount: Json.asInt(resume['lineCount']),
        lines: lignes,
      );
}

/// Versement vers le compte du vendeur.
///
/// `provider` A DISPARU. Le contrat rend `providerRef` — la RÉFÉRENCE de
/// l'opération chez l'opérateur, pas son nom. Le nom n'est nulle part dans
/// `PayoutSummary`, et l'ancien modèle affichait « — » en permanence. La référence
/// est plus utile : c'est ce qu'un vendeur cite à son opérateur mobile money quand
/// il ne retrouve pas son argent.
class Payout {
  Payout({
    required this.id,
    required this.gross,
    required this.commission,
    required this.amount,
    required this.currency,
    required this.status,
    required this.paidAt,
    required this.providerRef,
  });

  final String id;
  final double gross;
  final double commission;

  /// Le NET versé. C'est ce que le vendeur reçoit, et le seul montant qu'il
  /// pourra rapprocher de son relevé d'opérateur.
  final double amount;

  final String currency;

  /// Pending | Processing | Paid | Failed | Rejected.
  final String status;

  /// NUL TANT QUE LE VERSEMENT N'EST PAS PAYÉ. Le contrat n'a pas de date de
  /// DEMANDE : `PayoutSummary` ne rend que `PaidAtUtc`. Un versement en cours n'a
  /// donc aucune date à afficher — mieux vaut le dire que d'inventer « aujourd'hui ».
  final DateTime? paidAt;

  final String? providerRef;

  factory Payout.fromJson(Map d) => Payout(
        id: Json.str(d['id']),
        gross: Json.asDouble(d['grossAmount']),
        commission: Json.asDouble(d['commissionAmount']),
        amount: Json.asDouble(d['netAmount']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        status: Json.str(d['status']),
        paidAt: Json.asDate(d['paidAtUtc']),
        providerRef: (d['providerRef']?.toString().isNotEmpty ?? false)
            ? d['providerRef'].toString()
            : null,
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LE RELEVÉ FINANCIER — `/api/financial/settlements/sellers/{id}/…`
///
/// L'ANCIEN ENCADRÉ DISAIT « SANS AMONT PUBLIC », ET C'ÉTAIT VRAI À L'ÉPOQUE.
///
/// Il affirmait que `ReverseProxy:Routes` n'avait aucune entrée vers
/// `/api/financial/settlements/*`. L'entrée `settlements` existe désormais (ordre
/// 9, GET seulement) : les trois routes sont joignables depuis un téléphone.
///
/// IL AFFIRMAIT AUSSI « avec contrôle de propriété vendeur ». C'ÉTAIT FAUX.
///
/// Le métacommentaire de la passerelle le répétait, en nommant une méthode
/// `EnsureSellerAsync` qui n'existait NULLE PART dans financial-service. Les trois
/// lectures étaient ouvertes à tout compte authentifié : chiffre d'affaires brut,
/// commissions et net d'un concurrent, ligne par ligne. Corrigé par
/// `DenyUnlessOwnSellerAsync`. Un commentaire qui certifie une garde absente est
/// pire qu'un silence — il fait passer la relecture.
///
/// DEUX REQUÊTES POUR UN ÉCRAN, ET LA SECONDE PEUT ÉCHOUER SEULE.
///
/// Le résumé et les lignes sont deux routes. On les demande EN PARALLÈLE, et un
/// échec des lignes ne fait pas tomber le relevé : des totaux justes sans détail
/// valent mieux qu'un écran en erreur. L'écart entre `lineCount` et
/// `lines.length` rend la perte visible.
///
/// LA PÉRIODE EST OBLIGATOIRE CÔTÉ SERVEUR.
///
/// `periodStartUtc` et `periodEndUtc` ne sont pas nullables : les omettre rend
/// 400. `statementProvider` les calcule déjà à partir du nombre de jours choisi.
/// ═════════════════════════════════════════════════════════════════════════════
class FinanceApi extends ApiBase {
  const FinanceApi(super.dio);

  static const _p = '${AppConfig.settlements}/sellers';

  /// EN UTC, ET SÉRIALISÉ EXPLICITEMENT. `toIso8601String()` sur une date
  /// locale n'emporte aucun décalage : le serveur la lirait comme de l'UTC, et le
  /// relevé d'un vendeur béninois (UTC+1) glisserait d'une heure — assez pour
  /// qu'une vente de 23 h 30 change de jour, donc de période.
  static String _iso(DateTime d) => d.toUtc().toIso8601String();

  Future<Statement> statement(String sellerId, {required DateTime from, required DateTime to}) =>
      guard(() async {
        final params = {'periodStartUtc': _iso(from), 'periodEndUtc': _iso(to)};

        // Les deux appels partent ensemble : en série, l'écran attendrait deux
        // allers-retours bout à bout sur un réseau où chacun coûte cher.
        final resume = dio.get('$_p/$sellerId/statement', queryParameters: params);
        final lignes = dio.get('$_p/$sellerId/statement/lines', queryParameters: params);

        final r = await resume;

        List<StatementLine> detail;
        try {
          detail = Json.list((await lignes).data).map(StatementLine.fromJson).toList();
        } catch (_) {
          // ON AVALE CET ÉCHEC, ET C'EST LE SEUL DU FICHIER À L'ÊTRE.
          // Le résumé a réussi : le vendeur voit ses totaux. Propager ferait
          // disparaître une information exacte pour cause de détail manquant.
          detail = const [];
        }

        return Statement.assemble(
          resume: Json.map(r.data),
          lignes: detail,
          from: from,
          to: to,
        );
      });

  Future<List<Payout>> payouts(String sellerId) => guard(() async {
        final resp = await dio.get('$_p/$sellerId/payouts');
        return Json.list(resp.data).map(Payout.fromJson).toList();
      });
}

final financeApiProvider = Provider<FinanceApi>((ref) => FinanceApi(ref.watch(dioProvider)));

/// Nombre de jours du relevé affiché (30 / 90 / 365).
final statementRangeProvider = StateProvider<int>((ref) => 30);

/// Le relevé du vendeur connecté, sur la période choisie.
///
/// PASSE PAR `requiredSellerIdProvider` : sans dossier vendeur, ce fournisseur
/// porte une erreur NOMMÉE plutôt que d'envoyer un identifiant vide et de récolter
/// un 404 incompréhensible. Depuis que la route est gardée par appartenance, un
/// identifiant erroné rend 404 aussi — les deux cas seraient indiscernables.
final statementProvider = FutureProvider<Statement>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  final days = ref.watch(statementRangeProvider);
  final now = DateTime.now();
  return ref
      .watch(financeApiProvider)
      .statement(sellerId, from: now.subtract(Duration(days: days)), to: now);
});

final payoutsProvider = FutureProvider<List<Payout>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(financeApiProvider).payouts(sellerId);
});
