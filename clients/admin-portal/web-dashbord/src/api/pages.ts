/**
 * ═══════════════════════════════════════════════════════════════════════════
 * DEUX FORMES DE PAGE COEXISTENT DANS L'API. CE MODULE LES RÉCONCILIE.
 *
 * `/api/admin/orders` répond par `Results.Ok(pagedResult)` — l'objet NU :
 *
 *   { "items": [...], "total": 42, "page": 1, "pageSize": 20,
 *     "facets": { "Paid": 12, ... }, "totalPages": 3 }
 *
 * `/api/v1/catalog/admin/products` répond par `ApiResults.Page(...)` — ENVELOPPÉ,
 * et les mêmes informations changent de place ET de nom :
 *
 *   { "success": true, "data": [...],
 *     "meta": { "requestId": "...", "page": 1, "pageSize": 20,
 *               "total": 42, "hasNext": true, "facets": { ... } } }
 *
 * CE N'EST PAS UNE BIZARRERIE, C'EST UNE MIGRATION EN COURS. L'encadré de
 * `ApiResults.cs` la décrit et la juge : « un endpoint non migré rend encore
 * l'ancienne forme en succès et la nouvelle en erreur. Cette incohérence est
 * TEMPORAIRE et doit être suivie : c'est le pire état des deux mondes. »
 *
 * Le portail ne peut pas attendre la fin de cette migration, et il ne doit pas
 * non plus la propager dans chaque écran — sinon chaque nouvel écran devra
 * deviner de quel côté se trouve son endpoint, et se trompera une fois sur
 * deux. Une seule fonction lit les deux, et le jour où tout est migré c'est le
 * seul endroit à simplifier.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Page<T> = {
    items: T[]
    page: number
    pageSize: number
    total: number
    /** Répartition par statut, quand le service la fournit. */
    facettes: Record<string, number> | null
}

type PageNue<T> = {
    items?: T[]
    page?: number
    pageSize?: number
    total?: number
    facets?: Record<string, number> | null
}

type PageEnveloppee<T> = {
    success?: boolean
    data?: T[]
    meta?: {
        page?: number
        pageSize?: number
        total?: number
        facets?: Record<string, number> | null
    }
}

export function lirePage<T>(corps: unknown, pageDemandee: number, taille: number): Page<T> {
    if (!corps || typeof corps !== 'object') {
        return { items: [], page: pageDemandee, pageSize: taille, total: 0, facettes: null }
    }

    const enveloppee = corps as PageEnveloppee<T>
    if (Array.isArray(enveloppee.data)) {
        const m = enveloppee.meta ?? {}
        return {
            items: enveloppee.data,
            page: m.page ?? pageDemandee,
            pageSize: m.pageSize ?? taille,
            total: m.total ?? enveloppee.data.length,
            facettes: m.facets ?? null,
        }
    }

    const nue = corps as PageNue<T>
    if (Array.isArray(nue.items)) {
        return {
            items: nue.items,
            page: nue.page ?? pageDemandee,
            pageSize: nue.pageSize ?? taille,
            total: nue.total ?? nue.items.length,
            facettes: nue.facets ?? null,
        }
    }

    /*
     * NI L'UNE NI L'AUTRE. On ne rend pas une page vide en silence : un écran
     * qui affiche « aucun résultat » sur un contrat inattendu envoie chercher
     * la cause dans la base, c'est-à-dire au mauvais endroit.
     */
    throw new Error(
        "Réponse paginée non reconnue : ni `items` ni `data` — le contrat de " +
        "l'endpoint a changé, ou ce n'est pas une liste.",
    )
}

/** Construit la chaîne de requête, en omettant ce qui est vide. */
export function versQuery(params: Record<string, string | number | undefined | null>): string {
    const q = new URLSearchParams()
    for (const [cle, valeur] of Object.entries(params)) {
        if (valeur === undefined || valeur === null || valeur === '') continue
        q.set(cle, String(valeur))
    }
    const s = q.toString()
    return s ? `?${s}` : ''
}

/**
 * ENVELOPPE DE SUCCÈS SANS PAGINATION.
 *
 * `delivery-pricing-service` rend ses règles par
 * `Results.Ok(ApiEnvelope.Ok(rules))` : la forme du paragraphe 5 — donc une clé
 * `data` — mais AUCUNE méta de page, parce que ce n'est pas une page. Passer par
 * `lirePage` marcherait et inventerait un `total` égal au nombre d'éléments
 * reçus, présenté comme un total de plateforme. Autant lire ce qui est là.
 *
 * Une liste NUE est acceptée aussi : c'est ce que rendent les endpoints qui
 * n'ont pas migré vers l'enveloppe, et le portail en rencontre des deux sortes
 * dans le même service.
 */
export function lireListe<T>(corps: unknown): T[] {
    if (Array.isArray(corps)) return corps as T[]
    if (corps && typeof corps === 'object') {
        const donnees = (corps as { data?: unknown }).data
        if (Array.isArray(donnees)) return donnees as T[]
    }
    throw new Error(
        "Réponse de liste non reconnue : ni tableau nu, ni enveloppe `data` — le " +
        "contrat de l'endpoint a changé.",
    )
}
