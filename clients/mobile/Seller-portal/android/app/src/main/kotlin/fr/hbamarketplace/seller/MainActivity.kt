package fr.hbamarketplace.seller

import io.flutter.embedding.android.FlutterFragmentActivity

// FlutterFragmentActivity (et non FlutterActivity) est REQUIS par local_auth :
// le BiometricPrompt Android a besoin d'une FragmentActivity hôte.
class MainActivity : FlutterFragmentActivity()
