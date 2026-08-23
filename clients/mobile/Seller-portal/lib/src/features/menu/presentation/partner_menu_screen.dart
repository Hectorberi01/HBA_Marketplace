import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../activities/activities_data.dart';
import '../menu_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// MENU — carte d'UN restaurant, `GET /api/food/partner/restaurants/{id}/menu`.
///
/// « MENU », PAS « PRODUITS ». LE MOT COMPTE.
///
/// C'est ce même 3ᵉ onglet qui montre « Produits » chez un commerçant. Un
/// restaurateur ne pense pas ses plats comme des produits.
///
/// LA CARTE EST À DEUX NIVEAUX ; LA MAQUETTE N'EN MONTRAIT QU'UN.
///
/// Le domaine a CARTE → SECTION → PLAT : un restaurant peut avoir une carte
/// « Midi » et une carte « Soir », chacune avec ses sections et son créneau. La
/// maquette posait une simple barre de catégories (« Populaires », « Plats »,
/// « Boissons », « Desserts ») — quatre libellés écrits en dur, dont
/// « Populaires » qui n'est pas une section de carte mais une sélection.
///
/// Les puces suivent donc les SECTIONS RÉELLES de la carte servie, et un
/// sélecteur de carte apparaît quand il y en a plusieurs. Aplatir les deux
/// niveaux ferait disparaître les créneaux et mélangerait les deux services.
///
/// L'ÉCRAN N'EST PLUS EN LECTURE SEULE (VEN5).
///
/// Les deux boutons de chaque plat étaient grisés, faute de route. `PUT
/// .../items/{id}`, `PUT .../items/{id}/availability` et `DELETE .../items/{id}`
/// sont ouvertes : modifier, épuiser et supprimer fonctionnent, et l'icône
/// d'état du service pilote `POST .../pause` et `.../resume`.
///
/// « ÉPUISÉ » N'EST PLUS UNE BASCULE À DEUX POSITIONS, ET C'EST VOULU.
///
/// La maquette montrait un interrupteur « Indisponible » — donc deux états. Le
/// domaine en a trois, et les deux qu'il ajoute ne se valent pas : « épuisé
/// aujourd'hui » revient seul au service suivant, « retiré de la carte » ne
/// revient pas. Un interrupteur unique aurait forcé l'un des deux, et le mauvais
/// dans les deux sens — un plat qui disparaît pour des semaines, ou un plat
/// retiré exprès qui réapparaît le lendemain.
///
/// Le geste FRÉQUENT garde donc un seul appui — c'est celui qu'on fait en plein
/// service, une main sur le téléphone. Le geste RARE passe par le menu « … ».
///
/// CE QUI RESTE EN LECTURE SEULE : les cartes et les sections. Renommer,
/// masquer, réordonner et supprimer une carte ou une section ONT désormais leurs
/// routes et leurs méthodes dans `MenuApi` ; il leur manque un écran. Voir la
/// tâche VEN6.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerMenuScreen extends ConsumerStatefulWidget {
  const PartnerMenuScreen({super.key, required this.activity});

  final SellerActivity activity;

  @override
  ConsumerState<PartnerMenuScreen> createState() => _PartnerMenuScreenState();
}

class _PartnerMenuScreenState extends ConsumerState<PartnerMenuScreen> {
  /// Section retenue, par identifiant. `null` = la première de la carte.
  ///
  /// Un identifiant plutôt qu'un index : les sections changent d'ordre entre
  /// deux chargements, et un index périmé afficherait une autre section sans
  /// que rien ne le signale.
  String? _sectionId;

  /// Carte retenue, par identifiant. `null` = celle servie maintenant.
  String? _menuId;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final async = ref.watch(restaurantMenuProvider(widget.activity.id));

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: async.when(
          loading: () => const LoadingView(),

          // 403 ARRIVE ICI POUR UNE RAISON LÉGITIME : la route exige la
          // permission `restaurant.menu.manage`. Un cuisinier n'a pas à modifier
          // la carte, et le message du serveur le dit mieux qu'un « erreur ».
          error: (e, _) => ErrorView(
            message: e.toString(),
            onRetry: () => ref.invalidate(restaurantMenuProvider(widget.activity.id)),
          ),
          data: (menu) => _MenuBody(
            activity: widget.activity,
            menu: menu,
            selectedMenuId: _menuId,
            selectedSectionId: _sectionId,
            onMenuChanged: (id) => setState(() {
              _menuId = id;
              // La section retenue appartenait à l'autre carte : on repart sur
              // la première, sinon la liste s'affiche vide sans raison visible.
              _sectionId = null;
            }),
            onSectionChanged: (id) => setState(() => _sectionId = id),
          ),
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push('/dish/new'),
        // VERT HBA, PAS AMBRE, MÊME EN UNIVERS FOOD.
        //
        // L'ambre est la couleur de l'ATTENTION dans cette application :
        // acceptation en attente, stock faible, plat épuisé. L'employer pour
        // l'action principale d'un écran la viderait de ce sens partout ailleurs.
        backgroundColor: AppTheme.brandGreen,
        foregroundColor: Colors.white,
        icon: const Icon(Icons.add, size: 20),
        label: const Text(
          'Ajouter un plat',
          style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700),
        ),
      ),
    );
  }
}

