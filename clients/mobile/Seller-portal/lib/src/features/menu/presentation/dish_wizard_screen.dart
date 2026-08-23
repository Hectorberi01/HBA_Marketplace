import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../../core/media/media_upload.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../menu_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// AJOUT D'UN PLAT —
/// `POST /api/food/partner/restaurants/{id}/categories/{sectionId}/items`.
///
/// L'ASSISTANT EN QUATRE ÉTAPES A ÉTÉ REMPLACÉ PAR CE FORMULAIRE.
///
/// `CreateMenuItemRequest(Name, BasePrice)` : DEUX champs, et rien d'autre. Les
/// étapes 2 à 4 collectaient six valeurs qu'aucune route n'accepte :
///
///   • DESCRIPTION — `PUT .../items/{id}` existe désormais (#214) et se remplit
///     sur la fiche du plat, après création.
///   • PHOTO — voir l'encadré suivant : elle EST demandée ici, et elle est
///     obligatoire.
///   • TEMPS DE PRÉPARATION et STATION DE CUISINE — portés par le domaine
///     (`PreparationMinutes`, `PreparationStationId`), absents du contrat de
///     création. Ils se règlent aujourd'hui côté administration.
///   • GROUPES D'OPTIONS et SUPPLÉMENTS — ceux-là ONT des routes
///     (`POST .../items/{id}/option-groups` puis `.../options`), mais ils
///     exigent que le plat existe déjà. Ils s'ajoutent donc après, sur le plat.
///   • LA CATÉGORIE — l'assistant proposait « Plats · Boissons · Desserts »,
///     trois libellés écrits en dur. Les sections sont celles de la CARTE du
///     restaurant, et elles portent l'identifiant que la route attend. Les
///     inventer aurait rangé le plat n'importe où — ou nulle part.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PHOTO EST OBLIGATOIRE, ET ELLE SE DEMANDE AVANT LA CRÉATION.
///
/// Le serveur refuse de vendre un plat sans photo (`MenuItem.IsOrderableAt`). Un
/// formulaire qui n'en demanderait pas produirait donc, en une touche, un plat
/// invisible des clients — et le restaurateur chercherait pourquoi personne ne
/// commande son poulet.
///
/// DEUX APPELS, ET LA CRÉATION N'EST PAS ATOMIQUE.
///
/// `POST .../items` crée le plat, `PUT .../items/{id}/image` y attache la photo.
/// Le second peut échouer alors que le premier a réussi. On ne prétend pas
/// l'inverse : dans ce cas le plat existe, il apparaît « Photo manquante » dans la
/// carte, et le message le dit. Le contraire — déposer d'abord — est impossible :
/// media-service veut un `ownerId`, donc un plat déjà créé.
///
/// C'est aussi POURQUOI la règle vit dans `IsOrderableAt` et non dans
/// `MenuItem.Create` : si le domaine exigeait la photo à la création, cet
/// enchaînement en deux temps serait irréalisable.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// « POPULAIRES » N'A JAMAIS ÉTÉ UNE SECTION.
///
/// L'ancien écran l'écartait de la liste assignable, avec raison : c'est une
/// sélection qui recoupe les autres. Elle n'existe pas non plus dans le domaine,
/// donc la question ne se pose plus.
/// ═════════════════════════════════════════════════════════════════════════════
class DishWizardScreen extends ConsumerStatefulWidget {
  const DishWizardScreen({super.key});

  @override
  ConsumerState<DishWizardScreen> createState() => _DishWizardScreenState();
}

class _DishWizardScreenState extends ConsumerState<DishWizardScreen> {
  final _name = TextEditingController();
  final _price = TextEditingController();
  String? _sectionId;
  bool _saving = false;

  /// La photo choisie, gardée EN MÉMOIRE jusqu'à la validation.
  ///
  /// ON NE PEUT PAS LA DÉPOSER TOUT DE SUITE : media-service exige un
  /// `ownerId`, et le plat n'existe pas encore. Un dépôt sous un identifiant
  /// bidon laisserait un fichier que rien ne rattache à rien.
  ///
  /// LES OCTETS, PAS LE CHEMIN. Sur iOS, le fichier rendu par le sélecteur vit
  /// dans un cache que le système peut vider entre la sélection et la validation —
  /// et l'envoi échouerait sur un « fichier introuvable » incompréhensible.
  Uint8List? _photo;
  String? _photoNom;

  @override
  void dispose() {
    _name.dispose();
    _price.dispose();
    super.dispose();
  }

