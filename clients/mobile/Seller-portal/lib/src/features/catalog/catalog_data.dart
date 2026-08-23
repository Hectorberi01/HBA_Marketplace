import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/media/media_upload.dart';
import '../../core/network/api_base.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/paging/paged_list.dart';
import '../../shared/utils/formatters.dart';

/// Photo prête à l'envoi : l'originale, et sa version traitée par le serveur
/// (détourage + fond blanc via Cloudinary).
///
/// On garde les DEUX : le vendeur doit pouvoir comparer avant d'accepter. Une
/// retouche automatique qu'on ne montre pas est une retouche qu'on subit — et
/// si le détourage rate (ombre mangée, produit tronqué), il faut pouvoir le voir.
class ProcessedImage {
  ProcessedImage({
    required this.fileName,
    required this.sourcePath,
    required this.original,
    this.processed,
    this.error,
  });

  final String fileName;

  /// Chemin du fichier d'origine sur l'appareil. Conservé pour pouvoir RÉESSAYER
  /// le détourage : une panne Cloudinary est presque toujours passagère, et
  /// obliger le vendeur à re-sélectionner ses photos pour ça serait absurde.
  final String sourcePath;

  final Uint8List original;

  /// Version détourée renvoyée par le serveur. Null si le traitement a échoué.
  final Uint8List? processed;

  /// Motif de l'échec, le cas échéant (Cloudinary indisponible, format refusé…).
  final String? error;

  bool get isProcessed => processed != null;

  /// Ce qui part réellement au serveur : la version traitée si elle existe.
  Uint8List get bytes => processed ?? original;

  /// Le serveur refuse au-delà de 5 Mo (UploadValidation.MaxImageBytes). Mieux
  /// vaut le voir dans le formulaire qu'après un envoi de plusieurs mégaoctets
  /// sur une connexion mobile.
  static const int maxBytes = 5 * 1024 * 1024;

  bool get isTooLarge => bytes.length > maxBytes;

  ProcessedImage withResult(Uint8List? result, String? failure) => ProcessedImage(
        fileName: fileName,
        sourcePath: sourcePath,
        original: original,
        processed: result,
        error: failure,
      );
}

/// Photo d'un produit.
class ProductMedia {
  ProductMedia({
    required this.id,
    required this.mediaId,
    required this.url,
    required this.isPrimary,
    required this.position,
    required this.altText,
  });

  /// DEUX IDENTIFIANTS, ET LES ROUTES N'ATTENDENT PAS LE MÊME.
  ///
  /// `id` est celui de la LIAISON produit↔média dans catalog-service ; c'est lui
  /// que prennent `DELETE /products/{id}/media/{mediaId}` et
  /// `POST /products/{id}/media/{mediaId}/primary`, malgré le nom du paramètre.
  /// [mediaId] est celui du FICHIER dans media-service, et ne sert qu'à demander
  /// une URL signée. Les intervertir produit un 404 sur une photo bien présente.
  final String id;
  final String mediaId;

  final String url;

  /// Photo principale : la seule que voit l'acheteur dans les listes.
  final bool isPrimary;

  final int position;
  final String altText;

  factory ProductMedia.fromJson(Map d) => ProductMedia(
        id: Json.str(d['id']),
        mediaId: Json.str(d['mediaId']),
        url: Json.str(d['url']),
        isPrimary: Json.asBool(d['isPrimary']),
        position: Json.asInt(d['position']),
        altText: Json.str(d['altText']),
      );
}

/// Déclinaison du produit (taille, couleur…). Chaque déclinaison a son propre SKU,
/// donc son propre stock : c'est l'unité que l'on vend réellement.
class ProductVariant {
  ProductVariant({
    required this.id,
    required this.sku,
    required this.attributes,
    required this.barcode,
    required this.weightGrams,
    this.isActive = true,
  });

  final String id;
  final String sku;
  final Map<String, String> attributes;
  final String? barcode;
  final int weightGrams;

