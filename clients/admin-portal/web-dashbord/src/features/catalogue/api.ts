import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * CATALOGUE — `/api/v1/catalog/admin/products` (catalog-service).
 *
 * Cet endpoint rend TOUS les statuts et la répartition du catalogue, ce que la
 * route publique ne fait pas : le commentaire du service le dit — « c'est ce
 * qu'un administrateur doit voir, et ce qu'un visiteur ne doit pas ».
 */

export type Variante = {
    id?: string
    sku?: string
    price?: number
    currency?: string
}

export type Media = {
    url?: string
    isPrimary?: boolean
}

export type Produit = {
    id: string
    sellerId: string
    categoryId: string
    brandId?: string | null
    name: string
    description: string
    slug: string
    status: string
    gtin?: string | null
    ean?: string | null
    tags: string[]
    variants: Variante[]
    media: Media[]
}

export type FiltreProduits = {
    page: number
    taille: number
    recherche?: string
    statut?: string | null
    tri?: string | null
    sens?: string | null
}

export function listerProduits(
    filtre: FiltreProduits,
    signal?: AbortSignal,
): Promise<Page<Produit>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        search: filtre.recherche,
        status: filtre.statut,
        sort: filtre.tri,
        dir: filtre.sens,
    })
    return requete<unknown>(`/api/v1/catalog/admin/products${query}`, { signal }).then(corps =>
        lirePage<Produit>(corps, filtre.page, filtre.taille),
    )
}

/** Vocabulaire repris de `ProductStatus` (catalog-service, domaine). */
const STATUTS: Record<string, string> = {
    Draft: 'Brouillon',
    PendingReview: 'À valider',
    Approved: 'Validé',
    Rejected: 'Refusé',
    Published: 'Publié',
    Unpublished: 'Retiré',
    Suspended: 'Suspendu',
    Archived: 'Archivé',
}

export function libelleStatutProduit(statut: string): string {
    return STATUTS[statut] ?? statut
}

/** Statuts qui appellent une action humaine. */
export const STATUTS_A_TRAITER = new Set(['PendingReview'])
