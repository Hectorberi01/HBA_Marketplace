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

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * CRÉER UN COMPTE — ET LE CHEMIN QUE L'API IMPOSE.
 *
 * IL N'Y A AUCUNE ROUTE D'ADMINISTRATION POUR CRÉER UN UTILISATEUR.
 *
 * `MapAdminGroup("/api/identity/users")` monte six routes : lister, lire,
 * suspendre, réactiver, attribuer un rôle, retirer un rôle. Pas de POST « / ».
 * La seule création possible est `POST /api/v1/auth/register`, c'est-à-dire
 * l'inscription publique, `AllowAnonymous`.
 *
 * TROIS CONSÉQUENCES, TOUTES VISIBLES À L'ÉCRAN.
 *
 * 1. Le compte naît exactement comme si la personne s'était inscrite : statut
 *    `PendingVerification`, courriel de vérification envoyé. L'administrateur ne
 *    crée pas un compte « déjà validé ».
 *
 * 2. IL FAUT FOURNIR UN MOT DE PASSE POUR QUELQU'UN D'AUTRE. Le contrat l'exige
 *    et il n'existe pas de variante sans. Le portail en engendre donc un au
 *    hasard plutôt que de laisser l'administrateur en choisir un — voir
 *    `engendrerMotDePasse` — et propose aussitôt d'envoyer un lien de
 *    réinitialisation pour que la personne pose le sien.
 *
 * 3. LE RÔLE SE POSE EN UN SECOND APPEL. `register` n'en accepte aucun ; il faut
 *    ensuite `POST /api/identity/users/{id}/roles`. Deux appels, donc un échec
 *    partiel possible : compte créé, rôle absent. L'écran le dit précisément au
 *    lieu de rendre une erreur générale sur une opération à moitié faite.
 *
 * La route d'inscription porte `RequireRateLimiting(AuthRateLimiter.PolicyName)`
 * : créer plusieurs comptes d'affilée peut se heurter à la limite de débit, et
 * ce n'est pas un défaut du formulaire.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type NouveauCompte = {
    firstName: string
    lastName: string
    email: string
    phoneNumber: string
    password: string
}

export function creerCompte(compte: NouveauCompte): Promise<{ id: string }> {
    return requete<{ id: string }>('/api/v1/auth/register', {
        methode: 'POST',
        corps: compte,
    })
}

export function attribuerRole(userId: string, roleId: string): Promise<void> {
    return requete<void>(`/api/identity/users/${userId}/roles`, {
        methode: 'POST',
        corps: { roleId },
    })
}

/**
 * Envoie un lien de réinitialisation à l'adresse indiquée.
 *
 * C'EST CE QUI RATTRAPE LE MOT DE PASSE IMPOSÉ. Tant que la personne n'a pas
 * posé le sien, celui que le portail a engendré reste valide et a été affiché à
 * l'écran de quelqu'un d'autre.
 */
export function envoyerReinitialisation(email: string): Promise<void> {
    return requete<void>('/api/v1/auth/password/forgot', {
        methode: 'POST',
        anonyme: true,
        corps: { email },
    })
}

/**
 * MOT DE PASSE ENGENDRÉ AU HASARD, PAR `crypto.getRandomValues`.
 *
 * `Math.random` N'EST PAS UN GÉNÉRATEUR CRYPTOGRAPHIQUE : sa suite est
 * prédictible à partir de quelques tirages. Pour un mot de passe d'ouverture de
 * compte — même destiné à être remplacé — cela suffirait à le deviner.
 *
 * L'alphabet exclut les caractères qui se confondent à l'oral et à la lecture —
 * O et 0, l et 1, I — parce que ce mot de passe sera lu à voix haute ou recopié
 * à la main au moins une fois. La longueur (16) compense largement l'alphabet
 * réduit.
 *
 * Le service exige au minimum huit caractères (`RegisterUserCommandValidator`).
 */
const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'

export function engendrerMotDePasse(longueur = 16): string {
    const octets = new Uint32Array(longueur)
    crypto.getRandomValues(octets)
    let sortie = ''
    for (const n of octets) sortie += ALPHABET[n % ALPHABET.length]
    return sortie
}