  /// La déclinaison est-elle proposable à la vente ?
  ///
  /// VRAI PAR DÉFAUT SI LE CHAMP MANQUE, jamais faux. Une application plus
  /// récente que le serveur retirerait sinon tout le catalogue de la vente à
  /// l'écran — sur une donnée absente, pas sur une décision du vendeur.
  final bool isActive;

  /// « Taille: 42 · Couleur: Noir » — ou le SKU si aucun attribut.
  String get label {
    if (attributes.isEmpty) return sku;
    return attributes.entries.map((e) => '${e.key}: ${e.value}').join(' · ');
  }

  factory ProductVariant.fromJson(Map d) {
    final raw = Json.map(d['attributes']);
    return ProductVariant(
      id: Json.str(d['id']),
      sku: Json.str(d['sku']),
      attributes: {for (final e in raw.entries) e.key: e.value.toString()},
      barcode: (d['barcode']?.toString().isNotEmpty ?? false) ? d['barcode'].toString() : null,
      weightGrams: Json.asInt(d['weightGrams']),
      isActive: d['isActive'] is bool ? d['isActive'] as bool : true,
    );
  }
}

/// Produit du catalogue vendeur — même modèle pour la liste et le détail.
///
/// Les deux routes projettent la MÊME `ProductSummary` via `ProductMapping.ToSummary`
/// (`GET /seller/products` comme `GET /seller/products/{id}`) : une fiche issue de la
/// liste est donc aussi complète qu'une fiche issue du détail. Ce n'est pas un détail
/// esthétique — `updateProduct` renvoie la fiche ENTIÈRE, donc un modèle partiellement
/// rempli effacerait en base les champs qu'il n'a pas su lire.
class SellerProduct {
  SellerProduct({
    required this.id,
    required this.name,
    required this.description,
    required this.categoryId,
    required this.status,
    required this.brandId,
    required this.gtin,
    required this.ean,
    required this.productGroupId,
    required this.attributes,
    required this.tags,
    required this.media,
    required this.variants,
  });

  final String id;
  final String name;
  final String description;
  final String categoryId;
  final String status; // draft | active | archived

  // ───────────────────────────────────────────────────────────────────────────
  // TOUT CE QUE `PUT /seller/products/{id}` REMPLACE.
  //
  // Le serveur ne fusionne rien : `Product.Update` affecte `BrandId`,
  // `ProductGroupId`, `Attributes` et `Tags` avec ce qu'il reçoit, et l'absence
  // d'un champ y vaut « efface ». Un écran d'édition qui ne renvoie que les
  // champs qu'il affiche détruit donc silencieusement tout le reste — c'est
  // exactement ce qui se passait ici.
  //
  // `brandId`, `productGroupId` et `attributes` ne sont affichés PAR AUCUN widget
  // de cette application. Ils sont lus quand même, et c'est délibéré : leur seul
  // rôle est de survivre à une modification de fiche. Ne pas les supprimer sous
  // prétexte qu'ils ne servent à rien à l'écran.
  //
  // (`gtin`, `ean` et `tags`, eux, sont bien affichés — voir `_Infos`.)
  // ───────────────────────────────────────────────────────────────────────────
  final String? brandId;
  final String? gtin;
  final String? ean;
  final String? productGroupId;
  final Map<String, String> attributes;
  final List<String> tags;

  final List<ProductMedia> media;
  final List<ProductVariant> variants;

  /// Photo principale, à défaut la première : c'est l'image que l'acheteur verra.
  ProductMedia? get primaryImage {
    if (media.isEmpty) return null;
    return media.firstWhere((m) => m.isPrimary, orElse: () => media.first);
  }

  String? get imageUrl => primaryImage?.url;

  bool get isActive => status.toLowerCase() == 'active';