  /// PAS DE DÉTOURAGE POUR UN PLAT, contrairement aux photos produit :
  /// `ImageProcessing.pickAndProcess` découpe le sujet sur fond blanc, ce qui
  /// jetterait l'assiette, la sauce et la table — soit exactement ce qui fait
  /// envie. La photo part telle quelle, seulement redimensionnée.
  Future<void> _choisirPhoto(ImageSource source) async {
    final fichier = await ImagePicker().pickImage(
      source: source,
      // Mêmes bornes que les photos produit : le serveur refuse au-delà de 5 Mo.
      imageQuality: 88,
      maxWidth: 2000,
      maxHeight: 2000,
    );
    if (fichier == null) return;

    final octets = await File(fichier.path).readAsBytes();
    if (!mounted) return;
    setState(() {
      _photo = octets;
      _photoNom = fichier.name;
    });
  }

  void _ouvrirSourcePhoto() {
    showModalBottomSheet<void>(
      context: context,
      builder: (feuille) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // L'appareil photo d'abord : le plat est là, devant le restaurateur.
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Prendre une photo'),
              onTap: () {
                Navigator.of(feuille).pop();
                _choisirPhoto(ImageSource.camera);
              },
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Choisir dans la galerie'),
              onTap: () {
                Navigator.of(feuille).pop();
                _choisirPhoto(ImageSource.gallery);
              },
            ),
          ],
        ),
      ),
    );
  }

  /// Le prix saisi, ou `null` s'il n'est pas exploitable.
  ///
  /// `double.tryParse` REFUSE LA VIRGULE, ET UN CLAVIER FRANÇAIS EN MET UNE.
  /// Sans la substitution, « 5,5 » rendait `null` et le bouton restait grisé
  /// sans que rien n'explique pourquoi.
  double? get _basePrice {
    final raw = _price.text.trim().replaceAll(',', '.');
    final value = double.tryParse(raw);
    return (value == null || value <= 0) ? null : value;
  }

  /// LA PHOTO ENTRE DANS LA CONDITION, ET LE BOUTON DIT POURQUOI IL EST GRISÉ.
  ///
  /// Un bouton inerte sans explication fait recommencer la saisie. Voir
  /// [_RaisonBlocage], qui écrit ce qui manque juste au-dessus.
  bool get _canSubmit =>
      !_saving &&
      _name.text.trim().length >= 2 &&
      _basePrice != null &&
      _sectionId != null &&
      _photo != null;

  Future<void> _submit(String restaurantId) async {
    setState(() => _saving = true);
    try {
      final dishId = await ref.read(menuApiProvider).createDish(
            restaurantId,
            sectionId: _sectionId!,
            name: _name.text.trim(),
            basePrice: _basePrice!,
          );

      // ── La photo, en deux temps : dépôt puis rattachement ──────────────────
      //
      // L'ÉCHEC ICI NE DÉFAIT PAS LA CRÉATION, et on ne l'annule pas non plus.
      //
      // Supprimer le plat qu'on vient de créer parce que sa photo n'est pas passée
      // ferait perdre le nom, le prix et la section pour un problème de réseau. Le
      // plat reste, marqué « Photo manquante », et le message dit exactement quoi
      // faire — c'est réparable en deux touches depuis la fiche.
      var photoOk = false;
      try {
        final depose = await ref.read(mediaApiProvider).uploadBytes(
              bytes: _photo!,
              fileName: _photoNom ?? 'plat.jpg',
              ownerType: MediaOwner.menuItem,
              ownerId: dishId,
              mediaType: MediaKind.restaurantMedia,
            );
        await ref.read(menuApiProvider).setDishImage(
              restaurantId,
              dishId: dishId,
              mediaId: depose.mediaId,
              url: depose.url,
            );
        photoOk = true;
      } catch (_) {
        // Rien à faire ici : le message ci-dessous porte l'information, et
        // l'exception exacte n'aiderait pas un restaurateur en plein service.
      }

      // La création répond `201 { id }` sans le plat : il faut relire la carte,
      // sinon le restaurateur revient sur une liste où son plat n'est pas.
      ref.invalidate(restaurantMenuProvider(restaurantId));

      if (!mounted) return;
      context.pop();
      if (photoOk) {
        AppNotify.success(context, 'Plat ajouté à la carte.');
      } else {
        // SUCCÈS PARTIEL DIT COMME TEL. « Plat ajouté » seul mentirait sur ce
        // qui compte : le plat ne se vend pas encore.
        AppNotify.error(
          context,
          'Plat créé, mais la photo n\'a pas pu être envoyée. '
          'Il ne sera pas vendu tant qu\'elle manque — ouvrez sa fiche pour la '
          'réessayer.',
        );
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    // LE `restaurantId` NE VIENT PAS DE L'ACTIVITÉ COURANTE.
    //
    // Cet écran est atteint HORS de la coquille (`/dish/new`), et l'activité
    // sélectionnée peut être une boutique si le partenaire a basculé entre-temps.
    // `GET /api/food/partner/me` résout l'appartenance depuis le JETON : c'est la
    // seule source, et elle est unique (un compte est membre d'au plus un
    // établissement).
    final restaurant = ref.watch(partnerRestaurantProvider);

    return Scaffold(
      backgroundColor: colors.surface,
      appBar: AppBar(title: const Text('Nouveau plat')),
      body: restaurant.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(partnerRestaurantProvider),
        ),
        data: (r) => _Form(
          restaurantId: r.restaurantId,
          nameController: _name,
          priceController: _price,
          selectedSectionId: _sectionId,
          onSectionChanged: (id) => setState(() => _sectionId = id),
          onChanged: () => setState(() {}),
          photo: _photo,
          onPickPhoto: _saving ? null : _ouvrirSourcePhoto,
          canSubmit: _canSubmit,
          saving: _saving,
          onSubmit: () => _submit(r.restaurantId),
        ),
      ),
    );
  }
}

