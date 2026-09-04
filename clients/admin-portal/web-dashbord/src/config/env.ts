/**
 * CONFIGURATION D'EXÉCUTION.
 *
 * Une seule variable pour l'instant : l'adresse de l'API. Elle est LUE ICI ET
 * NULLE PART AILLEURS — un `import.meta.env` dispersé dans les modules rend
 * impossible de savoir, en lisant le code, ce que le portail attend de son
 * environnement.
 *
 * ON ÉCHOUE AU DÉMARRAGE, PAS À LA PREMIÈRE REQUÊTE.
 *
 * Une base absente donnerait des appels vers `/api/...` sur l'origine du
 * portail : en développement, le serveur Vite répond une page HTML avec un
 * code 200, et le client échoue plus loin sur « Unexpected token < in JSON ».
 * Ce message ne parle ni de configuration ni d'adresse. Mieux vaut refuser de
 * démarrer en nommant la variable manquante.
 */

const brute = import.meta.env.VITE_API_BASE_URL

if (typeof brute !== 'string' || brute.trim() === '') {
    throw new Error(
        "VITE_API_BASE_URL est absente ou vide. Créez un fichier .env.local à " +
        "la racine de web-dashbord avec, par exemple :\n\n" +
        "  VITE_API_BASE_URL=https://api.hba-express.com\n",
    )
}

/**
 * Adresse de l'API, sans barre oblique finale.
 *
 * LA VALEUR `same-origin` EST EXPLICITE, ET NON UNE CHAÎNE VIDE.
 *
 * En développement, les appels doivent partir en relatif — `/api/...` — pour
 * que le mandataire de Vite les relaie et que la politique d'origine du
 * navigateur ne s'applique pas. La base est donc vide.
 *
 * Mais « vide » et « absente » se ressemblent trop : une variable oubliée
 * donnerait exactement le même comportement qu'un choix délibéré, et le
 * portail taperait sur sa propre origine sans que rien ne le signale. Un mot
 * écrit à la main lève l'ambiguïté — on ne tape pas `same-origin` par accident.
 */
export const API_BASE_URL =
    brute.trim() === 'same-origin' ? '' : brute.trim().replace(/\/+$/, '')

/**
 * Rôle exigé pour entrer dans le portail.
 *
 * Le nom vient de `IdentityDataSeeder` : les rôles semés sont Buyer, Seller,
 * Driver, Admin, Dispatcher et Support. « Admin » porte `users.manage`,
 * `roles.manage` et `catalog.manage`.
 */
export const ROLE_REQUIS = 'Admin'