  factory SellerProduct.fromJson(Map d) {
    final media = Json.list(d['media']).map(ProductMedia.fromJson).toList()
      ..sort((a, b) => a.position.compareTo(b.position));

    final rawAttributes = Json.map(d['attributes']);

    return SellerProduct(
      id: Json.str(d['id']),
      name: Json.str(d['name'], 'Produit'),
      description: Json.str(d['description']),
      categoryId: Json.str(d['categoryId']),
      status: Json.str(d['status'], 'draft'),
      brandId: _nullIfEmpty(d['brandId']),
      gtin: _nullIfEmpty(d['gtin']),
      ean: _nullIfEmpty(d['ean']),
      productGroupId: _nullIfEmpty(d['productGroupId']),
      attributes: {for (final e in rawAttributes.entries) e.key: e.value.toString()},
      tags: (d['tags'] is List) ? (d['tags'] as List).map((e) => e.toString()).toList() : const [],
      media: media,
      variants: Json.list(d['variants']).map(ProductVariant.fromJson).toList(),
    );
  }

  /// `null` pour une valeur absente OU vide. Le serveur ne distingue pas les deux
  /// (`Clean()` côté domaine ramène le blanc à `null`), et renvoyer `""` là où il
  /// attend `null` réécrirait la valeur pour rien.
  static String? _nullIfEmpty(dynamic v) {
    final s = v?.toString().trim();
    return (s == null || s.isEmpty) ? null : s;
  }
}

/// Marque du référentiel plateforme (facultative sur un produit).
class Brand {
  Brand({required this.id, required this.name});

  final String id;
  final String name;

  factory Brand.fromJson(Map d) => Brand(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Marque'),
      );
}

/// Catégorie du référentiel plateforme.
class Category {
  Category({required this.id, required this.name, required this.path});

  final String id;
  final String name;
  final String path;

  String get display => path.isEmpty ? name : path.replaceAll('/', ' › ');

