import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../../core/media/media_upload.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../menu_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// FICHE D'UN PLAT — voir, photographier, modifier.
///
/// POURQUOI UN ÉCRAN, ALORS QU'UNE FEUILLE EXISTAIT DÉJÀ.
///
/// `_EditDishSheet` (dans la carte) porte trois champs et rend la main. Elle
/// suffisait tant que le plat n'avait rien d'autre. La photo change cela : elle
/// demande une prévisualisation, un dépôt qui prend plusieurs secondes, un état
/// d'échec réessayable, et un cadre assez grand pour juger si l'image est bonne.
/// Rien de tout cela ne tient dans une feuille de 40 % d'écran.
///
/// La feuille reste : c'est le geste rapide, « corriger un prix ». La fiche est
/// le geste complet.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PHOTO EST OBLIGATOIRE POUR VENDRE, ET C'EST LE SERVEUR QUI L'IMPOSE.
///
/// `MenuItem.IsOrderableAt` refuse tout plat sans `ImageMediaId`. Ce n'est donc
/// pas une validation de formulaire que cet écran pourrait contourner : un plat
/// sans photo existe, se modifie, et NE SE VEND PAS. L'écran ne fait que rendre
/// cette conséquence lisible — c'est tout ce qu'il peut faire, et c'est
/// suffisant.
///
/// TROIS APPELS SÉPARÉS, ET AUCUNE TRANSACTION POUR LES TENIR.
///
/// Nom/description (`PUT .../items/{id}`), prix (`PUT .../items/{id}/price`) et
/// photo (`PUT .../items/{id}/image`) sont trois routes. Un échec au deuxième
/// laisse le premier appliqué. On ne fait donc PAS semblant d'un enregistrement
/// atomique : chaque bloc a son propre bouton et sa propre confirmation, et le
/// restaurateur voit exactement ce qui a été pris. Un unique « Enregistrer »
/// aurait affiché « échec » sur un nom déjà changé.
/// ═════════════════════════════════════════════════════════════════════════════
class DishDetailScreen extends ConsumerWidget {
  const DishDetailScreen({super.key, required this.restaurantId, required this.dishId});

  final String restaurantId;
  final String dishId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final menu = ref.watch(restaurantMenuProvider(restaurantId));

    return Scaffold(
      backgroundColor: colors.surface,
      appBar: AppBar(title: const Text('Fiche du plat')),
      body: menu.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(restaurantMenuProvider(restaurantId)),
        ),
        data: (m) {
          // LE PLAT SE RETROUVE DANS LA CARTE, IL N'A PAS DE ROUTE À LUI.
          //
          // `GET .../items/{id}` n'existe pas côté serveur, et l'inventer pour cet
          // écran ajouterait une route dont la garde d'appartenance serait à écrire
          // et à tester. La carte est déjà chargée, déjà gardée, et déjà relue après
          // chaque geste : le plat s'y cherche.
          Dish? dish;
          String? sectionName;
          for (final carte in m.menus) {
            for (final section in carte.sections) {
              for (final plat in section.items) {
                if (plat.id == dishId) {
                  dish = plat;
                  sectionName = m.menus.length > 1
                      ? '${carte.name} › ${section.name}'
                      : section.name;
                }
              }
            }
          }

          if (dish == null) {
            // Un plat supprimé depuis un autre appareil, ou une relecture qui a
            // croisé une suppression. Ce n'est pas une erreur : c'est un écran qui
            // n'a plus d'objet.
            return const EmptyView(
              message: 'Ce plat n\'est plus dans la carte.\n'
                  'Il a peut-être été supprimé.',
              icon: Icons.no_meals_outlined,
            );
          }

          return _Body(
            restaurantId: restaurantId,
            dish: dish,
            sectionName: sectionName ?? '',
          );
        },
      ),
    );
  }
}

class _Body extends ConsumerStatefulWidget {
  const _Body({required this.restaurantId, required this.dish, required this.sectionName});

  final String restaurantId;
  final Dish dish;
  final String sectionName;

  @override
  ConsumerState<_Body> createState() => _BodyState();
}

class _BodyState extends ConsumerState<_Body> {
  /// CONTRÔLEURS POSSÉDÉS PAR L'ÉTAT, JAMAIS PRÊTÉS À UNE FEUILLE.
  ///
  /// Trois exceptions « TextEditingController used after being disposed » ont été
  /// corrigées dans ce dossier, toutes de la même cause : un contrôleur créé par
  /// l'appelant, passé à un dialogue, et libéré dès le retour de l'`await` — alors
  /// que l'animation de fermeture reconstruit encore les champs.
  late final TextEditingController _nom;
  late final TextEditingController _description;
  late final TextEditingController _prix;

