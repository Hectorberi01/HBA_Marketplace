import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../activities/activities_data.dart';
import '../../inventory/inventory_data.dart';
import '../../offers/offers_data.dart';
import '../../offers/presentation/offer_sheets.dart';
import '../catalog_data.dart';
import 'category_picker.dart';
import 'image_processing.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// CRÉATION D'UN PRODUIT, EN CINQ ÉTAPES.
///
/// L'ASSISTANT EST DE RETOUR, ET IL FAUT DIRE POURQUOI IL ÉTAIT PARTI.
///
/// Il avait été réduit à un formulaire plat parce que QUATRE de ses cinq étapes
/// n'avaient aucun amont : le prix vivait dans un module non extrait, le stock
/// était sous `MapAdminGroup` (403 pour un vendeur), les variantes proposaient
/// quatre options écrites en dur, et les photos étaient comptées sans être
/// déposées. Sa publication affichait « publié (simulation) » et sortait.
///
/// Les phases 2 et 3 ont fermé ces trois manques :
///
///   • ÉCRITURES DE STOCK — ouvertes au vendeur (VEN11), gardées par
///     `DenyUnlessOwnerAsync` sur le lieu d'expédition.
///   • MISES EN VENTE — greffées dans catalog-service (phase 3),
///     `POST /api/catalog/seller/offers`.
///   • DÉPÔT DE PHOTOS — media-service, via `productPhotoUploaderProvider`.
///
/// Chaque étape appelle donc une route RÉELLE. Ce qui n'est pas revenu, ce sont
/// les quatre variantes en dur (« Couleur : Noir / Argent ») : le contrat accepte
/// n'importe quel couple attribut/valeur, et les dicter reviendrait à décider ce
/// que le vendeur vend.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TOUT EST COLLECTÉ LOCALEMENT, ET RIEN N'EST ENVOYÉ AVANT L'ÉTAPE 5.
///
/// C'est le choix structurant de cet écran, et l'inverse aurait été plus simple à
/// écrire. Commettre au fil des étapes — produit créé en sortant de l'étape 2,
/// variantes en sortant de la 3 — aurait laissé, à chaque abandon, un brouillon
/// orphelin dans le catalogue du vendeur. Sur un réseau où l'on perd la
/// connexion en cours de saisie, ces brouillons s'accumulent, et personne ne sait
/// plus lesquels sont réels.
///
/// MAIS LA VALIDATION FINALE N'EST PAS ATOMIQUE, ET ON NE FAIT PAS SEMBLANT.
///
/// Il n'existe aucune route qui créerait produit + photos + variantes + offres +
/// stock en une transaction : ce sont trois services (catalog, media, inventory).
/// La séquence peut donc échouer au milieu. Dans ce cas [_publier] n'annule
/// RIEN — un retour arrière sur trois services sans transaction distribuée serait
/// plus dangereux que l'état partiel — mais il dit exactement où elle s'est
/// arrêtée et emmène le vendeur sur la FICHE de ce qui a été créé, où chaque
/// pièce manquante peut être ajoutée à la main. On ne perd jamais le produit.
///
/// LE PRODUIT NAÎT EN BROUILLON, ET C'EST LE SERVEUR QUI LE DÉCIDE.
///
/// `Product.Create` pose `status = draft`. La dernière étape propose de publier ;
/// refuser est légitime — un produit sans photo ne mérite pas la vitrine.
/// ═════════════════════════════════════════════════════════════════════════════
class ProductWizardScreen extends ConsumerStatefulWidget {
  const ProductWizardScreen({super.key});

  @override
  ConsumerState<ProductWizardScreen> createState() => _ProductWizardScreenState();
}

/// Une déclinaison en cours de saisie : elle n'existe pas encore côté serveur.
class _VariantDraft {
  _VariantDraft();

  final sku = TextEditingController();
  final attribut = TextEditingController();
  final valeur = TextEditingController();

  /// Quantité initiale en stock. PAR DÉCLINAISON, jamais par produit : c'est
  /// le SKU qu'inventory-service indexe, et deux tailles n'ont pas le même stock.
  final quantite = TextEditingController(text: '0');

  /// Vrai dès que le vendeur a modifié la référence lui-même.
  ///
  /// SANS CE DRAPEAU, LA SAISIE SERAIT ÉCRASÉE. Le code se régénère à chaque
  /// frappe sur le nom ou l'attribut ; si le vendeur corrige « CAFE-NOIR » en
  /// « CAF-2024 » puis retouche la couleur, sa correction disparaîtrait sous ses
  /// yeux. On ne reprend jamais la main sur un champ qu'il a touché.
  bool referenceManuelle = false;

  void dispose() {
    sku.dispose();
    attribut.dispose();
    valeur.dispose();
    quantite.dispose();
  }

  String get skuPropre => sku.text.trim();

  Map<String, String> get attributs {
    final a = attribut.text.trim();
    final v = valeur.text.trim();
    return (a.isEmpty || v.isEmpty) ? const {} : {a: v};
  }

  int get quantiteEntiere => int.tryParse(quantite.text.trim()) ?? 0;

