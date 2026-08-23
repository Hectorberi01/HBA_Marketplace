import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/providers/core_providers.dart';
import '../../../core/push/push_service.dart';
import '../../../core/security/biometric_service.dart';
import '../../account/account_data.dart';
import '../data/auth_api.dart';

enum AuthStatus { unknown, authenticated, unauthenticated }

/// Gère l'état de session et expose login / register / logout.
class AuthController extends Notifier<AuthStatus> {
  @override
  AuthStatus build() {
    // Réagit au signal de session expirée émis par l'intercepteur réseau.
    ref.listen(sessionExpiredProvider, (_, __) {
      state = AuthStatus.unauthenticated;
    });
    _restore();
    return AuthStatus.unknown;
  }

  Future<void> _restore() async {
    final has = await ref.read(tokenStorageProvider).hasSession;
    state = has ? AuthStatus.authenticated : AuthStatus.unauthenticated;

    // Session restaurée au lancement (le cas le PLUS fréquent : l'utilisateur ne
    // se reconnecte pas à chaque ouverture). Sans cette ligne, le jeton push ne
    // serait ré-enregistré qu'à la prochaine connexion explicite — c'est-à-dire
    // presque jamais.
    if (has) {
      unawaited(ref.read(pushServiceProvider).start());
    }
  }

  Future<void> login(String email, String password, {String? mfaCode}) async {
    final tokens = await ref.read(authApiProvider).login(email, password, mfaCode: mfaCode);
    await _persist(tokens);
  }

  /// Rouvre une session à partir du refresh token déverrouillé par biométrie —
  /// SANS mot de passe. On échange le jeton contre une session fraîche.
  Future<void> loginWithBiometrics(String refreshToken) async {
    final tokens = await ref.read(authApiProvider).refresh(refreshToken);
    await _persist(tokens);
  }

  /// Crée le compte et renvoie son identifiant. N'ouvre PAS de session : le
  /// compte doit d'abord vérifier son e-mail (code) puis être validé par un
  /// administrateur. L'écran d'inscription enchaîne sur la saisie du code.
  Future<String> register({
    required String firstName,
    required String lastName,
    required String email,
    String? phoneNumber,
    required String password,
  }) async {
    return ref.read(authApiProvider).register(
          firstName: firstName,
          lastName: lastName,
          email: email,
          phoneNumber: phoneNumber,
          password: password,
        );
  }

  Future<void> logout() async {
    // Le jeton push se retire AVANT que la session ne se ferme : l'appel exige
    // une autorisation valide. Après `storage.clear()`, il partirait sans jeton
    // et serait rejeté — l'appareil resterait alors abonné aux notifications du
    // compte précédent. Sur un téléphone partagé, ce sont les commandes et les
    // colis de quelqu'un d'autre qui s'afficheraient.
    //
    // `await`, et non `unawaited` : ici, l'ordre est tout.
    await ref.read(pushServiceProvider).stop();

    final storage = ref.read(tokenStorageProvider);
    final biometricArmed = await ref.read(biometricServiceProvider).isEnabled;
    final refresh = await storage.refreshToken;
    // On NE révoque PAS le refresh token quand la biométrie est armée : c'est lui
    // qui rouvre la session au Face ID après déconnexion. Il reste protégé par le
    // coffre de l'appareil + la biométrie — et n'est jamais le mot de passe.
    if (!biometricArmed && refresh != null && refresh.isNotEmpty) {
      await ref.read(authApiProvider).logout(refresh);
    }
    await storage.clear();
    state = AuthStatus.unauthenticated;
  }

  Future<void> _persist(AuthTokens tokens) async {
    await ref.read(tokenStorageProvider).save(
          accessToken: tokens.accessToken,
          refreshToken: tokens.refreshToken,
          name: tokens.name,
        );
    state = AuthStatus.authenticated;

    // La session existe : le jeton peut enfin s'enregistrer. `unawaited` — un
    // abonnement push lent ne doit pas retarder l'entrée dans l'application.
    unawaited(ref.read(pushServiceProvider).start());
  }
}

final authControllerProvider =
    NotifierProvider<AuthController, AuthStatus>(AuthController.new);

/// Nom affiché de l'utilisateur.
///
/// **Il vient du PROFIL, pas des jetons.** C'était là le bug de l'accueil, qui
/// saluait « BONJOUR, » suivi de rien.
///
/// L'ancienne version lisait `tokenStorage.userName`, alimenté par
/// `AuthTokens.name` au moment de la connexion. Or `/mobile/auth/login` ne
/// renvoie que `{ mfaRequired, tokens: { accessToken, refreshToken } }` : aucun
/// champ de nom, sous aucune des quatre orthographes que `AuthTokens.fromJson`
/// cherchait. La valeur stockée était donc TOUJOURS la chaîne vide, et le
/// `?? 'Mon compte'` ne rattrapait rien — une chaîne vide n'est pas `null`.
///
/// Le nom est une donnée de profil : sa source de vérité est
/// `/mobile/account/me`. Un effet utile au passage : modifier son profil met
/// l'accueil à jour, ce que le nom figé dans le coffre-fort ne faisait pas.
final userNameProvider = FutureProvider<String>((ref) async {
  // Recalcule quand l'état d'auth change (connexion, déconnexion).
  ref.watch(authControllerProvider);

  try {
    final profile = await ref.watch(profileProvider.future);
    // Le prénom suffit pour une salutation ; le nom complet en secours.
    final first = profile.firstName.trim();
    if (first.isNotEmpty) return first;
    final full = profile.fullName;
    if (full.isNotEmpty) return full;
  } on Exception {
    // Hors ligne ou profil indisponible : on ne casse pas l'accueil pour si peu.
  }

  return 'Mon compte';
});
