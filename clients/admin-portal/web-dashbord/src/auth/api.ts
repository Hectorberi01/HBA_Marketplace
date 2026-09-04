import { requete } from '../api/client'
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

/** Résumé du compte connecté, rendu par `GET /api/identity/account/me`. */
export type CompteConnecte = {
    id: string
    email?: string
    fullName?: string
    phoneNumber?: string
    status?: string
    roles?: string[]
}

export function lireMonCompte(signal?: AbortSignal): Promise<CompteConnecte> {
    return requete<CompteConnecte>('/api/identity/account/me', { signal })
}

/**
 * Déconnexion côté serveur : elle RÉVOQUE le jeton de rafraîchissement.
 *
 * Sans cet appel, effacer le stockage du navigateur ne ferme rien : le jeton
 * reste valide jusqu'à son expiration, et quiconque l'a copié garde la session.
 */
export function seDeconnecter(): Promise<void> {
    return requete<void>('/api/identity/account/me/logout', { methode: 'POST' })
}
