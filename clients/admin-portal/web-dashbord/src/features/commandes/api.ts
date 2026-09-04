import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * COMMANDES — `/api/admin/orders` (order-service, groupe `MapAdminGroup`).
 *
 * `page` ET `pageSize` SONT OBLIGATOIRES, ET CE N'EST PAS UN CHOIX DU PORTAIL.
 *
 * La signature côté service est `ListAllAsync(int page, int pageSize, ...)` :
 * des `int` NON nullables, sans valeur par défaut. Le liage de Minimal API rend
 * alors 400 « Required parameter was not provided » si l'un des deux manque —
 * une erreur de validation pour un paramètre que l'utilisateur n'a jamais vu.
 *
 * Le catalogue, lui, déclare `int? page, int? pageSize` et retombe sur 1 et 20.
 * Les deux endpoints voisins ne se comportent donc pas pareil sur une requête
 * nue. On envoie toujours les deux, ce qui est correct dans les deux cas.
 */

export type LigneCommande = {
    productId?: string
    variantId?: string
    quantity?: number
    unitPrice?: number
}

export type Commande = {
    id: string
    buyerId: string
    cartId: string
    currency: string
    status: string
    createdAtUtc: string
    subtotal: number
    totalSellerDiscount: number
    totalPlatformDiscount: number
    grandTotal: number
    lines: LigneCommande[]
    shippingFee?: number
    kind?: string
    restaurantId?: string | null
    /** Motif de mise en arbitrage — présent seulement si `status` vaut UnderReview. */
    reviewReason?: string | null
    underReviewSinceUtc?: string | null
}

export type FiltreCommandes = {
    page: number
    taille: number
    recherche?: string
    statut?: string | null
    tri?: string | null
    sens?: string | null
}

export function listerCommandes(
    filtre: FiltreCommandes,
    signal?: AbortSignal,
): Promise<Page<Commande>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        search: filtre.recherche,
        status: filtre.statut,
        sort: filtre.tri,
        dir: filtre.sens,
    })
    return requete<unknown>(`/api/admin/orders${query}`, { signal }).then(corps =>
        lirePage<Commande>(corps, filtre.page, filtre.taille),
    )
}

/**
 * VOCABULAIRE DES STATUTS, repris de `OrderStatus` (order-service, domaine).
 *
 * Un statut inconnu est rendu TEL QUEL plutôt que traduit en « Inconnu » : le
 * jour où le domaine en ajoute un, l'écran affichera son nom technique — lisible
 * et cherchable — au lieu de le faire disparaître derrière un libellé fourre-tout.
 */
const STATUTS: Record<string, string> = {
    Pending: 'En attente',
    AwaitingPayment: 'Paiement attendu',
    Paid: 'Payée',
    Confirmed: 'Confirmée',
    Cancelled: 'Annulée',
    Failed: 'Échouée',
    Delivered: 'Livrée',
    UnderReview: 'En arbitrage',
}

export function libelleStatutCommande(statut: string): string {
    return STATUTS[statut] ?? statut
}

/** Statuts qui appellent une action humaine, mis en avant dans la liste. */
export const STATUTS_A_TRAITER = new Set(['UnderReview', 'Failed'])
