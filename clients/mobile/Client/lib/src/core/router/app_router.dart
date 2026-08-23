import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/account/presentation/account_screen.dart';
import '../../features/account/presentation/addresses_screen.dart';
import '../../features/account/presentation/edit_profile_screen.dart';
import '../../features/account/presentation/delete_account_screen.dart';
import '../../features/account/presentation/notification_preferences_screen.dart';
import '../../features/account/presentation/notifications_screen.dart';
import '../../features/account/presentation/wishlist_screen.dart';
import '../../features/app_update/app_update_controller.dart';
import '../../features/app_update/presentation/update_required_screen.dart';
import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/auth/presentation/register_screen.dart';
import '../../features/auth/presentation/verify_email_screen.dart';
import '../../features/auth/data/auth_api.dart';
import '../../features/auth/presentation/splash_screen.dart';
import '../../features/cart/presentation/cart_screen.dart';
import '../../features/engagement/presentation/dispute_detail_screen.dart';
import '../../features/engagement/presentation/disputes_screen.dart';
import '../../features/engagement/presentation/loyalty_screen.dart';
import '../../features/engagement/presentation/returns_screen.dart';
import '../../features/catalog/presentation/category_screen.dart';
import '../../features/catalog/presentation/product_detail_screen.dart';
import '../../features/catalog/presentation/product_reviews_screen.dart';
import '../../features/catalog/presentation/search_screen.dart';
import '../../features/checkout/presentation/checkout_screen.dart';
import '../../features/home/presentation/express_home_screen.dart';
import '../../features/home/presentation/food_home_screen.dart';
import '../../features/home/presentation/home_screen.dart';
import '../../features/food/presentation/restaurant_detail_screen.dart';
import '../../features/legal/consent_controller.dart';
import '../../features/legal/presentation/consent_screen.dart';
import '../../features/legal/presentation/privacy_screen.dart';
import '../../features/legal/presentation/terms_screen.dart';
import '../../features/messaging/presentation/chat_screen.dart';
import '../../features/messaging/presentation/conversations_screen.dart';
import '../../features/orders/presentation/order_confirmation_screen.dart';
import '../../features/orders/presentation/order_detail_screen.dart';
import '../../features/orders/presentation/order_tracking_screen.dart';
import '../../features/orders/presentation/orders_screen.dart';
import '../../features/shop/presentation/shop_screen.dart';
import '../../features/support/presentation/faq_screen.dart';
import '../../navigation/main_shell.dart';

/// Routes accessibles SANS COMPTE (mode visiteur).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EXIGENCE APP STORE 5.1.1(v). L'app était refusée pour cette raison :
/// « the app requires users to register before browsing items ».
///
/// Une place de marché doit pouvoir être parcourue librement — catalogue, fiches
/// produit, recherche, boutiques, avis. Seules les fonctions RATTACHÉES AU COMPTE
/// (panier, commandes, profil, favoris, messagerie, litiges) peuvent exiger une
/// connexion.
///
/// Le test est un PRÉFIXE : « /product/… » couvre la fiche et ses avis.
/// ─────────────────────────────────────────────────────────────────────────────
/// Exposé pour `app.dart`, qui doit savoir si une destination en attente (lien
/// partagé, notification) peut être atteinte SANS session — sinon un visiteur
/// cliquant un lien produit est renvoyé à l'accueil, et le produit est perdu.
bool isPublicRoute(String location) => _isPublicRoute(location);

bool _isPublicRoute(String location) {
  const publicPrefixes = <String>[
    '/home',
    '/express',
    '/food',
    '/search',
    '/category/',
    '/product/',
    '/restaurant/',
    '/shop/',
    // Pages légales : consultables avant toute création de compte.
    '/terms',
    '/privacy',
    // Parcours d'authentification lui-même.
    '/login',
    '/register',
    '/verify-email',
  ];
  return publicPrefixes.any((p) => location == p || location.startsWith(p));
}