  factory Category.fromJson(Map d) => Category(
        id: Json.str(d['id']),
        name: Json.str(d['name']),
        path: Json.str(d['path']),
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// CATALOGUE VENDEUR — catalog-service.
///
/// DEUX PRÉFIXES, ET LA DIFFÉRENCE TIENT À UN « S ».
///
///   • `/api/catalog/sellers/{sellerId}/products` — LECTURE, au PLURIEL, anonyme ;
///   • `/api/catalog/seller/products/…`           — ÉCRITURE, au SINGULIER,
///                                                  authentifiée.
///
/// Ce n'est pas une élégance : ce sont deux groupes de routes distincts dans
/// `CatalogEndpoints`. Écrire l'un pour l'autre donne un 404 muet.
///
/// LE CONTRAT N'A NI PRIX NI STOCK, ET AUCUN ÉCRAN NE DOIT EN INVENTER.
///
/// `ProductSummary` porte l'identité du produit (nom, description, catégorie,
/// marque, attributs, tags, médias, déclinaisons) et RIEN D'AUTRE. Le prix vit
/// sur `OfferSummary`, dans le module Products/Offers encore enfermé dans le
/// monolithe (cf. `features/offers/offers_data.dart`) ; le stock vit dans
/// inventory-service, par SKU, et ses écritures sont réservées à
/// l'administration (cf. `features/inventory/inventory_data.dart`).
///
/// Une fiche produit ne peut donc afficher ni « 24 500 F CFA » ni « Stock : 41 ».
/// La liste des produits montre ce qu'elle sait — nom, photo, état de
/// publication — et dit ce qu'elle ignore, plutôt que d'afficher un zéro qu'on
/// lirait comme une rupture.
///
/// AUCUN CONTRÔLE DE PROPRIÉTÉ SUR LES ÉCRITURES (IDOR CONNU).
///
/// Le groupe `/api/catalog/seller` est en simple `MapAuthenticatedGroup` : le
/// serveur ne vérifie pas que le produit modifié appartient à l'appelant (tâche
/// « A1-7 — IDOR restant »). L'application n'exploite évidemment pas la faille —
/// elle n'envoie que des identifiants issus de SA propre liste — mais il ne faut
/// pas conclure de son bon fonctionnement que la garde existe.
/// ═════════════════════════════════════════════════════════════════════════════
class CatalogApi extends ApiBase {
  const CatalogApi(super.dio);

  /// Lecture publique : produits, catégories, marques.
  static const _read = AppConfig.catalog;

  /// Écriture vendeur — noter le SINGULIER.
  static const _write = '${AppConfig.catalog}/seller';

  /// Les produits d'un vendeur.
  ///
  /// NI PAGINATION, NI RECHERCHE : `ListSellerProductsAsync(Guid sellerId, …)`
  /// n'accepte AUCUN paramètre de requête et rend un TABLEAU NU (pas de
  /// `PagedResult`). Les `page`/`pageSize`/`search` que cette méthode envoyait
  /// étaient ignorés en silence, et l'écran croyait paginer.
  ///
  /// La route paginée `GET /api/catalog/products` existe, mais elle balaie le
  /// catalogue de TOUTE la plateforme : l'utiliser montrerait au vendeur les
  /// produits des autres.
  Future<List<SellerProduct>> products(String sellerId) => guard(() async {
        final resp = await dio.get('$_read/sellers/$sellerId/products');
        return Json.list(resp.data).map(SellerProduct.fromJson).toList();
      });

  Future<SellerProduct> product(String id) => guard(() async {
        final resp = await dio.get('$_read/products/$id');
        return SellerProduct.fromJson(Json.map(resp.data));
      });

  Future<List<Category>> categories() => guard(() async {
        final resp = await dio.get('$_read/categories');
        return Json.list(resp.data).map(Category.fromJson).toList();
      });

  Future<List<Brand>> brands() => guard(() async {
        final resp = await dio.get('$_read/brands');
        return Json.list(resp.data).map(Brand.fromJson).toList();
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// DÉTOURAGE — `POST /api/catalog/seller/products/images/process`.
  ///
  /// CETTE MÉTHODE LEVAIT `NotMigrated`, ET LE DIAGNOSTIC ÉTAIT À MOITIÉ FAUX.
  ///
  /// Le commentaire précédent affirmait qu'« aucun service n'a repris le
  /// détourage ». En réalité catalog-service portait DÉJÀ trois implémentations
  /// de `IImageProcessor` — rembg (local), Cloudinary, et un no-op — plus un
  /// drapeau de disponibilité et un enregistrement conditionnel. Ce qui manquait
  /// n'était pas le service, c'était **l'endpoint** : personne ne pouvait
  /// l'appeler. Le vendeur voyait donc « Le détourage a échoué », message exact
  /// dont la cause était introuvable côté client.
  ///
  /// LA RÉPONSE EST L'IMAGE, PAS UNE URL. À ce stade la photo n'appartient à
  /// aucun produit et rien n'est stocké : un vendeur qui annule ne laisse aucun
  /// objet derrière lui. Le dépôt vient après, à la création.
  ///
  /// UN 503 SIGNIFIE « NON CONFIGURÉ », PAS « EN PANNE ». Le serveur refuse
  /// plutôt que de rendre l'original en le faisant passer pour détouré — c'est
  /// exactement le piège que `IImageProcessingAvailability` existe pour éviter.
  /// L'appelant traite les deux pareil : il propose l'original, en le disant.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<Uint8List> processImage(File image) => guard(() async {
        final form = FormData.fromMap({
          'file': await MultipartFile.fromFile(
            image.path,
            filename: image.path.split('/').last,
          ),
        });

        final resp = await dio.post<List<int>>(
          '$_write/products/images/process',
          data: form,
          //`ResponseType.bytes` EST INDISPENSABLE. Par défaut Dio décode la
          // réponse en JSON puis en texte ; sur un JPEG, cela produit une chaîne
          // corrompue et une exception de décodage — que l'écran afficherait
          // comme « échec du détourage », en accusant le serveur.
          options: Options(responseType: ResponseType.bytes),
        );

        return Uint8List.fromList(resp.data ?? const []);
      });

  /// Crée un produit, puis y rattache ses photos.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// CE N'EST PLUS UN SEUL ENVOI MULTIPART : C'EST DU JSON, PUIS N DÉPÔTS.
  ///
  /// L'ancienne méthode postait tout d'un coup — champs et fichiers — sur
  /// `/seller/products/`, et lisait un `ProductWithImagesResult`. Ce contrat
  /// composite était propre au BFF du monolithe. catalog-service, lui :
  ///
  ///   1. `POST /api/catalog/seller/products` en JSON (`ProductRequest`), rend
  ///      `201 { id }` ;
  ///   2. les photos passent par media-service (`POST /api/media`), qui rend
  ///      `{ mediaId, url }` ;
  ///   3. chaque photo est rattachée par
  ///      `POST /api/catalog/seller/products/{id}/media` (`ProductMediaRequest`).
  ///
  /// `sellerId` EST DANS LE CORPS, ET IL VIENT DU SOCLE D'IDENTITÉ.
  ///
  /// `ProductRequest` le déclare en premier champ. Aucun écran ne doit le
  /// fabriquer : il est passé en paramètre par l'appelant, qui le tient de
  /// `requiredSellerIdProvider`.
  ///
  /// IL N'Y A PLUS D'EXIGENCE SERVEUR D'AU MOINS UNE PHOTO.
  ///
  /// Le BFF refusait un produit sans image (400). catalog-service l'accepte. Le
  /// formulaire garde la règle — un produit sans photo ne se vend pas — mais
  /// c'est désormais une règle D'ÉCRAN, et non une garantie de la plateforme.
  ///
  /// ÉCHEC PARTIEL POSSIBLE, ET ASSUMÉ. Si le dépôt d'une photo échoue après
  /// la création, le produit EXISTE, sans cette image. On laisse remonter
  /// l'erreur plutôt que de tenter une suppression compensatoire : détruire un
  /// produit déjà créé parce qu'une photo n'est pas passée ferait perdre au
  /// vendeur toute sa saisie.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String> createProduct({
    required String sellerId,
    required String categoryId,
    required String name,
    required List<ProcessedImage> images,
    required Future<UploadedMedia> Function(ProcessedImage image, String productId) uploadPhoto,
    String description = '',
    String? brandId,
    String? gtin,
    String? ean,
    String? productGroupId,
    List<String> tags = const [],
    Map<String, String> attributes = const {},
  }) =>
      guard(() async {
        final resp = await dio.post('$_write/products', data: {
          // PAS DE `sellerId` DANS LE CORPS — LE SERVEUR LE TIRE DU JETON.
          //
          // La route l'acceptait, et c'était la faille #179 : un compte acheteur
          // créait une fiche AU NOM du vendeur qu'il désignait, dont le catalogue
          // recevait le produit et les commandes. Le champ a été RETIRÉ du contrat
          // plutôt que vérifié — un champ d'identité présent finit par être cru.
          //
          // LE PARAMÈTRE [sellerId] RESTE DANS LA SIGNATURE, ET C'EST UTILE :
          // sa résolution par `requiredSellerIdProvider` échoue AVANT le premier
          // envoi pour un compte sans boutique. Sans elle, l'assistant déposerait
          // les photos sur media-service, puis récolterait un 404 sur la création
          // — des fichiers orphelins pour rien.
          'categoryId': categoryId,
          'name': name,
          // `Description` est non nullable côté serveur : envoyer `null` ferait
          // échouer la validation, pas la requête.
          'description': description,
          'brandId': brandId,
          'gtin': gtin,
          'ean': ean,
          'productGroupId': productGroupId,
          // Objets/listes en JSON : plus de « a,b,c » ni d'`attributesJson`, ces
          // aplatissements n'existaient que pour contourner le binding multipart.
          'attributes': attributes,
          'tags': tags,
        });

        final productId = Json.str(Json.map(resp.data)['id']);
        if (productId.isEmpty) {
          throw ApiException(
            "Le produit a été créé mais son identifiant est absent de la réponse.",
          );
        }

        // L'ORDRE compte toujours : la première photo devient la principale.
        for (var i = 0; i < images.length; i++) {
          final deposit = await uploadPhoto(images[i], productId);
          await attachMedia(
            productId,
            mediaId: deposit.mediaId,
            url: deposit.url,
            isPrimary: i == 0,
          );
        }

        return productId;
      });