  /// Propose une référence à partir du nom du produit et de la valeur choisie.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// POURQUOI ON L'ENGENDRE AU LIEU DE LA DEMANDER.
  ///
  /// « SKU » est un mot de logisticien. Un commerçant de Dantokpa ou de Ganhi ne
  /// l'a jamais rencontré, et le champ vide était le point d'abandon de
  /// l'assistant : on ne sait pas quoi écrire, donc on sort. Pire, une saisie
  /// improvisée — « 1 », « produit », « aaa » — produit des collisions dès le
  /// troisième article, et le serveur refuse en parlant de « déclinaison en
  /// double », ce qui n'éclaire personne.
  ///
  /// La référence n'a AUCUNE valeur commerciale : c'est une étiquette interne qui
  /// relie l'article à son stock. Rien ne justifie de la faire inventer par le
  /// vendeur. Elle reste modifiable, pour celui qui tient déjà un cahier de
  /// références.
  ///
  /// LA NORMALISATION SUIT `Sku.Create` : majuscules, et seulement lettres,
  /// chiffres, tirets et underscores, 64 caractères au plus. Les accents sont
  /// translittérés plutôt que supprimés — « Café » donne « CAFE », pas « CAF ».
  /// ═══════════════════════════════════════════════════════════════════════════
  static String proposerReference(String nomProduit, String valeurAttribut) {
    const accents = 'àâäáãåçèéêëìíîïñòóôöõùúûüýÿœæ';
    const sans = 'aaaaaaceeeeiiiinooooouuuuyyoa';

    String nettoyer(String brut) {
      final b = StringBuffer();
      for (final c in brut.toLowerCase().split('')) {
        final i = accents.indexOf(c);
        b.write(i >= 0 ? sans[i] : c);
      }
      return b
          .toString()
          .toUpperCase()
          .replaceAll(RegExp(r'[^A-Z0-9]+'), '-')
          .replaceAll(RegExp(r'-+'), '-')
          .replaceAll(RegExp(r'^-|-$'), '');
    }

    final base = nettoyer(nomProduit);
    final suffixe = nettoyer(valeurAttribut);
    final complet = suffixe.isEmpty ? base : '$base-$suffixe';

    // Tronqué proprement : `Sku.Create` refuse au-delà de 64, et un refus sur la
    // longueur d'une référence que l'application a elle-même fabriquée serait
    // incompréhensible pour le vendeur.
    return complet.length <= 64 ? complet : complet.substring(0, 64);
  }
}

class _ProductWizardScreenState extends ConsumerState<ProductWizardScreen> {
  static const _nbEtapes = 5;

  int _etape = 0;
  bool _envoi = false;

  /// Ce que la séquence d'envoi a déjà réussi. Sert au message d'échec partiel.
  String? _produitCree;

  // ── Étape 1 · Informations ────────────────────────────────────────────────
  final _nom = TextEditingController();
  final _description = TextEditingController();
  String? _categorieId;

  // ── Étape 2 · Photos ──────────────────────────────────────────────────────
  final List<ProcessedImage> _photos = [];

  // ── Étape 3 · Déclinaisons ────────────────────────────────────────────────
  final List<_VariantDraft> _variantes = [_VariantDraft()];

  // ── Étape 4 · Prix et stock ───────────────────────────────────────────────
  final _prix = TextEditingController();
  String _condition = kOfferConditions.first.value;
  String? _lieuId;
  String? _boutiqueId;
  int _delai = 2;

  double get _prixVendeur => double.tryParse(_prix.text.replaceAll(',', '.')) ?? 0;

  /// Vrai quand le vendeur a renseigné assez pour mettre en vente.
  ///
  /// L'ÉTAPE 4 EST FACULTATIVE, ET C'EST DÉLIBÉRÉ. Un vendeur qui découvre
  /// l'application n'a pas encore de lieu d'expédition ; l'exiger ici
  /// l'enfermerait à l'étape 4 de son tout premier produit. Sans prix, la fiche
  /// est créée et la mise en vente se fera plus tard.
  bool get _metEnVente => _prixVendeur > 0 && _lieuId != null;

  /// Les boutiques du vendeur (univers EXPRESS uniquement).
  ///
  /// UN RESTAURANT N'EST PAS UNE BOUTIQUE : son identifiant est un
  /// `RestaurantId`, que la route de mise en vente ne reconnaît pas.
  List<SellerActivity> _boutiquesDe(AsyncValue<BffResult<List<SellerActivity>>> a) =>
      (a.valueOrNull?.data ?? const <SellerActivity>[])
          .where((x) => x.universe == HbaUniverse.express)
          .toList();

  /// La boutique retenue : celle choisie, ou l'unique s'il n'y en a qu'une.
  ///
  /// CALCULÉE, JAMAIS AFFECTÉE DEPUIS `build`. Écrire un champ pendant la
  /// construction de l'arbre marche « la plupart du temps » et casse le jour où
  /// l'ordre de construction change — c'est le genre de bogue qu'on ne reproduit
  /// pas. Le repli sur la boutique unique se calcule donc à la lecture.
  String? get _boutiqueRetenue {
    if (_boutiqueId != null) return _boutiqueId;
    final b = _boutiquesDe(ref.read(activitiesProvider));
    return b.length == 1 ? b.first.id : null;
  }

  @override
  void dispose() {
    _nom.dispose();
    _description.dispose();
    _prix.dispose();
    for (final v in _variantes) {
      v.dispose();
    }
    super.dispose();
  }

  // ───────────────────────────────────────────────────────── validation d'étape

