/**
 * ERREURS D'API, AVEC LEUR CAUSE.
 *
 * L'ENVELOPPE N'EST PAS DU RFC 7807. Je l'avais supposé, à tort.
 *
 * `ApiResults.Problem` rend la forme du paragraphe 5 :
 *
 *   { "success": false,
 *     "error": { "code": "...", "message": "...", "details": [...] },
 *     "meta":  { "requestId": "...", "timestamp": "..." } }
 *
 * Les deux formes n'ont AUCUN champ en commun — l'encadré de ApiResults.cs le
 * dit : « ce n'est pas un enrichissement, c'est un remplacement ». Lire `detail`
 * et `title` ne rendait donc jamais rien, et l'écran de connexion serait retombé
 * sur « Le serveur a répondu 401. » à la place du message écrit par le service.
 *
 * Les deux formes sont acceptées ici. Certains endpoints n'ont pas encore migré
 * — le même fichier parle d'une incohérence « TEMPORAIRE » à suivre — et un
 * client qui ne comprend qu'une des deux affiche un message vide sur l'autre.
 */

/** Détail de validation, champ par champ. */
export type DetailErreur = {
    field?: string
    message?: string
}

/** Forme du paragraphe 5, rendue par `ApiResults`. */
export type EnveloppeErreur = {
    success?: false
    error?: {
        code?: string
        message?: string
        details?: DetailErreur[]
    }
    meta?: {
        requestId?: string
        timestamp?: string
    }
}

/** Forme RFC 7807, rendue par les endpoints non encore migrés. */
export type ProblemeRfc = {
    type?: string
    title?: string
    status?: number
    detail?: string
}

export class ApiError extends Error {
    readonly statut: number
    /** Code métier stable, du genre `identity.auth.suspended`. */
    readonly code: string | null
    /** Identifiant de requête, à citer dans un rapport de bogue. */
    readonly requestId: string | null
    readonly details: DetailErreur[]
    readonly corpsBrut: string | null
    /** Vrai quand la requête n'a jamais abouti (hors ligne, DNS, CORS, TLS). */
    readonly reseau: boolean

    constructor(args: {
        message: string
        statut: number
        code?: string | null
        requestId?: string | null
        details?: DetailErreur[]
        corpsBrut?: string | null
        reseau?: boolean
    }) {
        super(args.message)
        this.name = 'ApiError'
        this.statut = args.statut
        this.code = args.code ?? null
        this.requestId = args.requestId ?? null
        this.details = args.details ?? []
        this.corpsBrut = args.corpsBrut ?? null
        this.reseau = args.reseau ?? false
    }

    /**
     * Message destiné à l'écran. On préfère toujours celui du serveur : il est
     * écrit pour un humain et il est en français. Le code HTTP seul ne dit rien
     * à personne.
     */
    get messageLisible(): string {
        if (this.reseau) {
            return "Le serveur n'a pas répondu. Vérifiez votre connexion."
        }
        if (this.message && this.message !== `HTTP ${this.statut}`) {
            return this.message
        }
        if (this.corpsBrut && this.corpsBrut.length < 300) return this.corpsBrut
        return `Le serveur a répondu ${this.statut}.`
    }
}

/** Extrait message, code et requestId de l'une ou l'autre forme. */
export function lireErreur(corps: unknown, statut: number): {
    message: string
    code: string | null
    requestId: string | null
    details: DetailErreur[]
} {
    if (corps && typeof corps === 'object') {
        const enveloppe = corps as EnveloppeErreur
        if (enveloppe.error) {
            return {
                message: enveloppe.error.message ?? `HTTP ${statut}`,
                code: enveloppe.error.code ?? null,
                requestId: enveloppe.meta?.requestId ?? null,
                details: enveloppe.error.details ?? [],
            }
        }
        const rfc = corps as ProblemeRfc
        if (rfc.detail || rfc.title) {
            return {
                message: rfc.detail ?? rfc.title ?? `HTTP ${statut}`,
                code: rfc.type ?? null,
                requestId: null,
                details: [],
            }
        }
    }
    return { message: `HTTP ${statut}`, code: null, requestId: null, details: [] }
}