  /// Rattache un fichier DÉJÀ déposé sur media-service à un produit.
  ///
  /// `url` EST DANS LE CONTRAT ET N'EST PAS FACULTATIF.
  ///
  /// `ProductMediaRequest(Guid MediaId, string Url, string Type, …)` : le service
  /// conserve l'URL rendue par media-service pour éviter un aller-retour à chaque
  /// affichage. C'est celle du dépôt qu'il faut renvoyer, pas une reconstruite.
  Future<void> attachMedia(
    String productId, {
    required String mediaId,
    required String url,
    bool isPrimary = false,
    String? altText,
  }) =>
      guard(() async {
        await dio.post('$_write/products/$productId/media', data: {
          'mediaId': mediaId,
          'url': url,
          'type': 'Image',
          'altText': altText,
          'isPrimary': isPrimary,
        });
      });

  /// ───────────────────────────────────────────────────────────────────────────
  /// REMPLACEMENT INTÉGRAL. TOUS LES CHAMPS SONT OBLIGATOIRES, Y COMPRIS CEUX
  /// QUE L'ÉCRAN NE MODIFIE PAS.
  ///
  /// `Product.Update` côté serveur ne fusionne rien — il affecte :
  ///
  ///     BrandId        = brandId;
  ///     ProductGroupId = productGroupId;
  ///     Attributes     = attributes is null ? new Dictionary<…>() : …;
  ///     Tags           = tags?.Where(…).ToList() ?? new List<string>();
  ///
  /// Un champ absent du corps JSON arrive donc à `null`, et `null` veut dire
  /// EFFACER. Cette méthode n'envoyait que `name`, `description`, `gtin` et
  /// `ean` : chaque vendeur qui corrigeait une faute de frappe depuis l'app
  /// perdait sa marque, ses attributs et TOUS ses tags. Y compris `featured` —
  /// le seul tag qui compte, puisque `CatalogModuleApi.ListFeaturedAsync` filtre
  /// dessus pour alimenter la vitrine d'accueil de l'app acheteur. Le produit
  /// disparaissait de l'accueil, et personne ne pouvait relier la cause à
  /// l'effet.
  ///
  /// D'où les `required` sur des paramètres nullables — une bizarrerie assumée.
  /// `String? brandId` seul se serait laissé omettre en silence, ce qui est
  /// précisément le défaut qu'on corrige. `required String? brandId` force
  /// chaque appelant à écrire ce qu'il veut : la valeur existante (préserver) ou
  /// `null` (effacer). Ne PAS leur redonner de valeur par défaut.
  ///
  /// Le serveur n'a pas de route de mise à jour partielle : tant qu'il n'en aura
  /// pas, c'est à l'appelant de relire la fiche et de tout renvoyer.
  /// ───────────────────────────────────────────────────────────────────────────
  ///
  /// `sellerId` ET `categoryId` SONT DANS LE CORPS BIEN QU'IGNORÉS.
  ///
  /// `UpdateProductCommand` ne les relit pas — le produit garde son vendeur et sa
  /// catégorie. Mais `ProductRequest` les déclare NON nullables : les omettre
  /// fait échouer la désérialisation avant même d'atteindre le gestionnaire. On
  /// les envoie donc, en sachant qu'ils ne changeront rien.
  Future<void> updateProduct(
    String id, {
    required String sellerId,
    required String categoryId,
    required String name,
    required String? description,
    required String? brandId,
    required String? gtin,
    required String? ean,
    required String? productGroupId,
    required Map<String, String> attributes,
    required List<String> tags,
  }) =>
      guard(() async {
        await dio.put('$_write/products/$id', data: {
          // ENVOYÉ ICI, ET SEULEMENT ICI. `ProductRequest` le déclare non
          // nullable, donc l'omettre ferait échouer la désérialisation avant le
          // gestionnaire — mais `UpdateProductCommand` ne le lit PAS, et la route
          // vérifie désormais l'appartenance par le jeton
          // (`DenyUnlessProductOwnerAsync`). Le champ est donc inerte : le
          // renseigner faussement ne change rien, et ne permet plus rien.
          'sellerId': sellerId,
          'categoryId': categoryId,
          'name': name,
          'description': description ?? '',
          'brandId': brandId,
          'gtin': gtin,
          'ean': ean,
          'productGroupId': productGroupId,
          'attributes': attributes,
          'tags': tags,
        });
      });