  /// Ce qui manque à l'étape courante, ou `null` si l'on peut avancer.
  ///
  /// REND UNE PHRASE, PAS UN BOOLÉEN. Un bouton grisé sans explication est la
  /// première cause d'abandon d'un formulaire : le vendeur voit qu'il ne peut
  /// pas avancer, jamais pourquoi.
  String? get _blocage {
    switch (_etape) {
      case 0:
        if (_nom.text.trim().length < 3) return 'Donnez un nom d\'au moins 3 caractères.';
        if (_categorieId == null) return 'Choisissez une catégorie.';
        return null;
      case 2:
        final remplies = _variantes.where((v) => v.skuPropre.isNotEmpty).toList();
        if (remplies.isEmpty) return 'Donnez un nom au produit : le code d\'article s\'en déduit.';
        final refs = remplies.map((v) => v.skuPropre.toUpperCase()).toList();
        if (refs.toSet().length != refs.length) {
          // Le serveur refuserait la seconde, après avoir accepté la première :
          // le vendeur se retrouverait avec un produit à moitié décliné.
          return 'Deux déclinaisons portent le même code d\'article. '
              'Précisez leur type de variante pour les distinguer.';
        }
        return null;
      case 3:
        // Un prix sans lieu d'expédition ne peut pas partir : la route de mise en
        // vente exige `shipFromLocationId`. On refuse la moitié plutôt que de la
        // laisser échouer à l'étape 5.
        if (_prixVendeur > 0 && _lieuId == null) {
          return 'Choisissez un lieu d\'expédition, ou effacez le prix.';
        }
        if (_prixVendeur > 0 && _boutiqueRetenue == null) {
          return 'Choisissez la boutique qui vend cet article.';
        }
        return null;
      default:
        return null;
    }
  }

  /// Réécrit la référence engendrée de la déclinaison [i], si le vendeur ne l'a
  /// pas reprise à son compte.
  void _rafraichirReference(int i) {
    final v = _variantes[i];
    if (!v.referenceManuelle) {
      v.sku.text = _VariantDraft.proposerReference(_nom.text, v.valeur.text);
    }
    setState(() {});
  }

  /// Remet toutes les références engendrées à jour — appelé quand le NOM change.
  ///
  /// LE NOM EST À L'ÉTAPE 1, LES RÉFÉRENCES À L'ÉTAPE 3. Un vendeur qui revient
  /// corriger une faute de frappe dans le nom doit voir ses codes suivre ; les
  /// laisser porter l'ancienne orthographe créerait des étiquettes qui ne
  /// correspondent plus à rien.
  void _rafraichirToutesLesReferences() {
    for (var i = 0; i < _variantes.length; i++) {
      final v = _variantes[i];
      if (!v.referenceManuelle) {
        v.sku.text = _VariantDraft.proposerReference(_nom.text, v.valeur.text);
      }
    }
  }

  /// Le premier élément satisfaisant [test], ou `null`.
  static T? _premier<T>(Iterable<T> source, bool Function(T) test) {
    for (final e in source) {
      if (test(e)) return e;
    }
    return null;
  }

  void _suivant() {
    if (_blocage != null) {
      AppNotify.error(context, _blocage!);
      return;
    }
    if (_etape < _nbEtapes - 1) setState(() => _etape++);
  }

  void _precedent() {
    if (_etape > 0) setState(() => _etape--);
  }

  // ─────────────────────────────────────────────────────────────────── l'envoi