  bool _enregistreTexte = false;
  bool _enregistrePrix = false;
  bool _envoiePhoto = false;

  @override
  void initState() {
    super.initState();
    _nom = TextEditingController(text: widget.dish.name);
    _description = TextEditingController(text: widget.dish.description ?? '');
    _prix = TextEditingController(text: widget.dish.basePrice.toStringAsFixed(0));
  }

  @override
  void dispose() {
    _nom.dispose();
    _description.dispose();
    _prix.dispose();
    super.dispose();
  }

  Dish get _dish => widget.dish;
  MenuApi get _api => ref.read(menuApiProvider);

  double? get _prixSaisi {
    // `double.tryParse` REFUSE LA VIRGULE, ET UN CLAVIER FRANÇAIS EN MET UNE.
    final brut = _prix.text.trim().replaceAll(',', '.');
    final v = double.tryParse(brut);
    return (v == null || v <= 0) ? null : v;
  }

  /// Exécute [action] puis RELIT la carte — les routes rendent 204, sans le plat.
  Future<bool> _run(Future<void> Function() action, String succes) async {
    try {
      await action();
      ref.invalidate(restaurantMenuProvider(widget.restaurantId));
      if (mounted) AppNotify.success(context, succes);
      return true;
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
      return false;
    }
  }

  // ── Photo ───────────────────────────────────────────────────────────────────

