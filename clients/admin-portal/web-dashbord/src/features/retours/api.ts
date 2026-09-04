import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * RETOURS — `/api/v1/admin/returns` (return-refund-service).
 *
 * LE STATUT ARRIVE EN NOMBRE ET REPART EN NOM. CE N'EST PAS UNE ERREUR DE
 * LECTURE, C'EST L'ÉTAT DE L'API.
 *
 * `ReturnRequestDto.Status` est typé `ReturnStatus`, une énumération C#, et
 * AUCUN `JsonStringEnumConverter` n'est enregistré dans le dépôt. System.Text.Json
 * sérialise donc la valeur entière : une ligne rend `"status": 8`, pas
 * `"status": "RefundPending"`.
 *
 * Mais les deux autres bouts parlent en NOMS :
 *
 *   — le filtre : `Enum.TryParse<ReturnStatus>(query.Status, ignoreCase: true)`,
 *     donc `?status=RefundPending` ;
 *   — les facettes : `comptes.ToDictionary(x => x.Statut.ToString(), ...)`,
 *     donc des clés `"RefundPending"`.
 *
 * Le portail traduit donc dans un seul sens, ici. La correction propre est
 * côté serveur — `ConfigureHttpJsonOptions` avec un `JsonStringEnumConverter` —
 * et elle vaut pour TOUS les services : `ResolutionRequested` et `ReasonCode`
 * souffrent du même défaut sur ce même DTO, et d'autres services exposent
 * sûrement des énumérations de la même façon.
 *
 * ATTENTION LE JOUR OÙ CE CONVERTISSEUR SERA AJOUTÉ : `status` deviendra une
 * chaîne et `nomStatut` ci-dessous doit accepter les deux, sinon l'écran
 * casse au moment précis où le serveur s'améliore.
 *
 * PAS DE RECHERCHE : la route n'accepte que `page`, `pageSize` et `status`.
 * ═══════════════════════════════════════════════════════════════════════════
 */

/** `ReturnStatus`, dans l'ordre du domaine — l'index EST la valeur sérialisée. */
const ORDRE_STATUTS = [
    'Requested',
    'EligibilityCheck',
    'AwaitingApproval',
    'Approved',
    'AwaitingReturn',
    'InReturnTransit',
    'Received',
    'InspectionPending',
    'RefundPending',
    'Refunded',
    'Closed',
    'Rejected',
    'RejectedAfterInspection',
    'Cancelled',
    'Expired',
    'ManualReview',
] as const

const LIBELLES: Record<string, string> = {
    Requested: 'Demandé',
    EligibilityCheck: "Contrôle d'éligibilité",
    AwaitingApproval: 'Approbation attendue',
    Approved: 'Approuvé',
    AwaitingReturn: 'Retour attendu',
    InReturnTransit: 'Retour en transit',
    Received: 'Reçu',
    InspectionPending: 'Inspection à faire',
    RefundPending: 'Remboursement à faire',
    Refunded: 'Remboursé',
    Closed: 'Clos',
    Rejected: 'Refusé',
    RejectedAfterInspection: 'Refusé après inspection',
    Cancelled: 'Annulé',
    Expired: 'Expiré',
    ManualReview: 'Arbitrage manuel',
}

/**
 * Rend le NOM du statut, que le serveur ait envoyé un nombre ou une chaîne.
 *
 * Un nombre hors de l'énumération est rendu tel quel — `statut 42` se cherche,
 * « Inconnu » ne se cherche pas.
 */
export function nomStatut(brut: number | string): string {
    if (typeof brut === 'string') return brut
    return ORDRE_STATUTS[brut] ?? `statut ${brut}`
}

export function libelleStatutRetour(brut: number | string): string {
    const nom = nomStatut(brut)
    return LIBELLES[nom] ?? nom
}

export type Montant = { amount: number; currency: string }

export type LigneRetour = {
    id: string
    orderItemId: string
    productId: string
    variantId?: string | null
    quantity?: number
}

export type Retour = {
    id: string
    returnNumber: string
    orderId: string
    customerId: string
    sellerId: string
    storeId: string
    /** Nombre aujourd'hui, chaîne le jour où le convertisseur sera posé. */
    status: number | string
    resolutionRequested: number | string
    reasonCode: number | string
    estimatedRefund: Montant
    approvedRefund?: Montant | null
    returnShippingPayer: string
    createdAtUtc: string
    expiresAtUtc: string
    resolvedAtUtc?: string | null
    items: LigneRetour[]
}

export type FiltreRetours = {
    page: number
    taille: number
    statut?: string | null
}

export function listerRetours(filtre: FiltreRetours, signal?: AbortSignal): Promise<Page<Retour>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        status: filtre.statut,
    })
    return requete<unknown>(`/api/v1/admin/returns${query}`, { signal }).then(corps =>
        lirePage<Retour>(corps, filtre.page, filtre.taille),
    )
}

/**
 * Ce qui appelle un geste humain.
 *
 * Le service refuse délibérément de figer une « file des litiges » côté serveur
 * — « décider ici lesquels pressent figerait dans le serveur un jugement
 * d'exploitation ». Ce jugement est donc pris ICI, où il se change sans
 * redéployer un service.
 */
export const STATUTS_A_TRAITER = new Set([
    'ManualReview',
    'InspectionPending',
    'RefundPending',
])