final routerProvider = Provider<GoRouter>((ref) {
  // Le routeur écoute la session ET le consentement : c'est le seul endroit qui
  // garantit qu'aucun écran n'est atteignable avant l'acceptation des conditions.
  // Poser le garde-fou écran par écran, c'est en oublier un — et un seul suffit.
  final refresh = ValueNotifier(0);
  ref.listen(authControllerProvider, (_, __) => refresh.value++);
  ref.listen(consentControllerProvider, (_, __) => refresh.value++);
  // La porte « mise à jour requise » précède l'auth : un client périmé ne doit
  // même pas pouvoir se connecter. Le routeur se réévalue dès que son état change.
  ref.listen(appUpdateControllerProvider, (_, __) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/splash',
    refreshListenable: refresh,
    redirect: (context, state) {
      final status = ref.read(authControllerProvider);
      final loc = state.matchedLocation;
      final onAuthPage = loc == '/login' || loc == '/register' || loc == '/verify-email';
      final onSplash = loc == '/splash';
      final onConsent = loc == '/consent';
      final onUpdate = loc == '/update-required';

      // PORTE N°0 — mise à jour requise, AVANT toute considération de session.
      switch (ref.read(appUpdateControllerProvider)) {
        case AppUpdateStatus.unknown:
          return onSplash ? null : '/splash';
        case AppUpdateStatus.updateRequired:
          return onUpdate ? null : '/update-required';
        case AppUpdateStatus.upToDate:
        case AppUpdateStatus.unavailable:
          break;
      }
      if (onUpdate) return '/splash'; // porte franchie : cet écran n'a plus lieu d'être

      if (status == AuthStatus.unknown) return onSplash ? null : '/splash';

      // VISITEUR (pas de session) : il parcourt librement le catalogue. On ne
      // l'envoie vers la connexion que s'il demande une fonction liée au compte.
      // Le splash le dépose sur l'accueil, pas sur l'écran de connexion.
      if (status == AuthStatus.unauthenticated) {
        if (onSplash) return '/home';
        return _isPublicRoute(loc) ? null : '/login';
      }

      // Session ouverte. Reste à savoir si les conditions sont acceptées.
      switch (ref.read(consentControllerProvider)) {
        // Vérification en cours : on patiente. Ouvrir l'app « en attendant »
        // reviendrait à laisser entrer sans contrôle.
        case ConsentStatus.unknown:
          return onSplash ? null : '/splash';

        // Accord manquant ou texte modifié : rien d'autre n'est atteignable.
        case ConsentStatus.required:
          return onConsent ? null : '/consent';

        // Vérification impossible (hors ligne) : on laisse passer et on
        // redemandera. Bloquer ici rendrait l'app inutilisable sans réseau, sans
        // même pouvoir enregistrer une acceptation.
        case ConsentStatus.granted:
        case ConsentStatus.unavailable:
          break;
      }

      // Authentifié et à jour : ne reste pas sur splash/login/register/consent.
      if (onSplash || onAuthPage || onConsent) return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, __) => const SplashScreen()),
      // Mise à jour requise : hors coquille, sans onglets ni retour (porte n°0).
      GoRoute(path: '/update-required', builder: (_, __) => const UpdateRequiredScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/register', builder: (_, __) => const RegisterScreen()),

      // Saisie du code de vérification d'e-mail. Reçoit ses données via `extra` ;
      // sans elles (deep-link, reload), on renvoie vers l'inscription.
      GoRoute(
        path: '/verify-email',
        builder: (_, s) => s.extra is EmailVerifyArgs
            ? VerifyEmailScreen(args: s.extra as EmailVerifyArgs)
            : const RegisterScreen(),
      ),

      // Consentement : HORS de la coquille, sans barre d'onglets. L'y placer
      // laisserait les onglets accessibles — et l'écran cesserait d'être bloquant.
      GoRoute(path: '/consent', builder: (_, __) => const ConsentScreen()),
      GoRoute(path: '/terms', builder: (_, __) => const TermsScreen()),
      GoRoute(path: '/privacy', builder: (_, __) => const PrivacyScreen()),

      // Coquille avec barre de navigation inférieure.
      ShellRoute(
        builder: (context, state, child) => MainShell(location: state.uri.path, child: child),
        routes: [
          GoRoute(path: '/home', builder: (_, __) => const HomeScreen()),
          GoRoute(path: '/express', builder: (_, __) => const ExpressHomeScreen()),
          GoRoute(path: '/food', builder: (_, __) => const FoodHomeScreen()),
          GoRoute(path: '/search', builder: (_, __) => const SearchScreen()),
          GoRoute(path: '/cart', builder: (_, __) => const CartScreen()),
          GoRoute(path: '/orders', builder: (_, __) => const OrdersScreen()),
          GoRoute(path: '/wishlist', builder: (_, __) => const WishlistScreen()),
          GoRoute(path: '/account', builder: (_, __) => const AccountScreen()),
          GoRoute(path: '/account/addresses', builder: (_, __) => const AddressesScreen()),
          GoRoute(path: '/account/edit', builder: (_, __) => const EditProfileScreen()),
          GoRoute(path: '/account/notifications', builder: (_, __) => const NotificationPreferencesScreen()),
          GoRoute(path: '/account/faq', builder: (_, __) => const FaqScreen()),
          GoRoute(path: '/notifications', builder: (_, __) => const NotificationsScreen()),
        ],
      ),

      // Routes empilées (hors coquille).
      GoRoute(
        path: '/category/:id',
        builder: (_, s) => CategoryScreen(
          categoryId: s.pathParameters['id']!,
          categoryName: s.extra is String ? s.extra as String : null,
        ),
      ),
      GoRoute(path: '/product/:id', builder: (_, s) => ProductDetailScreen(productId: s.pathParameters['id']!)),
      GoRoute(path: '/product/:id/reviews', builder: (_, s) => ProductReviewsScreen(productId: s.pathParameters['id']!)),
      GoRoute(path: '/restaurant/:id', builder: (_, s) => RestaurantDetailScreen(restaurantId: s.pathParameters['id']!)),
      GoRoute(path: '/checkout', builder: (_, __) => const CheckoutScreen()),
      GoRoute(path: '/order/:id', builder: (_, s) => OrderDetailScreen(orderId: s.pathParameters['id']!)),
      GoRoute(path: '/order/:id/confirmation', builder: (_, s) => OrderConfirmationScreen(orderId: s.pathParameters['id']!)),
      GoRoute(path: '/order/:id/tracking', builder: (_, s) => OrderTrackingScreen(orderId: s.pathParameters['id']!)),
      // Suppression du compte — exigée par Apple (Guideline 5.1.1(v)) dès lors que
      // l'application permet d'en créer un. Sans cette route, l'app est rejetée.
      GoRoute(path: '/account/delete', builder: (_, __) => const DeleteAccountScreen()),
      // Route /account/payments retirée : moyens de paiement enregistrés non
      // branchés (FedaPay uniquement, cf. account_screen). À réactiver avec une
      // tokenisation avant de rouvrir cet écran.
      GoRoute(path: '/account/returns', builder: (_, __) => const ReturnsScreen()),
      GoRoute(path: '/account/loyalty', builder: (_, __) => const LoyaltyScreen()),
      GoRoute(path: '/account/disputes', builder: (_, __) => const DisputesScreen()),
      GoRoute(path: '/dispute/:id', builder: (_, s) => DisputeDetailScreen(disputeId: s.pathParameters['id']!)),
      GoRoute(path: '/shop/:id', builder: (_, s) => ShopScreen(sellerId: s.pathParameters['id']!)),
      GoRoute(path: '/conversations', builder: (_, __) => const ConversationsScreen()),
      GoRoute(path: '/chat/:id', builder: (_, s) => ChatScreen(conversationId: s.pathParameters['id']!)),
    ],
  );
});
