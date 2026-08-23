import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/providers/core_providers.dart';
import '../../../core/push/push_service.dart';
import '../data/auth_api.dart';

enum AuthStatus { unknown, authenticated, unauthenticated }

/// État de session : restauration au démarrage, connexion, déconnexion.
class AuthController extends Notifier<AuthStatus> {
  @override
  AuthStatus build() {
    // Réagit au signal émis par l'intercepteur réseau quand le refresh échoue :
    // c'est ce qui fait sortir l'utilisateur de l'app plutôt que de le laisser
    // sur des écrans vides remplis d'erreurs 401.
    //
    // PLUS DE GARDE « MODE SIMULÉ » ICI. Elle existait parce que la connexion
    // factice posait des jetons invalides : le premier appel réel tombait en
    // 401 et refermait aussitôt la session. `MockAuth` a disparu avec
    // `core/mock/`, et l'écoute est donc inconditionnelle.
    ref.listen(sessionExpiredProvider, (_, __) => state = AuthStatus.unauthenticated);
    _restore();
    return AuthStatus.unknown;
  }

  Future<void> _restore() async {
    // Lecture DÉFENSIVE du coffre-fort : Keychain/Keystore peut être
    // indisponible (appareil verrouillé au réveil, profil d'entreprise, plugin
    // absent en test). Une exception ici partirait dans le vide et laisserait
    // l'app figée sur l'écran de démarrage, pour toujours.
    var has = false;
    try {
      has = await ref.read(tokenStorageProvider).hasSession;
    } catch (e) {
      // On ne peut pas prouver qu'il y a une session : on demande à se
      // reconnecter. C'est le seul repli sûr — supposer l'inverse ouvrirait
      // l'app sans jeton valide.
      has = false;
    }

    state = has ? AuthStatus.authenticated : AuthStatus.unauthenticated;

    // Session restaurée au lancement : le jeton push doit être réenregistré. Il
    // change tout seul (réinstallation, restauration de sauvegarde), et un jeton
    // périmé fait taire les notifications en silence.
    if (has) {
      unawaited(ref.read(pushServiceProvider).start());
    }
  }

  /// IL N'Y A PLUS DE BRANCHE DE CONNEXION SIMULÉE.
  ///
  /// Elle acceptait un couple de démonstration et posait des jetons factices,
  /// sous `AppConfig.useMockData`. Le contournement d'authentification a été
  /// supprimé avec `core/mock/` : la connexion passe par
  /// `POST /api/auth/login`, et rien d'autre.
  Future<void> login(String email, String password, {String? mfaCode}) async {
    final tokens = await ref.read(authApiProvider).login(email, password, mfaCode: mfaCode);

    // Les jetons D'ABORD, l'état ENSUITE : la création de boutique ci-dessous
    // est un appel authentifié, elle a besoin du jeton en place.
    await ref.read(tokenStorageProvider).save(
          accessToken: tokens.accessToken,
          refreshToken: tokens.refreshToken,
        );

    await _createPendingShopIfAny();

    _activateSession();
  }

  /// ═══════════════════════════════════════════════════════════════════════════
  /// LA BOUTIQUE LAISSÉE EN ATTENTE PAR L'INSCRIPTION, CRÉÉE À LA CONNEXION.
  ///
  /// CE N'EST PAS UN CONFORT : SANS ELLE, LE NOM DE BOUTIQUE SAISI À
  ///    L'INSCRIPTION ÉTAIT PERDU, ET LE COMPTE RESTAIT ACHETEUR.
  ///
  /// L'ancien parcours tenait en deux appels au BFF du monolithe :
  /// `/seller/auth/verify` validait le code, CRÉAIT la boutique et attribuait le
  /// rôle vendeur. Rien de tout cela n'existe. Sur la plateforme HBA, la boutique
  /// se crée par `POST /api/merchants`, qui exige une SESSION — donc après la
  /// connexion, pas pendant la vérification de l'adresse.
  ///
  /// L'écran de vérification dépose donc son nom de boutique ici, et la première
  /// connexion qui suit le consomme. Sans ce relais, le vendeur s'inscrivait,
  /// saisissait « Chez Mama », se connectait… et arrivait sur une application
  /// vendeur sans vendeur, avec 403 sur toutes les routes `MerchantOnly`.
  ///
  /// APPELÉ AVANT LE BASCULEMENT D'ÉTAT, PAS APRÈS.
  ///
  /// `_activateSession` déclenche la redirection du routeur. La déclencher avant
  /// que la boutique existe enverrait le vendeur sur `bffMerchant/activities`,
  /// qui répondrait 403 — un échec que rien à l'écran ne relierait à l'étape
  /// manquante.
  ///
  /// UN ÉCHEC ICI N'EMPÊCHE PAS DE SE CONNECTER.
  ///
  /// Le compte existe et son adresse est vérifiée : le retenir dehors parce que
  /// merchant-service tousse serait le pire des deux mondes. On garde l'intention
  /// en mémoire — la prochaine connexion réessaiera — et l'application ouvre sur
  /// un compte sans boutique, cas qu'elle sait déjà présenter.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<void> _createPendingShopIfAny() async {
    final pending = ref.read(pendingShopProvider);
    if (pending == null || pending.shopName.trim().isEmpty) return;