class _MenuBody extends StatelessWidget {
  const _MenuBody({
    required this.activity,
    required this.menu,
    required this.selectedMenuId,
    required this.selectedSectionId,
    required this.onMenuChanged,
    required this.onSectionChanged,
  });

  final SellerActivity activity;
  final RestaurantMenu menu;
  final String? selectedMenuId;
  final String? selectedSectionId;
  final ValueChanged<String> onMenuChanged;
  final ValueChanged<String> onSectionChanged;

  /// La carte affichée : celle qu'on a choisie, sinon celle SERVIE MAINTENANT,
  /// sinon la première. Retomber sur la première par défaut afficherait la carte
  /// du soir à midi.
  Menu? get _current {
    if (menu.menus.isEmpty) return null;
    if (selectedMenuId != null) {
      for (final m in menu.menus) {
        if (m.id == selectedMenuId) return m;
      }
    }
    for (final m in menu.menus) {
      if (m.isServedNow) return m;
    }
    return menu.menus.first;
  }

  @override
  Widget build(BuildContext context) {
    final current = _current;

    if (current == null || current.sections.isEmpty) {
      return Column(
        children: [
          _Header(activity: activity, menu: menu),
          const Expanded(
            child: PartnerEmptyState(
              icon: Icons.restaurant_menu_outlined,
              message: 'Aucune carte n\'est encore composée.\n'
                  'Ajoutez un premier plat pour commencer.',
            ),
          ),
        ],
      );
    }

    final section = current.sections.firstWhere(
      (s) => s.id == selectedSectionId,
      orElse: () => current.sections.first,
    );

    return Column(
      children: [
        _Header(activity: activity, menu: menu),
        const SizedBox(height: 14),

        // Sélecteur de CARTE, uniquement s'il y en a plusieurs. Une puce unique
        // ne serait pas un choix.
        if (menu.menus.length > 1) ...[
          SizedBox(
            height: 36,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              children: [
                for (final m in menu.menus)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: PartnerFilterChip(
                      // Le créneau est dans le libellé quand il existe : c'est
                      // ce qui distingue « Midi » de « Soir » autrement que par
                      // un nom qu'un restaurateur choisit librement.
                      label: m.servedFrom == null || m.servedUntil == null
                          ? m.name
                          : '${m.name} · ${m.servedFrom}–${m.servedUntil}',
                      selected: m.id == current.id,
                      onTap: () => onMenuChanged(m.id),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 10),
        ],

        SizedBox(
          height: 36,
          child: ListView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            children: [
              for (final s in current.sections)
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: PartnerFilterChip(
                    label: s.name,
                    selected: s.id == section.id,
                    onTap: () => onSectionChanged(s.id),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 14),

        Expanded(
          child: section.items.isEmpty
              ? PartnerEmptyState(
                  icon: Icons.restaurant_menu_outlined,
                  message: 'Aucun plat dans « ${section.name} ».',
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 96),
                  itemCount: section.items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 12),
                  itemBuilder: (_, i) => _DishCard(
                    restaurantId: menu.restaurantId,
                    dish: section.items[i],
                  ),
                ),
        ),
      ],
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.activity, required this.menu});

  final SellerActivity activity;
  final RestaurantMenu menu;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 16, 0),
        child: PartnerScreenHeader(
          title: 'Menu',
          activity: activity,
          // L'ÉTAT DU SERVICE VIENT DE LA CARTE, PAS DE `isOpenNow`.
          //
          // `MerchantActivityDto.isOpenNow` est nullable — l'amont ne sait pas
          // toujours répondre — alors que `RestaurantMenuView.acceptsOrdersNow`
          // est la valeur que food-service applique réellement.
          // DEUX ACTIONS DANS L'EN-TÊTE, ET L'ORDRE N'EST PAS INDIFFÉRENT.
          //
          // L'état du service est le geste QUOTIDIEN — on ferme, on rouvre — donc
          // il reste le plus à droite, sous le pouce. L'organisation des cartes est
          // un geste RARE : elle vient avant, moins accessible à l'aveugle.
          //
          // SANS CETTE ENTRÉE, `MenusScreen` SERAIT INATTEIGNABLE — exactement
          // le défaut qu'on vient de reprocher à `/analytics` : un écran branché
          // que la navigation n'offre pas.
          trailing: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              IconButton(
                tooltip: 'Organiser mes cartes',
                icon: const Icon(Icons.menu_book_outlined),
                onPressed: () => context.push('/menus', extra: menu.restaurantId),
              ),
              _ServiceIcon(
                restaurantId: menu.restaurantId,
                acceptsOrdersNow: menu.acceptsOrdersNow,
                blockedReason: menu.blockedReason,
              ),
            ],
          ),
        ),
      );
}

