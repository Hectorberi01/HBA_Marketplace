import { API_BASE_URL } from '../config/env'
import { ApiError, lireErreur } from './errors'
import {
    accesBientotExpire,
    effacerJetons,
    enregistrerJetons,
    lireJetonAcces,
    lireJetonRafraichissement,
    type AuthTokens,
} from '../auth/tokens'

/**
 * CLIENT HTTP.
 *
 * Trois responsabilités, et rien d'autre : porter le jeton, rafraîchir quand il
 * est périmé, et transformer une réponse d'erreur en `ApiError` qui dit sa
 * cause.
 *
 * LE RAFRAÎCHISSEMENT EST À VOL UNIQUE.
 *
 * Un écran d'administration lance volontiers cinq requêtes en parallèle au
 * montage. Si le jeton vient d'expirer, une implémentation naïve envoie cinq
 * rafraîchissements simultanés. Or identity-service fait TOURNER le jeton de
 * rafraîchissement : le premier appel le consomme et en rend un nouveau, les
 * quatre autres présentent un jeton déjà consommé et sont refusés. La session
 * est perdue au moment précis où l'écran se charge, et le journal ne montre que
 * des 401 sans lien apparent avec la concurrence.
 *
 * `rafraichissementEnCours` fait donc attendre les appels concurrents sur la
 * MÊME promesse.
 */

let rafraichissementEnCours: Promise<boolean> | null = null

/** Appelé quand la session est définitivement perdue. Branché par AuthProvider. */
let surDeconnexion: (() => void) | null = null

export function brancherDeconnexion(rappel: () => void): void {
    surDeconnexion = rappel
}

async function lireCorps(reponse: Response): Promise<{ corps: unknown; brut: string | null }> {
    let texte: string | null = null
    try {
        texte = await reponse.text()
    } catch {
        return { corps: null, brut: null }
    }
    if (!texte) return { corps: null, brut: null }
    try {
        return { corps: JSON.parse(texte), brut: texte }
    } catch {
        // Une passerelle en erreur rend du HTML, pas du JSON. On garde le texte
        // : « <html><body>502 Bad Gateway » est une information, « erreur de
        // parsing » n'en est pas une.
        return { corps: null, brut: texte }
    }
}

/**
 * Échange le jeton de rafraîchissement contre une nouvelle paire.
 * Rend `false` si la session est perdue — l'appelant doit alors déconnecter.
 */
async function rafraichir(): Promise<boolean> {
    const refresh = lireJetonRafraichissement()
    if (!refresh) return false

    try {
        const reponse = await fetch(`${API_BASE_URL}/api/v1/auth/refresh`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ refreshToken: refresh }),
        })
        if (!reponse.ok) return false
        const jetons = (await reponse.json()) as AuthTokens
        if (!jetons?.accessToken) return false
        enregistrerJetons(jetons)
        return true
    } catch {
        return false
    }
}

async function assurerJetonFrais(): Promise<void> {
    if (!accesBientotExpire()) return
    if (!lireJetonRafraichissement()) return

    rafraichissementEnCours ??= rafraichir().finally(() => {
        rafraichissementEnCours = null
    })
    const reussi = await rafraichissementEnCours
    if (!reussi) {
        effacerJetons()
        surDeconnexion?.()
    }
}

export type OptionsRequete = {
    methode?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
    corps?: unknown
    /** Requête publique : on n'attache pas de jeton et on ne rafraîchit pas. */
    anonyme?: boolean
    signal?: AbortSignal
}

export async function requete<T>(chemin: string, options: OptionsRequete = {}): Promise<T> {
    return executer<T>(chemin, options, false)
}

/**
 * `dejaReessaye` est un drapeau INTERNE, absent de l'API publique.
 *
 * Première version : après un rafraîchissement réussi, la requête était rejouée
 * avec `anonyme: true` pour éviter de re-rafraîchir. Elle repartait donc SANS
 * jeton — exactement l'inverse de ce qu'on venait d'obtenir. Le serveur rendait
 * un second 401, cette fois définitif, et la session était perdue juste après
 * avoir été renouvelée avec succès.
 *
 * Le drapeau borne la récursion sans toucher à l'authentification.
 */
async function executer<T>(
    chemin: string,
    options: OptionsRequete,
    dejaReessaye: boolean,
): Promise<T> {
    const { methode = 'GET', corps, anonyme = false, signal } = options

    if (!anonyme) await assurerJetonFrais()

    const entetes: Record<string, string> = {}
    if (corps !== undefined) entetes['Content-Type'] = 'application/json'
    if (!anonyme) {
        const jeton = lireJetonAcces()
        if (jeton) entetes.Authorization = `Bearer ${jeton}`
    }

    let reponse: Response
    try {
        reponse = await fetch(`${API_BASE_URL}${chemin}`, {
            method: methode,
            headers: entetes,
            body: corps === undefined ? undefined : JSON.stringify(corps),
            signal,
        })
    } catch (cause) {
        // `fetch` ne rejette QUE si la requête n'a pas abouti : hors ligne, DNS,
        // TLS, CORS, ou abandon. Un 500 est une promesse tenue. Distinguer les
        // deux évite d'annoncer une panne serveur quand c'est le Wi-Fi.
        if (signal?.aborted) throw cause
        throw new ApiError({
            message: 'requête non aboutie',
            statut: 0,
            reseau: true,
        })
    }

    // UN SEUL NOUVEL ESSAI, ET SEULEMENT SUR 401.
    // Boucler sur le rafraîchissement transformerait un refus permanent — un
    // compte suspendu, par exemple — en martèlement silencieux du serveur.
    if (reponse.status === 401 && !anonyme && !dejaReessaye && lireJetonRafraichissement()) {
        rafraichissementEnCours ??= rafraichir().finally(() => {
            rafraichissementEnCours = null
        })
        const reussi = await rafraichissementEnCours
        if (reussi) {
            return executer<T>(chemin, options, true)
        }
        effacerJetons()
        surDeconnexion?.()
    }

    if (!reponse.ok) {
        const { corps, brut } = await lireCorps(reponse)
        const lu = lireErreur(corps, reponse.status)
        throw new ApiError({
            message: lu.message,
            statut: reponse.status,
            code: lu.code,
            requestId: lu.requestId,
            details: lu.details,
            corpsBrut: brut,
        })
    }

    if (reponse.status === 204) return undefined as T
    const texte = await reponse.text()
    if (!texte) return undefined as T
    return JSON.parse(texte) as T
}
