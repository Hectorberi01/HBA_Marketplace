import { requete } from '../../api/client'
import { lireDonnee, lireListe, lirePage, versQuery, type Page } from '../../api/pages'

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

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * LA FICHE D'UN VENDEUR, SES BOUTIQUES, ET LES GESTES DE GOUVERNANCE.
 *
 * L'ADMINISTRATEUR PASSE LES GARDES DE PROPRIÉTÉ, ET CE N'EST PAS UN
 *     CONTOURNEMENT.
 *
 * `GET /api/v1/merchants/{sellerId}` et le groupe `/stores` sont montés par
 * `MapSellerGroup` — « admet Seller, Admin et Moderator ». La garde interne
 * `DenyUnlessOwnSellerAsync` rend `null` d'emblée sur `IsInRole(AdminRole)` :
 * la console lit donc la fiche de n'importe quel vendeur sans se faire passer
 * pour lui. C'est écrit dans le service, pas déduit de son nom.
 *
 * LA FICHE PORTE CE QUE LA LISTE REFUSE DE PORTER.
 *
 * `SellerListItem` omet délibérément le compte de retrait, le RCCM, l'IFU et le
 * téléphone du gérant — « une console a le droit d'afficher ces données sur la
 * fiche qu'un humain ouvre, pas dans un listing qu'un écran charge au réveil ».
 * `SellerDetail` les porte. L'écran de fiche est donc exactement l'endroit
 * prévu pour eux, et le listing reste sobre.
 *
 * LES BOUTIQUES ARRIVENT DEUX FOIS, ET ON NE LIT QUE L'UNE.
 *
 * `SellerDetail.stores` porte déjà la liste, et `GET .../stores` la rend aussi.
 * On se contente de la première : un second appel donnerait deux vérités à
 * afficher, qui divergeraient le temps d'un rafraîchissement.
 *
 * AUCUNE DE CES DEUX LECTURES N'EST PAGINÉE. `ListSellerStoresQuery(sellerId)`
 * ne prend ni page ni borne : la liste est complète, et un vendeur en a
 * quelques-unes. Rien à compter « au moins ».
 * ═══════════════════════════════════════════════════════════════════════════
 */

/** Une plage d'ouverture, telle que la rend `StoreOpeningHourSummary`. */
export type PlageOuverture = {
    dayOfWeek: number | string
    opensAt?: string | null
    closesAt?: string | null
    isClosed?: boolean
}

export type Boutique = {
    id: string
    sellerId: string
    name: string
    logoUrl?: string | null
    description?: string | null
    contactPhone: string
    contactEmail?: string | null
    status: string
    /** La boutique vend-elle en ce moment — ouverte ET non suspendue. */
    isSelling: boolean
    fulfillmentLocationId?: string | null
    statusReason?: string | null
    openingHours: PlageOuverture[]
    createdOnUtc: string
}

/** Un document de dossier KYB. Le contenu n'est pas exposé, seulement sa nature. */
export type DocumentKyb = {
    id: string
    type?: string | null
    fileName?: string | null
    url?: string | null
    uploadedOnUtc?: string | null
}

/** `PayoutAccountSummary` — le compte de reversement. */
export type CompteReversement = {
    provider?: string | null
    accountNumber?: string | null
    accountName?: string | null
    bankName?: string | null
    currency?: string | null
}

/** `SellerCompanyInfoSummary` — les mentions légales de l'entreprise. */
export type InfosEntreprise = {
    legalName?: string | null
    rccm?: string | null
    ifu?: string | null
    taxId?: string | null
    managerPhone?: string | null
    addressLine?: string | null
    city?: string | null
}

/** `SellerDetail` : les huit champs du résumé, à plat, plus la vue riche. */
export type VendeurDetail = Vendeur & {
    description?: string | null
    /** FRACTION, pas pourcentage — voir `formaterTaux`. */
    commissionRate: number
    rating: number
    salesCount: number
    payout?: CompteReversement | null
    kybDocuments: DocumentKyb[]
    metadata?: InfosEntreprise | null
    stores: Boutique[]
}

export function lireVendeur(sellerId: string, signal?: AbortSignal): Promise<VendeurDetail> {
    return requete<unknown>(`/api/v1/merchants/${sellerId}`, { signal }).then(corps =>
        lireDonnee<VendeurDetail>(corps),
    )
}

export function listerBoutiques(sellerId: string, signal?: AbortSignal): Promise<Boutique[]> {
    return requete<unknown>(`/api/v1/merchants/${sellerId}/stores`, { signal }).then(corps =>
        lireListe<Boutique>(corps),
    )
}

export function lireBoutique(
    sellerId: string,
    storeId: string,
    signal?: AbortSignal,
): Promise<Boutique> {
    return requete<unknown>(`/api/v1/merchants/${sellerId}/stores/${storeId}`, { signal }).then(
        corps => lireDonnee<Boutique>(corps),
    )
}

/** `StoreStatus` — état de la vitrine, distinct de l'état du compte vendeur. */
const STATUTS_BOUTIQUE: Record<string, string> = {
    Draft: 'Brouillon',
    Open: 'Ouverte',
    Closed: 'Fermée',
    Suspended: 'Suspendue',
}

export function libelleStatutBoutique(statut: string): string {
    return STATUTS_BOUTIQUE[statut] ?? statut
}

/*
 * ─────────────────────────────────────────────────────────────────────────────
 * LES GESTES. TOUS RENDENT 204, AUCUN NE REND L'ÉTAT D'APRÈS.
 *
 * L'écran doit donc réinvalider la fiche après chaque geste plutôt que de
 * deviner le nouvel état. Deviner marcherait onze fois sur douze et afficherait
 * un état faux la douzième — quand le domaine refuse une transition, ou en
 * impose une autre que celle qu'on croyait déclencher.
 *
 * LE MOTIF EST OBLIGATOIRE SUR LES REFUS ET LES SUSPENSIONS. `ReasonRequest`
 * l'accepte nullable pour rendre une erreur lisible plutôt qu'un 400 sur corps
 * mal formé ; les agrégats, eux, refusent le vide. L'écran l'exige donc avant
 * d'envoyer : faire découvrir la contrainte par un 422 serait un aller-retour
 * pour rien.
 * ─────────────────────────────────────────────────────────────────────────────
 */

function poster(chemin: string, corps?: unknown): Promise<void> {
    return requete<void>(chemin, { methode: 'POST', corps })
}

export const approuverKyb = (sellerId: string) =>
    poster(`/api/v1/merchants/${sellerId}/kyb/approve`)

export const refuserKyb = (sellerId: string, motif: string) =>
    poster(`/api/v1/merchants/${sellerId}/kyb/reject`, { reason: motif })

export const activerVendeur = (sellerId: string) =>
    poster(`/api/v1/merchants/${sellerId}/activate`)

export const suspendreVendeur = (sellerId: string, motif: string) =>
    poster(`/api/v1/merchants/${sellerId}/suspend`, { reason: motif })

export const leverSuspensionVendeur = (sellerId: string) =>
    poster(`/api/v1/merchants/${sellerId}/lift-suspension`)

export const approuverReactivation = (sellerId: string) =>
    poster(`/api/v1/merchants/${sellerId}/reactivation/approve`)

export const suspendreBoutique = (sellerId: string, storeId: string, motif: string) =>
    poster(`/api/v1/merchants/${sellerId}/stores/${storeId}/suspend`, { reason: motif })

export const leverSuspensionBoutique = (sellerId: string, storeId: string) =>
    poster(`/api/v1/merchants/${sellerId}/stores/${storeId}/lift-suspension`)
