import { requete } from '../../api/client'
import { lireListe, versQuery } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * LIVRAISON — livreurs et grille tarifaire.
 *
 *   GET /api/v1/admin/drivers?status=&take=       liste NUE
 *   GET /api/v1/admin/delivery-pricing/rules      liste ENVELOPPÉE, sans page
 *
 * LE FILTRE DE STATUT DES LIVREURS N'EST PAS OPTIONNEL, IL EST PAR DÉFAUT.
 *
 * La signature est `ListAsync(status = DriverVerificationStatus.UnderReview,
 * take = 100)`. Un appel NU ne rend donc PAS tous les livreurs : il rend ceux
 * qui attendent une décision. C'est le bon défaut pour une file d'exploitation,
 * et c'est un piège pour une console qui l'ignorerait — on lirait « 3 livreurs »
 * sur une plateforme qui en compte deux cents, sans qu'aucun message ne le
 * signale.
 *
 * L'écran envoie donc TOUJOURS un statut explicite, et l'affiche.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type PieceLivreur = {
    id: string
    type: string
    status: string
    submittedAtUtc: string
    reviewedAtUtc?: string | null
    rejectionReason?: string | null
}

export type VehiculeLivreur = {
    id: string
    type: string
    make?: string | null
    model?: string | null
    plate?: string | null
    active: boolean
    capacityKg?: number | null
}

export type Livreur = {
    driverId: string
    userId: string
    fullName: string
    phone: string
    verificationStatus: string
    statusReason?: string | null
    /** Le dossier autorise-t-il à recevoir des courses. */
    dispatchable: boolean
    registeredAtUtc: string
    submittedAtUtc?: string | null
    decidedAtUtc?: string | null
    documents: PieceLivreur[]
    vehicles: VehiculeLivreur[]
    /** Pièces obligatoires encore absentes — calculé par le service. */
    missingDocuments: string[]
}

export function listerLivreurs(
    statut: string,
    take: number,
    signal?: AbortSignal,
): Promise<Livreur[]> {
    const query = versQuery({ status: statut, take })
    return requete<unknown>(`/api/v1/admin/drivers${query}`, { signal }).then(corps =>
        lireListe<Livreur>(corps),
    )
}

/** `DriverVerificationStatus`, dans l'ordre du parcours. */
const STATUTS: Record<string, string> = {
    PendingDocuments: 'Pièces manquantes',
    UnderReview: 'À examiner',
    Verified: 'Vérifié',
    Rejected: 'Refusé',
    Suspended: 'Suspendu',
}

export const STATUTS_LIVREUR = Object.keys(STATUTS)

export function libelleStatutLivreur(s: string): string {
    return STATUTS[s] ?? s
}

/**
 * RÈGLE TARIFAIRE.
 *
 * LES MONTANTS SONT DES ENTIERS, DANS L'UNITÉ DE LA DEVISE.
 *
 * `BaseFee`, `PerKmFee`, `PerMinuteFee`, `MinFee` et `MaxFee` sont des `long`.
 * Le franc CFA n'a pas de sous-unité, donc ce sont des francs entiers — pas des
 * centimes. LA RÈGLE NE PORTE AUCUNE DEVISE : c'est le DEVIS qui en pose une,
 * `request.Currency ?? "XOF"`. L'écran affiche donc en XOF et le dit, faute de
 * pouvoir faire mieux avec ce contrat.
 */
export type RegleTarifaire = {
    id: string
    name: string
    scope: string
    serviceLevel: string
    vehicleType?: string | null
    baseFee: number
    perKmFee: number
    perMinuteFee: number
    minFee: number
    maxFee?: number | null
    activeFrom: string
    activeTo?: string | null
    /** Départage les règles de même portée : le plus grand l'emporte. */
    priority: number
    surgeMultiplier: number
    /** ACTIVE ou INACTIVE. */
    status: string
}

export function listerRegles(signal?: AbortSignal): Promise<RegleTarifaire[]> {
    return requete<unknown>('/api/v1/admin/delivery-pricing/rules', { signal }).then(corps =>
        lireListe<RegleTarifaire>(corps),
    )
}

export const DEVISE_TARIFS = 'XOF'
