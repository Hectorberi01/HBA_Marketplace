/**
 * ERREURS D'API, AVEC LEUR CAUSE.
 *
 * Le but de ce module est qu'aucune erreur affichée ne dise seulement « une
 * erreur est survenue ». Trois causes très différentes se ressemblent vues du
 * navigateur, et il faut pouvoir les distinguer :
 *
 *   - le réseau n'a rien rendu       -> `ApiError.reseau`
 *   - le serveur a répondu une erreur -> code HTTP + corps du problème
 *   - le corps n'était pas du JSON    -> on garde le texte brut
 */

/** Forme d'erreur rendue par les services (RFC 7807, plus les champs HBA). */
export type ProblemeApi = {
    type?: string
    title?: string
    status?: number
    detail?: string
    /** Code métier stable, du genre `identity.auth.suspended`. */
    code?: string
    /** Identifiant de requête, à citer dans un rapport de bogue. */
    requestId?: string
    errors?: Record<string, string[]>
}

export class ApiError extends Error {
    readonly statut: number
    readonly probleme: ProblemeApi | null
    readonly corpsBrut: string | null
    /** Vrai quand la requête n'a jamais abouti (hors ligne, DNS, CORS, TLS). */
    readonly reseau: boolean

    constructor(args: {
        message: string
        statut: number
        probleme?: ProblemeApi | null
        corpsBrut?: string | null
        reseau?: boolean
    }) {
        super(args.message)
        this.name = 'ApiError'
        this.statut = args.statut
        this.probleme = args.probleme ?? null
        this.corpsBrut = args.corpsBrut ?? null
        this.reseau = args.reseau ?? false
    }

    /**
     * Message destiné à l'écran. On préfère toujours le `detail` du serveur :
     * il est écrit pour un humain et il est traduit. Le code HTTP seul ne dit
     * rien à personne.
     */
    get messageLisible(): string {
        if (this.reseau) {
            return "Le serveur n'a pas répondu. Vérifiez votre connexion."
        }
        const p = this.probleme
        if (p?.detail) return p.detail
        if (p?.title) return p.title
        if (this.corpsBrut && this.corpsBrut.length < 300) return this.corpsBrut
        return `Le serveur a répondu ${this.statut}.`
    }
}