    try {
      await ref.read(sellerIdentityApiProvider).registerShop(
            shopName: pending.shopName.trim(),
            metadata: pending.metadata,
          );

      // Le rôle `Seller` est attribué par identity-service en réaction à
      // l'événement d'inscription : le jeton qu'on vient d'obtenir est ANTÉRIEUR
      // et ne le porte pas. Sans cette reprise, tout ce qui suit reçoit 403.
      final refresh = await ref.read(tokenStorageProvider).refreshToken;
      if (refresh != null && refresh.isNotEmpty) {
        final refreshed = await ref.read(authApiProvider).refresh(refresh);
        await ref.read(tokenStorageProvider).save(
              accessToken: refreshed.accessToken,
              refreshToken: refreshed.refreshToken,
            );
      }

      ref.read(pendingShopProvider.notifier).state = null;
    } catch (_) {
      // Volontairement muet : voir le commentaire ci-dessus. L'intention reste
      // en mémoire pour la prochaine connexion.
    }
  }

  /// Crée la boutique du compte connecté, puis REPREND un jeton.
  ///
  /// LE RAFRAÎCHISSEMENT N'EST PAS UNE PRÉCAUTION, C'EST L'ÉTAPE MANQUANTE.
  ///
  /// Le rôle `Seller` est attribué par identity-service à la réception de
  /// l'événement « vendeur inscrit ». Le jeton obtenu à la connexion est
  /// antérieur : il ne le porte pas, et un JWT ne se met pas à jour tout seul.
  /// Sans cette reprise, le vendeur venait de créer sa boutique et recevait 403
  /// sur toutes les routes `MerchantOnly` — sans qu'aucun message ne relie les
  /// deux faits.
  ///
  /// L'attribution du rôle est ASYNCHRONE (événement d'intégration) : un
  /// rafraîchissement immédiat peut arriver trop tôt. On ne boucle pas ici — ce
  /// serait masquer le problème — l'écran d'accueil vendeur doit savoir gérer un
  /// 403 transitoire en proposant de réessayer.
  Future<String> registerShop({
    required String shopName,
    Map<String, dynamic>? metadata,
  }) async {
    final sellerId = await ref
        .read(sellerIdentityApiProvider)
        .registerShop(shopName: shopName, metadata: metadata);

    final refresh = await ref.read(tokenStorageProvider).refreshToken;
    if (refresh != null && refresh.isNotEmpty) {
      final tokens = await ref.read(authApiProvider).refresh(refresh);
      await ref.read(tokenStorageProvider).save(
            accessToken: tokens.accessToken,
            refreshToken: tokens.refreshToken,
          );
    }

    // L'identité vendeur n'existait pas au dernier calcul : on la redemande.
    ref.invalidate(sellerIdentityProvider);
    return sellerId;
  }

  /// Bascule la session en « ouverte », une fois les jetons en place.
  ///
  /// Séparé de l'écriture des jetons parce que la connexion doit pouvoir glisser
  /// une étape ENTRE les deux — la création de la boutique en attente, qui a
  /// besoin du jeton mais doit précéder la redirection du routeur.
  void _activateSession() {
    state = AuthStatus.authenticated;

    // L'identité vendeur d'une session précédente ne vaut rien pour celle-ci :
    // sur un téléphone partagé, elle désignerait la boutique de quelqu'un d'autre.
    ref.invalidate(sellerIdentityProvider);

    // APRÈS l'ouverture de session : le jeton s'attache au vendeur connecté.
    // Le démarrer avant enregistrerait l'appareil sans savoir pour qui.
    unawaited(ref.read(pushServiceProvider).start());
  }

  /// Déconnexion — elle ABOUTIT TOUJOURS.
  ///
  /// Chaque étape est isolée : le serveur peut être injoignable, le jeton déjà
  /// révoqué (401), le Keychain capricieux — rien de tout cela ne doit empêcher
  /// le vendeur de sortir. Auparavant, une seule de ces erreurs remontait, les
  /// jetons restaient en place et l'app se retrouvait dans un état bâtard :
  /// « déconnecté » à l'écran, encore porteur d'une session sur le disque.
  ///
  /// La purge locale et le basculement d'état sont donc dans un `finally` : ce
  /// sont les deux seules choses qui comptent vraiment.
  Future<void> logout() async {
    try {
      // Désabonnement AVANT de purger les jetons : l'appel exige la session. Sinon
      // l'appareil continuerait de recevoir les notifications du vendeur précédent
      // — sur un téléphone partagé ou revendu, c'est une fuite de données.
      await ref.read(pushServiceProvider).stop();

      final refresh = await ref.read(tokenStorageProvider).refreshToken;
      if (refresh != null && refresh.isNotEmpty) {
        // Révocation côté serveur : sans elle, le jeton de rafraîchissement
        // resterait valide jusqu'à son expiration naturelle.
        await ref.read(authApiProvider).logout(refresh);
      }
    } catch (_) {
      // Volontairement muet : l'échec de la révocation distante ne regarde pas
      // le vendeur, et surtout il ne doit pas le retenir dans l'app.
    } finally {
      try {
        await ref.read(tokenStorageProvider).clear();
      } catch (_) {
        // Coffre-fort inaccessible : on bascule quand même l'état. Un jeton
        // orphelin sur le disque est moins grave qu'une app dont on ne sort pas.
      }
      // L'identité vendeur suit la session : la garder en cache montrerait la
      // boutique précédente à qui se connecterait ensuite.
      ref.invalidate(sellerIdentityProvider);
      state = AuthStatus.unauthenticated;
    }
  }
}