class _Form extends ConsumerWidget {
  const _Form({
    required this.restaurantId,
    required this.nameController,
    required this.priceController,
    required this.selectedSectionId,
    required this.onSectionChanged,
    required this.onChanged,
    required this.photo,
    required this.onPickPhoto,
    required this.canSubmit,
    required this.saving,
    required this.onSubmit,
  });

  final String restaurantId;
  final TextEditingController nameController;
  final TextEditingController priceController;
  final String? selectedSectionId;
  final ValueChanged<String> onSectionChanged;
  final VoidCallback onChanged;
  final Uint8List? photo;
  final VoidCallback? onPickPhoto;
  final bool canSubmit;
  final bool saving;
  final VoidCallback onSubmit;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final menu = ref.watch(restaurantMenuProvider(restaurantId));

    return menu.when(
      loading: () => const LoadingView(),
      error: (e, _) => ErrorView(
        message: e.toString(),
        onRetry: () => ref.invalidate(restaurantMenuProvider(restaurantId)),
      ),
      data: (m) {
        // Toutes les sections, toutes cartes confondues, avec le nom de leur
        // carte quand il y en a plusieurs : « Midi › Entrées » et « Soir ›
        // Entrées » sont deux sections distinctes, et le restaurateur doit
        // pouvoir les distinguer.
        final sections = <({String id, String label})>[
          for (final menu in m.menus)
            for (final s in menu.sections)
              (
                id: s.id,
                label: m.menus.length > 1 ? '${menu.name} › ${s.name}' : s.name,
              ),
        ];

        // SANS SECTION, LA ROUTE N'A NULLE PART OÙ POSER LE PLAT.
        //
        // On refuse le formulaire plutôt que de laisser saisir un nom et un prix
        // pour rien. Créer une carte et une section a bien un amont
        // (`POST .../menus`, `.../categories`) : c'est un écran qui manque, pas
        // une route.
        if (sections.isEmpty) {
          return const EmptyView(
            message: 'Aucune section de carte n\'existe encore.\n'
                'Un plat doit être rangé dans une section.',
            icon: Icons.menu_book_outlined,
          );
        }

        return ListView(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 32),
          children: [
            // LA PHOTO EN PREMIER, PARCE QU'ELLE EST OBLIGATOIRE.
            //
            // La mettre en bas ferait remplir nom, prix et section, appuyer sur un
            // bouton grisé, et remonter chercher pourquoi. Ce qui bloque se demande
            // d'abord.
            _PhotoField(bytes: photo, onTap: onPickPhoto),
            const SizedBox(height: 20),

            TextField(
              controller: nameController,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => onChanged(),
              decoration: const InputDecoration(
                labelText: 'Nom du plat',
                hintText: 'Poulet braisé',
              ),
            ),
            const SizedBox(height: 16),

            TextField(
              controller: priceController,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
              onChanged: (_) => onChanged(),
              decoration: const InputDecoration(
                labelText: 'Prix de base (${Format.cfa})',
                hintText: '5500',
                // Le prix HORS options : les suppléments s'y ajoutent au panier,
                // et leur total dépend de ce que le client choisit.
                helperText: 'Hors suppléments et options.',
              ),
            ),
            const SizedBox(height: 20),

            Text(
              'SECTION DE LA CARTE',
              style: TextStyle(
                fontSize: 11,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.9,
                color: colors.subtle,
              ),
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final s in sections)
                  ChoiceChip(
                    label: Text(s.label),
                    selected: s.id == selectedSectionId,
                    onSelected: (_) => onSectionChanged(s.id),
                  ),
              ],
            ),
            const SizedBox(height: 20),

            _MissingFieldsNote(colors: colors),
            const SizedBox(height: 16),

            // LE BOUTON GRISÉ DOIT DIRE POURQUOI, AU MÊME ENDROIT QUE LUI.
            //
            // C'est le défaut corrigé en 3.6 sur « Mettre en vente » : un bouton
            // inerte dont la raison est écrite ailleurs — ou nulle part — fait
            // recommencer la saisie au hasard.
            if (!canSubmit && !saving)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: _RaisonBlocage(
                  colors: colors,
                  manquants: [
                    if (photo == null) 'une photo',
                    if (nameController.text.trim().length < 2) 'un nom',
                    if (priceController.text.trim().isEmpty) 'un prix',
                    if (selectedSectionId == null) 'une section',
                  ],
                ),
              ),

