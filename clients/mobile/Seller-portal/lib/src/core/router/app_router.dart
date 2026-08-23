import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../features/activities/presentation/activity_selection_screen.dart';
import '../../features/activities/presentation/activities_tab_screen.dart';
import '../../features/account/presentation/account_screen.dart';
import '../../features/account/presentation/notifications_screen.dart';
import '../../features/account/presentation/privacy_screen.dart';
import '../../features/account/presentation/terms_screen.dart';
import '../../features/account/presentation/profile_screen.dart';
import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/auth/presentation/register_screen.dart';
import '../../features/auth/presentation/verify_code_screen.dart';
import '../../features/auth/presentation/forgot_password_screen.dart';
import '../../features/auth/presentation/reset_password_screen.dart';
import '../../features/auth/data/auth_api.dart';
import '../../features/auth/presentation/splash_screen.dart';
import '../../features/app_update/app_update_controller.dart';
import '../../features/app_update/presentation/update_required_screen.dart';
import '../../features/catalog/presentation/product_detail_screen.dart';
import '../../features/legal/consent_controller.dart';
import '../../features/legal/presentation/consent_screen.dart';
import '../../features/disputes/presentation/dispute_screen.dart';
import '../../features/returns/presentation/returns_screen.dart';
import '../../features/catalog/presentation/product_preview_screen.dart';
import '../../features/catalog/presentation/product_wizard_screen.dart';
import '../../features/activities/presentation/activity_wizard_screen.dart';
import '../../features/menu/presentation/dish_detail_screen.dart';
import '../../features/menu/presentation/dish_wizard_screen.dart';
import '../../features/menu/presentation/kitchen_screen.dart';
import '../../features/menu/presentation/menus_screen.dart';
import '../../features/dashboard/presentation/partner_home_screen.dart';
import '../../features/finance/presentation/finance_screen.dart';
import '../../features/finance/presentation/partner_finance_screen.dart';
import '../../features/messaging/presentation/chat_screen.dart';
import '../../features/messaging/presentation/conversations_screen.dart';
import '../../features/account/presentation/notification_preferences_screen.dart';
import '../../features/help/presentation/help_screen.dart';
import '../../features/settings/presentation/settings_screen.dart';
import '../../features/offers/presentation/offers_screen.dart';
import '../../features/offers/presentation/shipping_locations_screen.dart';
import '../../features/orders/presentation/partner_order_detail_screen.dart';
import '../../features/orders/presentation/orders_tab_screen.dart';
import '../../features/reviews/presentation/reviews_screen.dart';
import '../../features/shipments/presentation/shipments_screen.dart';
import '../../features/shop/presentation/shop_screen.dart';
import '../../features/wallet/presentation/wallet_screen.dart';
import '../../navigation/main_shell.dart';

