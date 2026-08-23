/// Configuration globale de l'application vendeur.
///
/// `baseUrl` pointe sur la PASSERELLE HBA — plus sur un BFF par audience.
///
/// L'HÔTE « seller. » EST UN RELIQUAT DU MONOLITHE, ET LE DÉFAUT LE PORTE
/// ENCORE. Le monolithe servait une façade par public (`m.`, `seller.`) ; la
/// passerelle, elle, est publiée sur UN seul domaine (`API_DOMAIN` dans
/// `compose.gateway.yml`) et distingue les publics par le chemin et le rôle du
/// jeton. Le repli ci-dessous n'a donc plus d'amont : tout build destiné à être
/// utilisé DOIT fournir l'URL de la passerelle.
///
/// Surchargeable au build :
///   flutter run --dart-define=API_BASE_URL=https://<API_DOMAIN de la passerelle>
///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:8080   (émulateur Android + stack locale)
/// ─────────────────────────────────────────────────────────────────────────────
///  UN BUILD DE RELEASE SANS `API_BASE_URL` REFUSE DE DÉMARRER (voir main.dart).
///
/// L'URL par défaut est le STAGING. Un build de release qui oublie `--dart-define`
/// partirait donc sur le serveur de test — en silence. Pour une app VENDEUR, cela
/// signifie des commandes réelles invisibles, des expéditions déclarées dans le vide,
/// et un vendeur persuadé que la plateforme est en panne.
///
/// La correction imposerait de republier : plusieurs jours de revue. On échoue donc au
/// premier lancement, sur l'appareil du testeur, quand cela ne coûte encore rien.
/// ─────────────────────────────────────────────────────────────────────────────
class AppConfig {
  const AppConfig._();

  /// L'URL a-t-elle été fournie EXPLICITEMENT au build ?
  ///
  /// `baseUrl` renvoie toujours une valeur (le repli de développement) : la lire ne dit
  /// donc pas si quelqu'un l'a réellement choisie. C'est cette constante qui le dit.
  static const bool isExplicitlyConfigured = bool.hasEnvironment('API_BASE_URL');

  /// Opt-in EXPLICITE pour distribuer un build release qui parle au STAGING.
  ///
  /// Par défaut `false` : un build release visant un serveur de test est refusé
  /// (voir [isForbiddenReleaseTarget]). Pour un build de test staging assumé :
  ///   flutter build … --dart-define=API_BASE_URL=https://…staging… --dart-define=ALLOW_STAGING_RELEASE=true
  static const bool _allowStagingRelease =
      bool.fromEnvironment('ALLOW_STAGING_RELEASE', defaultValue: false);

  /// L'URL de base vise-t-elle un serveur de TEST (staging) ou une machine LOCALE ?
  ///
  /// Le fait qu'une URL ait été fournie explicitement (`isExplicitlyConfigured`) ne
  /// garantit PAS que c'est la bonne : un `--dart-define` avec l'URL staging passerait
  /// le premier garde-fou et servirait des données de test aux vrais vendeurs. On
  /// détecte donc les hôtes non-production par motif.
  static bool get isTestOrLocalTarget {
    final u = baseUrl.toLowerCase();
    return u.contains('staging') ||
        u.contains('localhost') ||
        u.contains('127.0.0.1') ||
        u.contains('10.0.2.2') || // loopback hôte depuis l'émulateur Android
        u.contains('10.0.3.2'); // idem Genymotion
  }

  /// Un build de RELEASE ne doit jamais viser un serveur de test, sauf opt-in explicite.
  /// Consommé par le garde-fou de démarrage (main.dart).
  static bool get isForbiddenReleaseTarget =>
      isTestOrLocalTarget && !_allowStagingRelease;

  /// Repli de DÉVELOPPEMENT uniquement — jamais atteint en release.
  static const String _developmentFallback = 'https://seller.marketplace-staging.hba-marketplace.fr';