  /// Change l'état de publication.
  ///
  /// C'EST UN `POST`, PAS UN `PATCH`, ET IL N'Y A PAS DE `/publish`.
  ///
  /// L'appel partait en `PATCH /seller/products/{id}/status`. catalog-service
  /// déclare `seller.MapPost("/products/{id:guid}/status", …)` : un `PATCH` sur
  /// un chemin qui n'accepte que `POST` rend 405, que l'application traduisait en
  /// « erreur serveur ». Il n'existe par ailleurs ni `/publish` ni `/unpublish` —
  /// tout passe par ce champ `status`.
  Future<void> changeStatus(String id, String status) => guard(() async {
        await dio.post('$_write/products/$id/status', data: {'status': status});
      });

  Future<void> deleteProduct(String id) => guard(() async {
        await dio.delete('$_write/products/$id');
      });

  // ---------- Images ----------

  /// IL N'Y A PLUS DE ROUTE D'UPLOAD DANS catalog-service.
  ///
  /// `POST /seller/products/{id}/media/upload` n'existe pas : le fichier passe
  /// d'abord par media-service, puis on rattache son `mediaId` avec
  /// [attachMedia]. Voir `core/media/media_upload.dart` — le dépôt en deux temps
  /// y est expliqué une fois pour toutes.
  ///
  /// Cette méthode est conservée comme point d'entrée unique pour les écrans :
  /// elle enchaîne les deux appels.
  Future<void> uploadImage(
    String productId,
    ProcessedImage image, {
    required Future<UploadedMedia> Function(ProcessedImage image, String productId) uploadPhoto,
    bool isPrimary = false,
  }) async {
    final deposit = await uploadPhoto(image, productId);
    await attachMedia(productId, mediaId: deposit.mediaId, url: deposit.url, isPrimary: isPrimary);
  }