final routerProvider = Provider<GoRouter>((ref) {
  // Le routeur se réévalue à chaque changement de session : c'est ce qui fait
  // sortir l'utilisateur de l'app dès que son jeton n'est plus valide.
  //
  // Il écoute AUSSI le consentement : c'est le seul moyen de garantir qu'aucun
  // écran de l'app n'est atteignable avant l'acceptation des conditions. Poser le
  // garde-fou dans chaque écran, c'est en oublier un — et un seul suffit.
  final refresh = ValueNotifier(0);
  ref.listen(authControllerProvider, (_, __) => refresh.value++);
  ref.listen(consentControllerProvider, (_, __) => refresh.value++);
  // La porte « mise à jour requise » précède l'auth : un client périmé ne doit
  // même pas pouvoir se connecter. Le routeur doit donc se réévaluer dès que son
  // état est connu.
  ref.listen(appUpdateControllerProvider, (_, __) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/splash',
    refreshListenable: refresh,

    // Sans errorBuilder, une destination qui ne correspond à aucune route ne
    // produit RIEN : l'écran reste noir, sans message, sans bouton, sans issue.
    // Le vendeur croit l'app plantée. On préfère un écran qui dit ce qui se
    // passe et qui mise en vente au moins un chemin de retour.
    errorBuilder: (context, state) => _RouteError(message: state.error?.toString()),

    redirect: (context, state) {
      final status = ref.read(authControllerProvider);
      final loc = state.matchedLocation;
      final onLogin = loc == '/login';
      final onRegister = loc == '/register';
      final onVerify = loc == '/verify';
      final onForgot = loc == '/forgot-password';
      final onReset = loc == '/reset-password';
      final onSplash = loc == '/splash';
      final onConsent = loc == '/consent';
      final onUpdate = loc == '/update-required';

      // PORTE N°0 — mise à jour requise, AVANT toute considération de session.
      // Un build trop ancien est bloqué ici et nulle part ailleurs : le poser en
      // premier garantit qu'aucun écran (pas même le login) n'est atteignable.
      final update = ref.read(appUpdateControllerProvider);
      switch (update) {
        // Vérification en cours : on patiente sur le splash (comme pour l'auth).
        case AppUpdateStatus.unknown:
          return onSplash ? null : '/splash';
        // Build périmé : l'écran de mise à jour est le SEUL accessible.
        case AppUpdateStatus.updateRequired:
          return onUpdate ? null : '/update-required';
        // À jour, ou vérification impossible (fail-open) : on continue.
        case AppUpdateStatus.upToDate:
        case AppUpdateStatus.unavailable:
          break;
      }

      // L'écran de mise à jour n'a plus lieu d'être une fois la porte franchie.
      if (onUpdate) return '/splash';

      if (status == AuthStatus.unknown) return onSplash ? null : '/splash';

      // Écrans hors session : login, auto-inscription, mot de passe oublié.
      if (status == AuthStatus.unauthenticated) {
        return (onLogin || onRegister || onVerify || onForgot || onReset) ? null : '/login';
      }

      // Session ouverte. Reste à savoir si les conditions sont acceptées.
      final consent = ref.read(consentControllerProvider);

      switch (consent) {
        // Vérification en cours : on patiente sur le démarrage. Ouvrir l'app
        // « en attendant » reviendrait à laisser entrer sans contrôle.
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

      // ═══════════════════════════════════════════════════════════════════════
      // ATTERRISSAGE APRÈS CONNEXION.
      //
      // La maquette pose un aiguillage : « affiché après connexion UNIQUEMENT si
      // le compte possède plus d'une activité ». Un partenaire mono-boutique ne
      // doit jamais le voir — il n'aurait qu'un seul bouton à presser, ce qui
      // est une étape de plus, pas un choix.
      //
      // CE N'EST PAS UNE BOUCLE.
      //
      // Depuis `/select-activity`, les boutons mènent à `/home`. Cette
      // redirection ne se déclenche que pour `/splash`, `/login` et `/consent` :
      // `/home` n'en fait pas partie et reste donc atteignable.
      //
      // L'ACTIVITÉ CHOISIE N'EST PAS MÉMORISÉE, ET CELA SE VERRA.
      //
      // À chaque redémarrage avec une session valide, le partenaire repassera
      // par l'aiguillage. Acceptable tant que les données sont simulées ; en
      // usage réel il faudra retenir le dernier choix, sans quoi ouvrir l'app
      // vingt fois par jour imposera vingt fois le même écran.
      // ═══════════════════════════════════════════════════════════════════════
      // ON PASSE TOUJOURS PAR L'AIGUILLAGE, MÊME AVEC UNE SEULE ACTIVITÉ.
      //
      // Le test portait sur une liste d'activités FIGÉE, donc lisible
      // instantanément. Les activités viennent maintenant de
      // `GET /api/v1/bff/merchant/activities` — un appel
      // réseau. Un `redirect` de GoRouter est SYNCHRONE : il ne peut ni attendre
      // la réponse, ni s'abonner.
      //
      // Trois issues étaient possibles :
      //   • lire le cache du provider — il est vide au premier passage, donc on
      //     enverrait tout le monde sur `/home`, y compris les multi-activités ;
      //   • bloquer sur le splash le temps de l'appel — une porte de plus avant
      //     d'entrer, et un écran figé si le BFF est lent ;
      //   • passer par l'aiguillage, qui SAIT attendre.
      //
      // C'est la troisième : `ActivitySelectionScreen` porte déjà ses états de
      // chargement, d'erreur et de vide, et il est le seul endroit qui connaisse
      // le nombre d'activités.
      //
      // CONSÉQUENCE ASSUMÉE : un partenaire mono-boutique voit désormais un
      // écran avec un seul bouton, ce que la maquette voulait éviter. À corriger
      // DANS l'écran — passer tout droit quand la liste n'a qu'un élément — et
      // non ici, où l'information n'est pas disponible.
      if (onSplash || onLogin || onConsent) {
        return '/select-activity';
      }
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/register', builder: (_, __) => const RegisterScreen()),

      // Saisie du code : reçoit ses données via `extra`. Si l'écran est atteint
      // sans données (deep-link, reload), on renvoie vers l'inscription.
      GoRoute(
        path: '/verify',
        builder: (_, s) => s.extra is SellerVerifyArgs
            ? VerifyCodeScreen(args: s.extra as SellerVerifyArgs)
            : const RegisterScreen(),
      ),
      GoRoute(path: '/forgot-password', builder: (_, __) => const ForgotPasswordScreen()),
      GoRoute(
        path: '/reset-password',
        builder: (_, s) => s.extra is String && (s.extra as String).isNotEmpty
            ? ResetPasswordScreen(email: s.extra as String)
            : const ForgotPasswordScreen(),
      ),

      // Consentement : HORS de la coquille, sans barre d'onglets. Le placer dans
      // la coquille laisserait les onglets accessibles — et l'écran cesserait
      // d'être bloquant.
      // Aiguillage post-connexion. HORS ShellRoute : la maquette écrit « Pas de
      // bottom nav » — une barre suppose un contexte choisi, et « Commandes »
      // n'aurait ici aucune activité à interroger.
      GoRoute(path: '/select-activity', builder: (_, __) => const ActivitySelectionScreen()),
      GoRoute(path: '/consent', builder: (_, __) => const ConsentScreen()),

      // Mise à jour requise : HORS coquille, sans barre d'onglets ni retour.
      // Écran terminal tant que le build est périmé (cf. porte n°0 du redirect).
      GoRoute(path: '/update-required', builder: (_, __) => const UpdateRequiredScreen()),

      // Onglets (coquille avec barre de navigation).
      ShellRoute(
        builder: (context, state, child) => MainShell(location: state.uri.path, child: child),
        routes: [
          GoRoute(path: '/home', builder: (_, __) => const PartnerHomeScreen()),
          // MÊME AIGUILLAGE QUE L'ACCUEIL : consolidé, ou par activité.
          //
          // La branche CONSOLIDÉE des deux onglets (`GlobalDashboardScreen` et
          // `GlobalOrdersScreen`) annonce désormais son indisponibilité : aucune
          // agrégation multi-activités n'existe côté serveur. Les branches PAR
          // ACTIVITÉ sont intactes — ce sont elles que VEN3 câblera.
          //
          // ═══════════════════════════════════════════════════════════════════
          // `/products` A ÉTÉ SUPPRIMÉE, ET AVEC ELLE QUATRE FICHIERS.
          //
          // `ProductsScreen`, `ProductWizardSheet`, `ProductCreateSheet` et
          // `OrdersScreen` dataient d'avant le modèle multi-activités. Elles
          // faisaient doublon avec `PartnerProductsScreen`, `ProductWizardScreen`
          // et `ActivityOrdersScreen`, qui font le même travail DANS le contexte
          // d'une activité — ce qui est la seule façon juste de le faire depuis
          // qu'un compte peut porter plusieurs boutiques et restaurants.
          //
          // Aucune n'était atteignable : `/products` figurait ici mais dans
          // aucun des cinq onglets, et son unique `push` venait de
          // `StartupChecklist`, widget qu'aucun fichier n'importe.
          // `ProductCreateSheet` n'avait pas un seul appelant.
          //
          // CE QU'ON A PERDU, ET POURQUOI CE N'EST PAS UNE PERTE.
          // `ProductWizardSheet` portait trois étapes que `ProductWizardScreen`
          // n'a pas : créer l'offre, l'article de stock et le lieu d'expédition.
          // Ce sont exactement les trois qui lèvent `NotMigrated` aujourd'hui
          // (`offers`, `sellerInventoryWrite`). Elles reviendront avec leur
          // amont, dans l'assistant qui reste — pas dans deux assistants
          // concurrents qu'il faudrait maintenir en parallèle.
          //
          // `FinanceScreen` et `AnalyticsScreen`, EUX, sont conservés hors
          // routeur : ce ne sont pas des doublons mais des écrans qui attendent
          // leur amont (`sellerStatement`, `analytics`). Les supprimer
          // obligerait à les réécrire.
          //
          // CETTE AFFIRMATION ÉTAIT FAUSSE POUR `AnalyticsScreen` JUSQU'À #221 :
          // sa route était bien déclarée, plus bas dans ce même fichier, sans
          // qu'aucun écran n'y mène. Elle a été retirée — voir l'encadré à
          // l'endroit où elle se trouvait.
          // ═══════════════════════════════════════════════════════════════════
          GoRoute(path: '/orders', builder: (_, __) => const OrdersTabScreen()),
          GoRoute(path: '/messages', builder: (_, __) => const ConversationsScreen()),
          // UNE ROUTE, TROIS ÉCRANS — cf. `ActivitiesTabScreen`.
          // Liste des activités en vue consolidée, catalogue pour une boutique,
          // carte pour un restaurant.
          GoRoute(path: '/activities', builder: (_, __) => const ActivitiesTabScreen()),

          // `/finance` REJOINT LA COQUILLE : C'EST DEVENU UN ONGLET.
          //
          // Il était déclaré hors `ShellRoute`, donc affiché SANS barre du bas —
          // ce qui convenait quand on y arrivait depuis « Plus ». Devenu l'un des
          // cinq onglets, il doit conserver la barre, sans quoi le partenaire s'y
          // retrouve sans aucun moyen de revenir.
    

            // `/finance` EST UN ONGLET : IL DOIT VIVRE DANS LA COQUILLE.
            //
            // Il était déclaré plus bas, hors `ShellRoute`. L'onglet Finances
            // ouvrait donc un écran SANS barre du bas — et sans retour, puisque
            // la barre navigue par `go`, qui remplace la pile au lieu de l'empiler.
            // Le partenaire s'y retrouvait enfermé.
            // MÊME AIGUILLAGE QUE L'ACCUEIL : consolidé, ou par activité.
            //
            // `FinanceScreen` (l'ancien écran branché sur l'API) n'est plus
            // routé. Il est CONSERVÉ car il porte le câblage des relevés réels
            // dont la version définitive aura besoin — le portefeuille, lui, a
            // un amont (`/api/wallet`), à la différence du tableau de bord.
            // Son import a donc été retiré d'ici : garder un import mort ferait
            // croire, à la lecture des routes, que l'écran est encore atteignable.
            GoRoute(path: '/finance', builder: (_, __) => const PartnerFinanceScreen()),
          GoRoute(path: '/account', builder: (_, __) => const AccountScreen()),
        ],
      ),

      // Écrans empilés (hors coquille) : ils ont leur propre bouton retour.
      //
      // `/order/:ref` PORTE MAINTENANT UN IDENTIFIANT, PAS UNE RÉFÉRENCE.
      //
      // Le nom du paramètre est conservé pour ne pas casser d'éventuels liens,
      // mais les listes y poussent `order.id` : `CMD-XXXXXXXX` est une
      // abréviation calculée pour l'écran, pas une clé. L'écran résout la
      // commande dans `ordersProvider` — il n'existe aucune route de détail
      // vendeur (`GET /api/orders/{id}` est scopée par l'ACHETEUR).
      //
      // L'ancien `OrderDetailScreen` a été supprimé : il dépendait de
      // `shipments_data.dart` et `disputes_data.dart`, deux modules jamais
      // extraits du monolithe.
      GoRoute(
        path: '/order/:ref',
        builder: (_, s) => PartnerOrderDetailScreen(reference: s.pathParameters['ref']!),
      ),
      // ═══════════════════════════════════════════════════════════════════════
      // `/product/new` DOIT PRÉCÉDER `/product/:id`. NE PAS RÉORDONNER.
      //
      // go_router retient la PREMIÈRE route qui correspond, dans l'ordre de
      // déclaration — et le segment littéral « new » satisfait le paramètre
      // `:id`. Quand `/product/:id` venait en premier, `context.push('/product/new')`
      // ouvrait la FICHE PRODUIT avec `productId = "new"` : l'écran appelait
      // `GET /api/catalog/products/new`, la contrainte `{id:guid}` de
      // catalog-service ne matchait pas, et le vendeur recevait « Ressource
      // introuvable » sous un titre « Fiche produit » qu'il n'avait pas demandé.
      //
      // Deux conséquences pour le prix d'une : `ProductWizardScreen` était du
      // CODE MORT par cette route, et l'anomalie n'apparaissait que sur un seul
      // chemin de navigation — le bouton flottant de « Mes produits ». L'onglet
      // Produits, lui, ouvrait l'assistant en feuille sans passer par le
      // routeur, donc il fonctionnait. (Cet écran-là a depuis été supprimé —
      // voir le bloc `/products` plus bas.)
      //
      // Règle générale : tout segment littéral se déclare AVANT le paramètre qui
      // l'absorberait.
      // ═══════════════════════════════════════════════════════════════════════
      //
      // HORS COQUILLE : l'assistant n'affiche pas la barre d'onglets.
      // Un tap sur « Finances » en pleine saisie perdrait le brouillon.
      GoRoute(path: '/product/new', builder: (_, __) => const ProductWizardScreen()),
      GoRoute(
        path: '/product/:id',
        builder: (_, s) => ProductDetailScreen(productId: s.pathParameters['id']!),
      ),
      GoRoute(
        path: '/product/:id/preview',
        builder: (_, s) => ProductPreviewScreen(productId: s.pathParameters['id']!),
      ),
      // `/activity/new` — HORS DE LA COQUILLE, ET SANS `extra`.
      //
      // L'écran ne dépend d'AUCUNE activité sélectionnée : c'est précisément celui
      // qu'on ouvre quand on veut en créer une. Lui passer l'activité courante
      // l'aurait rendu inatteignable pour un compte qui n'en a pas encore.
      GoRoute(path: '/activity/new', builder: (_, __) => const ActivityWizardScreen()),

      GoRoute(path: '/dish/new', builder: (_, __) => const DishWizardScreen()),

      // `/dish/detail` — ET NON `/dish/:id`, POUR LA MÊME RAISON QUE `/menus`.
      //
      // Un chemin littéral et deux identifiants dans `extra` : la fiche a besoin du
      // restaurant ET du plat, et les mettre dans l'URL inviterait à en essayer
      // d'autres. La garde d'appartenance côté serveur refuserait — mais go_router
      // n'a aucune raison de proposer la tentative.
      //
      // `/dish/new` DOIT RESTER DÉCLARÉ AVANT : go_router prend la PREMIÈRE
      // correspondance, et une route `/dish/:id` placée en tête avalerait « new »
      // comme identifiant de plat.
      GoRoute(
        path: '/dish/detail',
        builder: (_, s) {
          final args = s.extra! as ({String restaurantId, String dishId});
          return DishDetailScreen(restaurantId: args.restaurantId, dishId: args.dishId);
        },
      ),

      // ═══════════════════════════════════════════════════════════════════════
      // HORS COQUILLE, ET C'EST LE POINT DE L'ÉCRAN.
      //
      // La cuisine se consulte les mains occupées, à un mètre, pendant un
      // service. La barre d'onglets n'y a rien à faire : un appui parasite sur
      // « Finances » en plein coup de feu fait perdre le fil des tickets.
      //
      // `extra` PORTE LE `restaurantId`, ET NON UN SEGMENT D'URL.
      //
      // Cet identifiant vient de l'activité courante, résolue depuis le jeton.
      // Le mettre dans le chemin (`/kitchen/:id`) inviterait à le fabriquer ou
      // à le recopier — c'est exactement ce que `seller_identity.dart` interdit
      // pour le `sellerId`, et pour la même raison : le service compare
      // l'appartenance et répond 404 sinon.
      // ═══════════════════════════════════════════════════════════════════════
      // `/menus` — CARTES ET SECTIONS (tâche #214). Même forme que `/kitchen` :
      // le `restaurantId` passe par `extra`, jamais par le chemin.
      //
      // Le mettre dans l'URL en ferait un identifiant à deviner sur une route que
      // la garde d'appartenance protège certes côté serveur, mais qui inviterait à
      // essayer. `extra` vient de l'activité sélectionnée, donc d'une donnée que le
      // vendeur possède déjà.
      GoRoute(
        path: '/menus',
        builder: (_, s) => MenusScreen(restaurantId: s.extra! as String),
      ),
      GoRoute(
        path: '/kitchen',
        builder: (_, s) => KitchenScreen(restaurantId: s.extra! as String),
      ),
      // `/statement` — L'ÉCRAN QUE #228b VIENT DE BRANCHER.
      //
      // `FinanceScreen` était « conservé hors routeur » en attendant son amont.
      // L'amont existe (`/api/financial/settlements/sellers/{id}/statement`), donc
      // la route revient — ET l'entrée qui y mène est ajoutée dans
      // `partner_finance_screen.dart`. Brancher sans offrir d'entrée aurait refait
      // exactement ce qu'on vient de reprocher à `/analytics`.
      //
      // IL NE REMPLACE PAS `/finance`. L'onglet Finances montre les SOLDES
      // (portefeuille) ; celui-ci montre la DÉCOMPOSITION sur une période — brut,
      // commission, frais, net, ligne par ligne. Deux questions différentes.
      GoRoute(path: '/statement', builder: (_, __) => const FinanceScreen()),
      GoRoute(path: '/offers', builder: (_, __) => const OffersScreen()),
      GoRoute(path: '/shipments', builder: (_, __) => const ShipmentsScreen()),
      GoRoute(path: '/locations', builder: (_, __) => const ShippingLocationsScreen()),
      GoRoute(path: '/help', builder: (_, __) => const HelpScreen()),
      GoRoute(path: '/notification-preferences', builder: (_, __) => const NotificationPreferencesScreen()),
      GoRoute(path: '/settings', builder: (_, __) => const SettingsScreen()),
      // `/analytics` A ÉTÉ RETIRÉE, ET LE COMMENTAIRE PLUS HAUT DISAIT DÉJÀ
      //    QU'ELLE NE DEVAIT PAS ÊTRE LÀ (tâche #221).
      //
      // Il affirmait `AnalyticsScreen` « conservé hors routeur » — et la route
      // était déclarée trente lignes plus bas. Le code et son commentaire se
      // contredisaient, ce qui est pire que l'un ou l'autre : on finit par ne
      // plus croire aucun des deux.
      //
      // POURQUOI ELLE, ET PAS `/shipments` NI `/returns`.
      //
      // Ces deux-là mènent aussi à un état « pas encore disponible », mais elles
      // sont ATTEINGNABLES depuis l'écran Compte, et leur module existe : il vit
      // dans le monolithe et sera extrait. Promettre « bientôt » y est exact.
      //
      // `/analytics` était la SEULE route de ce fichier sans aucun appelant — rien
      // n'y menait. Et l'analytique n'est pas un module en attente d'extraction :
      // aucun service ne la porte, nulle part, dans aucun état d'avancement.
      // Déclarer une route que personne n'offre, vers une fonction qui n'existe
      // dans aucun projet, n'achète rien et fait croire à un chantier en cours.
      //
      // POUR LA RÉTABLIR : redéclarer la `GoRoute` ici ET ajouter l'entrée
      // correspondante dans `account_screen.dart`. L'écran, lui, est conservé.
      GoRoute(path: '/returns', builder: (_, __) => const ReturnsScreen()),
      // `/dispute/:id` EST AUSSI SANS APPELANT, ET ELLE RESTE — LA RAISON
      //    DIFFÈRE DE CELLE D'`/analytics`.
      //
      // On n'arrive pas sur un litige par un menu : on y arrive DEPUIS un retour
      // ou une commande, qui en fournit l'identifiant. Le module `returns` n'étant
      // pas extrait, ce point d'entrée n'existe pas encore — l'absence d'appelant
      // est donc temporaire et attendue, non le signe d'une route oubliée.
      //
      // La retirer obligerait à la redéclarer avec l'extraction de `returns`, dans
      // le même geste. `/analytics`, elle, n'attendait aucun point d'entrée : rien
      // n'était en chantier pour en créer un.
      GoRoute(
        path: '/dispute/:id',
        builder: (_, s) => DisputeScreen(disputeId: s.pathParameters['id']!),
      ),
      GoRoute(path: '/wallet', builder: (_, __) => const WalletScreen()),
      GoRoute(path: '/reviews', builder: (_, __) => const ReviewsScreen()),
      GoRoute(path: '/shop', builder: (_, __) => const ShopScreen()),
      GoRoute(path: '/profile', builder: (_, __) => const ProfileScreen()),
      GoRoute(path: '/notifications', builder: (_, __) => const NotificationsScreen()),
      GoRoute(path: '/privacy', builder: (_, __) => const PrivacyScreen()),
      GoRoute(path: '/terms', builder: (_, __) => const TermsScreen()),
      GoRoute(
        path: '/chat/:id',
        // PLUS DE NOM EN « EXTRA » : LA MESSAGERIE N'EN REND AUCUN.
        //
        // La liste poussait ici `conversation.customer`, un champ absent de
        // `ConversationSummary` : l'extra valait donc toujours « Client ».
        // Le titre du fil est désormais assumé comme générique — cf.
        // `conversations_screen.dart` et `Conversation.participantIds`.
        builder: (_, s) => ChatScreen(conversationId: s.pathParameters['id']!),
      ),
    ],
  );
});

/// Route introuvable. Un cul-de-sac de navigation ne doit jamais se traduire par
/// un écran noir : le vendeur doit toujours pouvoir rentrer chez lui.
class _RouteError extends StatelessWidget {
  const _RouteError({this.message});
  final String? message;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Scaffold(
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.explore_off_outlined, size: 48, color: Color(0xFF7A8580)),
              const SizedBox(height: 16),
              Text(
                l.routeNotFoundTitle,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: Color(0xFF18211C)),
              ),
              if (kDebugMode && message != null) ...[
                const SizedBox(height: 10),
                Text(
                  message!,
                  textAlign: TextAlign.center,
                  maxLines: 4,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontSize: 12, color: Color(0xFFA0A8A4)),
                ),
              ],
              const SizedBox(height: 20),
              FilledButton(
                onPressed: () => context.go('/home'),
                child: Text(l.routeBackHome),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