            FilledButton(
              onPressed: canSubmit ? onSubmit : null,
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
              ),
              child: saving
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                    )
                  : const Text('Ajouter à la carte'),
            ),
          ],
        );
      },
    );
  }
}

/// Le cadre de la photo : vide et rouge, ou l'aperçu et un bouton de changement.
class _PhotoField extends StatelessWidget {
  const _PhotoField({required this.bytes, required this.onTap});

  final Uint8List? bytes;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final image = bytes;

    return GestureDetector(
      onTap: onTap,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        child: Container(
          height: 170,
          width: double.infinity,
          color: image == null ? AppTheme.dangerSoft : colors.bg,
          child: image == null
              ? Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 28),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.add_a_photo_outlined,
                          size: 32, color: AppTheme.danger),
                      const SizedBox(height: 10),
                      const Text(
                        'Photo du plat — obligatoire',
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w800,
                          color: AppTheme.danger,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        // Le POURQUOI, pas la règle. « Obligatoire » seul se lit
                        // comme une tracasserie ; la conséquence se comprend.
                        'Sans photo, un plat n\'est pas proposé aux clients.',
                        textAlign: TextAlign.center,
                        style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
                      ),
                    ],
                  ),
                )
              : Stack(
                  fit: StackFit.expand,
                  children: [
                    // `Image.memory`, PAS `Image.file` : les octets sont déjà en
                    // mémoire, et sur iOS le fichier du sélecteur vit dans un cache
                    // que le système peut vider avant la validation.
                    Image.memory(image, fit: BoxFit.cover),
                    Positioned(
                      right: 10,
                      bottom: 10,
                      child: Material(
                        color: Colors.black54,
                        shape: const StadiumBorder(),
                        child: const Padding(
                          padding: EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                          child: Text(
                            'Changer',
                            style: TextStyle(
                              fontSize: 12.5,
                              fontWeight: FontWeight.w700,
                              color: Colors.white,
                            ),
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

/// Écrit ce qui manque, au-dessus du bouton qui refuse.
class _RaisonBlocage extends StatelessWidget {
  const _RaisonBlocage({required this.colors, required this.manquants});

  final AppColors colors;
  final List<String> manquants;

  @override
  Widget build(BuildContext context) {
    if (manquants.isEmpty) return const SizedBox.shrink();

    // « une photo et un prix », « une photo, un nom et un prix » — la liste se lit,
    // une énumération à virgules non.
    final texte = manquants.length == 1
        ? manquants.first
        : '${manquants.sublist(0, manquants.length - 1).join(', ')} '
            'et ${manquants.last}';

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Icon(Icons.error_outline, size: 17, color: AppTheme.danger),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            'Il manque $texte.',
            style: const TextStyle(
              fontSize: 12.5,
              height: 1.4,
              fontWeight: FontWeight.w600,
              color: AppTheme.danger,
            ),
          ),
        ),
      ],
    );
  }
}

/// Dit ce que le formulaire ne demande pas, et pourquoi.
class _MissingFieldsNote extends StatelessWidget {
  const _MissingFieldsNote({required this.colors});

  final AppColors colors;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: colors.bg,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(color: colors.line),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(Icons.info_outline, size: 18, color: colors.subtle),
            const SizedBox(width: 10),
            Expanded(
              // CETTE NOTE DISAIT « DESCRIPTION, PHOTO … NE SONT PAS ENCORE
              // MODIFIABLES ». Elle était vraie, puis elle a cessé de l'être sans
              // que personne ne la relise : #214 a ouvert `PUT .../items/{id}`, et
              // la photo a maintenant sa route. Une note obsolète est pire qu'une
              // absence de note — elle fait renoncer à un geste possible.
              child: Text(
                'La description se remplit sur la fiche du plat, après création. '
                'Temps de préparation et poste de cuisine se règlent côté '
                'administration.',
                style: TextStyle(fontSize: 12.5, height: 1.45, color: colors.subtle),
              ),
            ),
          ],
        ),
      );
}
