import { requete } from '../api/client'
import { lireJetonRafraichissement } from './tokens'
import type { AuthTokens } from './tokens'

/**
 * Appels d'identity-service utilisés par le portail.
 *
 * Ces types sont ÉCRITS À LA MAIN, et c'est une dette assumée. Les services
 * exposent OpenAPI via `AddHbaOpenApi`, mais aucune route de la passerelle ne
 * relaie ces documents : `/openapi/v1.json` n'est pas proxifié. Tant que la
 * pile ne tourne pas en local — ou qu'une route ne relaie pas les documents —
 * la génération n'a pas de source à lire, et recopier un contrat à la main est
 * la seule option honnête. Elle a le défaut de ce qu'elle est : rien ne
 * signalera une divergence, elle se découvrira à l'exécution.
 *
 * Contrats, tels qu'ils sont dans le code du service :
 *   IdentityEndpoints.LoginRequest(string Email, string Password, string? MfaCode)
 *   AuthModels.LoginResponse(bool MfaRequired, AuthTokens? Tokens)
 *   AuthModels.AuthTokens(AccessToken, AccessTokenExpiresOnUtc,
 *                         RefreshToken, RefreshTokenExpiresOnUtc)
 */

export type ReponseConnexion = {
    mfaRequired: boolean
    tokens: AuthTokens | null
}

export function seConnecter(
    email: string,
    motDePasse: string,
    codeMfa?: string,
): Promise<ReponseConnexion> {
    return requete<ReponseConnexion>('/api/v1/auth/login', {
        methode: 'POST',
        anonyme: true,
        corps: { email, password: motDePasse, mfaCode: codeMfa ?? null },
    })
}

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * LE COMPTE CONNECTÉ — `GET /api/identity/account/me`.
 *
 * CE TYPE ÉTAIT INVENTÉ, PAS LU.
 *
 * Il déclarait `fullName`, `roles` et rendait tout optionnel. La route rend un
 * `UserSummary` : `firstName` et `lastName` SÉPARÉS, `roleIds` et non `roles`,
 * plus `emailVerified`, `mfaEnabled` et l'acceptation des conditions. `fullName`
 * et `roles` valaient donc `undefined` à chaque lecture — sans erreur, puisque
 * TypeScript croyait sur parole une forme que personne n'avait vérifiée.
 *
 * C'est exactement l'inverse de ce que fait le reste du portail, où chaque type
 * est recopié du contrat C# avec sa source en commentaire.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export type CompteConnecte = {
    id: string
    firstName: string
    lastName: string
    email: string
    phoneNumber: string
    status: string
    emailVerified: boolean
    mfaEnabled: boolean
    roleIds: string[]
    acceptedTermsVersion?: string | null
    acceptedTermsOnUtc?: string | null
    /** Renseignée = adresse attestée par un administrateur, pas vérifiée par son titulaire. */
    emailVerifiedByAdminOnUtc?: string | null
}

