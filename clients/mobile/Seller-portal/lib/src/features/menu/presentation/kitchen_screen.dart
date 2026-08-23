import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../kitchen_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// ÉCRAN DE CUISINE — `GET /api/v1/bff/restaurant/restaurants/{id}/kitchen`.
///
/// C'est le poste de travail quotidien d'un restaurateur, et il n'existait pas :
/// `kitchenBoardProvider` et tout `KitchenApi` étaient écrits, testés, branchés
/// sur une route qui répond — sans un seul consommateur.
///
/// NI « ACCEPTER » NI « REFUSER » NE FIGURENT ICI, ET CE N'EST PAS UN OUBLI.
///
/// C'est la chose la plus importante à comprendre de cet écran. Les trois seaux
/// rendus par le BFF sont des `KitchenTicketStatus`, et non des
/// `FoodOrderStatus` :
///
///   • `Pending`   — ticket ACCEPTÉ, aucun article commencé ;
///   • `Preparing` — au moins un article commencé ;
///   • `Ready`     — tous les articles prêts, sur le passe.
///
/// Or le ticket de cuisine N'EXISTE QU'APRÈS l'acceptation. Une commande en
/// attente de décision (`FoodOrderStatus.PendingRestaurantAcceptance`) n'a pas
/// encore de ticket : elle n'apparaît dans aucun des trois seaux, et aucune
/// route ne la rend — `ListPendingFoodOrdersQuery` et `GetFoodOrderQuery` sont
/// écrites dans food-service et branchées sur rien.
///
/// `KitchenApi.accept` et `KitchenApi.reject` restent donc sans écran : les
/// méthodes sont justes, il leur manque la LISTE de ce sur quoi les appliquer.
/// Poser ici deux boutons qui n'auraient jamais de ticket à traiter serait pire
/// que leur absence.
///
/// LE TEMPS ÉCOULÉ EST CALCULÉ PAR LA PASSERELLE, PAS ICI.
///
/// `elapsedSeconds` arrive dans la réponse. Le recalculer depuis `receivedAt`
/// dépendrait de l'horloge du téléphone — et un appareil déréglé de dix minutes
/// afficherait des tickets « en retard » qui ne le sont pas.
///
/// En contrepartie, ce chiffre VIEILLIT entre deux chargements. D'où le
/// rafraîchissement périodique ci-dessous : sans lui, un ticket ouvert à 12 h 03
/// afficherait encore « il y a 2 min » à 12 h 20.
///
/// AUCUN MONTANT N'EST AFFICHÉ. `KitchenTicketDto` n'en porte aucun, et c'est
/// une décision serveur : un poste de cuisine a des plats à préparer, pas des
/// prix à connaître. La feuille de la maquette qui montre « Total 12 500 F CFA »
/// n'a donc pas d'amont ici.
///
/// ON NE PEUT PAS COCHER UN PLAT, SEULEMENT LE TICKET ENTIER.
/// `StartKitchenItemCommand` et `MarkKitchenItemReadyCommand` existent sans
/// route. Un vrai écran multi-postes en aura besoin ; celui-ci s'en passe.
/// ═════════════════════════════════════════════════════════════════════════════
class KitchenScreen extends ConsumerStatefulWidget {
  const KitchenScreen({super.key, required this.restaurantId});

  final String restaurantId;

  @override
  ConsumerState<KitchenScreen> createState() => _KitchenScreenState();
}

class _KitchenScreenState extends ConsumerState<KitchenScreen> {
  /// Seau affiché : 0 = à préparer, 1 = en cours, 2 = prêt.
  int _seau = 0;

  Timer? _tick;

  /// Un geste est en vol : on grise les boutons du ticket concerné.
  String? _enCours;

