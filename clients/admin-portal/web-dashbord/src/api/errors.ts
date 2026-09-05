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

/**
 * Forme RFC 7807.
 *
 * ELLE N'EST PAS SEULEMENT « LES ENDPOINTS NON MIGRÉS ». C'est aussi ce que rend
 * le middleware d'exception de `HBA.Shared.Hosting` sur TOUTE erreur non gérée —
 * donc sur chaque 500 de chaque service.
 *
 * Ce middleware refuse délibérément d'interpoler `exception.Message`, et il a
 * raison : une `NpgsqlException` porte la chaîne de connexion, mot de passe
 * PostgreSQL compris, et elle traverserait la passerelle jusqu'au navigateur.
 * Le message est donc fixe et n'apprend rien.
 *
 * CE QUI APPREND QUELQUE CHOSE, C'EST `correlationId`. Le middleware le pose en
 * extension à côté de `traceId`, et c'est la seule chose qui relie l'écran à la
 * ligne d'exception dans les journaux du service. Ne pas l'afficher revient à
 * demander à l'utilisateur de décrire ce qu'il a vu.
 */
export type ProblemeRfc = {
    type?: string
    title?: string
    status?: number
    detail?: string
    /** Extensions posées par le middleware d'exception. */
    correlationId?: string
    traceId?: string
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

        /*
         * UN 500 DIT OÙ CHERCHER.
         *
         * Le message du serveur est volontairement fixe — il ne peut pas nommer
         * la cause sans risquer de divulguer la chaîne de connexion. Le seul
         * geste utile est donc d'aller lire les journaux du service, et le
         * `correlationId` est ce qui y mène. On le dit, plutôt que de laisser
         * une phrase close sur elle-même.
         */
        if (this.statut === 500) {
            const trace = this.requestId
                ? ` Journal du service, corrélation ${this.requestId}.`
                : ''
            return (
                `Le service a rencontré une erreur qu'il ne détaille pas — son ` +
                `message est volontairement muet pour ne rien divulguer.${trace}`
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
                // `correlationId` d'abord : c'est celui que le service écrit
                // dans sa ligne de journal. `traceId` ne sert qu'à défaut.
                requestId: rfc.correlationId ?? rfc.traceId ?? null,
                details: [],
            }
        }
    }
    return { message: `HTTP ${statut}`, code: null, requestId: null, details: [] }
}
