import { requete } from '../../api/client'
import { lireListe, lirePage, versQuery, type Page } from '../../api/pages'

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

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * LES OFFRES D'UNE BOUTIQUE — CE QUI EST RÉELLEMENT EN VENTE.
 *
 * UNE FICHE PRODUIT N'EST PAS UNE MISE EN VENTE, ET LA CONFONDRE FAUSSE TOUT
 *     CE QU'ON CROIT COMPTER.
 *
 * Le produit décrit un article ; l'OFFRE le met en vente dans UNE boutique, à
 * un prix, dans un état, avec un délai de préparation. Un produit approuvé sans
 * offre n'est vendu nulle part ; une boutique peut porter plusieurs offres du
 * même produit — une par variante. C'est donc l'offre, et non le produit, qui
 * répond à « que vend cette boutique ».
 *
 * `GET /api/v1/catalog/seller/stores/{storeId}/offers` EST OUVERTE À
 *     L'ADMINISTRATION.
 *
 * Le groupe est `MapSellerGroup`, et le handler garde la boutique par
 * appartenance — « sinon la liste des mises en vente d'un concurrent serait
 * lisible avec un seul identifiant ». La garde commence par `if (!IsAdmin(user))` :
 * la console passe, un vendeur ne voit que sa boutique.
 *
 * `ListStoreOffersQuery(storeId)` NE PREND NI PAGE NI BORNE. La liste rendue
 * est complète. C'est ce qui autorise à en tirer des statistiques exactes plutôt
 * qu'un « au moins N » — et c'est aussi ce qui la rendra lourde sur une très
 * grosse boutique, sans pagination pour s'en protéger.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Offre = {
    id: string
    productId: string
    productName: string
    variantId: string
    sku?: string | null
    storeId: string
    sellerId: string
    /** Ce que le vendeur touche avant commission. */
    sellerPrice: number
    /** Ce que l'acheteur paie hors promotion. */
    buyerPrice: number
    promotionalPrice?: number | null
    /** Le prix qui s'applique aujourd'hui — promotion comprise. */
    effectivePrice: number
    promotionEndsOnUtc?: string | null
    commissionAmount: number
    providerFeeAmount: number
    currency: string
    status: string
    statusReason?: string | null
    condition: string
    handlingTimeDays: number
}

export function listerOffresBoutique(storeId: string, signal?: AbortSignal): Promise<Offre[]> {
    return requete<unknown>(`/api/v1/catalog/seller/stores/${storeId}/offers`, { signal }).then(
        corps => lireListe<Offre>(corps),
    )
}

/** `OfferStatus`, tel que le domaine le nomme. */
const STATUTS_OFFRE: Record<string, string> = {
    Draft: 'Brouillon',
    Active: 'En vente',
    Paused: 'En pause',
    OutOfStock: 'Rupture',
    Suspended: 'Suspendue',
    Archived: 'Archivée',
}

export function libelleStatutOffre(statut: string): string {
    return STATUTS_OFFRE[statut] ?? statut
}

const CONDITIONS: Record<string, string> = {
    New: 'Neuf',
    Used: 'Occasion',
    Refurbished: 'Reconditionné',
}

export function libelleCondition(condition: string): string {
    return CONDITIONS[condition] ?? condition
}

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * VALIDATION DES FICHES (§16) — SIX ROUTES QUE PERSONNE N'APPELAIT.
 *
 * `Product.Approve`, `Reject`, `Suspend` et `Restore` existent depuis le lot 1,
 * testés, et le service le dit lui-même : « appelés par personne. Le parcours du
 * §28 s'arrêtait à l'étape 4 ». Une fiche soumise ne pouvait donc jamais être
 * approuvée autrement qu'en base.
 *
 * LE RELECTEUR VIENT DU JETON, JAMAIS DU CORPS. Le service refuse un
 * `reviewerId` fourni par l'appelant : « un relecteur pris dans la requête
 * permettrait d'attribuer sa propre approbation à quelqu'un d'autre. Le journal
 * `product_reviews` n'aurait alors plus aucune valeur d'audit ». Le portail
 * n'envoie donc que le commentaire et les motifs.
 *
 * LE REFUS EXIGE AU MOINS UN MOTIF. `RejectRequest.Reasons` est nullable dans le
 * contrat, mais l'agrégat rend `catalog.review.reason_required` sur une liste
 * vide — le nullable sert à rendre une erreur lisible, pas à autoriser le vide.
 * L'écran l'impose avant l'envoi : « un vendeur qui apprend que sa fiche est
 * refusée sans savoir quoi corriger resoumet à l'identique ».
 * ═══════════════════════════════════════════════════════════════════════════
 */