export function lireMonCompte(signal?: AbortSignal): Promise<CompteConnecte> {
    return requete<CompteConnecte>('/api/identity/account/me', { signal })
}

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * DÉCONNEXION CÔTÉ SERVEUR — ET ELLE NE PARTAIT PAS.
 *
 * `LogoutAsync(LogoutRequest request, …)` attend `{ refreshToken }` dans le
 * CORPS. Cet appel partait sans corps : 400, à chaque déconnexion, silencieux
 * parce que l'appelant avale l'échec et efface le stockage local de toute façon.
 *
 * CE QUE ÇA COÛTAIT. Le commentaire d'origine décrivait précisément le défaut
 * qu'il causait : « sans cet appel, effacer le stockage du navigateur ne ferme
 * rien : le jeton reste valide jusqu'à son expiration, et quiconque l'a copié
 * garde la session. » L'appel existait, il ne révoquait rien, et l'encadré
 * expliquait pourquoi c'était grave.
 *
 * LE JETON EST LU AU MOMENT DE L'APPEL, pas passé en argument : l'appelant
 * (`AuthContext.deconnexion`) n'a pas à connaître le stockage, et une
 * déconnexion déclenchée depuis un autre endroit ne pourra pas oublier de le
 * fournir.
 *
 * SANS JETON, ON N'APPELLE PAS. Une session dont le rafraîchissement a déjà été
 * effacé n'a rien à révoquer ; envoyer une chaîne vide rendrait un 400 qui
 * ressemblerait à une panne.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export function seDeconnecter(): Promise<void> {
    const rafraichissement = lireJetonRafraichissement()
    if (!rafraichissement) return Promise.resolve()

    return requete<void>('/api/identity/account/me/logout', {
        methode: 'POST',
        corps: { refreshToken: rafraichissement },
    })
}

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * SON PROPRE COMPTE — CE QUE `/api/identity/account` PERMET, ET CE QU'IL NE
 *     PERMET PAS.
 *
 * TROIS GESTES SEULEMENT SONT OFFERTS PAR L'ÉCRAN : le profil, le mot de passe,
 * la double authentification. Le reste de ce groupe existe et n'est pas branché,
 * chacun pour une raison écrite.
 *
 * L'ADRESSE E-MAIL NE SE CHANGE PAS. `UpdateProfileRequest` porte prénom, nom et
 * téléphone — pas l'adresse. Aucune route ne la modifie, et c'est cohérent :
 * elle est l'identifiant de connexion, et la changer sans re-vérification
 * ouvrirait une prise de compte. Le champ est donc affiché, jamais éditable.
 *
 * LA SUPPRESSION DE COMPTE N'EST PAS OFFERTE ICI. `DELETE /me` existe — c'est
 * une exigence bloquante de l'App Store pour les applications mobiles, et le
 * dépôt le documente. Sur une console d'administration, elle anonymiserait le
 * compte de l'administrateur qui la clique. Or notification-service n'est pas
 * déployé : aucun courriel de réinitialisation ne part, et le seul compte admin
 * de la plateforme est le seul moyen d'entrer. Ce geste appartient aux
 * applications mobiles, pas à cet écran.
 *
 * IL N'Y A PAS DE « DÉCONNECTER TOUS MES APPAREILS ». `RevokeUserSessions`
 * existe, en gRPC interne uniquement : aucune route HTTP ne l'expose au
 * titulaire. `POST /me/logout` révoque UN jeton, celui qu'on lui donne.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export function modifierMonProfil(entree: {
    firstName: string
    lastName: string
    phoneNumber: string
}): Promise<void> {
    return requete<void>('/api/identity/account/me', { methode: 'PUT', corps: entree })
}

export function changerMonMotDePasse(entree: {
    currentPassword: string
    newPassword: string
}): Promise<void> {
    return requete<void>('/api/identity/account/me/change-password', {
        methode: 'POST',
        corps: entree,
    })
}

/**
 * Ce que rend `POST /me/mfa/setup` — `MfaSetupResponse(Secret, OtpAuthUri)`.
 *
 * LE SECRET N'EST RENDU QU'ICI, ET UNE SEULE FOIS. Il n'est plus lisible ensuite :
 * l'écran doit donc le montrer tant que la configuration n'est pas confirmée, et
 * ne pas le faire disparaître au premier clic à côté.
 */
export type MiseEnPlaceMfa = { secret: string; otpAuthUri: string }

export function demarrerMfa(): Promise<MiseEnPlaceMfa> {
    return requete<MiseEnPlaceMfa>('/api/identity/account/me/mfa/setup', { methode: 'POST' })
}

export function confirmerMfa(code: string): Promise<void> {
    return requete<void>('/api/identity/account/me/mfa/confirm', {
        methode: 'POST',
        corps: { code },
    })
}

export function desactiverMfa(code: string): Promise<void> {
    return requete<void>('/api/identity/account/me/mfa/disable', {
        methode: 'POST',
        corps: { code },
    })
}
