import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * VENDEURS — `/api/v1/merchants` (seller-service, groupe de gouvernance).
 *
 * DEUX FILTRES INDÉPENDANTS, et c'est le fond du métier ici : `status` dit où
 * en est le COMPTE (Pending, Active, Suspended, Closed), `kybStatus` dit où en
 * est le DOSSIER de vérification (NotStarted, InReview, Verified, Rejected).
 * Un vendeur peut être actif avec un KYB rejeté, ou en attente avec un KYB
 * vérifié — les confondre ferait disparaître exactement les cas qu'on cherche.
 *
 * LA LISTE NE PORTE PAS LES DONNÉES SENSIBLES, À DESSEIN. Le service le dit :
 * `SellerListItem` ne contient ni compte de retrait, ni RCCM, ni IFU, ni
 * téléphone du gérant — « une console a le droit d'afficher ces données sur la
 * fiche qu'un humain ouvre, pas dans un listing qu'un écran charge au réveil ».
 */

export type Vendeur = {
    id: string
    userId: string
    shopName: string
    logoUrl?: string | null
    status: string
    kybStatus: string
    kybDocumentCount: number
    kybRejectionReason?: string | null
    createdOnUtc: string
}

export type FiltreVendeurs = {
    page: number
    taille: number
    recherche?: string
    statut?: string | null
    statutKyb?: string | null
}

export function listerVendeurs(
    filtre: FiltreVendeurs,
    signal?: AbortSignal,
): Promise<Page<Vendeur>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        search: filtre.recherche,
        status: filtre.statut,
        kybStatus: filtre.statutKyb,
    })
    return requete<unknown>(`/api/v1/merchants${query}`, { signal }).then(corps =>
        lirePage<Vendeur>(corps, filtre.page, filtre.taille),
    )
}

/** `SellerStatus` — état du compte vendeur. */
const STATUTS: Record<string, string> = {
    Pending: 'En attente',
    Active: 'Actif',
    Suspended: 'Suspendu',
    Closed: 'Fermé',
}

/** `KybStatus` — état du dossier de vérification. */
const KYB: Record<string, string> = {
    NotStarted: 'Non commencé',
    InReview: 'En revue',
    Verified: 'Vérifié',
    Rejected: 'Refusé',
}

export function libelleStatutVendeur(statut: string): string {
    return STATUTS[statut] ?? statut
}

export function libelleKyb(statut: string): string {
    return KYB[statut] ?? statut
}

export const STATUTS_VENDEUR = Object.keys(STATUTS)
export const STATUTS_KYB = Object.keys(KYB)

/** Ce qui appelle un geste : un dossier en revue, un compte suspendu. */
export const A_TRAITER_KYB = new Set(['InReview'])