  @override
  void initState() {
    super.initState();

    // TRENTE SECONDES, ET C'EST UN COMPROMIS ASSUMÉ.
    //
    // Plus court userait la batterie d'un téléphone posé sur un passe toute la
    // journée ; plus long laisserait un ticket entrant invisible trop longtemps.
    // Le vrai correctif serait un flux temps réel — communication-service
    // n'expose aucun hub aujourd'hui (cf. `chat_realtime.dart`).
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      if (!mounted) return;
      ref.invalidate(kitchenBoardProvider(widget.restaurantId));
      // LA FILE D'ACCEPTATION AUSSI, et c'est même la plus urgente des deux :
      // un ticket en cuisine attend un cuisinier, une commande non acceptée
      // attend un client qui, lui, annule.
      ref.invalidate(pendingOrdersProvider(widget.restaurantId));
    });
  }

  @override
  void dispose() {
    _tick?.cancel();
    super.dispose();
  }

  /// Exécute une transition, puis RELIT le tableau.
  ///
  /// LES DEUX ROUTES RENDENT 204 SANS CORPS. Sans relecture, le ticket
  /// resterait dans son seau d'origine et le cuisinier appuierait deux fois.
  Future<void> _transition(
    String foodOrderId,
    Future<void> Function() action, {
    required String succes,
  }) async {
    setState(() => _enCours = foodOrderId);
    try {
      await action();
      ref.invalidate(kitchenBoardProvider(widget.restaurantId));
      ref.invalidate(pendingOrdersProvider(widget.restaurantId));
      if (mounted) AppNotify.success(context, succes);
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _enCours = null);
    }
  }

  /// Accepte une commande, puis relit LES DEUX listes.
  ///
  /// LA COMMANDE CHANGE DE LISTE : elle quitte la file d'acceptation et
  /// apparaît en cuisine. Ne relire qu'une des deux la laisserait visible aux deux
  /// endroits, ou nulle part.
  Future<void> _accepter(PendingFoodOrder commande) => _transition(
        commande.id,
        () => ref.read(kitchenApiProvider).accept(widget.restaurantId, commande.id),
        succes: 'Commande acceptée. Elle est passée en cuisine.',
      );

  /// Refuse une commande, avec un MOTIF obligatoire.
  ///
  /// LE MOTIF N'EST PAS UNE FORMALITÉ. Il part au client et à la plateforme :
  /// « ingrédient épuisé » et « cuisine saturée » n'ont pas les mêmes conséquences
  /// — le premier justifie un remboursement immédiat, le second alimente la
  /// métrique de saturation du §21. Un refus sans motif ne dit rien à personne.
  Future<void> _refuser(PendingFoodOrder commande) async {
    final motif = await showModalBottomSheet<({String value, String? comment})>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _RejectSheet(commande: commande),
    );
    if (motif == null || !mounted) return;

    await _transition(
      commande.id,
      () => ref.read(kitchenApiProvider).reject(
            widget.restaurantId,
            commande.id,
            reason: motif.value,
            comment: motif.comment,
          ),
      succes: 'Commande refusée. Le client est prévenu.',
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final async = ref.watch(kitchenBoardProvider(widget.restaurantId));
    final attente = ref.watch(pendingOrdersProvider(widget.restaurantId));

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(title: const Text('Cuisine')),
      body: async.when(
        loading: () => const LoadingView(),

        // 403 arrive ici pour une raison légitime : `preparing` et `ready`
        // exigent `restaurant.kitchen.manage`. Le message du serveur le dit
        // mieux qu'un « erreur ».
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(kitchenBoardProvider(widget.restaurantId)),
        ),
        data: (resultat) {
          final board = resultat.data;
          final seaux = <({String libelle, List<KitchenTicket> tickets})>[
            (libelle: 'À préparer', tickets: board.pending),
            (libelle: 'En cours', tickets: board.preparing),
            (libelle: 'Prêt', tickets: board.ready),
          ];
          final courant = seaux[_seau];

          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(kitchenBoardProvider(widget.restaurantId)),
            child: Column(
              children: [
                // ═══════════════════════════════════════════════════════════════
                // LA FILE D'ACCEPTATION — AU-DESSUS DES SEAUX, PAS DANS UN ONGLET.
                //
                // UN QUATRIÈME ONGLET AURAIT REPRODUIT LE DÉFAUT D'ORIGINE.
                //
                // Le manque corrigé ici n'était pas « la donnée est absente » mais
                // « la décision est invisible ». La ranger derrière une puce que le
                // restaurateur doit penser à ouvrir, alors qu'il travaille les yeux
                // sur ses tickets, l'aurait rendue invisible autrement. Elle
                // s'impose donc en haut, quel que soit le seau consulté.
                //
                // RIEN NE S'AFFICHE QUAND LA FILE EST VIDE. Un bandeau permanent
                // « 0 commande à accepter » userait l'attention exactement là où on
                // en a besoin.
                //
                // UNE ERREUR ICI NE VIDE PAS L'ÉCRAN DE CUISINE : on tait la
                // bande et les tickets restent. Des plats sont en cours ; les
                // effacer parce qu'une seconde lecture a échoué serait pire que
                // l'absence de file.
                // ═══════════════════════════════════════════════════════════════
                ...switch (attente) {
                  AsyncData(:final value) when value.isNotEmpty => [
                      _FileAcceptation(
                        commandes: value,
                        enCours: _enCours,
                        onAccepter: _accepter,
                        onRefuser: _refuser,
                      ),
                    ],
                  _ => const <Widget>[],
                },

                const SizedBox(height: 10),
                SizedBox(
                  height: 36,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                    children: [
                      for (var i = 0; i < seaux.length; i++)
                        Padding(
                          padding: const EdgeInsets.only(right: 8),
                          child: PartnerFilterChip(
                            // Le compte est DANS la puce : en cuisine, on veut
                            // savoir combien attendent sans changer d'onglet.
                            label: '${seaux[i].libelle} (${seaux[i].tickets.length})',
                            selected: i == _seau,
                            onTap: () => setState(() => _seau = i),
                          ),
                        ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),

                Expanded(
                  child: courant.tickets.isEmpty
                      ? PartnerEmptyState(
                          icon: Icons.restaurant_outlined,
                          message: _vide(_seau),
                        )
                      : ListView.separated(
                          padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
                          itemCount: courant.tickets.length,
                          separatorBuilder: (_, __) => const SizedBox(height: 12),
                          itemBuilder: (_, i) => _TicketCard(
                            ticket: courant.tickets[i],
                            seau: _seau,
                            occupe: _enCours == courant.tickets[i].foodOrderId,
                            onAction: () => _lancer(courant.tickets[i]),
                          ),
                        ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }

  /// LE MESSAGE DE SEAU VIDE DIT CE QUE ÇA VEUT DIRE, PAS « AUCUN RÉSULTAT ».
  ///
  /// Un passe vide est une bonne nouvelle ; une liste « à préparer » vide aussi.
  /// Écrire « aucun ticket » aux trois endroits ferait douter que l'écran
  /// fonctionne.
  String _vide(int seau) => switch (seau) {
        0 => 'Rien à préparer pour le moment.',
        1 => 'Aucune préparation en cours.',
        _ => 'Rien sur le passe — tout est parti.',
      };

  void _lancer(KitchenTicket t) {
    final api = ref.read(kitchenApiProvider);

    switch (_seau) {
      case 0:
        _transition(
          t.foodOrderId,
          () => api.startPreparing(widget.restaurantId, t.foodOrderId),
          succes: '${t.reference} en préparation.',
        );
      case 1:
        _transition(
          t.foodOrderId,
          () => api.markReady(widget.restaurantId, t.foodOrderId),
          succes: '${t.reference} est prêt.',
        );
      // Seau « Prêt » : aucune action. La suite appartient à HBA Delivery —
      // c'est le livreur qui enlève, et `MarkFoodOrderPickedUpCommand` est
      // déclenchée par un événement, pas par le restaurant.
    }
  }
}

class _TicketCard extends StatelessWidget {
  const _TicketCard({
    required this.ticket,
    required this.seau,
    required this.occupe,
    required this.onAction,
  });

  final KitchenTicket ticket;
  final int seau;
  final bool occupe;
  final VoidCallback onAction;

  /// L'AMBRE À PARTIR DE QUINZE MINUTES, LE ROUGE À TRENTE.
  ///
  /// Deux seuils écrits en dur, et il faut le savoir : le domaine ne porte
  /// AUCUNE échéance de préparation, et rien côté serveur ne qualifie un ticket
  /// de « en retard ». Ce sont donc des repères visuels, pas une règle métier —
  /// aucune conséquence n'en découle, et l'écran ne promet rien.
  Color _couleurDuTemps(BuildContext context) {
    final minutes = ticket.elapsedSeconds ~/ 60;
    if (minutes >= 30) return AppTheme.danger;
    if (minutes >= 15) return AppTheme.foodAmber;
    return AppColors.of(context).subtle;
  }

  String get _duree {
    final m = ticket.elapsedSeconds ~/ 60;
    if (m < 1) return 'à l\'instant';
    if (m < 60) return '$m min';
    return '${m ~/ 60} h ${(m % 60).toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final tempsCouleur = _couleurDuTemps(context);

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                ticket.reference,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              const SizedBox(width: 8),
              if (ticket.priority > 0)
                const PartnerStatusDot(
                  label: 'Prioritaire',
                  color: AppTheme.foodAmber,
                  background: AppTheme.foodAmberSoft,
                ),
              const Spacer(),
              Icon(Icons.schedule, size: 15, color: tempsCouleur),
              const SizedBox(width: 4),
              Text(
                _duree,
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w700,
                  color: tempsCouleur,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          for (final item in ticket.items) ...[
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // La quantité d'abord, en gras : c'est ce qu'on lit en premier
                // quand on prépare.
                SizedBox(
                  width: 28,
                  child: Text(
                    '${item.quantity}×',
                    style: TextStyle(
                      fontSize: 14.5,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        item.name,
                        style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w600,
                          color: colors.ink,
                        ),
                      ),
                      // Options déjà mises en forme par le serveur
                      // (« Taille : Grande ») : on ne les recompose pas.
                      if (item.options.isNotEmpty)
                        Text(
                          item.options.join(' · '),
                          style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
                        ),
                      // LA NOTE D'UNE LIGNE EST EN AMBRE : « sans piment »
                      // noyé dans le gris se rate, et se rater coûte un plat
                      // refait.
                      if (item.notes != null)
                        Text(
                          item.notes!,
                          style: const TextStyle(
                            fontSize: 12.5,
                            height: 1.4,
                            fontWeight: FontWeight.w700,
                            color: AppTheme.foodAmber,
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
          ],

          if (ticket.customerNote != null) ...[
            const SizedBox(height: 2),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              decoration: BoxDecoration(
                color: AppTheme.foodAmberSoft,
                borderRadius: BorderRadius.circular(AppTheme.radiusField),
              ),
              child: Text(
                ticket.customerNote!,
                style: const TextStyle(
                  fontSize: 13,
                  height: 1.4,
                  fontWeight: FontWeight.w600,
                  color: AppTheme.foodAmber,
                ),
              ),
            ),
          ],

          // « D'AUTRES POSTES PRÉPARENT ENCORE » CHANGE LE SENS DU BOUTON.
          //
          // Sans cette ligne, un cuisinier qui a fini SA part marque le ticket
          // prêt alors que la garniture n'est pas sortie. Le compteur vient du
          // serveur ; il vaut zéro tant qu'aucun poste n'est configuré.
          if (ticket.otherStationsPending > 0) ...[
            const SizedBox(height: 8),
            Text(
              '${ticket.otherStationsPending} autre(s) poste(s) préparent encore.',
              style: TextStyle(fontSize: 12.5, color: colors.subtle),
            ),
          ],

          if (seau < 2) ...[
            const SizedBox(height: 14),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: occupe ? null : onAction,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                  backgroundColor: AppTheme.brandGreen,
                ),
                child: occupe
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : Text(seau == 0 ? 'Commencer la préparation' : 'Marquer prêt'),
              ),
            ),
          ],
        ],
      ),
    );
  }
}


// ═════════════════════════════════════════════════════════════════════════════
// LA BANDE DES COMMANDES À ACCEPTER.
// ═════════════════════════════════════════════════════════════════════════════
class _FileAcceptation extends StatelessWidget {
  const _FileAcceptation({
    required this.commandes,
    required this.enCours,
    required this.onAccepter,
    required this.onRefuser,
  });

  final List<PendingFoodOrder> commandes;
  final String? enCours;
  final Future<void> Function(PendingFoodOrder) onAccepter;
  final Future<void> Function(PendingFoodOrder) onRefuser;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      width: double.infinity,
      color: AppTheme.foodAmberSoft,
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(Icons.notifications_active_outlined,
                  size: 18, color: AppTheme.promoOrange),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  commandes.length == 1
                      ? '1 commande à accepter'
                      : '${commandes.length} commandes à accepter',
                  style: TextStyle(
                      fontWeight: FontWeight.w800, fontSize: 14.5, color: colors.ink),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),

          for (final c in commandes) ...[
            CardSection(
              margin: const EdgeInsets.only(bottom: 8),
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          Format.money(c.total, c.currency),
                          style: TextStyle(
                              fontWeight: FontWeight.w800, fontSize: 15, color: colors.ink),
                        ),
                      ),
                      // TEMPS ÉCOULÉ, PAS TEMPS RESTANT. Le domaine ne porte
                      // aucune échéance d'acceptation : un décompte atteindrait
                      // zéro sans conséquence, et ce serait une promesse fausse.
                      Text(
                        c.attente.inMinutes < 1
                            ? "à l'instant"
                            : 'depuis ${c.attente.inMinutes} min',
                        style: TextStyle(
                          fontSize: 12,
                          fontWeight: c.tarde ? FontWeight.w700 : FontWeight.w400,
                          color: c.tarde ? AppTheme.danger : colors.subtle,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),

                  for (final l in c.lines)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 2),
                      child: Text(l,
                          style: TextStyle(fontSize: 13, color: colors.ink, height: 1.35)),
                    ),

                  // La note du client est ce qui distingue « sans piment » d'un
                  // litige : elle passe avant la décision, pas après.
                  if (c.customerNote case final note?) ...[
                    const SizedBox(height: 6),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                      decoration: BoxDecoration(
                        color: colors.bg,
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text('« $note »',
                          style: TextStyle(
                              fontSize: 12.5, fontStyle: FontStyle.italic, color: colors.ink)),
                    ),
                  ],

                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton(
                          onPressed: enCours == c.id ? null : () => onRefuser(c),
                          style: OutlinedButton.styleFrom(foregroundColor: AppTheme.danger),
                          child: const Text('Refuser'),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        flex: 2,
                        child: FilledButton(
                          onPressed: enCours == c.id ? null : () => onAccepter(c),
                          child: enCours == c.id
                              ? const SizedBox(
                                  width: 18,
                                  height: 18,
                                  child: CircularProgressIndicator(
                                      strokeWidth: 2, color: Colors.white))
                              : const Text('Accepter'),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// Feuille de refus : un MOTIF obligatoire, un commentaire facultatif.
///
/// AUCUN MOTIF N'EST PRÉSÉLECTIONNÉ, ET C'EST VOLONTAIRE. Un premier choix
/// coché d'avance serait celui qui part le plus souvent, par simple inertie — et
/// « ingrédient épuisé » déclenche un remboursement là où « cuisine saturée »
/// alimente une métrique de charge. Le restaurateur doit choisir.
class _RejectSheet extends StatefulWidget {
  const _RejectSheet({required this.commande});
  final PendingFoodOrder commande;

  @override
  State<_RejectSheet> createState() => _RejectSheetState();
}

class _RejectSheetState extends State<_RejectSheet> {
  String? _motif;
  final _commentaire = TextEditingController();

  @override
  void dispose() {
    _commentaire.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: sheetPadding(context),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text('Refuser cette commande',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(
              'Le motif est transmis au client. Il détermine aussi le traitement '
              'du remboursement — choisissez celui qui correspond vraiment.',
              style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
            ),
            const SizedBox(height: 16),

            // PAS DE `RadioListTile`, ET CE N'EST PAS UN CAPRICE DE STYLE.
            //
            // Ses paramètres `groupValue` et `onChanged` sont dépréciés depuis
            // Flutter 3.32 au profit d'un ancêtre `RadioGroup`. Employer l'un
            // laisse un avertissement à chaque analyse — et on cesse de les lire ;
            // employer l'autre lie cet écran à une API très récente pour une liste
            // de six choix.
            //
            // Une `ListTile` avec l'icône d'état fait exactement la même chose,
            // sans dépendance ni avertissement.
            for (final m in kRejectionReasons)
              ListTile(
                dense: true,
                contentPadding: EdgeInsets.zero,
                leading: Icon(
                  _motif == m.value ? Icons.radio_button_checked : Icons.radio_button_unchecked,
                  color: _motif == m.value ? AppTheme.brandGreen : colors.subtle,
                  size: 20,
                ),
                title: Text(m.label, style: const TextStyle(fontSize: 14)),
                onTap: () => setState(() => _motif = m.value),
              ),
            const SizedBox(height: 8),

            TextField(
              controller: _commentaire,
              maxLines: 2,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Précision (facultatif)',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 20),

            FilledButton(
              // FERMÉ TANT QU'AUCUN MOTIF N'EST CHOISI : le serveur le refuserait,
              // et un aller-retour pour l'apprendre serait du temps perdu au passe.
              onPressed: _motif == null
                  ? null
                  : () => Navigator.pop(context, (
                        value: _motif!,
                        comment: _commentaire.text.trim().isEmpty
                            ? null
                            : _commentaire.text.trim(),
                      )),
              style: FilledButton.styleFrom(
                backgroundColor: AppTheme.danger,
                minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
              ),
              child: const Text('Confirmer le refus'),
            ),
            const SizedBox(height: 8),
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Annuler'),
            ),
          ],
        ),
      ),
    );
  }
}
