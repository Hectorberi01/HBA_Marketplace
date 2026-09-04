import { requete } from '../../api/client'
import { lireListe, versQuery } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * RESTAURATION — ce que l'administration peut lire, et ce qu'elle ne peut pas.
 *
 * ÉTABLISSEMENTS : `GET /api/food/admin/restaurants/pending?take=N`
 *
 * C'EST LA SEULE LECTURE ADMIN DU SERVICE, et elle ne rend QUE les dossiers en
 * attente. Il n'existe aucune route qui liste les restaurants en activité,
 * suspendus ou refusés : `MapAdminGroup("/api/food/admin")` monte une lecture et
 * quatre écritures, rien d'autre. Cet écran est donc une FILE DE VALIDATION, pas
 * un annuaire — et il le dit, plutôt que de laisser croire que la plateforme
 * compte trois restaurants.
 *
 * COMMANDES REPAS : AUCUNE LECTURE ADMIN N'EXISTE.
 *
 * `MapAdminGroup("/api/admin/food/orders")` ne monte que deux POST — reprendre
 * ou rembourser une commande en arbitrage. Aucun GET. Et les trois requêtes de
 * l'application sont toutes portées : par identifiant de commande, par acheteur,
 * par restaurant. Aucune ne liste l'ensemble.
 *
 * La route voisine `/api/food/restaurant/orders` ne comble pas le manque : son
 * gestionnaire appelle `GetStaffMembershipAsync(userId)` et rend 403 si le
 * compte n'appartient au personnel d'aucun établissement. Un administrateur
 * n'en fait partie d'aucun.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type HoraireService = {
    day: string
    opensAt: string
    closesAt: string
}

export type Etablissement = {
    id: string
    ownerUserId: string
    name: string
    description?: string | null
    logoMediaId?: string | null
    coverMediaId?: string | null
    legacyLogoUrl?: string | null
    phone: string
    status: string
    /** Calculé, pas stocké : le service s'en sert pour refuser une commande. */
    acceptsOrdersNow: boolean
    blockedReason: string
    preparationMinutes: number
    acceptanceMode: string
    minimumOrderAmount?: number | null
    loadLevel: string
    extraWaitMinutes: number
    specialClosureReason?: string | null
    /** Point d'expédition : sans lui, aucun livreur ne sait où retirer. */
    fulfillmentLocationId?: string | null
    /** Dossier vendeur qui encaisse : sans lui, l'établissement vend sans être payé. */
    payoutSellerId?: string | null
    serviceHours: HoraireService[]
    isPubliclyVisible: boolean
}

export function listerEnAttente(take: number, signal?: AbortSignal): Promise<Etablissement[]> {
    const query = versQuery({ take })
    return requete<unknown>(`/api/food/admin/restaurants/pending${query}`, { signal }).then(corps =>
        lireListe<Etablissement>(corps),
    )
}

/** `RestaurantStatus`. */
const STATUTS: Record<string, string> = {
    Draft: 'Brouillon',
    PendingApproval: 'À valider',
    Active: 'En activité',
    Suspended: 'Suspendu',
    Rejected: 'Refusé',
    Closed: 'Fermé',
}

export function libelleStatutEtablissement(s: string): string {
    return STATUTS[s] ?? s
}

const JOURS: Record<string, string> = {
    Monday: 'lun',
    Tuesday: 'mar',
    Wednesday: 'mer',
    Thursday: 'jeu',
    Friday: 'ven',
    Saturday: 'sam',
    Sunday: 'dim',
}

export function libelleJour(j: string): string {
    return JOURS[j] ?? j
}
