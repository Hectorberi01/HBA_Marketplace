import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * FINANCE — trois lectures, trois formes. Encore.
 *
 *   GET /api/financial/settlements   liste NUE, aucun paramètre
 *   GET /api/financial/commissions   liste NUE, aucun paramètre
 *   GET /api/financial/invoices      paginée, enveloppée, avec facettes
 *
 * Les deux premières viennent de `ListSettlementBatchesQuery` et
 * `ListCommissionRulesQuery`, qui ne prennent RIEN : pas de page, pas de
 * filtre, pas de recherche. Les écrans correspondants filtrent donc localement,
 * et le disent.
 *
 * LES ÉCRITURES DE RÈGLEMENT NE SONT PAS ATTEIGNABLES, ET C'EST DÉLIBÉRÉ.
 *
 * `MapAdminGroup("/api/financial/settlements")` monte quatre POST : lancer un
 * règlement, marquer un versement payé, le marquer échoué, annuler un lot. La
 * route `settlements` de la passerelle n'accepte que GET, HEAD et OPTIONS — le
 * service l'écrit noir sur blanc : « comme .../paid, elle n'est PAS relayée par
 * la passerelle […] Elle n'est donc atteignable que depuis le réseau interne. »
 *
 * Ce n'est pas un oubli à corriger : ces gestes déplacent de l'argent dans les
 * deux sens, et `MarkPayoutFailed` RECRÉDITE un vendeur. Les garder hors du web
 * est une décision. L'écran de règlements est donc en lecture, définitivement,
 * tant que cette décision tient.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Versement = {
    id: string
    sellerId: string
    grossAmount: number
    commissionAmount: number
    netAmount: number
    currency: string
    status: string
    providerRef?: string | null
    paidAtUtc?: string | null
}

export type LotReglement = {
    id: string
    periodStartUtc: string
    periodEndUtc: string
    currency: string
    totalNet: number
    status: string
    createdAtUtc: string
    payouts: Versement[]
}

export type RegleCommission = {
    id: string
    /** Global, Category ou Seller. */
    scope: string
    /** Identifiant de la catégorie ou du vendeur visé — nul si Global. */
    targetId?: string | null
    /** FRACTION, pas pourcentage : 0.1 vaut dix pour cent. */
    rate: number
    fixedFee: number
    currency: string
    minFee?: number | null
    maxFee?: number | null
    effectiveFromUtc: string
    isActive: boolean
}

export type Facture = {
    id: string
    sellerId: string
    periodStartUtc: string
    periodEndUtc: string
    currency: string
    totalAmount: number
    status: string
}

export function listerLots(signal?: AbortSignal): Promise<LotReglement[]> {
    return requete<LotReglement[]>('/api/financial/settlements', { signal })
}

export function listerRegles(signal?: AbortSignal): Promise<RegleCommission[]> {
    return requete<RegleCommission[]>('/api/financial/commissions', { signal })
}

export type FiltreFactures = {
    page: number
    taille: number
    statut?: string | null
    vendeur?: string | null
}

export function listerFactures(
    filtre: FiltreFactures,
    signal?: AbortSignal,
): Promise<Page<Facture>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        status: filtre.statut,
        sellerId: filtre.vendeur,
    })
    return requete<unknown>(`/api/financial/invoices${query}`, { signal }).then(corps =>
        lirePage<Facture>(corps, filtre.page, filtre.taille),
    )
}

/** `SettlementStatus` — état d'un lot. */
const LOTS: Record<string, string> = {
    Pending: 'En attente',
    Processing: 'En cours',
    Completed: 'Terminé',
    PartiallyFailed: 'Partiellement échoué',
    Cancelled: 'Annulé',
}

/** `PayoutStatus` du domaine wallet — trois états, pas six. */
const VERSEMENTS: Record<string, string> = {
    Scheduled: 'Programmé',
    Paid: 'Payé',
    Failed: 'Échoué',
}

/** `InvoiceStatus`. */
const FACTURES: Record<string, string> = {
    Draft: 'Brouillon',
    Issued: 'Émise',
    Paid: 'Payée',
}

/** `CommissionScope`. */
const PORTEES: Record<string, string> = {
    Global: 'Plateforme',
    Category: 'Catégorie',
    Seller: 'Vendeur',
}

export function libelleLot(s: string): string {
    return LOTS[s] ?? s
}

export function libelleVersement(s: string): string {
    return VERSEMENTS[s] ?? s
}

export function libelleFacture(s: string): string {
    return FACTURES[s] ?? s
}

export function libellePortee(s: string): string {
    return PORTEES[s] ?? s
}

export const LOTS_A_TRAITER = new Set(['PartiallyFailed', 'Processing'])