/** Un motif de refus. `code` est libre côté contrat ; `field` désigne le champ visé. */
export type MotifRefus = {
    code: string
    field?: string | null
    message: string
}

/**
 * Motifs proposés par l'écran.
 *
 * CE VOCABULAIRE EST CELUI DU PORTAIL, PAS CELUI DU SERVICE. `MotifSaisi(Code,
 * Field, Message)` accepte n'importe quelle chaîne : aucune énumération ne
 * l'encadre côté serveur. Figer une liste ici rend les refus comparables entre
 * eux — sans quoi chaque relecteur écrit son propre code et le journal d'audit
 * devient illisible. Le champ libre reste possible.
 */
export const MOTIFS_REFUS: MotifRefus[] = [
    { code: 'IMAGE_MANQUANTE', field: 'media', message: 'Aucune image utilisable.' },
    { code: 'IMAGE_QUALITE', field: 'media', message: 'Images de qualité insuffisante.' },
    { code: 'TITRE', field: 'name', message: 'Le titre ne décrit pas le produit.' },
    { code: 'DESCRIPTION', field: 'description', message: 'Description absente ou hors sujet.' },
    { code: 'CATEGORIE', field: 'categoryId', message: 'Catégorie inadaptée.' },
    { code: 'MARQUE', field: 'brandId', message: 'Marque absente ou non reconnue.' },
    { code: 'PRIX', field: 'variants', message: 'Prix manifestement erroné.' },
    { code: 'INTERDIT', field: null, message: 'Article interdit à la vente sur la plateforme.' },
    { code: 'CONTREFACON', field: null, message: 'Soupçon de contrefaçon.' },
]

export function listerFichesAValider(
    page: number,
    taille: number,
    signal?: AbortSignal,
): Promise<Page<Produit>> {
    const query = versQuery({ page, pageSize: taille })
    return requete<unknown>(`/api/v1/catalog/admin/products/reviews${query}`, { signal }).then(
        corps => lirePage<Produit>(corps, page, taille),
    )
}

/** Une décision rendue — `ProductReviewSummary`. */
export type Decision = {
    id: string
    productId: string
    revisionId: string
    /**
     * LE NUMÉRO DE VERSION N'EST PAS DÉCORATIF : il dit si les motifs portent sur
     * ce qu'on voit à l'écran ou sur ce que le vendeur avait soumis avant de
     * corriger.
     */
    revisionVersion: number
    sellerId: string
    reviewedBy: string
    decision: string
    comment?: string | null
    reviewedAtUtc: string
    reasons: MotifRefus[]
}

export function lireDecisions(productId: string, signal?: AbortSignal): Promise<Decision[]> {
    return requete<unknown>(`/api/v1/catalog/admin/products/${productId}/review`, { signal }).then(
        corps => lireListe<Decision>(corps),
    )
}

export const approuverFiche = (productId: string, commentaire?: string) =>
    requete<void>(`/api/v1/catalog/admin/products/${productId}/approve`, {
        methode: 'POST',
        corps: { comment: commentaire || null },
    })

export const refuserFiche = (productId: string, motifs: MotifRefus[], commentaire?: string) =>
    requete<void>(`/api/v1/catalog/admin/products/${productId}/reject`, {
        methode: 'POST',
        corps: { comment: commentaire || null, reasons: motifs },
    })

export const suspendreFiche = (productId: string, motif?: string) =>
    requete<void>(`/api/v1/catalog/admin/products/${productId}/suspend`, {
        methode: 'POST',
        corps: { reason: motif || null },
    })

export const retablirFiche = (productId: string) =>
    requete<void>(`/api/v1/catalog/admin/products/${productId}/restore`, { methode: 'POST' })