  /// [productMediaId] est l'identifiant de la LIAISON (`ProductMedia.id`), pas
  /// celui du fichier — cf. le commentaire de [ProductMedia].
  Future<void> setPrimaryImage(String productId, String productMediaId) => guard(() async {
        await dio.post('$_write/products/$productId/media/$productMediaId/primary');
      });

  Future<void> removeImage(String productId, String productMediaId) => guard(() async {
        await dio.delete('$_write/products/$productId/media/$productMediaId');
      });

  // ---------- Déclinaisons ----------

  Future<void> addVariant(
    String productId, {
    required String sku,
    Map<String, String> attributes = const {},
    String? barcode,
    int weightGrams = 0,
  }) =>
      guard(() async {
        await dio.post('$_write/products/$productId/variants', data: {
          'sku': sku,
          'attributes': attributes,
          'barcode': barcode,
          'weightGrams': weightGrams,
        });
      });

  /// Retire une déclinaison de la vente, ou l'y remet.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// CE N'EST PAS `removeVariant`, ET LE VENDEUR DOIT COMPRENDRE LA DIFFÉRENCE.
  ///
  /// Supprimer efface la ligne : le SKU se libère, les attributs et le code-barres
  /// sont perdus, et l'historique des commandes qui la référence pointe vers rien.
  /// Retirer de la vente garde tout et ferme la vitrine — c'est ce qu'un vendeur
  /// veut neuf fois sur dix (« plus de taille 42 cette saison »).
  ///
  /// REND LE NOMBRE D'OFFRES ARCHIVÉES, ET IL FAUT L'AFFICHER.
  ///
  /// La désactivation archive les mises en vente de cette déclinaison, et
  /// `Archived` est TERMINAL : réactiver ne les rétablit pas. Le vendeur devra
  /// refixer son prix. Ne pas le dire lui ferait découvrir la conséquence sur un
  /// écran de mises en vente devenu vide.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<int> setVariantActive(String productId, String variantId, {required bool active}) =>
      guard(() async {
        final resp = await dio.post(
          '$_write/products/$productId/variants/$variantId/status',
          data: {'active': active},
        );
        return Json.asInt(Json.map(resp.data)['archivedOffers']);
      });