class _DishCard extends ConsumerWidget {
  const _DishCard({required this.restaurantId, required this.dish});

  final String restaurantId;
  final Dish dish;

  /// Exécute [action], puis RELIT la carte.
  ///
  /// TOUTES CES ROUTES RENDENT 204, SANS LE PLAT MODIFIÉ. Sans relecture,
  /// l'écran continuerait d'afficher l'ancien état — un restaurateur qui vient
  /// de marquer son poisson épuisé le verrait encore disponible, et
  /// recommencerait.
  ///
  /// ON NE FAIT PAS DE MISE À JOUR OPTIMISTE. Peindre le nouvel état avant la
  /// réponse afficherait « épuisé » sur un plat que le serveur a refusé de
  /// changer, et le mensonge tiendrait jusqu'au prochain chargement.
  Future<void> _run(
    BuildContext context,
    WidgetRef ref,
    Future<void> Function() action, {
    required String success,
  }) async {
    try {
      await action();
      ref.invalidate(restaurantMenuProvider(restaurantId));
      if (context.mounted) AppNotify.success(context, success);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  /// Nom, description et prix d'un plat.
  ///
  /// DEUX APPELS, PARCE QUE LE PRIX A SA PROPRE ROUTE.
  ///
  /// `PUT .../items/{id}` porte le nom, la description et le rang ;
  /// `PUT .../items/{id}/price` porte le prix, seul. Ce n'est pas une bizarrerie
  /// d'API : changer un prix est un fait commercial que le domaine traite à part,
  /// et le fondre dans une mise à jour générale ferait passer une modification de
  /// prix pour une correction de faute de frappe.
  ///
  /// On n'appelle la seconde QUE si le prix a bougé — sinon un simple changement
  /// de libellé écrirait un événement de prix identique à l'ancien.
  Future<void> _openEditSheet(BuildContext context, WidgetRef ref) async {
    // ═════════════════════════════════════════════════════════════════════════
    // LA FEUILLE REND SES VALEURS ; ELLE NE PRÊTE PLUS SES CONTRÔLEURS.
    //
    // La version précédente créait ici trois `TextEditingController`, les passait
    // à la feuille, lisait leur contenu après l'`await` puis les libérait. Son
    // commentaire prenait même soin de préciser « les valeurs sont lues MAINTENANT,
    // avant que les contrôleurs soient libérés ».
    //
    // L'ordre de lecture était juste et le défaut ailleurs : `showModalBottomSheet`
    // rend la main dès le `pop`, mais l'ANIMATION DE FERMETURE continue. Flutter
    // reconstruit donc les trois `TextField` pendant la sortie, sur des contrôleurs
    // déjà libérés, et lève « A TextEditingController was used after being
    // disposed » — une image plus tard, dans `RawGestureDetector`, en pointant un
    // endroit innocent.
    //
    // Le même défaut existait dans l'écran des cartes ; il s'y est déclenché.
    // Ici il dormait, parce que la feuille se ferme plus vite qu'on ne la
    // rouvre.
    //
    // CE N'EST PAS QU'UNE CORRECTION DE CYCLE DE VIE : c'est le bon découpage.
    // Une feuille qui rend `true` oblige l'appelant à savoir OÙ elle a rangé sa
    // saisie. Qu'elle rende la saisie elle-même, et il n'y a plus de contrôleur à
    // partager, donc plus de durée de vie à deviner.
    // ═════════════════════════════════════════════════════════════════════════
    final saisie = await showModalBottomSheet<({String nom, String description, double? prix})>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _EditDishSheet(
        nomInitial: dish.name,
        descriptionInitiale: dish.description ?? '',
        prixInitial: dish.basePrice,
      ),
    );

    if (saisie == null || !context.mounted) return;

    final nouveauNom = saisie.nom;
    final nouvelleDescription = saisie.description;
    final nouveauPrix = saisie.prix;

    await _run(
      context,
      ref,
      () async {
        final api = ref.read(menuApiProvider);
        await api.updateDish(
          restaurantId,
          dishId: dish.id,
          name: nouveauNom,
          description: nouvelleDescription.isEmpty ? null : nouvelleDescription,
        );

        // SEULEMENT SI LE PRIX A BOUGÉ. Réécrire le même prix produirait un
        // fait commercial identique à l'ancien à chaque correction de libellé.
        if (nouveauPrix != null && nouveauPrix > 0 && nouveauPrix != dish.basePrice) {
          await api.changeDishPrice(restaurantId, dishId: dish.id, basePrice: nouveauPrix);
        }
      },
      success: 'Plat modifié.',
    );
  }

  /// Les gestes RARES : retrait durable, remise en vente, suppression.
  Future<void> _openMoreSheet(BuildContext context, WidgetRef ref) async {
    final api = ref.read(menuApiProvider);

    await showModalBottomSheet<void>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // La correction rapide — nom, description, prix — sans quitter la
            // liste. Elle a perdu le bouton large au profit de la fiche, qui porte
            // la photo ; elle reste ici parce que corriger « 5500 » en « 6000 » ne
            // mérite pas un changement d'écran.
            ListTile(
              leading: const Icon(Icons.edit_outlined),
              title: const Text('Correction rapide'),
              subtitle: const Text('Nom, description, prix.'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _openEditSheet(context, ref);
              },
            ),
            const Divider(height: 1),

            // « RETIRER DE LA CARTE » N'EST PAS « ÉPUISÉ AUJOURD'HUI ».
            //
            // Le sous-titre le dit à chaque fois plutôt qu'une seule dans une
            // aide que personne ne lira : c'est la confusion qui coûte le plus
            // cher sur cet écran.
            ListTile(
              leading: const Icon(Icons.visibility_off_outlined),
              title: const Text('Retirer de la carte'),
              subtitle: const Text('Jusqu\'à nouvel ordre — ne revient pas seul.'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _run(
                  context,
                  ref,
                  () => api.setDishAvailability(
                    restaurantId,
                    dishId: dish.id,
                    state: DishAvailability.unavailable,
                  ),
                  success: '« ${dish.name} » est retiré de la carte.',
                );
              },
            ),
            // PAS DE « REMETTRE EN VENTE » QUAND C'EST LA PHOTO QUI MANQUE.
            //
            // La route répondrait 204 et le plat resterait invendable : le serveur
            // refuse la vente sur l'absence de photo, pas sur la disponibilité.
            // L'offrir ici ferait appuyer, réussir, et ne rien changer — le pire
            // enchaînement possible.
            if (!dish.isOrderable && dish.status != DishStatus.photoManquante)
              ListTile(
                leading: const Icon(Icons.visibility_outlined),
                title: const Text('Remettre en vente'),
                onTap: () {
                  Navigator.of(sheetContext).pop();
                  _run(
                    context,
                    ref,
                    () => api.setDishAvailability(
                      restaurantId,
                      dishId: dish.id,
                      state: DishAvailability.available,
                    ),
                    success: '« ${dish.name} » est de nouveau en vente.',
                  );
                },
              ),
            const Divider(height: 1),

            // SUPPRIMER DEMANDE UNE CONFIRMATION, ÉPUISER NON. La suppression
            // est irréversible et sert aux erreurs de saisie ; les deux autres se
            // défont d'un appui.
            ListTile(
              leading: const Icon(Icons.delete_outline, color: AppTheme.danger),
              title: const Text(
                'Supprimer le plat',
                style: TextStyle(color: AppTheme.danger),
              ),
              subtitle: const Text('Définitif. Pour un arrêt temporaire, retirez-le de la carte.'),
              onTap: () async {
                Navigator.of(sheetContext).pop();
                final confirme = await showDialog<bool>(
                  context: context,
                  builder: (d) => AlertDialog(
                    title: Text('Supprimer « ${dish.name} » ?'),
                    content: const Text(
                      'Cette suppression est définitive. Si le plat doit revenir, '
                      'retirez-le de la carte au lieu de le supprimer.',
                    ),
                    actions: [
                      TextButton(
                        onPressed: () => Navigator.of(d).pop(false),
                        child: const Text('Annuler'),
                      ),
                      TextButton(
                        onPressed: () => Navigator.of(d).pop(true),
                        style: TextButton.styleFrom(foregroundColor: AppTheme.danger),
                        child: const Text('Supprimer'),
                      ),
                    ],
                  ),
                );

                if (confirme == true && context.mounted) {
                  await _run(
                    context,
                    ref,
                    () => api.deleteDish(restaurantId, dishId: dish.id),
                    success: 'Plat supprimé.',
                  );
                }
              },
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // ON AFFICHE `imageUrl`, ET LE SERVEUR A DÉJÀ FAIT LE REPLI.
              //
              // Avant, l'écran n'affichait QUE `legacyImageUrl` — donc aucune photo
              // pour tout plat créé depuis la bascule média, et rien n'indiquait
              // pourquoi. food-service rend maintenant `displayImageUrl` : média
              // repris, ou URL héritée, tranché une fois côté serveur.
              //
              // LE REPLI D'ERREUR NE DIT PAS LA MÊME CHOSE QUE L'ABSENCE.
              //
              // `hasImage` vrai avec une URL cassée = média repris, adresse
              // obsolète (bucket renommé). Le plat est VENDABLE et son image ne
              // s'affiche pas : la vignette doit le signaler autrement qu'un plat
              // sans photo, qui n'est pas vendable du tout.
              ClipRRect(
                borderRadius: BorderRadius.circular(11),
                child: SizedBox(
                  width: 52,
                  height: 52,
                  child: dish.imageUrl != null
                      ? Image.network(
                          dish.imageUrl!,
                          fit: BoxFit.cover,
                          errorBuilder: (_, __, ___) =>
                              _DishPlaceholder(name: dish.name, broken: true),
                        )
                      : _DishPlaceholder(name: dish.name, missing: !dish.hasImage),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      dish.name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w700,
                        // Un plat indisponible reste lisible mais s'efface : le
                        // barrer ou le griser complètement le ferait passer pour
                        // supprimé.
                        color: dish.isOrderable ? colors.ink : colors.subtle,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      // Le prix de BASE, hors options : `basePrice` est le seul
                      // montant du contrat. Les suppléments s'y ajoutent au
                      // panier, et leur total dépend de ce que le client choisit.
                      Format.money(dish.basePrice, dish.currency),
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                        color: dish.isOrderable ? colors.ink : colors.subtle,
                      ),
                    ),
                    const SizedBox(height: 6),
                    _DishStatusBadge(status: dish.status),

                    // LE MOTIF SE DIT SOUS LE BADGE, PAS DANS LE BADGE.
                    //
                    // « Photo manquante » sans la suite — « le plat n'est pas
                    // vendu » — laisse croire à un détail esthétique. C'est la
                    // conséquence qu'il faut écrire, parce que c'est elle qui
                    // décide si le restaurateur agit maintenant ou dans un mois.
                    if (dish.status == DishStatus.photoManquante) ...[
                      const SizedBox(height: 4),
                      Text(
                        'Ce plat n\'est pas vendu tant qu\'il n\'a pas de photo.',
                        style: TextStyle(
                          fontSize: 12,
                          height: 1.35,
                          fontWeight: FontWeight.w600,
                          color: AppTheme.danger,
                        ),
                      ),
                    ]

                    // Retour prévu de disponibilité, quand food-service le
                    // connaît. `null` = indisponible sans échéance.
                    else if (dish.status == DishStatus.epuiseAujourdhui) ...[
                      const SizedBox(height: 4),
                      Text(
                        'De retour le ${Format.date(dish.backAt)}',
                        style: TextStyle(fontSize: 12, color: colors.subtle),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          // LE BOUTON LARGE PORTE LE GESTE DU SERVICE, EN UN SEUL APPUI.
          //
          // « On n'a plus de poisson » se dit une main sur le téléphone, au
          // milieu d'un coup de feu. Ce geste-là ne doit demander ni menu, ni
          // confirmation, ni réflexion sur la différence entre « épuisé » et
          // « retiré ». Les états rares — retrait durable, suppression — sont
          // derrière le « … », où leur coût d'accès reflète leur fréquence.
          Row(
            children: [
              Expanded(
                child: _GhostButton(
                  // « MODIFIER » MÈNE À LA FICHE, PLUS À LA FEUILLE.
                  //
                  // La feuille ne portait que trois champs — donc pas la photo, qui
                  // est désormais ce qui décide si le plat se vend. Un restaurateur
                  // qui voit « Photo manquante » et touche « Modifier » doit
                  // atterrir là où l'on ajoute une photo, pas sur trois champs de
                  // texte. La feuille reste accessible sous le « … » : c'est le
                  // geste rapide, pas le geste complet.
                  label: 'Modifier',
                  onTap: () => context.push(
                    '/dish/detail',
                    extra: (restaurantId: restaurantId, dishId: dish.id),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                flex: 2,

                // SANS PHOTO, LE BOUTON DE SERVICE NE SERT À RIEN — il change
                // la disponibilité d'un plat que le serveur refuse de vendre pour
                // une AUTRE raison. Il cède donc sa place au seul geste qui débloque
                // la situation. C'est la même erreur que « Mettre en vente » actif
                // sans déclinaison, corrigée en 3.6 : un bouton ne doit pas pouvoir
                // réussir sans effet.
                child: dish.status == DishStatus.photoManquante
                    ? _GhostButton(
                        label: 'Ajouter une photo',
                        onTap: () => context.push(
                          '/dish/detail',
                          extra: (restaurantId: restaurantId, dishId: dish.id),
                        ),
                      )
                    : _GhostButton(
                        label: dish.isOrderable ? 'Épuisé aujourd\'hui' : 'Remettre en vente',
                        onTap: () => _run(
                          context,
                          ref,
                          () => ref.read(menuApiProvider).setDishAvailability(
                                restaurantId,
                                dishId: dish.id,
                                state: dish.isOrderable
                                    ? DishAvailability.soldOutToday
                                    : DishAvailability.available,
                              ),
                          success: dish.isOrderable
                              ? '« ${dish.name} » est épuisé pour aujourd\'hui.'
                              : '« ${dish.name} » est de nouveau en vente.',
                        ),
                      ),
              ),
              const SizedBox(width: 8),
              _GhostIconButton(
                icon: Icons.more_horiz,
                onTap: () => _openMoreSheet(context, ref),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Vignette de repli : initiales dérivées du nom du plat.
///
/// TROIS CAS, ET DEUX SEULEMENT SONT DES PROBLÈMES.
///
/// [missing] — aucune photo, donc plat invendable : on le dit en rouge.
/// [broken]  — photo enregistrée dont l'adresse ne répond plus. Le plat EST
///             vendable ; c'est un incident d'affichage, pas une action du
///             restaurateur. Les confondre ferait chercher un fichier qu'il a
///             déjà déposé.
/// Ni l'un ni l'autre — un plat hérité sans image, neutre.
class _DishPlaceholder extends StatelessWidget {
  const _DishPlaceholder({required this.name, this.missing = false, this.broken = false});

  final String name;
  final bool missing;
  final bool broken;

  String get _initials {
    final words = name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty).toList();
    if (words.isEmpty) return '?';
    if (words.length == 1) {
      return words.first.substring(0, words.first.length >= 2 ? 2 : 1).toUpperCase();
    }
    return '${words[0][0]}${words[1][0]}'.toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    if (missing || broken) {
      return Container(
        color: missing ? AppTheme.dangerSoft : colors.bg,
        alignment: Alignment.center,
        child: Icon(
          missing ? Icons.add_a_photo_outlined : Icons.broken_image_outlined,
          size: 20,
          color: missing ? AppTheme.danger : colors.subtle,
        ),
      );
    }

    return Container(
      color: colors.bg,
      alignment: Alignment.center,
      child: Text(
        _initials,
        style: TextStyle(fontSize: 13, fontWeight: FontWeight.w800, color: colors.subtle),
      ),
    );
  }
}

/// Le badge de statut d'un plat. Un mot, une couleur, un motif.
///
/// TROIS COULEURS POUR QUATRE ÉTATS, PARCE QUE LA COULEUR DIT « FAUT-IL AGIR ».
///
/// Vert : rien à faire. Rouge : action requise maintenant (sans photo, le plat
/// n'est pas vendu). Gris : décidé par le restaurateur, il sait pourquoi.
/// « Épuisé aujourd'hui » est gris et non orange : c'est un geste NORMAL de
/// service, plusieurs fois par jour, et le peindre en alerte finirait par rendre
/// toute la carte alarmante.
class _DishStatusBadge extends StatelessWidget {
  const _DishStatusBadge({required this.status});

  final DishStatus status;

  @override
  Widget build(BuildContext context) {
    final (label, color, background) = switch (status) {
      DishStatus.enVente => ('En vente', AppTheme.brandGreen, AppTheme.brandGreenSoft),
      DishStatus.photoManquante => ('Photo manquante', AppTheme.danger, AppTheme.dangerSoft),
      DishStatus.epuiseAujourdhui =>
        ('Épuisé aujourd\'hui', AppTheme.slate, const Color(0xFFEDEFF1)),
      DishStatus.retire => ('Retiré de la carte', AppTheme.slate, const Color(0xFFEDEFF1)),
    };

    return PartnerStatusDot(label: label, color: color, background: background);
  }
}

/// La feuille d'édition d'un plat. Rend `true` si le restaurateur valide.
/// Feuille de modification d'un plat, PROPRIÉTAIRE de ses trois contrôleurs.
///
/// `StatefulWidget` POUR LE CYCLE DE VIE, pas pour l'état visuel. Rien ici ne
/// change d'apparence ; l'unique raison est que `dispose()` soit appelé par Flutter
/// au démontage réel — et non par l'appelant dès le retour de
/// `showModalBottomSheet`, alors que l'animation de fermeture reconstruit encore
/// les champs. Voir l'encadré de `_openEditSheet`.
class _EditDishSheet extends StatefulWidget {
  const _EditDishSheet({
    required this.nomInitial,
    required this.descriptionInitiale,
    required this.prixInitial,
  });

  final String nomInitial;
  final String descriptionInitiale;
  final double prixInitial;

  @override
  State<_EditDishSheet> createState() => _EditDishSheetState();
}

class _EditDishSheetState extends State<_EditDishSheet> {
  late final TextEditingController name =
      TextEditingController(text: widget.nomInitial);
  late final TextEditingController description =
      TextEditingController(text: widget.descriptionInitiale);
  late final TextEditingController price =
      TextEditingController(text: widget.prixInitial.toStringAsFixed(0));

  @override
  void dispose() {
    name.dispose();
    description.dispose();
    price.dispose();
    super.dispose();
  }

  void _enregistrer() => Navigator.of(context).pop((
        nom: name.text.trim(),
        description: description.text.trim(),
        // La virgule d'un clavier français rendrait `null` sans cette
        // substitution. Le remplacement vit ICI et non chez l'appelant : c'est la
        // feuille qui connaît le clavier.
        prix: double.tryParse(price.text.trim().replaceAll(',', '.')),
      ));

  @override
  Widget build(BuildContext context) => Padding(
        // Le clavier recouvre la feuille sans ce décalage, et le champ « prix »
        // — le dernier — devient invisible pendant qu'on le saisit.
        padding: EdgeInsets.fromLTRB(
            20, 20, 20, MediaQuery.of(context).viewInsets.bottom + 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Modifier le plat',
              style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: name,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Nom'),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: description,
              maxLines: 2,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Description',
                // Vider le champ EFFACE la description côté serveur : le contrat
                // remplace, il ne fusionne pas. Le dire évite qu'on l'apprenne
                // en la perdant.
                helperText: 'Laisser vide efface la description.',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: price,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(
                labelText: 'Prix de base (${Format.cfa})',
                helperText: 'Hors suppléments et options.',
              ),
            ),
            const SizedBox(height: 20),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _enregistrer,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                ),
                child: const Text('Enregistrer'),
              ),
            ),
          ],
        ),
      );
}

/// Icône d'état du service — et, désormais, le levier pour le suspendre.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE NE FAISAIT RIEN, FAUTE DE ROUTE. `POST .../pause` et `.../resume`
/// sont ouvertes (VEN5-c).
///
/// FERMER DEMANDE UNE DURÉE ; ROUVRIR EST IMMÉDIAT.
///
/// L'asymétrie est délibérée. Une fermeture sans échéance qu'on oublie de lever
/// retire l'établissement de la vitrine pour la soirée entière, et personne ne
/// s'en aperçoit avant de compter les commandes du lendemain. La réouverture,
/// elle, ne peut rien casser : elle n'a donc rien à demander.
/// ═════════════════════════════════════════════════════════════════════════════
class _ServiceIcon extends ConsumerWidget {
  const _ServiceIcon({
    required this.restaurantId,
    required this.acceptsOrdersNow,
    required this.blockedReason,
  });

  final String restaurantId;
  final bool acceptsOrdersNow;
  final String blockedReason;

  /// Durées proposées. DES PROPOSITIONS, PAS UNE SAISIE LIBRE : on ferme en
  /// plein service, et choisir « 30 min » d'un appui vaut mieux que taper un
  /// nombre. Elles couvrent le coup de feu, la panne, et la fin de journée.
  static const _durees = <({int minutes, String label})>[
    (minutes: 15, label: '15 minutes'),
    (minutes: 30, label: '30 minutes'),
    (minutes: 60, label: '1 heure'),
    (minutes: 180, label: '3 heures'),
  ];

  Future<void> _pause(BuildContext context, WidgetRef ref) async {
    final minutes = await showModalBottomSheet<int>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(20, 20, 20, 4),
              child: Text(
                'Suspendre la prise de commande',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
              ),
            ),
            const Padding(
              padding: EdgeInsets.fromLTRB(20, 0, 20, 12),
              child: Text(
                'Le service reprend seul à la fin du délai. '
                'Les commandes déjà acceptées ne sont pas touchées.',
                style: TextStyle(fontSize: 13.5, height: 1.4),
              ),
            ),
            for (final d in _durees)
              ListTile(
                leading: const Icon(Icons.pause_circle_outline),
                title: Text(d.label),
                onTap: () => Navigator.of(sheetContext).pop(d.minutes),
              ),
          ],
        ),
      ),
    );

    if (minutes == null || !context.mounted) return;

    try {
      await ref.read(menuApiProvider).pauseService(restaurantId, minutes: minutes);
      ref.invalidate(restaurantMenuProvider(restaurantId));
      if (context.mounted) {
        AppNotify.success(context, 'Service suspendu pour $minutes minutes.');
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  Future<void> _resume(BuildContext context, WidgetRef ref) async {
    try {
      await ref.read(menuApiProvider).resumeService(restaurantId);
      ref.invalidate(restaurantMenuProvider(restaurantId));
      if (context.mounted) AppNotify.success(context, 'Service repris.');
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    // ON NE PROPOSE DE ROUVRIR QUE CE QUE LE RESTAURATEUR A FERMÉ.
    //
    // `acceptsOrdersNow` est faux pour CINQ raisons distinctes
    // (`OrderingBlockedReason`) : dossier non validé ou suspendu, hors horaires,
    // pause déclarée, ou carte entièrement épuisée. `POST .../resume` ne lève
    // QUE la pause déclarée. Proposer « Reprendre » à un établissement fermé
    // parce qu'il est 3 h du matin donnerait un bouton qui semble ne rien faire
    // — et le restaurateur chercherait la panne dans l'application.
    //
    // Comparaison au nom exact de l'énumération, et non `contains('pause')` :
    // une correspondance approximative attraperait le jour où un autre motif
    // contiendrait le mot.
    final enPause = !acceptsOrdersNow &&
        blockedReason.toLowerCase() == 'temporarilypaused';

    return InkWell(
      onTap: () => enPause ? _resume(context, ref) : _pause(context, ref),
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: AppTheme.minTapTarget,
        height: AppTheme.minTapTarget,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colors.line),
        ),
        child: Icon(
          acceptsOrdersNow ? Icons.wb_sunny_outlined : Icons.nightlight_outlined,
          size: 20,
          color: acceptsOrdersNow ? AppTheme.foodAmber : colors.subtle,
        ),
      ),
    );
  }
}

/// Le « … » d'une carte de plat. Même hauteur et même bordure que
/// [_GhostButton] : sur une même rangée, deux hauteurs se remarquent.
class _GhostIconButton extends StatelessWidget {
  const _GhostIconButton({required this.icon, required this.onTap});

  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: Container(
        width: 44,
        height: 40,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: colors.line),
        ),
        child: Icon(icon, size: 20, color: colors.ink),
      ),
    );
  }
}

class _GhostButton extends StatelessWidget {
  const _GhostButton({required this.label, required this.onTap});

  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final tint = onTap == null ? colors.subtle : colors.ink;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: Container(
        height: 40,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: colors.line),
        ),
        child: Text(
          label,
          style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: tint),
        ),
      ),
    );
  }
}
