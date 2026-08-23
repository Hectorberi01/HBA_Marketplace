import Flutter
import UIKit
import FirebaseCore
import FirebaseMessaging

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    // 1) Configure Firebase NATIVEMENT, le plus tôt possible — avant que le jeton
    //    APNs n'arrive. `Firebase.initializeApp()` côté Dart réutilisera cette
    //    instance par défaut. Le guard évite un double-configure (qui lèverait).
    if FirebaseApp.app() == nil {
      FirebaseApp.configure()
    }

    let result = super.application(application, didFinishLaunchingWithOptions: launchOptions)

    // 2) Déclenche EXPLICITEMENT l'enregistrement APNs.
    //
    //    Avec la nouvelle architecture iOS de Flutter (SceneDelegate +
    //    FlutterImplicitEngineDelegate), le swizzling de FirebaseMessaging ne
    //    s'accroche pas de façon fiable : `registerForRemoteNotifications` n'est
    //    jamais appelé, aucun jeton APNs n'arrive, FCM n'émet pas de jeton, et
    //    l'appareil n'est jamais enregistré (le symptôme observé).
    //
    //    On force donc l'enregistrement nous-mêmes. C'est sans danger : demander
    //    le jeton d'appareil est distinct de la PERMISSION d'afficher des
    //    notifications (gérée côté Dart via requestPermission). Idempotent.
    application.registerForRemoteNotifications()

    return result
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
  }

  // 3) JETON APNs → FIREBASE, transmis EXPLICITEMENT (le swizzling peut le rater
  //    avec l'archi Scene). Idempotent si le swizzling fonctionne aussi.
  override func application(
    _ application: UIApplication,
    didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data
  ) {
    Messaging.messaging().apnsToken = deviceToken
    NSLog("[Push] Jeton APNs reçu (\(deviceToken.count) octets) et transmis à Firebase.")
    super.application(application, didRegisterForRemoteNotificationsWithDeviceToken: deviceToken)
  }

  // 4) Si iOS échoue à enregistrer l'appareil auprès d'APNs, on TRACE la raison
  //    exacte (ex. « no valid aps-environment entitlement », réseau…) — au lieu
  //    d'un silence qui laisse deviner.
  override func application(
    _ application: UIApplication,
    didFailToRegisterForRemoteNotificationsWithError error: Error
  ) {
    NSLog("[Push] Échec d'enregistrement APNs : \(error.localizedDescription)")
    super.application(application, didFailToRegisterForRemoteNotificationsWithError: error)
  }
}