  /// La séquence complète, dans l'ordre imposé par les dépendances.
  ///
  /// L'ORDRE N'EST PAS ARBITRAIRE :
  ///   1. le produit — tout le reste a besoin de son identifiant ;
  ///   2. les déclinaisons — l'offre référence un `variantId`, le stock un SKU ;
  ///   3. l'offre et le stock, déclinaison par déclinaison ;
  ///   4. la publication en DERNIER — publier un produit sans offre le rendrait
  ///      visible et non achetable, ce qui est pire qu'invisible.
  Future<void> _publier({required bool publier}) async {
    setState(() => _envoi = true);
    var etapeCourante = 'la création du produit';

    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      final deposerPhoto = ref.read(productPhotoUploaderProvider);

      // LES PHOTOS PARTENT AVEC LA CRÉATION : `createProduct` les dépose sur
      // media-service PUIS les rattache, parce qu'un média a besoin de l'id du
      // produit qu'il illustre. C'est la seule étape qui parle à deux services.
      final produitId = await ref.read(catalogApiProvider).createProduct(
            sellerId: sellerId,
            categoryId: _categorieId!,
            name: _nom.text.trim(),
            description: _description.text.trim(),
            images: _photos,
            uploadPhoto: deposerPhoto,
          );
      _produitCree = produitId;

      etapeCourante = 'l\'ajout des déclinaisons';
      final aCreer = _variantes.where((v) => v.skuPropre.isNotEmpty).toList();
      final catalog = ref.read(catalogApiProvider);
      for (final v in aCreer) {
        await catalog.addVariant(produitId, sku: v.skuPropre, attributes: v.attributs);
      }

      if (_metEnVente) {
        // IL FAUT RELIRE LA FICHE : `addVariant` ne rend pas l'identifiant
        // créé, et l'offre en a besoin. On la recharge une fois, après toutes
        // les déclinaisons — pas une fois par déclinaison.
        etapeCourante = 'la mise en vente';
        final fiche = await catalog.product(produitId);
        final offers = ref.read(offersApiProvider);
        final inventory = ref.read(inventoryApiProvider);

        for (final v in aCreer) {
          final variante = fiche.variants.firstWhere(
            (x) => x.sku.toUpperCase() == v.skuPropre.toUpperCase(),
            orElse: () => throw StateError(
                'La déclinaison ${v.skuPropre} n\'a pas été retrouvée après création.'),
          );

          await offers.create(
            productId: produitId,
            variantId: variante.id,
            storeId: _boutiqueRetenue!,
            sellerPrice: _prixVendeur,
            shipFromLocationId: _lieuId!,
            condition: _condition,
            handlingTime: _delai,
          );

          // LE STOCK EST POSÉ MÊME À ZÉRO, ET C'EST VOULU. L'article
          // d'inventaire est ce qui RELIE un SKU à un lieu ; sans lui, la
          // première commande n'aurait rien à décrémenter, et le vendeur ne
          // verrait pas la ligne dans son stock pour la corriger.
          etapeCourante = 'l\'enregistrement du stock';
          await inventory.createItem(
            sku: v.skuPropre,
            locationId: _lieuId!,
            onHand: v.quantiteEntiere,
          );
        }
      }

      if (publier) {
        etapeCourante = 'la publication';
        await catalog.changeStatus(produitId, 'active');
      }

      ref.invalidate(productsProvider);
      ref.invalidate(offersProvider);
      // PAS D'INVALIDATION DU STOCK : `inventoryBySkuProvider` est une famille
      // indexée par SKU. L'invalider globalement rechargerait chaque SKU déjà
      // consulté ; les nouveaux, eux, seront lus à leur première demande.

      if (!mounted) return;
      // On REMPLACE l'assistant par la fiche : revenir en arrière depuis la fiche
      // doit ramener au catalogue, pas à l'assistant d'un produit déjà créé.
      context.pushReplacement('/product/$produitId');
      AppNotify.success(
        context,
        publier ? 'Produit publié.' : 'Produit enregistré en brouillon.',
      );
    } catch (e) {
      if (!mounted) return;

      // ÉCHEC PARTIEL : ON NE REVIENT PAS EN ARRIÈRE, ON EMMÈNE SUR LA FICHE.
      //
      // Le produit existe peut-être déjà. Le supprimer pour « nettoyer » ferait
      // perdre la saisie ; le taire laisserait le vendeur le recréer en double.
      // On nomme l'étape qui a lâché et on l'emmène là où il peut finir à la
      // main.
      if (_produitCree case final id?) {
        context.pushReplacement('/product/$id');
        AppNotify.error(
          context,
          'Le produit est créé, mais $etapeCourante a échoué : $e\n'
          'Terminez depuis sa fiche.',
        );
        return;
      }

      AppNotify.error(context, 'Échec de $etapeCourante : $e');
      setState(() => _envoi = false);
    }
  }

  // ────────────────────────────────────────────────────────────────── affichage

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final categories = ref.watch(categoriesProvider);

    return Scaffold(
      backgroundColor: colors.surface,
      appBar: AppBar(
        title: const Text('Nouveau produit'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(52),
          child: _Progression(etape: _etape, total: _nbEtapes, colors: colors),
        ),
      ),
      body: categories.when(
        loading: () => const LoadingView(),

        // Sans référentiel de catégories, la création est IMPOSSIBLE :
        // `categoryId` est obligatoire côté serveur. On refuse d'ouvrir
        // l'assistant plutôt que de laisser saisir cinq étapes pour rien.
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(categoriesProvider),
        ),
        data: (toutes) => toutes.isEmpty
            ? const EmptyView(
                message: 'Le référentiel de catégories est vide.\n'
                    'Un produit ne peut pas être créé sans catégorie.',
                icon: Icons.category_outlined,
              )
            : Column(
                children: [
                  Expanded(
                    child: ListView(
                      padding: const EdgeInsets.fromLTRB(20, 20, 20, 24),
                      children: [
                        switch (_etape) {
                          0 => _etapeInformations(colors, toutes),
                          1 => _etapePhotos(colors),
                          2 => _etapeDeclinaisons(colors),
                          3 => _etapePrixEtStock(colors),
                          _ => _etapeResume(colors),
                        },
                      ],
                    ),
                  ),
                  _BarreNavigation(
                    colors: colors,
                    etape: _etape,
                    total: _nbEtapes,
                    envoi: _envoi,
                    blocage: _blocage,
                    metEnVente: _metEnVente,
                    onPrecedent: _precedent,
                    onSuivant: _suivant,
                    onBrouillon: () => _publier(publier: false),
                    onPublier: () => _publier(publier: true),
                  ),
                ],
              ),
      ),
    );
  }

  // ── 1 · Informations ──────────────────────────────────────────────────────
  Widget _etapeInformations(AppColors colors, List<Category> categories) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _TitreEtape(
            titre: 'Informations',
            aide: 'Le nom est ce que l\'acheteur lit en premier. La catégorie '
                'décide où votre produit apparaît dans la recherche.',
            colors: colors,
          ),
          TextField(
            controller: _nom,
            textCapitalization: TextCapitalization.sentences,
            onChanged: (_) => setState(_rafraichirToutesLesReferences),
            decoration: const InputDecoration(
              labelText: 'Nom du produit',
              hintText: 'Écouteurs sans fil HBA Pods',
            ),
          ),
          const SizedBox(height: 16),
          CategoryField(
            categories: categories,
            selectedId: _categorieId,
            onSelected: (c) => setState(() => _categorieId = c.id),
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _description,
            maxLines: 5,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Description',
              alignLabelWithHint: true,
            ),
          ),
        ],
      );

  // ── 2 · Photos ────────────────────────────────────────────────────────────
  Widget _etapePhotos(AppColors colors) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _TitreEtape(
            titre: 'Photos',
            aide: 'La première photo sert de vignette dans la recherche. '
                'Vous pourrez en changer depuis la fiche.',
            colors: colors,
          ),
          if (_photos.isEmpty)
            _Encadre(
              colors: colors,
              texte: 'Aucune photo. Un produit sans image se vend mal, mais rien '
                  'ne vous empêche d\'en ajouter plus tard depuis sa fiche.',
            )
          else
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                for (var i = 0; i < _photos.length; i++)
                  Stack(
                    children: [
                      ClipRRect(
                        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                        child: Image.memory(_photos[i].bytes,
                            width: 96, height: 96, fit: BoxFit.cover),
                      ),
                      Positioned(
                        top: 2,
                        right: 2,
                        child: InkWell(
                          onTap: () => setState(() => _photos.removeAt(i)),
                          child: const CircleAvatar(
                            radius: 12,
                            backgroundColor: Colors.black54,
                            child: Icon(Icons.close, size: 14, color: Colors.white),
                          ),
                        ),
                      ),
                    ],
                  ),
              ],
            ),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: () async {
              final choisies = await ImageProcessing.pickAndProcess(context, ref);
              if (choisies.isNotEmpty) setState(() => _photos.addAll(choisies));
            },
            icon: const Icon(Icons.add_photo_alternate_outlined),
            label: const Text('Ajouter des photos'),
          ),
        ],
      );

  // ── 3 · Déclinaisons ──────────────────────────────────────────────────────
  Widget _etapeDeclinaisons(AppColors colors) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _TitreEtape(
            titre: 'Déclinaisons',
            aide: 'Une déclinaison, c\'est une version de votre article : une '
                'taille, une couleur, un format. Chacune a son propre stock et '
                'son propre prix. Vous ne vendez qu\'un seul modèle ? Laissez '
                'une seule déclinaison.',
            colors: colors,
          ),
          for (var i = 0; i < _variantes.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: CardSection(
                margin: EdgeInsets.zero,
                padding: const EdgeInsets.all(14),
                child: Column(
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text('Déclinaison ${i + 1}',
                              style: const TextStyle(fontWeight: FontWeight.w700)),
                        ),
                        if (_variantes.length > 1)
                          IconButton(
                            onPressed: () => setState(() {
                              _variantes.removeAt(i).dispose();
                            }),
                            icon: const Icon(Icons.delete_outline,
                                color: AppTheme.danger, size: 20),
                          ),
                      ],
                    ),
                    // ATTRIBUT LIBRE, PAS UNE LISTE FERMÉE. L'ancien assistant
                    // proposait « Couleur : Noir / Argent » en dur. Un vendeur de
                    // pagnes décline en métrage, un vendeur de riz en kilos.
                    //
                    // ET IL VIENT AVANT LA RÉFÉRENCE, ce qui n'est pas un
                    // détail d'ordre : c'est de lui que la référence est
                    // engendrée. Demander le code avant la couleur obligerait à
                    // le réécrire après.
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            controller: _variantes[i].attribut,
                            onChanged: (_) => _rafraichirReference(i),
                            decoration: const InputDecoration(
                              labelText: 'Type de variante',
                              hintText: 'Couleur, Taille, Poids…',
                            ),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: TextField(
                            controller: _variantes[i].valeur,
                            onChanged: (_) => _rafraichirReference(i),
                            decoration: const InputDecoration(
                              labelText: 'Précisez',
                              hintText: 'Noir, 42, 5 kg…',
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 14),

                    TextField(
                      controller: _variantes[i].sku,
                      textCapitalization: TextCapitalization.characters,
                      onChanged: (_) {
                        // Dès que le vendeur y touche, on cesse de la réécrire :
                        // reprendre la main sur un champ qu'il vient de corriger
                        // est la façon la plus sûre de le rendre furieux.
                        _variantes[i].referenceManuelle = true;
                        setState(() {});
                      },
                      decoration: const InputDecoration(
                        labelText: 'Code de l\'article',
                        helperMaxLines: 4,
                        helperText:
                            'Rempli pour vous. C\'est votre étiquette interne : '
                            'elle sert à suivre le stock de cette variante. '
                            'L\'acheteur ne la voit jamais. Modifiez-la si vous '
                            'tenez déjà vos propres références.',
                      ),
                    ),
                  ],
                ),
              ),
            ),
          OutlinedButton.icon(
            onPressed: () => setState(() {
              final v = _VariantDraft();
              // Préremplie dès l'ajout : un champ vide sur la deuxième
              // déclinaison redonnerait au vendeur le problème qu'on vient de
              // lui retirer sur la première.
              v.sku.text = _VariantDraft.proposerReference(_nom.text, '');
              _variantes.add(v);
            }),
            icon: const Icon(Icons.add),
            label: const Text('Ajouter une déclinaison'),
          ),
        ],
      );

  // ── 4 · Prix et stock ─────────────────────────────────────────────────────
  Widget _etapePrixEtStock(AppColors colors) {
    final lieux = ref.watch(locationsProvider);
    final activites = ref.watch(activitiesProvider);
    final remplies = _variantes.where((v) => v.skuPropre.isNotEmpty).toList();

    final boutiques = _boutiquesDe(activites);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _TitreEtape(
          titre: 'Prix et stock',
          aide: 'Facultatif. Sans prix, le produit est créé mais pas encore en '
              'vente — vous fixerez son prix depuis sa fiche.',
          colors: colors,
        ),

        TextField(
          controller: _prix,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          onChanged: (_) => setState(() {}),
          decoration: const InputDecoration(
            labelText: 'Votre prix (net perçu)',
            suffixText: 'XOF',
          ),
        ),
        const SizedBox(height: 14),

        // LA DÉCOMPOSITION EST AFFICHÉE DÈS LA SAISIE, PAS À LA FIN.
        //
        // Le vendeur saisit son NET ; l'acheteur paie plus. Découvrir l'écart au
        // récapitulatif, c'est le découvrir trop tard pour ajuster.
        if (_prixVendeur > 0) ...[
          PricePreview(sellerPrice: _prixVendeur, currency: 'XOF'),
          const SizedBox(height: 14),
        ],

        if (boutiques.length > 1) ...[
          AppDropdown<String>(
            value: _boutiqueId,
            label: 'Boutique',
            options: [for (final b in boutiques) (value: b.id, label: b.name)],
            onChanged: (v) => setState(() => _boutiqueId = v),
          ),
          const SizedBox(height: 14),
        ],

        AppDropdown<String>(
          value: _condition,
          label: 'État du produit',
          options: [for (final c in kOfferConditions) (value: c.value, label: c.label)],
          onChanged: (v) => setState(() => _condition = v ?? _condition),
        ),
        const SizedBox(height: 14),

        lieux.when(
          loading: () => const LinearProgressIndicator(),
          error: (e, _) => Text('Lieux d\'expédition indisponibles : $e',
              style: const TextStyle(color: AppTheme.danger, fontSize: 12)),
          data: (liste) => liste.isEmpty
              ? _Encadre(
                  colors: colors,
                  texte: 'Aucun lieu d\'expédition. Déclarez-en un depuis '
                      '« Lieux d\'expédition » pour pouvoir vendre. Le produit, '
                      'lui, peut être créé dès maintenant.',
                )
              : AppDropdown<String>(
                  value: _lieuId,
                  label: 'Lieu d\'expédition',
                  options: [for (final l in liste) (value: l.id, label: l.label)],
                  onChanged: (v) => setState(() => _lieuId = v),
                ),
        ),
        const SizedBox(height: 14),

        AppDropdown<int>(
          value: _delai,
          label: 'Délai de préparation',
          options: const [
            (value: 1, label: '1 jour'),
            (value: 2, label: '2 jours'),
            (value: 3, label: '3 jours'),
            (value: 5, label: '5 jours'),
          ],
          onChanged: (v) => setState(() => _delai = v ?? 2),
        ),

        if (_prixVendeur > 0 && remplies.isNotEmpty) ...[
          const SizedBox(height: 20),
          const SectionHeader(title: 'Stock de départ'),
          for (final v in remplies)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Row(
                children: [
                  Expanded(
                    child: Text(v.skuPropre,
                        style: const TextStyle(fontWeight: FontWeight.w600)),
                  ),
                  SizedBox(
                    width: 96,
                    child: TextField(
                      controller: v.quantite,
                      keyboardType: TextInputType.number,
                      textAlign: TextAlign.end,
                      decoration: const InputDecoration(labelText: 'Qté'),
                    ),
                  ),
                ],
              ),
            ),
        ],
      ],
    );
  }

  // ── 5 · Résumé ────────────────────────────────────────────────────────────
  ///
  /// IL MONTRE TOUT, Y COMPRIS LES PHOTOS — ET C'EST LA RAISON D'ÊTRE DE
  ///    L'ÉTAPE.
  ///
  /// La première version se contentait de trois compteurs : « Photos 1,
  /// Déclinaisons 1 ». Un récapitulatif qui compte au lieu de montrer ne permet
  /// de vérifier RIEN — ni que c'est la bonne photo, ni que la catégorie est la
  /// bonne, ni que le lieu d'expédition est le bon entrepôt. Or c'est le dernier
  /// écran avant un envoi qui touche trois services et qui n'est pas annulable.
  Widget _etapeResume(AppColors colors) {
    final remplies = _variantes.where((v) => v.skuPropre.isNotEmpty).toList();

    // `_premier` PLUTÔT QUE `firstOrNull` : ce dernier vient de
    // `package:collection`, qui n'est pas une dépendance de ce projet, et
    // `firstWhere` sans `orElse` LÈVE quand rien ne correspond — ce qui ferait
    // tomber le récapitulatif pour une catégorie supprimée entre-temps.
    final categorie = _premier(
        ref.watch(categoriesProvider).valueOrNull ?? const <Category>[],
        (c) => c.id == _categorieId);
    final lieu = _premier(
        ref.watch(locationsProvider).valueOrNull ?? const <ShipLocation>[],
        (l) => l.id == _lieuId);
    final boutique = _premier(
        _boutiquesDe(ref.watch(activitiesProvider)), (b) => b.id == _boutiqueRetenue);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _TitreEtape(
          titre: 'Résumé',
          aide: 'Vérifiez avant d\'envoyer. Rien n\'a encore été transmis : tout '
              'part au moment où vous validez ci-dessous.',
          colors: colors,
        ),

        // ── Les photos, en vrai ────────────────────────────────────────────
        //
        // MONTRÉES, PAS COMPTÉES. C'est le seul endroit où le vendeur peut
        // constater qu'il a choisi la bonne image — et, quand le détourage a
        // échoué, à quoi ressemblera réellement sa vignette.
        if (_photos.isNotEmpty) ...[
          Text('Photos (${_photos.length})',
              style: TextStyle(fontWeight: FontWeight.w700, color: colors.ink)),
          const SizedBox(height: 8),
          SizedBox(
            height: 86,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: _photos.length,
              separatorBuilder: (_, __) => const SizedBox(width: 8),
              itemBuilder: (_, i) => Stack(
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                    child: Image.memory(_photos[i].bytes,
                        width: 86, height: 86, fit: BoxFit.cover),
                  ),
                  // La première est la vignette du catalogue : le dire ici évite
                  // la surprise de la voir choisie sans l'avoir décidé.
                  if (i == 0)
                    Positioned(
                      bottom: 4,
                      left: 4,
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: AppTheme.brandGreen,
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: const Text('Principale',
                            style: TextStyle(fontSize: 9, color: Colors.white)),
                      ),
                    ),
                  if (!_photos[i].isProcessed)
                    Positioned(
                      top: 4,
                      right: 4,
                      child: Container(
                        padding: const EdgeInsets.all(3),
                        decoration: const BoxDecoration(
                          color: AppTheme.promoOrange,
                          shape: BoxShape.circle,
                        ),
                        child: const Icon(Icons.warning_amber_rounded,
                            size: 11, color: Colors.white),
                      ),
                    ),
                ],
              ),
            ),
          ),
          if (_photos.any((x) => !x.isProcessed)) ...[
            const SizedBox(height: 6),
            const Text(
              'Les photos marquées n\'ont pas pu être détourées : elles partiront '
              'telles quelles, sans le fond blanc du catalogue.',
              style: TextStyle(fontSize: 11.5, color: AppTheme.promoOrange, height: 1.4),
            ),
          ],
          const SizedBox(height: 16),
        ] else ...[
          _Encadre(
            colors: colors,
            texte: 'Aucune photo. Un article sans image se vend très mal — vous '
                'pourrez en ajouter depuis sa fiche.',
          ),
          const SizedBox(height: 16),
        ],

        // ── Informations ───────────────────────────────────────────────────
        CardSection(
          margin: EdgeInsets.zero,
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              KeyValueRow(label: 'Nom', value: _nom.text.trim(), strong: true),
              KeyValueRow(label: 'Catégorie', value: categorie?.name ?? '—'),
              if (_description.text.trim().isNotEmpty) ...[
                const Divider(height: 20),
                Align(
                  alignment: Alignment.centerLeft,
                  child: Text('Description',
                      style: TextStyle(fontSize: 12, color: colors.subtle)),
                ),
                const SizedBox(height: 4),
                Text(_description.text.trim(),
                    style: TextStyle(fontSize: 13, height: 1.4, color: colors.ink)),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),

        // ── Déclinaisons ───────────────────────────────────────────────────
        Text('Déclinaisons (${remplies.length})',
            style: TextStyle(fontWeight: FontWeight.w700, color: colors.ink)),
        const SizedBox(height: 8),
        CardSection(
          margin: EdgeInsets.zero,
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (final v in remplies) ...[
                if (v != remplies.first) const Divider(height: 20),
                Text(
                  v.attributs.isEmpty
                      ? 'Modèle unique'
                      : v.attributs.entries.map((e) => '${e.key} : ${e.value}').join(' · '),
                  style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13.5),
                ),
                const SizedBox(height: 4),
                KeyValueRow(label: 'Code article', value: v.skuPropre),
                // Le stock n'est posé que si l'article est mis en vente : afficher
                // une quantité qui ne partira pas serait un mensonge discret.
                if (_metEnVente)
                  KeyValueRow(label: 'Stock de départ', value: '${v.quantiteEntiere}'),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),

        // ── Prix et expédition ─────────────────────────────────────────────
        if (_metEnVente) ...[
          Text('Mise en vente',
              style: TextStyle(fontWeight: FontWeight.w700, color: colors.ink)),
          const SizedBox(height: 8),
          CardSection(
            margin: EdgeInsets.zero,
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                KeyValueRow(
                    label: 'Vous percevez',
                    value: Format.money(_prixVendeur, 'XOF'),
                    strong: true),
                KeyValueRow(
                    label: 'Commission plateforme',
                    value: Format.money(Pricing.commission(_prixVendeur), 'XOF')),
                KeyValueRow(
                    label: 'Frais de paiement',
                    value: Format.money(Pricing.providerFee(_prixVendeur), 'XOF')),
                const Divider(height: 20),
                KeyValueRow(
                  label: 'Prix affiché au client',
                  value: Format.money(Pricing.productPrice(_prixVendeur), 'XOF'),
                  color: AppTheme.brandGreenDark,
                  strong: true,
                ),
                const Divider(height: 20),
                KeyValueRow(
                    label: 'État',
                    value: kOfferConditions
                        .firstWhere((c) => c.value == _condition,
                            orElse: () => kOfferConditions.first)
                        .label),
                if (boutique != null) KeyValueRow(label: 'Boutique', value: boutique.name),
                KeyValueRow(label: 'Expédié depuis', value: lieu?.label ?? '—'),
                KeyValueRow(
                    label: 'Délai de préparation',
                    value: _delai == 1 ? '1 jour' : '$_delai jours'),
              ],
            ),
          ),
          const SizedBox(height: 16),
        ],

        _Encadre(
          colors: colors,
          texte: _metEnVente
              ? 'Le produit sera créé, décliné, mis en vente et son stock '
                  'enregistré. Publier le rend visible aux acheteurs.'
              : 'Aucun prix renseigné : le produit sera créé mais ne sera pas '
                  'encore achetable. Fixez son prix depuis sa fiche, ensuite.',
        ),
      ],
    );
  }
}

