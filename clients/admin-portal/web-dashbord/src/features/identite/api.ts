import { requete } from '../../api/client'
import { lirePage, versQuery, type Page } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * IDENTITÉ — utilisateurs et rôles (identity-service).
 *
 *   GET /api/identity/users   paginé, enveloppé, avec facettes par statut
 *   GET /api/identity/roles   liste NUE, sans pagination
 *
 * DEUX ENDPOINTS VOISINS, DEUX FORMES. Encore. `ListUsersAsync` rend
 * `ApiResults.Page(...)`, `ListRolesAsync` rend `Results.Ok(roles)` — le module
 * `api/pages` absorbe la première, la seconde se lit telle quelle.
 *
 * LE STATUT DE L'UTILISATEUR EST UNE CHAÎNE, contrairement à celui des retours :
 * `ListUsersQuery` fait `u.Status.ToString()` avant de construire le DTO. Le
 * défaut d'énumération sérialisée en nombre ne touche donc pas cet écran.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Utilisateur = {
    id: string
    firstName: string
    lastName: string
    email: string
    phoneNumber: string
    status: string
    emailVerified: boolean
    mfaEnabled: boolean
    roleIds: string[]
    acceptedTermsVersion?: string | null
    acceptedTermsOnUtc?: string | null
    /**
     * Renseignée = l'adresse a été marquée vérifiée PAR UN ADMINISTRATEUR, sur
     * attestation, et non par le titulaire cliquant un lien. Le contrat le dit
     * explicitement : « la console doit pouvoir le montrer — Oui et Oui, sur
     * parole ne valent pas la même chose ».
     */
    emailVerifiedByAdminOnUtc?: string | null
}

export type Role = {
    id: string
    name: string
    description?: string | null
    /** Rôle du socle : il ne se supprime pas. */
    isSystem: boolean
    permissions: string[]
}

export type FiltreUtilisateurs = {
    page: number
    taille: number
    recherche?: string
    statut?: string | null
    tri?: string | null
    sens?: string | null
}

export function listerUtilisateurs(
    filtre: FiltreUtilisateurs,
    signal?: AbortSignal,
): Promise<Page<Utilisateur>> {
    const query = versQuery({
        page: filtre.page,
        pageSize: filtre.taille,
        search: filtre.recherche,
        status: filtre.statut,
        sort: filtre.tri,
        dir: filtre.sens,
    })
    return requete<unknown>(`/api/identity/users${query}`, { signal }).then(corps =>
        lirePage<Utilisateur>(corps, filtre.page, filtre.taille),
    )
}

export function listerRoles(signal?: AbortSignal): Promise<Role[]> {
    return requete<Role[]>('/api/identity/roles', { signal })
}

/** `UserStatus` — état du compte. */
const STATUTS: Record<string, string> = {
    PendingVerification: 'À vérifier',
    Active: 'Actif',
    Suspended: 'Suspendu',
    Deleted: 'Supprimé',
}

export function libelleStatutUtilisateur(statut: string): string {
    return STATUTS[statut] ?? statut
}

export const STATUTS_A_TRAITER = new Set(['PendingVerification'])
