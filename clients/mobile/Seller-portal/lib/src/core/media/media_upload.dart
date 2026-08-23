import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http_parser/http_parser.dart';

import '../config/app_config.dart';
import '../network/api_base.dart';
import '../network/api_exception.dart';
import '../providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Fichier déposé : son identifiant, et l'URL rendue par le service.
class UploadedMedia {
  const UploadedMedia({required this.mediaId, required this.url});

  /// C'EST L'IDENTIFIANT QUI COMPTE, PAS L'URL.
  ///
  /// Les services métier stockent désormais un `MediaId` et non une URL publique
  /// (migration « Media 4 »). merchant-service attend `AddKybDocumentRequest(Type,
  /// MediaId)`, catalog-service attend `ProductMediaRequest(MediaId, Url, …)`,
  /// communication-service attend `MessageAttachmentInput(MediaId, ContentType)`.
  /// Une URL recopiée à la place ferait échouer la liaison du Guid.
  final String mediaId;

  /// URL rendue à l'issue du dépôt. Utilisable telle quelle pour les natures
  /// PUBLIQUES (image produit, logo). Pour les natures PRIVÉES — pièces KYB,
  /// documents — elle n'est pas directement lisible : il faut demander une URL
  /// signée à `GET /api/media/{id}/download-url`.
  final String url;
}

/// Natures de fichier (`MediaType` côté serveur). Valeurs EXACTES : le service
/// fait un `Enum.TryParse` insensible à la casse et répond 400
/// `invalid_media_type` avec la liste attendue si le nom ne colle pas.
class MediaKind {
  const MediaKind._();

  static const productImage = 'ProductImage';
  static const storeMedia = 'StoreMedia';
  static const restaurantMedia = 'RestaurantMedia';
  static const userAvatar = 'UserAvatar';

  /// PRIVÉ : pièces légales du vendeur (KYB).
  static const sellerDocument = 'SellerDocument';

  /// RESTREINT : pièce jointe de discussion. La seule nature générique du
  /// contrat — il n'existe pas de `MessageAttachment` côté media-service.
  static const attachment = 'Attachment';
}

/// Types de propriétaire (`MediaOwnerType`). Même règle : valeurs exactes.
class MediaOwner {
  const MediaOwner._();

  static const product = 'Product';
  static const store = 'Store';
  static const restaurant = 'Restaurant';
  static const menuItem = 'MenuItem';
  static const user = 'User';
  static const seller = 'Seller';
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LE DÉPÔT DE FICHIERS EST CENTRALISÉ, PARCE QU'IL A CHANGÉ DE FORME PARTOUT.
///
/// IL N'Y A PLUS DE ROUTE D'UPLOAD PAR DOMAINE.
///
/// L'application postait ses fichiers là où ils servaient : `/seller/shop/logo`,
/// `/seller/shop/kyb-documents/upload`, `/seller/products/{id}/media/upload`,
/// `/seller/conversations/attachments`. Aucune de ces quatre routes n'existe.
///
/// La plateforme a un service dédié : `POST /api/media`, multipart, champ
/// « file », avec trois paramètres de REQUÊTE — `ownerType`, `ownerId`,
/// `mediaType`. Il rend `{ mediaId, url }`. C'est ensuite au service métier qu'on
/// rattache l'identifiant obtenu.
///
/// Le dépôt se fait donc en DEUX temps, et il faut le savoir en écrivant les
/// écrans : téléverser d'abord, rattacher ensuite. Un dépôt réussi suivi d'un
/// rattachement raté laisse un fichier orphelin dans media-service — moins grave
/// qu'une fiche produit qui référence une image inexistante, mais à nettoyer un
/// jour.
///
/// LE TYPE MIME DÉCLARÉ NE SERT À RIEN, ET C'EST VOLONTAIRE CÔTÉ SERVEUR.
///
/// `UploadValidation` inspecte les OCTETS et ignore le `Content-Type` du
/// multipart — un en-tête écrit par le client se forge en une ligne de curl. On
/// l'envoie quand même, correctement déduit de l'extension : certains proxys s'en
/// servent, et un `application/octet-stream` peut être refusé en amont.
/// ═════════════════════════════════════════════════════════════════════════════
class MediaApi extends ApiBase {
  const MediaApi(super.dio);

  static const _p = AppConfig.media;

  Future<UploadedMedia> uploadBytes({
    required Uint8List bytes,
    required String fileName,
    required String ownerType,
    required String ownerId,
    required String mediaType,
  }) =>
      guard(() async {
        final form = FormData();
        form.files.add(MapEntry(
          // LE CHAMP S'APPELLE « file », AU SINGULIER ET EN MINUSCULES : c'est
          // le nom du paramètre `IFormFile? file` du endpoint. « images » ou
          // « File » ne se lient pas, et le serveur répond « fichier manquant ».
          'file',
          MultipartFile.fromBytes(bytes, filename: fileName, contentType: mediaTypeOf(fileName)),
        ));

        final resp = await dio.post(
          _p,
          data: form,
          queryParameters: {
            'ownerType': ownerType,
            'ownerId': ownerId,
            'mediaType': mediaType,
          },
        );

        final data = Json.map(resp.data);
        final id = Json.str(data['mediaId']);
        if (id.isEmpty) {
          // Un dépôt « réussi » sans identifiant ne peut être rattaché à rien :
          // la fiche partirait avec un Guid vide et échouerait à la liaison, une
          // étape plus loin, sans rapport apparent avec le fichier.
          throw ApiException(
            "Le fichier a été déposé, mais son identifiant est absent de la réponse.",
          );
        }
        return UploadedMedia(mediaId: id, url: Json.str(data['url']));
      });

  /// URL signée, de courte durée, pour un média PRIVÉ (pièce KYB, document).
  ///
  /// Les médias publics sont directement lisibles par leur `url` ; les privés ne
  /// le sont pas, et afficher leur URL brute rendrait une image cassée.
  Future<String> signedUrl(String mediaId) => guard(() async {
        final resp = await dio.get('$_p/$mediaId/download-url');
        final data = Json.map(resp.data);
        return Json.str(data['url'] ?? data['downloadUrl']);
      });

  /// Type MIME déduit de l'extension. Seuls jpeg/png/webp/pdf traversent les
  /// listes blanches du serveur — pas de branche HEIC : la déclarer reviendrait à
  /// annoncer un format que `UploadValidation` refuse.
  static MediaType mediaTypeOf(String fileName) {
    final ext = fileName.contains('.') ? fileName.split('.').last.toLowerCase() : '';
    switch (ext) {
      case 'pdf':
        return MediaType('application', 'pdf');
      case 'png':
        return MediaType('image', 'png');
      case 'webp':
        return MediaType('image', 'webp');
      default:
        return MediaType('image', 'jpeg');
    }
  }
}

final mediaApiProvider = Provider<MediaApi>((ref) => MediaApi(ref.watch(dioProvider)));
