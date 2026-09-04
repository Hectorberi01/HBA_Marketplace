import { requete } from '../../api/client'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * STOCK — `/api/inventory` (inventory-service, groupe `MapAdminGroup`).
 *
 * CE GROUPE N'A QUE DEUX LECTURES, ET AUCUNE N'EST PAGINÉE.
 *
 *   GET /api/inventory/low-stock?take=N   → liste NUE d'articles sous seuil
 *   GET /api/inventory/locations          → liste NUE des lieux d'expédition
 *
 * Ni recherche, ni tri, ni pagination, ni facettes : `Results.Ok(items)` sur un
 * `IReadOnlyList`. L'écran ne peut donc pas offrir ce que les deux précédents
 * offrent, et prétendre le contraire par un filtrage côté navigateur serait
 * mentir sur la portée — un champ de recherche qui ne cherche que dans les
 * cinquante lignes déjà chargées ressemble en tout point à une recherche
 * complète, et rate silencieusement le reste.
 *
 * Le filtre local existe quand même, mais il est ANNONCÉ comme tel à l'écran.
 *
 * LES SEPT ROUTES D'ÉCRITURE SUR LE STOCK NE SONT PLUS ICI : elles sont
 * passées au groupe vendeur (VEN11), parce que le stock appartient à celui qui
 * le détient. Ce qui reste à l'administration, ce sont les réservations —
 * `POST /reservations`, `/release`, `/confirm` — que le service décrit comme
 * « une trappe d'exploitation », et qui ne sont PAS branchées ici : libérer la
 * réservation d'une commande payée fait repartir la quantité à la vente.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type ArticleStock = {
    id: string
    sku: string
    locationId: string
    onHand: number
    reserved: number
    available: number
    reorderThreshold: number
    isLowStock: boolean
}

export type LieuExpedition = {
    id: string
    type: string
    ownerId?: string | null
    communeCode: string
    communeName: string
    quartier?: string | null
    landmark?: string | null
    line?: string | null
    countryCode: string
    latitude?: number | null
    longitude?: number | null
}

export function listerSousSeuil(take: number, signal?: AbortSignal): Promise<ArticleStock[]> {
    return requete<ArticleStock[]>(`/api/inventory/low-stock?take=${take}`, { signal })
}

export function listerLieux(signal?: AbortSignal): Promise<LieuExpedition[]> {
    return requete<LieuExpedition[]>('/api/inventory/locations', { signal })
}

/** Types de lieu, tels que le domaine les nomme. */
const TYPES: Record<string, string> = {
    Warehouse: 'Entrepôt',
    Store: 'Boutique',
    PickupPoint: 'Point de retrait',
    SellerAddress: 'Adresse vendeur',
}

export function libelleTypeLieu(type: string): string {
    return TYPES[type] ?? type
}
