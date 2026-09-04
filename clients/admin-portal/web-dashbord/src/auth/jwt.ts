/**
 * LECTURE DU JETON, POUR L'AFFICHAGE SEULEMENT.
 *
 * CE MODULE NE VÉRIFIE RIEN, ET NE PEUT RIEN VÉRIFIER.
 *
 * La signature d'un JWT HS256 se vérifie avec la clé secrète du serveur. Cette
 * clé n'est pas — et ne doit jamais être — dans le navigateur. Tout ce que fait
 * ce fichier, c'est décoder une charge utile que l'utilisateur pourrait
 * fabriquer lui-même.
 *
 * Ce qu'on en tire sert donc UNIQUEMENT à décider quoi montrer : masquer une
 * entrée de menu, afficher un nom. L'autorité reste le serveur, qui rend 401 ou
 * 403 quel que soit ce que le portail a cru lire. Un menu masqué n'est pas un
 * contrôle d'accès.
 */

/**
 * ASP.NET écrit les rôles sous l'URI longue de `ClaimTypes.Role`. Certaines
 * configurations les réécrivent en `role` court. On accepte les deux : deviner
 * lequel est en vigueur depuis le navigateur n'est pas possible, et se tromper
 * viderait silencieusement la liste des rôles.
 */
const CLES_ROLE = [
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
    'role',
    'roles',
]

const CLES_NOM = [
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
    'name',
    'unique_name',
]

const CLES_EMAIL = [
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress',
    'email',
]

export type ContenuJeton = {
    sujet: string | null
    nom: string | null
    email: string | null
    roles: string[]
    permissions: string[]
    expireLe: number | null
}

function decoderCharge(jeton: string): Record<string, unknown> | null {
    const parties = jeton.split('.')
    if (parties.length !== 3) return null
    try {
        // base64url -> base64, puis décodage en UTF-8. `atob` seul rend des
        // octets latin-1 : un nom accentué ressortirait en mojibake.
        const b64 = parties[1].replace(/-/g, '+').replace(/_/g, '/')
        const octets = Uint8Array.from(atob(b64.padEnd(Math.ceil(b64.length / 4) * 4, '=')), c =>
            c.charCodeAt(0),
        )
        return JSON.parse(new TextDecoder().decode(octets)) as Record<string, unknown>
    } catch {
        return null
    }
}

/**
 * Un claim répété devient un tableau, un claim unique reste une chaîne. Les
 * deux formes arrivent selon le nombre de rôles portés par le compte.
 */
function versListe(valeur: unknown): string[] {
    if (typeof valeur === 'string') return [valeur]
    if (Array.isArray(valeur)) return valeur.filter((v): v is string => typeof v === 'string')
    return []
}

function premier(charge: Record<string, unknown>, cles: string[]): string | null {
    for (const cle of cles) {
        const v = charge[cle]
        if (typeof v === 'string' && v !== '') return v
    }
    return null
}

export function lireJeton(jeton: string): ContenuJeton | null {
    const charge = decoderCharge(jeton)
    if (!charge) return null

    const roles = CLES_ROLE.flatMap(cle => versListe(charge[cle]))
    const exp = typeof charge.exp === 'number' ? charge.exp * 1000 : null

    return {
        sujet: premier(charge, ['sub', 'nameid']),
        nom: premier(charge, CLES_NOM),
        email: premier(charge, CLES_EMAIL),
        roles: [...new Set(roles)],
        permissions: versListe(charge.permission),
        expireLe: exp,
    }
}