  /// PAS DE DÉTOURAGE POUR UN PLAT, CONTRAIREMENT AUX PRODUITS.
  ///
  /// `ImageProcessing.pickAndProcess` détoure sur fond blanc, ce qui donne au
  /// catalogue son aspect homogène. Appliqué à un plat, cela découpe le poulet et
  /// jette l'assiette, la sauce et la table — soit exactement ce qui fait envie.
  /// La photo d'un plat part telle quelle, seulement redimensionnée.
  Future<void> _changerPhoto(ImageSource source) async {
    final fichier = await ImagePicker().pickImage(
      source: source,
      // Mêmes bornes que les photos produit : le serveur refuse au-delà de 5 Mo et
      // un capteur récent en produit 8 sans effort.
      imageQuality: 88,
      maxWidth: 2000,
      maxHeight: 2000,
    );
    if (fichier == null || !mounted) return;

    setState(() => _envoiePhoto = true);
    try {
      final octets = await File(fichier.path).readAsBytes();

      // ── 1. dépôt sur media-service ────────────────────────────────────────
      final depose = await ref.read(mediaApiProvider).uploadBytes(
            bytes: octets,
            fileName: fichier.name,
            // `MenuItem` EST UN TYPE DE PROPRIÉTAIRE À PART ENTIÈRE côté
            // serveur (`MediaOwnerType.MenuItem`). Déposer sous `Restaurant`
            // rangerait la photo du plat avec le logo de l'établissement, et
            // `ListByOwnerAsync` ne pourrait plus les distinguer.
            ownerType: MediaOwner.menuItem,
            ownerId: _dish.id,
            mediaType: MediaKind.restaurantMedia,
          );

      // ── 2. rattachement au plat ───────────────────────────────────────────
      //
      // UN ÉCHEC ICI LAISSE UN FICHIER ORPHELIN dans media-service. C'est le
      // moindre mal : l'inverse — un plat qui référence une image inexistante —
      // afficherait une vignette cassée que personne ne peut réparer.
      await _run(
        () => _api.setDishImage(
          widget.restaurantId,
          dishId: _dish.id,
          mediaId: depose.mediaId,
          url: depose.url,
        ),
        'Photo enregistrée. Le plat peut être vendu.',
      );
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _envoiePhoto = false);
    }
  }

  Future<void> _retirerPhoto() async {
    // LA CONFIRMATION DIT LA CONSÉQUENCE, PAS « ÊTES-VOUS SÛR ».
    //
    // Retirer la photo rend le plat INVENDABLE. Un « êtes-vous sûr ? » se clique
    // sans lire ; « le plat ne sera plus vendu » se lit.
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Retirer la photo ?'),
        content: const Text(
          'Sans photo, ce plat n\'apparaît plus aux clients et ne peut pas être '
          'commandé, même s\'il reste dans votre carte.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(c).pop(false), child: const Text('Annuler')),
          TextButton(
            onPressed: () => Navigator.of(c).pop(true),
            child: const Text('Retirer', style: TextStyle(color: AppTheme.danger)),
          ),
        ],
      ),
    );
    if (ok != true) return;

    setState(() => _envoiePhoto = true);
    await _run(
      () => _api.setDishImage(widget.restaurantId, dishId: _dish.id, mediaId: null, url: null),
      'Photo retirée. Le plat n\'est plus vendu.',
    );
    if (mounted) setState(() => _envoiePhoto = false);
  }

  void _ouvrirSourcePhoto() {
    showModalBottomSheet<void>(
      context: context,
      builder: (feuille) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // L'APPAREIL PHOTO EN PREMIER, ET DANS CET ORDRE POUR UNE RAISON.
            //
            // Un restaurateur photographie son plat au moment où il le sort. La
            // galerie sert au rattrapage ; la mettre en tête ferait chercher dans
            // des captures d'écran une photo qui n'existe pas encore.
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Prendre une photo'),
              onTap: () {
                Navigator.of(feuille).pop();
                _changerPhoto(ImageSource.camera);
              },
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Choisir dans la galerie'),
              onTap: () {
                Navigator.of(feuille).pop();
                _changerPhoto(ImageSource.gallery);
              },
            ),
            if (_dish.hasImage) ...[
              const Divider(height: 1),
              ListTile(
                leading: const Icon(Icons.delete_outline, color: AppTheme.danger),
                title: const Text('Retirer la photo',
                    style: TextStyle(color: AppTheme.danger)),
                subtitle: const Text('Le plat ne sera plus vendu.'),
                onTap: () {
                  Navigator.of(feuille).pop();
                  _retirerPhoto();
                },
              ),
            ],
          ],
        ),
      ),
    );
  }

  // ── Texte et prix ───────────────────────────────────────────────────────────

  Future<void> _enregistrerTexte() async {
    setState(() => _enregistreTexte = true);
    final description = _description.text.trim();
    await _run(
      () => _api.updateDish(
        widget.restaurantId,
        dishId: _dish.id,
        name: _nom.text.trim(),
        // Une description vide vaut `null` : envoyer la chaîne vide écrirait une
        // description « présente mais blanche », que la vitrine afficherait comme
        // un espace sous le nom du plat.
        description: description.isEmpty ? null : description,
      ),
      'Plat modifié.',
    );
    if (mounted) setState(() => _enregistreTexte = false);
  }

  Future<void> _enregistrerPrix() async {
    final prix = _prixSaisi;
    if (prix == null) return;

    setState(() => _enregistrePrix = true);
    await _run(
      () => _api.changeDishPrice(widget.restaurantId, dishId: _dish.id, basePrice: prix),
      'Prix mis à jour.',
    );
    if (mounted) setState(() => _enregistrePrix = false);
  }

  // ── Rendu ───────────────────────────────────────────────────────────────────

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final statut = _dish.status;

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 40),
      children: [
        _PhotoBloc(
          dish: _dish,
          envoi: _envoiePhoto,
          onTap: _envoiePhoto ? null : _ouvrirSourcePhoto,
        ),
        const SizedBox(height: 16),

        // LE STATUT EST AU-DESSUS DES CHAMPS, PAS EN BAS DE PAGE.
        //
        // « Photo manquante » lu après avoir corrigé un prix arrive trop tard : le
        // restaurateur est venu ici pour une raison, et si son plat n'est pas
        // vendu, c'est la seule qui compte.
        _StatutBloc(status: statut, backAt: _dish.backAt),
        const SizedBox(height: 20),

        if (widget.sectionName.isNotEmpty) ...[
          PartnerSectionTitle(widget.sectionName),
          const SizedBox(height: 10),
        ],

        PartnerCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextField(
                controller: _nom,
                textCapitalization: TextCapitalization.sentences,
                onChanged: (_) => setState(() {}),
                decoration: const InputDecoration(labelText: 'Nom du plat'),
              ),
              const SizedBox(height: 14),
              TextField(
                controller: _description,
                maxLines: 3,
                textCapitalization: TextCapitalization.sentences,
                onChanged: (_) => setState(() {}),
                decoration: const InputDecoration(
                  labelText: 'Description',
                  hintText: 'Poulet mariné, riz au gras, piment doux',
                  helperText: 'Ce que le client lit avant de choisir.',
                ),
              ),
              const SizedBox(height: 14),
              FilledButton(
                onPressed: (_enregistreTexte || _nom.text.trim().length < 2)
                    ? null
                    : _enregistrerTexte,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                ),
                child: _enregistreTexte
                    ? const _Attente()
                    : const Text('Enregistrer le nom et la description'),
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),

        // LE PRIX A SA PROPRE CARTE ET SON PROPRE BOUTON.
        //
        // `PUT .../items/{id}/price` est une route distincte parce que changer un
        // prix est un fait commercial que le domaine traite à part. Le fondre dans
        // le bloc précédent ferait passer une hausse de prix pour une correction de
        // faute de frappe — pour le serveur comme pour le restaurateur.
        PartnerCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextField(
                controller: _prix,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
                onChanged: (_) => setState(() {}),
                decoration: InputDecoration(
                  labelText: 'Prix de base (${Format.cfa})',
                  helperText: 'Hors suppléments et options.',
                ),
              ),
              const SizedBox(height: 14),
              FilledButton(
                onPressed: (_enregistrePrix ||
                        _prixSaisi == null ||
                        _prixSaisi == _dish.basePrice)
                    ? null
                    : _enregistrerPrix,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                ),
                child: _enregistrePrix ? const _Attente() : const Text('Mettre à jour le prix'),
              ),
            ],
          ),
        ),

        // ── Options, en lecture seule ────────────────────────────────────────
        if (_dish.optionGroups.isNotEmpty) ...[
          const SizedBox(height: 20),
          const PartnerSectionTitle('Suppléments et options'),
          const SizedBox(height: 10),
          for (final groupe in _dish.optionGroups)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: PartnerCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            groupe.name,
                            style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
                          ),
                        ),
                        if (groupe.isRequired)
                          PartnerStatusDot(
                            label: 'Obligatoire',
                            color: AppTheme.info,
                            background: const Color(0xFFEAF1FE),
                          ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    for (final o in groupe.options)
                      Padding(
                        padding: const EdgeInsets.only(top: 4),
                        child: Row(
                          children: [
                            Icon(
                              o.isAvailable
                                  ? Icons.check_circle_outline
                                  : Icons.remove_circle_outline,
                              size: 15,
                              color: o.isAvailable ? AppTheme.brandGreen : colors.subtle,
                            ),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                o.name,
                                style: TextStyle(
                                  fontSize: 13,
                                  color: o.isAvailable ? colors.ink : colors.subtle,
                                ),
                              ),
                            ),
                            Text(
                              // UN ÉCART, PAS UN PRIX : `+500` et `−200` se lisent
                              // comme des suppléments ; « 500 F » se lirait comme le
                              // prix de l'option seule.
                              o.priceDelta == 0
                                  ? 'Inclus'
                                  : '${o.priceDelta > 0 ? '+' : '−'}'
                                      '${Format.money(o.priceDelta.abs(), _dish.currency)}',
                              style: TextStyle(
                                fontSize: 12.5,
                                fontWeight: FontWeight.w600,
                                color: colors.subtle,
                              ),
                            ),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
            ),

          // LECTURE SEULE, ET IL FAUT LE DIRE PLUTÔT QUE DE LAISSER CHERCHER.
          //
          // `POST .../items/{id}/option-groups` et `.../options` existent — donc on
          // POURRAIT les modifier ici. Ce qui manque, c'est la modification et la
          // suppression d'un groupe existant : ouvrir l'ajout seul donnerait un
          // écran où l'on empile des groupes sans jamais pouvoir en retirer un.
          const SizedBox(height: 6),
          Text(
            'Les suppléments ne sont pas encore modifiables depuis l\'application : '
            'on peut les ajouter, pas les retirer, et un écran qui ne sait qu\'ajouter '
            'ferait plus de dégâts que de bien.',
            style: TextStyle(fontSize: 12, height: 1.45, color: colors.subtle),
          ),
        ],
      ],
    );
  }
}

