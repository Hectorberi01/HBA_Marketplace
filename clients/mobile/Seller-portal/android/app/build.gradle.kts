// IMPORT OBLIGATOIRE. Dans les blocs de la DSL Android (`signingConfigs { … }`),
// l'identifiant `java` désigne l'EXTENSION Gradle `java` apportée par AGP, et non le
// package `java.*`. Écrire `java.util.Properties()` y échoue donc sur
// « Unresolved reference: util ». On importe le type pour ne plus dépendre du package.
import java.util.Properties
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("kotlin-android")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")

    // Lit google-services.json au build et en injecte les clés dans l'app. SANS ce
    // plugin, le fichier est ignoré EN SILENCE : Firebase s'initialise sans projet,
    // aucun jeton n'est délivré, et aucune erreur n'apparaît. Le push ne marche
    // simplement pas, sans qu'on sache pourquoi.
    id("com.google.gms.google-services")
    // Crashlytics : upload des mappings au build pour des traces symbolisées.
    id("com.google.firebase.crashlytics")
}

android {
    // Identifiant DÉFINITIF, hérité de la première publication. Il ne suit pas la
    // convention `com.hbaexpress.*` de l'app cliente, et c'est trop tard pour le
    // regretter : un applicationId ne change plus une fois l'application publiée, et
    // celui-ci est en outre lié au google-services.json du projet Firebase.
    namespace = "fr.hbamarketplace.seller"

    // ÉPINGLÉ À 36, PAS `flutter.compileSdkVersion`.
    //
    // Depuis le 31 août 2026, Google Play refuse toute nouvelle application et
    // toute mise à jour qui ne cible pas Android 16 (API 36). Déléguer ce nombre
    // au SDK Flutter revient à faire dépendre la recevabilité d'une publication
    // de la version de Flutter installée sur la machine qui compile — un envoi
    // refusé la veille d'une échéance, sans qu'aucune ligne de ce dépôt n'ait
    // changé. Le nombre est donc écrit ici, et se relève à la main.
    //
    // Chaîne d'outils vérifiée : AGP 8.11.1 + Gradle 8.14 acceptent l'API 36.
    compileSdk = 36
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        applicationId = "fr.hbamarketplace.seller"
        minSdk = flutter.minSdkVersion // exigé par Firebase (Auth/Messaging)

        // Épinglé pour la même raison que `compileSdk`. Cibler l'API 36 active les
        // changements de comportement d'Android 16, dont l'affichage bord à bord
        // IMPOSÉ (la barre de navigation ne réserve plus son espace). Ce point est
        // déjà traité : `SystemUiMode.edgeToEdge` et les marges de sécurité ont été
        // posés. À revérifier tout de même sur un appareil Android 16 réel.
        targetSdk = 36
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FLAVORS : staging vs prod = DEUX applications distinctes sur le Play Store.
    //
    // Le suffixe .staging donne un applicationId différent → deux fiches Play, deux
    // apps installables CÔTE À CÔTE sur un même téléphone (le testeur garde la prod).
    // Chaque flavor porte son propre nom d'affichage (app_name) pour qu'on les
    // distingue sur l'écran d'accueil.
    //
    // FIREBASE : l'applicationId de staging (fr.hbamarketplace.seller.staging) doit
    // exister dans google-services.json, sinon le build échoue avec « No matching
    // client found for package name ». Deux options :
    //   1) ajouter l'app staging au MÊME projet Firebase, re-télécharger
    //      google-services.json (il contiendra alors les DEUX package_name) et
    //      remplacer android/app/google-services.json ;
    //   2) déposer un fichier par flavor : android/app/src/staging/google-services.json.
    //
    // Depuis l'ajout des flavors, un flavor est OBLIGATOIRE :
    //   flutter run --flavor staging --dart-define=API_BASE_URL=…
    // ─────────────────────────────────────────────────────────────────────────────
    flavorDimensions += "env"

    productFlavors {
        create("staging") {
            dimension = "env"
            applicationIdSuffix = ".staging"
            versionNameSuffix = "-staging"
            resValue("string", "app_name", "HBA Pro Staging")
        }

        create("prod") {
            dimension = "env"
            resValue("string", "app_name", "HbaExpress Pro")
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SIGNATURE DE PRODUCTION.
    //
    // AVANT : signature avec le keystore de DÉBOGAGE — clé publique, mot de passe
    // « android ». Le Play Store refuse un tel AAB, et cette signature ne prouve rien.
    //
    // LE KEYSTORE N'ENTRE JAMAIS DANS LE DÉPÔT. Il est lu depuis
    // `android/key.properties`, ignoré par Git.
    //
    // ET IL NE SE PERD PAS. Google n'accepte de mise à jour que signée par la MÊME
    // clé. Un keystore perdu = une nouvelle application à publier, en abandonnant les
    // utilisateurs, les avis et l'historique de l'ancienne. Sauvegardez-le hors de
    // votre machine.
    //
    // CE KEYSTORE DOIT ÊTRE DISTINCT DE CELUI DE L'APP CLIENTE. Ce sont deux
    // applications différentes sur le Play Store ; partager une clé lie leur destin
    // sans aucun bénéfice.
    //
    // Contenu attendu de android/key.properties :
    //     storeFile=/chemin/absolu/vers/hbaexpress-seller.jks
    //     storePassword=…
    //     keyAlias=hbaexpress
    //     keyPassword=…
    // ─────────────────────────────────────────────────────────────────────────────
    signingConfigs {
        create("release") {
            val props = Properties()
            val propsFile = rootProject.file("key.properties")

            if (propsFile.exists()) {
                propsFile.inputStream().use { props.load(it) }
                storeFile = file(props.getProperty("storeFile"))
                storePassword = props.getProperty("storePassword")
                keyAlias = props.getProperty("keyAlias")
                keyPassword = props.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            // Sans `key.properties` (poste de dev, CI sans secrets), on retombe sur la
            // clé de débogage pour que `flutter run --release` fonctionne encore — mais
            // on le DIT bruyamment, sans quoi le problème n'apparaîtrait qu'au moment
            // de téléverser sur le Play Store.
            val hasKeystore = rootProject.file("key.properties").exists()

            signingConfig = if (hasKeystore) {
                signingConfigs.getByName("release")
            } else {
                logger.warn(
                    " android/key.properties ABSENT : build signé avec la clé de DÉBOGAGE. " +
                        "Ce binaire sera REFUSÉ par le Play Store."
                )
                signingConfigs.getByName("debug")
            }

            // NOTE : la minification (R8) n'est PAS activée ici, délibérément — comme
            // sur l'app cliente. Elle obfusque les classes que Firebase résout par
            // réflexion, et se paie en plantages qui n'existent qu'en release. C'est un
            // chantier à part, avec ses règles ProGuard et sa campagne de tests.
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CIBLE JVM DU CODE KOTLIN.
//
// Remplace l'ancien `android { kotlinOptions { jvmTarget = "17" } }`, retiré depuis
// Kotlin 2.2 : la propriété y est dépréciée au niveau ERREUR, ce qui empêche la
// COMPILATION MÊME du script Gradle — le build échoue avant d'atteindre la moindre
// tâche, avec pour seul message un numéro de version. C'est exactement la panne qui a
// immobilisé l'app cliente ; ce fichier la portait encore (settings.gradle.kts déclare
// Kotlin 2.2.20).
//
// La cible doit rester alignée sur `compileOptions` (Java 17) ci-dessus : un écart
// entre les deux produit des erreurs d'édition de liens à l'exécution, pas au build.
// ─────────────────────────────────────────────────────────────────────────────
kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

flutter {
    source = "../.."
}
