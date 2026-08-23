/// Configuration globale de l'application.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// UN BUILD DE RELEASE SANS `API_BASE_URL` REFUSE DE DÉMARRER.
///
/// Avant, l'URL par défaut était le STAGING. Un build oubliant `--dart-define`
/// partait donc sur le staging — silencieusement. Et ce n'était pas théorique : le
/// pipeline CI construisait l'IPA envoyée sur TestFlight avec l'URL de staging.
///
/// Une application publiée sur les stores qui parle au mauvais serveur, c'est :
///   • des commandes réelles enregistrées dans une base de test ;
///   • des paiements qui n'aboutissent nulle part ;
///   • et une correction qui exige de REPUBLIER — plusieurs jours de revue Apple.
///
/// Désormais, `main()` vérifie au démarrage : en mode release, si l'URL n'a pas été
/// fournie explicitement au build, l'application affiche un écran d'erreur et
/// s'arrête. Bruyant, immédiat, impossible à manquer.
///
/// En développement (debug/profile), le staging reste la valeur par défaut : personne
/// ne veut taper une variable d'environnement pour lancer l'app sur son émulateur.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// Exemples :
///   flutter run                                                   (→ staging, dev)
///   flutter build appbundle --dart-define=API_BASE_URL=https://m.votre-domaine.com
///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:8080   (stack locale, émulateur Android)
class AppConfig {
  const AppConfig._();

  /// L'URL a-t-elle été fournie EXPLICITEMENT au build ?
  ///
  /// C'est la question qui compte : `baseUrl` renvoie toujours quelque chose (le repli
  /// de développement), donc la lire ne dit pas si quelqu'un l'a vraiment choisie.
  /// `bool.hasEnvironment` répond à cette question-là, et il est évaluable au build.
  static const bool isExplicitlyConfigured = bool.hasEnvironment('API_BASE_URL');

  /// Autorise EXPLICITEMENT un build de release à viser un serveur de test.
  ///
  ///   flutter build … --dart-define=API_BASE_URL=https://…staging… \
  ///                   --dart-define=ALLOW_STAGING_RELEASE=true
  static const bool _allowStagingRelease =
      bool.fromEnvironment('ALLOW_STAGING_RELEASE', defaultValue: false);

  /// L'URL vise-t-elle un serveur de TEST ou une machine LOCALE ?
  static bool get isTestOrLocalTarget {
    final u = baseUrl.toLowerCase();
    return u.contains('staging') ||
        u.contains('localhost') ||
        u.contains('127.0.0.1') ||
        u.contains('10.0.2.2') || // loopback hôte depuis l'émulateur Android
        u.contains('10.0.3.2'); // idem Genymotion
  }

  /// ───────────────────────────────────────────────────────────────────────────
  /// UN BUILD DE RELEASE NE DOIT PAS VISER UN SERVEUR DE TEST — SAUF OPT-IN.
  ///
  /// Ce contrôle existait dans l'application VENDEUR, pas ici. L'asymétrie était
  /// à l'envers : les scripts de livraison construisent le binaire « staging »
  /// avec l'identifiant applicatif de PRODUCTION (une seule fiche par store), et
  /// `_common.sh` le dit lui-même — « rien ne le distingue à l'œil ». Deux IPA
  /// posées côte à côte sur TestFlight sont indiscernables ; il suffit de
  /// soumettre la mauvaise.
  ///
  /// Ici, la conséquence est plus lourde que côté vendeur : ce sont de vrais
  /// acheteurs, de vraies commandes et de vrais paiements Mobile Money qui
  /// atterriraient dans une base de test.
  ///
  /// Fournir une URL ne prouve donc pas qu'on a fourni la BONNE. On vérifie
  /// laquelle.
  /// ───────────────────────────────────────────────────────────────────────────
  static bool get isForbiddenReleaseTarget =>
      isTestOrLocalTarget && !_allowStagingRelease;

  /// Repli de DÉVELOPPEMENT uniquement. Jamais utilisé en release : le garde-fou de
  /// `main()` fait échouer l'application avant.
  static const String _developmentFallback = 'https://m.marketplace-staging.hba-marketplace.fr';

  /// URL de base du BFF Mobile (hôte « m. » ; les routes sont sous /mobile).
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: _developmentFallback,
  );

  /// ═════════════════════════════════════════════════════════════════════════
  /// `apiPrefix` A DISPARU, ET CE N'EST PAS UN RENOMMAGE.
  ///
  /// Cette application visait `/mobile/*` — le BFF du MONOLITHE. Un préfixe
  /// unique suffisait parce qu'un seul service répondait à tout.
  ///
  /// La passerelle HBA n'a pas cette forme : chaque domaine a son chemin, et
  /// certains passent par une agrégation BFF quand d'autres sont relayés tels
  /// quels vers leur service. Un `apiPrefix` unique ne peut plus rien décrire.
  ///
  /// Le BFF `/mobile` du monolithe reste en service pour l'ANCIENNE version de
  /// l'application. Cette version-ci ne doit plus jamais l'appeler.
  /// ═════════════════════════════════════════════════════════════════════════

  /// Authentification : inscription, connexion, jetons, mot de passe oublié.
  static const String auth = '/api/auth';

  /// Compte et administration Identity (`/account/me/*`, rôles).
  static const String identity = '/api/identity';

  /// Profil et adresses, portés par user-service.
  static const String users = '/api/users';

  static const String cart = '/api/cart';
  static const String wishlist = '/api/wishlist';
  static const String orders = '/api/orders';
  static const String payments = '/api/payments';
  static const String reviews = '/api/reviews';
  static const String catalog = '/api/catalog';
  static const String food = '/api/food';
  static const String media = '/api/media';

  /// Boîte de réception, préférences, appareils, et la messagerie.
  static const String notifications = '/api/notifications';

  /// ── Agrégations BFF ───────────────────────────────────────────────────
  ///
  /// HBAExpress ET HBA Food SONT DEUX EXPÉRIENCES DISTINCTES.
  ///
  /// Elles ne se mélangent jamais dans un même flux. Deux racines, jamais une
  /// seule « accueil » qui trierait ensuite.
  static const String bffExpress = '/api/v1/bff/client/express';
  static const String bffFood = '/api/v1/bff/client/food';

  /// Délais réseau.
  static const Duration connectTimeout = Duration(seconds: 15);
  static const Duration receiveTimeout = Duration(seconds: 20);

  /// Devise par défaut (affichage) si l'API n'en fournit pas.
  static const String defaultCurrency = 'XOF';
}