/// Le bloc photo : grand, cliquable, et explicite quand elle manque.
class _PhotoBloc extends StatelessWidget {
  const _PhotoBloc({required this.dish, required this.envoi, required this.onTap});

  final Dish dish;
  final bool envoi;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final url = dish.imageUrl;

    return GestureDetector(
      onTap: onTap,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        child: Container(
          height: 200,
          width: double.infinity,
          color: dish.hasImage ? colors.bg : AppTheme.dangerSoft,
          child: Stack(
            fit: StackFit.expand,
            children: [
              if (url != null)
                Image.network(
                  url,
                  fit: BoxFit.cover,
                  // UNE PHOTO ENREGISTRÉE DONT L'ADRESSE NE RÉPOND PLUS N'EST
                  // PAS UNE PHOTO ABSENTE. Le plat reste vendable ; c'est un
                  // incident d'affichage. Dire « ajoutez une photo » ferait
                  // redéposer un fichier déjà là.
                  errorBuilder: (_, __, ___) => _PhotoVide(
                    icone: Icons.broken_image_outlined,
                    titre: 'Photo indisponible',
                    detail: 'L\'image est enregistrée mais ne s\'affiche pas. '
                        'Touchez pour la remplacer.',
                    alerte: false,
                  ),
                )
              else
                _PhotoVide(
                  icone: Icons.add_a_photo_outlined,
                  titre: 'Aucune photo',
                  detail: 'La photo est obligatoire : sans elle, ce plat n\'est '
                      'pas proposé aux clients. Touchez pour l\'ajouter.',
                  alerte: true,
                ),

              if (envoi)
                Container(
                  color: Colors.black38,
                  alignment: Alignment.center,
                  child: const CircularProgressIndicator(color: Colors.white),
                ),

              // Le bouton de changement n'apparaît que s'il y a quelque chose à
              // changer — sur un cadre vide, tout le bloc est déjà le bouton.
              if (url != null && !envoi)
                Positioned(
                  right: 10,
                  bottom: 10,
                  child: Material(
                    color: Colors.black54,
                    shape: const StadiumBorder(),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                      child: Row(
                        children: const [
                          Icon(Icons.photo_camera_outlined, size: 16, color: Colors.white),
                          SizedBox(width: 6),
                          Text(
                            'Changer',
                            style: TextStyle(
                              fontSize: 12.5,
                              fontWeight: FontWeight.w700,
                              color: Colors.white,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _PhotoVide extends StatelessWidget {
  const _PhotoVide({
    required this.icone,
    required this.titre,
    required this.detail,
    required this.alerte,
  });

  final IconData icone;
  final String titre;
  final String detail;
  final bool alerte;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final teinte = alerte ? AppTheme.danger : colors.subtle;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 28),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icone, size: 34, color: teinte),
          const SizedBox(height: 10),
          Text(
            titre,
            style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w800, color: teinte),
          ),
          const SizedBox(height: 6),
          Text(
            detail,
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
          ),
        ],
      ),
    );
  }
}

/// Ce que le plat vaut aujourd'hui, en une phrase et un badge.
///
/// LE MOTIF, PUIS LE GESTE. Un statut qui ne dit pas quoi faire n'aide pas :
/// « Épuisé aujourd'hui » sans « revient demain matin » laisse croire qu'il faut
/// agir, et « Photo manquante » sans « le plat n'est pas vendu » laisse croire
/// qu'il ne faut pas.
class _StatutBloc extends StatelessWidget {
  const _StatutBloc({required this.status, required this.backAt});

  final DishStatus status;
  final DateTime? backAt;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    final (label, couleur, fond, phrase) = switch (status) {
      DishStatus.enVente => (
          'En vente',
          AppTheme.brandGreen,
          AppTheme.brandGreenSoft,
          'Ce plat est proposé aux clients dès que sa carte est servie.',
        ),
      DishStatus.photoManquante => (
          'Photo manquante',
          AppTheme.danger,
          AppTheme.dangerSoft,
          'Ce plat n\'est PAS vendu. Ajoutez une photo pour le mettre en vente.',
        ),
      DishStatus.epuiseAujourdhui => (
          'Épuisé aujourd\'hui',
          AppTheme.slate,
          const Color(0xFFEDEFF1),
          backAt != null
              ? 'De retour le ${Format.date(backAt)}, sans rien avoir à faire.'
              : 'De retour au prochain service, sans rien avoir à faire.',
        ),
      DishStatus.retire => (
          'Retiré de la carte',
          AppTheme.slate,
          const Color(0xFFEDEFF1),
          'Ce plat ne revient pas seul : il faut le remettre en vente.',
        ),
    };

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          PartnerStatusDot(label: label, color: couleur, background: fond),
          const SizedBox(height: 8),
          Text(phrase, style: TextStyle(fontSize: 13, height: 1.45, color: colors.subtle)),
        ],
      ),
    );
  }
}

class _Attente extends StatelessWidget {
  const _Attente();

  @override
  Widget build(BuildContext context) => const SizedBox(
        width: 20,
        height: 20,
        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
      );
}
