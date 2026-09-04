/**
 * JETONS : OÙ ILS VIVENT, ET CE QUE CE CHOIX EXPOSE.
 *
 * Le jeton d'ACCÈS ne quitte jamais la mémoire. Il est court, il est rejoué à
 * chaque requête, et le mettre dans le stockage du navigateur n'apporte rien :
 * il serait périmé au prochain chargement de toute façon.
 *
 * Le jeton de RAFRAÎCHISSEMENT est dans `sessionStorage`.
 *
 *   CE QUE CELA DONNE   : la session survit à un rechargement de la page, et
 *                         disparaît à la fermeture de l'onglet. Ouvrir un
 *                         second onglet demande une nouvelle connexion.
 *
 *   CE QUE CELA EXPOSE  : tout script exécuté dans la page peut le lire. Une
 *                         faille XSS dans ce portail — ou dans une dépendance
 *                         — donne à l'attaquant une session administrateur
 *                         complète, renouvelable jusqu'à l'expiration du jeton
 *                         de rafraîchissement.
 *
 *   LA VRAIE PARADE     : que identity-service pose le rafraîchissement dans
 *                         un cookie `HttpOnly; Secure; SameSite=Strict`, hors
 *                         de portée de tout script. Il rend aujourd'hui les
 *                         deux jetons dans le corps JSON, donc ce n'est pas
 *                         faisable côté client seul.
 *
 * `localStorage` a été écarté : il persiste après la fermeture du navigateur,
 * ce qui allonge la fenêtre d'exploitation sans rien apporter à un outil
 * d'administration qu'on ouvre pour une tâche puis qu'on quitte.
 *
 * TOUT PASSE PAR CE MODULE. Le jour où le cookie HttpOnly existe, c'est le
 * seul fichier à réécrire.
 */

const CLE_RAFRAICHISSEMENT = 'hba.admin.refresh'

/** Paire rendue par `POST /api/v1/auth/login` et `/refresh`. */
export type AuthTokens = {
    accessToken: string
    accessTokenExpiresOnUtc: string
    refreshToken: string
    refreshTokenExpiresOnUtc: string
}

let jetonAcces: string | null = null
let accesExpireLe: number | null = null

export function lireJetonAcces(): string | null {
    return jetonAcces
}

/**
 * Vrai quand le jeton d'accès est absent ou sur le point d'expirer.
 *
 * LA MARGE DE TRENTE SECONDES N'EST PAS DE LA PRUDENCE DÉCORATIVE. Sans elle,
 * un jeton valide au moment de l'envoi peut être expiré à l'arrivée : l'horloge
 * du navigateur et celle du serveur ne sont pas les mêmes, et la requête met du
 * temps à voyager. Le symptôme serait un 401 intermittent, impossible à
 * reproduire.
 */
export function accesBientotExpire(margeMs = 30_000): boolean {
    if (!jetonAcces || accesExpireLe === null) return true
    return Date.now() + margeMs >= accesExpireLe
}

export function lireJetonRafraichissement(): string | null {
    try {
        return sessionStorage.getItem(CLE_RAFRAICHISSEMENT)
    } catch {
        // Navigation privée stricte, stockage désactivé par une politique
        // d'entreprise : l'accès lève au lieu de rendre null. La session
        // devient alors valable pour ce chargement de page uniquement.
        return null
    }
}

export function enregistrerJetons(jetons: AuthTokens): void {
    jetonAcces = jetons.accessToken
    const expire = Date.parse(jetons.accessTokenExpiresOnUtc)
    accesExpireLe = Number.isNaN(expire) ? null : expire
    try {
        sessionStorage.setItem(CLE_RAFRAICHISSEMENT, jetons.refreshToken)
    } catch {
        // Voir ci-dessus. On garde l'accès en mémoire : la session fonctionne,
        // elle ne survivra simplement pas au rechargement.
    }
}

export function effacerJetons(): void {
    jetonAcces = null
    accesExpireLe = null
    try {
        sessionStorage.removeItem(CLE_RAFRAICHISSEMENT)
    } catch {
        // Rien à faire : s'il n'a pas pu être écrit, il n'est pas là.
    }
}