final authControllerProvider = NotifierProvider<AuthController, AuthStatus>(AuthController.new);

/// Boutique qu'un compte fraîchement inscrit attend de voir créée.
///
/// Déposée par l'écran de vérification du code, consommée par la connexion qui
/// suit (voir `AuthController._createPendingShopIfAny`).
class PendingShop {
  const PendingShop({required this.shopName, this.metadata});

  final String shopName;

  /// Informations société déclarées à l'inscription (`SellerCompanyInfo` côté
  /// merchant-service). Facultatives : c'est du déclaratif de pré-remplissage,
  /// la preuve reste le KYB.
  final Map<String, dynamic>? metadata;
}

/// EN MÉMOIRE SEULEMENT, ET C'EST UNE LIMITE À CONNAÎTRE.
///
/// Un vendeur qui ferme l'application entre la vérification de son adresse et sa
/// première connexion perd le nom qu'il avait saisi : son compte s'ouvrira sans
/// boutique. Le persister exigerait de choisir un stockage et une durée de vie
/// pour une donnée à moitié engagée — arbitrage qui n'a pas lieu d'être tant que
/// merchant-service n'expose pas d'inscription vendeur en une passe.
///
/// L'application sait présenter un compte sans boutique : ce n'est pas une
/// impasse, seulement une étape à refaire.
final pendingShopProvider = StateProvider<PendingShop?>((ref) => null);

/// Nom de la boutique affiché dans l'application.
///
/// IL VIENT DE `GET /api/merchants/me`, PLUS DU COFFRE-FORT.
///
/// L'ancienne version lisait `tokenStorage.sellerName`, alimenté par
/// `AuthTokens.name` à la connexion. Or le login ne renvoie que
/// `{ mfaRequired, tokens: {…} }` : aucun champ de nom, sous aucune des
/// orthographes cherchées. La valeur stockée était donc TOUJOURS vide, et
/// l'en-tête affichait « Ma boutique » à tous les vendeurs, en permanence.
///
/// Effet utile au passage : renommer sa boutique met l'en-tête à jour, ce qu'un
/// nom figé dans le coffre-fort ne faisait pas.
final sellerNameProvider = FutureProvider<String>((ref) async {
  ref.watch(authControllerProvider); // recalcule à chaque changement de session

  try {
    final seller = await ref.watch(sellerIdentityProvider.future);
    if (seller != null && seller.shopName.isNotEmpty) return seller.shopName;
  } on Exception {
    // Hors ligne, ou compte sans boutique : on n'affiche pas une erreur pour un
    // titre d'écran.
  }

  // Repli hors ligne : le dernier nom vu, sinon un titre neutre. Ce n'est PAS
  // une donnée fraîche — ne rien en déduire d'autre que « quoi écrire en
  // en-tête tant que `/api/merchants/me` n'a pas répondu ».
  return (await ref.watch(tokenStorageProvider).sellerName) ?? 'Ma boutique';
});