// ─────────────────────────────────────────────────────────────── sous-composants

class _Progression extends StatelessWidget {
  const _Progression({required this.etape, required this.total, required this.colors});

  final int etape;
  final int total;
  final AppColors colors;

  static const _noms = ['Informations', 'Photos', 'Déclinaisons', 'Prix et stock', 'Résumé'];

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                for (var i = 0; i < total; i++)
                  Expanded(
                    child: Container(
                      height: 4,
                      margin: EdgeInsets.only(right: i == total - 1 ? 0 : 4),
                      decoration: BoxDecoration(
                        // LES ÉTAPES PASSÉES RESTENT PLEINES : la barre montre
                        // le chemin parcouru, pas seulement la position.
                        color: i <= etape ? AppTheme.brandGreen : colors.line,
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'Étape ${etape + 1} sur $total · ${_noms[etape]}',
              style: TextStyle(fontSize: 12, color: colors.subtle),
            ),
          ],
        ),
      );
}

class _TitreEtape extends StatelessWidget {
  const _TitreEtape({required this.titre, required this.aide, required this.colors});

  final String titre;
  final String aide;
  final AppColors colors;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(titre,
                style: TextStyle(
                    fontSize: 20, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(aide,
                style: TextStyle(fontSize: 12.5, height: 1.45, color: colors.subtle)),
          ],
        ),
      );
}

