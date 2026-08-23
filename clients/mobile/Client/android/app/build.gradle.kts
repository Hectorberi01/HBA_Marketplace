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
    namespace = "com.hbaexpress.client"

    // ÉPINGLÉ À 36, PAS `flutter.compileSdkVersion`.
    //
    // Depuis le 31 août 2026, Google Play refuse toute nouvelle application et
    // toute mise à jour qui ne cible pas Android 16 (API 36). Déléguer ce nombre
    // au SDK Flutter fait dépendre la recevabilité d'une publication de la
    // version de Flutter installée sur la machine qui compile — un envoi refusé
    // sans qu'aucune ligne de ce dépôt n'ait changé.
    //
    // Chaîne d'outils vérifiée : AGP 8.11.1 + Gradle 8.14 acceptent l'API 36.
    compileSdk = 36
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // Identifiant DÉFINITIF. Une fois l'app publiée, il ne peut plus jamais
        // changer : c'est lui qui identifie l'application sur le Play Store, et le
        // fichier google-services.json y est lié. « com.example.* » était par
        // ailleurs un motif de refus automatique par Google.
        applicationId = "com.hbaexpress.client"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion

        // Épinglé pour la même raison que `compileSdk`. Cibler l'API 36 active les
        // changements de comportement d'Android 16, dont l'affichage bord à bord
        // IMPOSÉ : la barre de navigation ne réserve plus son espace. C'est déjà
        // traité ici (`SystemUiMode.edgeToEdge` et marges de sécurité), mais à
        // revérifier sur un appareil Android 16 réel.
        targetSdk = 36
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FLAVORS : staging vs prod = DEUX applications distinctes sur le Play Store.
    //
    // Le suffixe .staging donne un applicationId différent → deux fiches Play, deux
    // apps installables CÔTE À CÔTE. Chaque flavor porte son propre nom d'affichage.
    //
    // FIREBASE : l'applicationId de staging (com.hbaexpress.client.staging) doit
    // exister dans google-services.json, sinon le build échoue (« No matching client
    // found for package name »). Ajouter l'app staging au projet Firebase et
    // re-télécharger google-services.json (il contiendra les deux package_name).
    //
    // Depuis les flavors, un flavor est OBLIGATOIRE :
    // flutter run --flavor staging --dart-define=API_BASE_URL=…
    // ─────────────────────────────────────────────────────────────────────────────
    flavorDimensions += "env"
    productFlavors {
        create("staging") {
            dimension = "env"
            applicationIdSuffix = ".staging"
            versionNameSuffix = "-staging"
            resValue("string", "app_name", "HbaExpress Staging")
        }
        create("prod") {
            dimension = "env"
            resValue("string", "app_name", "HbaExpress")
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SIGNATURE DE PRODUCTION.
    //
    // AVANT : `signingConfig = signingConfigs.getByName("debug")`. L'application était
    // signée avec le keystore de DÉBOGAGE — celui que le SDK Android génère seul, dont
    // la clé est publique et le mot de passe littéralement « android ».
    //
    // Deux conséquences :
    //   • le Play Store REFUSE un AAB signé en debug. Blocage pur et simple ;
    //   • cette signature ne prouve rien : n'importe qui peut produire un binaire
    //     signé de la même façon et le faire passer pour le vôtre.
    //
    // LE KEYSTORE NE DOIT JAMAIS ENTRER DANS LE DÉPÔT.
    //
    // Il est lu depuis `android/key.properties`, un fichier IGNORÉ par Git (voir
    // .gitignore). Le perdre, c'est perdre la capacité de PUBLIER UNE MISE À JOUR :
    // Google n'accepte que des versions signées par la même clé. Sauvegardez-le
    // ailleurs que sur votre machine — un keystore perdu oblige à publier une NOUVELLE
    // application, en abandonnant les utilisateurs et les avis de l'ancienne.
    //
    // Contenu attendu de android/key.properties :
    //     storeFile=/chemin/absolu/vers/hbaexpress-client.jks
    //     storePassword=…
    //     keyAlias=hbaexpress
    //     keyPassword=…
    //
    // Créer le keystore :
    //     keytool -genkey -v -keystore hbaexpress-client.jks -keyalg RSA \
    //             -keysize 2048 -validity 10000 -alias hbaexpress
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
            // Si `key.properties` est absent (poste de dev, CI sans secrets), on
            // retombe sur la signature de débogage — pour que `flutter run --release`
            // continue de fonctionner localement.
            //
            // Un tel binaire est INPUBLIABLE. Le message ci-dessous est là pour
            // qu'un build de release non signé ne passe pas inaperçu : sans lui, on
            // découvre le problème au moment de téléverser sur le Play Store.
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

            // NOTE : la minification (R8) n'est PAS activée ici, délibérément. Elle
            // réduirait la taille de l'APK, mais elle obfusque aussi les classes que
            // Firebase résout par réflexion — et se paie en plantages qui n'existent
            // qu'en release. C'est un chantier à part, avec ses règles ProGuard et sa
            // campagne de tests. On ne le glisse pas dans une correction de signature.
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CIBLE JVM DU CODE KOTLIN.
//
// Remplace l'ancien `android { kotlinOptions { jvmTarget = "17" } }`, retiré depuis
// Kotlin 2.2 : la propriété y est dépréciée au niveau ERREUR, ce qui empêchait la
// COMPILATION MÊME du script Gradle — le build échouait avant d'atteindre la moindre
// tâche, avec pour seul message le numéro de version de Kotlin.
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
