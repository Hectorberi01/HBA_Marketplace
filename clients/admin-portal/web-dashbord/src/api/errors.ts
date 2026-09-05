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
    /** Chemin appelé — sans lui, un 502 ne dit pas QUELLE route est morte. */
    readonly chemin: string | null
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
        chemin?: string | null
        code?: string | null
        requestId?: string | null
        details?: DetailErreur[]
        corpsBrut?: string | null
        reseau?: boolean
    }) {
        super(args.message)
        this.name = 'ApiError'
        this.statut = args.statut
        this.chemin = args.chemin ?? null
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

        /*
         * UN 502 N'EST PAS « LE SERVEUR A REPONDU 502 ».
         *
         * Ces trois codes viennent de la PASSERELLE, pas du service : elle a
         * bien reçu la requête et n'a trouvé personne au bout de la route. Le
         * corps est vide — YARP n'enveloppe rien — donc `lireErreur` ne rend
         * qu'un `HTTP 502`, et le message générique laissait croire à une panne
         * du serveur qu'on vient justement de joindre.
         *
         * La distinction change ce qu'on va regarder : un 4xx envoie vers la
         * requête, un 502 envoie vers le service amont — est-il déployé, est-il
         * démarré, l'adresse configurée dans la passerelle existe-t-elle dans
         * le compose. Nommer la route donne le point de départ.
         */
        if (this.statut === 502 || this.statut === 503 || this.statut === 504) {
            const ou = this.chemin ? ` (${this.chemin})` : ''
            const detail =
                this.statut === 504
                    ? "a mis trop de temps à répondre"
                    : "n'a pas répondu"
            return (
                `La passerelle a été jointe, mais le service qui sert cette route ` +
                `${detail}${ou}. Vérifiez qu'il est déployé et démarré.`
            )
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