  Future<void> removeVariant(String productId, String variantId) => guard(() async {
        await dio.delete('$_write/products/$productId/variants/$variantId');
      });
}

final catalogApiProvider = Provider<CatalogApi>((ref) => CatalogApi(ref.watch(dioProvider)));

/// Dépose une photo de produit sur media-service.
///
/// Fourni aux écrans sous forme de fonction pour que `CatalogApi` — qui ne parle
/// qu'à catalog-service — n'ait pas à connaître media-service.
final productPhotoUploaderProvider =
    Provider<Future<UploadedMedia> Function(ProcessedImage, String)>((ref) {
  final media = ref.watch(mediaApiProvider);
  return (image, productId) => media.uploadBytes(
        bytes: image.bytes,
        fileName: image.fileName,
        ownerType: MediaOwner.product,
        ownerId: productId,
        mediaType: MediaKind.productImage,
      );
});

/// Les produits du vendeur connecté.
///
/// LE `sellerId` VIENT DU SOCLE D'IDENTITÉ : la route le porte dans l'URL, là
/// où l'ancien chemin `/seller/products/` le déduisait du jeton.
final productsProvider = FutureProvider<List<SellerProduct>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(catalogApiProvider).products(sellerId);
});

/// Liste « paginée » de l'écran Produits.
///
/// LA PAGINATION ET LA RECHERCHE SONT LOCALES : LE SERVEUR N'EN OFFRE AUCUNE.
///
/// `GET /api/catalog/sellers/{id}/products` rend tout le catalogue du vendeur
/// d'un bloc, sans paramètre. La première page rend donc l'ensemble, les
/// suivantes rien — ce qui arrête proprement le défilement infini. La recherche
/// filtre sur le nom et le SKU des déclinaisons, seuls textes dont l'application
/// dispose.
class ProductsPagedNotifier extends PagedNotifier<SellerProduct> {
  @override
  Future<List<SellerProduct>> fetch({
    required int page,
    required int pageSize,
    required String search,
  }) async {
    if (page > 1) return const [];

    final all = await ref.read(productsProvider.future);
    final q = search.trim().toLowerCase();
    if (q.isEmpty) return all;

    return all
        .where((p) =>
            p.name.toLowerCase().contains(q) ||
            p.variants.any((v) => v.sku.toLowerCase().contains(q)))
        .toList();
  }
}

final productsPagedProvider =
    NotifierProvider.autoDispose<ProductsPagedNotifier, PagedState<SellerProduct>>(ProductsPagedNotifier.new);

final productProvider =
    FutureProvider.family<SellerProduct, String>((ref, id) => ref.watch(catalogApiProvider).product(id));

final categoriesProvider = FutureProvider<List<Category>>((ref) => ref.watch(catalogApiProvider).categories());
final brandsProvider = FutureProvider<List<Brand>>((ref) => ref.watch(catalogApiProvider).brands());

/// Libellé d'une catégorie à partir de son identifiant (le produit ne porte que
/// l'id ; afficher un GUID au vendeur n'aurait aucun sens).
final categoryLabelProvider = Provider.family<String, String>((ref, id) {
  final categories = ref.watch(categoriesProvider).valueOrNull;
  if (categories == null || id.isEmpty) return '—';
  for (final c in categories) {
    if (c.id == id) return c.display;
  }
  return '—';
});