  /// URL de base de la passerelle HBA (racine des `/api/*` ci-dessous).
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: _developmentFallback,
  );

  // ═════════════════════════════════════════════════════════════════════════
  // LE MODE DONNÉES SIMULÉES A DISPARU AVEC `core/mock/`.
  //
  // Il reposait sur `USE_MOCK_DATA`, une constante de compilation qui activait
  // trois choses : un contournement d'authentification (`MockAuth`), un
  // intercepteur Dio qui fabriquait des réponses (`MockInterceptor`), et le jeu
  // de données figées des maquettes (`PartnerMockData`). Les trois sont
  // supprimés : chaque écran parle désormais à la passerelle, ou annonce que son
  // amont n'existe pas encore (voir `core/network/not_migrated.dart`).
  //
  // NE PAS LE RÉINTRODUIRE POUR « DÉMONTRER HORS LIGNE ». C'est précisément ce
  // qui rendait indiscernable un chiffre réel d'un chiffre fabriqué, une fois
  // les écrans voisins branchés.
  // ═════════════════════════════════════════════════════════════════════════

  /// ═════════════════════════════════════════════════════════════════════════
  /// `apiPrefix` A DISPARU, ET CE N'EST PAS UN RENOMMAGE.
  ///
  /// Toutes les requêtes de cette application partaient sous `/seller` — le BFF
  /// vendeur du MONOLITHE, disparu avec lui. La passerelle HBA n'expose RIEN
  /// sous ce préfixe : chaque appel repartait en 404, et AUCUNE ligne de code ne
  /// le disait. Côté vendeur, cela se voyait comme des écrans vides et des
  /// « erreur serveur » intermittents ; rien ne pouvait faire deviner que
  /// l'amont visé n'existait plus du tout.
  ///
  /// La passerelle n'a pas la forme d'un BFF unique : chaque domaine a son
  /// chemin PUBLIC, relayé tel quel ou réécrit vers le service qui le sert, et
  /// deux agrégations BFF s'ajoutent pour les écrans composites. Un préfixe
  /// unique ne peut plus rien décrire — d'où des constantes par domaine.
  ///
  /// LE CHEMIN PUBLIC N'EST PAS CELUI DU SERVICE.
  ///
  /// `/api/wallet` devient `/api/financial/wallets`, `/api/reviews` devient
  /// `/api/engagement/reviews`, `/api/delivery` devient `/api/deliveries`. Les
  /// constantes ci-dessous sont les chemins d'ENTRÉE, relevés un par un dans
  /// `ReverseProxy:Routes` de `HBA.Gateway.Api/appsettings.json`. Les déduire du
  /// nom du service produirait des 404 aussi muets que les précédents.
  /// ═════════════════════════════════════════════════════════════════════════

  /// Inscription, connexion, jetons, mot de passe oublié (route « auth »,
  /// réécrite vers `/api/identity/auth/*`). Anonyme, et limitée à 10 essais/min.
  static const String auth = '/api/auth';

  /// Compte et administration Identity : `/account/me/*` (dont la déconnexion),
  /// rôles. Route « identity-admin », SANS réécriture — le service porte déjà ce
  /// préfixe.
  static const String identity = '/api/identity';

  /// Profil et adresses, portés par user-service (route « users »).
  static const String users = '/api/users';

  /// LE CŒUR DE L'APPLICATION VENDEUR, ET LE SEUL ENDROIT OÙ LIRE SON
  /// `sellerId` : `GET /api/merchants/me`. Boutiques, KYB, compte de retrait et
  /// informations société vivent aussi ici (routes « merchants-read/write »).
  ///
  /// La passerelle ne réécrit PAS `/api/merchants` en `/api/sellers` : elle l'a
  /// fait, c'était un reliquat du monolithe, et cela envoyait toute la façade
  /// vendeur sur un chemin inexistant.
  static const String merchants = '/api/merchants';

  /// Produits, catégories, marques (route « catalog-read » en lecture anonyme,
  /// « catalog-write » authentifiée).
  static const String catalog = '/api/catalog';

  /// Stock et mouvements (route « inventory »).
  static const String inventory = '/api/inventory';

  /// Référentiel administratif du Bénin — 12 départements, 77 communes.
  ///
  /// ANONYME, ET C'EST NÉCESSAIRE : le sélecteur de commune s'affiche à
  /// l'inscription, avant qu'aucun jeton n'existe. Route YARP « geo »
  /// (`Order: 5`, `AuthorizationPolicy: anonymous`), servie par user-service.
  ///
  /// CE N'EST PAS UNE TABLE, C'EST UNE CONSTANTE DU PROGRAMME. Le service la
  /// rend depuis `BeninGeography` — la MÊME classe qui valide le code envoyé.
  /// C'est ce qui garantit qu'une commune proposée par l'application ne sera
  /// jamais refusée à l'enregistrement.
  static const String geo = '/api/geo';

  /// CETTE ROUTE REND LES COMMANDES OÙ L'UTILISATEUR EST ACHETEUR.
  ///
  /// `GET /api/orders` est scopée par le `buyerId` du jeton : pour un vendeur,
  /// elle rend SES PROPRES ACHATS, pas les commandes reçues dans sa boutique.
  ///
  /// Les commandes du vendeur se lisent par `GET /api/sellers/{sellerId}/orders`
  /// — qui exige désormais de PROUVER qu'on est ce vendeur — et cette route
  /// n'est routée par AUCUNE entrée de la passerelle à ce jour : seul le BFF
  /// merchant l'atteint, en interne, pour le tableau de bord d'une boutique.
  /// Tant qu'elle n'est pas exposée, l'écran « Commandes » n'a pas d'amont.
  static const String orders = '/api/orders';

  /// HBA Food : restaurants, menus, articles, écran de cuisine (routes
  /// « food-read » / « food-write »).
  static const String food = '/api/food';

  /// Courses et suivi de livraison (route « delivery », réécrite au PLURIEL
  /// vers `/api/deliveries/*`).
  static const String delivery = '/api/delivery';

  /// Portefeuille, solde, mouvements, retraits (route « wallet », réécrite vers
  /// `/api/financial/wallets/*`).
  static const String wallet = '/api/wallet';

  /// Relevé de compte vendeur : `statement`, `statement/lines`, `payouts`.
  ///
  /// PAS DE RÉÉCRITURE SUR CE PRÉFIXE, contrairement à `wallet`. L'entrée YARP
  /// `wallet` traduit `/api/wallet` en `/api/financial/wallets` ; celle-ci
  /// transmet le chemin tel quel, parce que financial-service préfixe déjà son
  /// groupe par `/api/financial/settlements`. Écrire `/api/settlements` ici
  /// rendrait 404, sans aucune erreur de configuration pour l'expliquer.
  ///
  /// GET SEULEMENT côté passerelle. Lancer un règlement ou marquer un versement
  /// payé sont des gestes d'ADMINISTRATION — ils ne passent pas par cette route.
  static const String settlements = '/api/financial/settlements';

  /// Paiements (route « payments », réécrite vers `/api/financial/payments/*`).
  static const String payments = '/api/payments';

  /// Avis clients (route « reviews-read/write », réécrite vers
  /// `/api/engagement/reviews/*`).
  static const String reviews = '/api/reviews';

  /// Boîte de réception, préférences, appareils, et la messagerie sous
  /// `/api/notifications/messaging` (route « notifications »).
  static const String notifications = '/api/notifications';

  /// Dépôt de fichiers : photos produit, pièces KYB (routes « media-read » et
  /// « media-write »).
  static const String media = '/api/media';

  /// ── Agrégations BFF ───────────────────────────────────────────────────
  ///
  /// DEUX FAÇADES, PARCE QUE CE SONT DEUX RÔLES DISTINCTS.
  ///
  /// `bffMerchant` exige le rôle `Seller`, `bffRestaurant` le rôle
  /// `FoodPartner` (politiques `MerchantOnly` / `RestaurantOnly`). Un compte qui
  /// n'a que l'un des deux reçoit 403 sur l'autre — ce n'est pas une panne, et
  /// l'application ne doit pas le présenter comme telle.
  ///
  /// Le sélecteur d'activité passe par `bffMerchant/activities`, qui rend
  /// boutiques ET restaurants : c'est le point d'entrée unique après connexion.
  static const String bffMerchant = '/api/v1/bff/merchant';
  static const String bffRestaurant = '/api/v1/bff/restaurant';

  /// ═════════════════════════════════════════════════════════════════════════
  /// `chatHubPath` A DISPARU : LE HUB N'EXISTE PLUS CÔTÉ SERVEUR.
  ///
  /// La constante valait `/seller/hubs/chat`. Aucun service HBA n'appelle
  /// `MapHub` — la seule trace du sujet est `IMessagingModuleApi`, qui prépare
  /// l'autorisation d'un futur `ChatHub` — et la passerelle n'a aucune route,
  /// donc aucun passage WebSocket, vers un hub.
  ///
  /// Garder une adresse qui compile aurait coûté une négociation SignalR sortante
  /// à chaque ouverture de conversation, pour un 404 avalé par le `catch` du
  /// service temps réel : le fil serait resté muet jusqu'au prochain
  /// rafraîchissement, sans que rien n'explique pourquoi.
  ///
  /// La neutralisation est dans `features/messaging/chat_realtime.dart`.
  /// ═════════════════════════════════════════════════════════════════════════

  static const Duration connectTimeout = Duration(seconds: 15);
  static const Duration receiveTimeout = Duration(seconds: 20);

  /// Devise par défaut si l'API n'en fournit pas.
  static const String defaultCurrency = 'XOF';

  /// Tarification : le vendeur saisit son prix NET ; la plateforme ajoute la
  /// commission puis les frais provider pour obtenir le prix payé par l'acheteur.
  /// Ces taux doivent rester alignés sur la section « Pricing » du backend.
  static const double commissionRate = 0.10;
  static const double providerFeeRate = 0.05;
  static const double priceMultiplier = 1 + commissionRate + providerFeeRate;
}
