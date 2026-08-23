allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
}
subprojects {
    project.evaluationDependsOn(":app")
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}

// ─────────────────────────────────────────────────────────────────────────────────
// PAS DE BLOC `dependencies` ICI — et ce n'est pas un oubli.
//
// Un bloc de la forme
//
//     dependencies {
//         implementation("androidx.vectordrawable:vectordrawable-animated:1.2.0")
//         implementation("com.android.support:animated-vector-drawable:28.0.0")
//     }
//
// avait été ajouté à la fin de ce fichier. Il posait trois problèmes, chacun suffisant
// à le retirer :
//
//   1. Ce script est celui du projet RACINE, qui n'applique aucun plugin Java ou
//      Android. La configuration `implementation` n'y existe donc pas, et la DSL Kotlin
//      échoue à la compilation du script sur « Unresolved reference: implementation ».
//      Le build s'arrête avant la moindre tâche.
//
//   2. À supposer qu'il compile, il ne servirait toujours à rien : les dépendances
//      déclarées sur le projet racine ne sont héritées par AUCUN sous-projet. Une
//      dépendance de l'application se déclare dans `app/build.gradle.kts`.
//
//   3. Les deux artefacts sont la MÊME bibliothèque à deux époques :
//      `com.android.support:28.0.0` est l'ancêtre pré-AndroidX de
//      `androidx.vectordrawable`. Les charger ensemble donne des classes en double à la
//      dexation — et ce projet a `android.useAndroidX=true` sans `enableJetifier`, donc
//      rien ne réécrit l'ancienne au passage.
//
// Si l'application a réellement besoin de `vectordrawable-animated`, la ligne va dans
// `app/build.gradle.kts`, en version AndroidX UNIQUEMENT. En pratique elle est déjà
// tirée transitivement par AppCompat/Material : à vérifier avec
// `./gradlew :app:dependencies` avant d'ajouter quoi que ce soit.
// ─────────────────────────────────────────────────────────────────────────────────