class _Encadre extends StatelessWidget {
  const _Encadre({required this.colors, required this.texte});

  final AppColors colors;
  final String texte;

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
              child: Text(texte,
                  style:
                      TextStyle(fontSize: 12.5, height: 1.45, color: colors.subtle)),
            ),
          ],
        ),
      );
}

/// Les commandes du bas : « Retour / Suivant », puis les deux issues finales.
class _BarreNavigation extends StatelessWidget {
  const _BarreNavigation({
    required this.colors,
    required this.etape,
    required this.total,
    required this.envoi,
    required this.blocage,
    required this.metEnVente,
    required this.onPrecedent,
    required this.onSuivant,
    required this.onBrouillon,
    required this.onPublier,
  });

  final AppColors colors;
  final int etape;
  final int total;
  final bool envoi;
  final String? blocage;
  final bool metEnVente;
  final VoidCallback onPrecedent;
  final VoidCallback onSuivant;
  final VoidCallback onBrouillon;
  final VoidCallback onPublier;

  @override
  Widget build(BuildContext context) {
    final derniere = etape == total - 1;

    return Container(
      padding: EdgeInsets.fromLTRB(
          20, 12, 20, 12 + MediaQuery.of(context).padding.bottom),
      decoration: BoxDecoration(
        color: colors.surface,
        border: Border(top: BorderSide(color: colors.line)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // LE MOTIF DU BLOCAGE EST ÉCRIT AU-DESSUS DU BOUTON, pas révélé
          // après l'avoir pressé. Le bouton reste ACTIF : c'est `_suivant` qui
          // refuse et redit pourquoi — un bouton grisé sans phrase laisse
          // chercher.
          if (blocage case final motif? when !derniere) ...[
            Text(motif,
                style: const TextStyle(fontSize: 12, color: AppTheme.promoOrange)),
            const SizedBox(height: 10),
          ],
          Row(
            children: [
              if (etape > 0) ...[
                Expanded(
                  child: OutlinedButton(
                    onPressed: envoi ? null : onPrecedent,
                    style: OutlinedButton.styleFrom(
                        minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight)),
                    child: const Text('Retour'),
                  ),
                ),
                const SizedBox(width: 12),
              ],
              Expanded(
                flex: 2,
                child: FilledButton(
                  // SANS PRIX, LE BOUTON PRINCIPAL NE PUBLIE PAS.
                  //
                  // Publier un produit qu'aucune offre ne couvre le rend VISIBLE
                  // et NON ACHETABLE : l'acheteur le trouve, l'ouvre, et ne peut
                  // rien en faire. C'est pire qu'invisible, et c'est un piège
                  // d'autant plus facile que l'étape 4 est facultative. On
                  // enregistre donc en brouillon, et la publication attend la
                  // fiche — là où le prix peut être posé.
                  onPressed: envoi
                      ? null
                      : (!derniere
                          ? onSuivant
                          : (metEnVente ? onPublier : onBrouillon)),
                  style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight)),
                  child: envoi
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(
                              strokeWidth: 2, color: Colors.white))
                      : Text(!derniere
                          ? 'Suivant'
                          : (metEnVente ? 'Créer et publier' : 'Créer le produit')),
                ),
              ),
            ],
          ),

          // « ENREGISTRER SANS PUBLIER » N'EST PAS UN REPLI, C'EST UN CHOIX.
          //
          // Un produit sans photo se vend mal ; le vendeur doit pouvoir tout
          // enregistrer et publier plus tard, depuis la fiche, quand il aura pris
          // ses photos. L'issue n'est offerte QUE si l'autre bouton publie —
          // sinon les deux feraient la même chose.
          if (derniere && metEnVente) ...[
            const SizedBox(height: 8),
            TextButton(
              onPressed: envoi ? null : onBrouillon,
              child: const Text('Enregistrer sans publier'),
            ),
          ],
        ],
      ),
    );
  }
}
