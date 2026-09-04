import { useQuery } from '@tanstack/react-query'
import { listerRoles, type Role } from './api'

/**
 * LES RÔLES SONT CHARGÉS UNE FOIS, ET PARTAGÉS.
 *
 * L'écran des utilisateurs en a besoin pour traduire `roleIds` — une liste de
 * GUID — en noms lisibles. L'écran des rôles affiche la même liste. Une clé de
 * requête commune suffit à ce que TanStack Query ne la charge qu'une fois et la
 * serve aux deux.
 *
 * `staleTime` long ET ASSUMÉ : les rôles d'une plateforme changent une fois par
 * trimestre, pas une fois par minute. Les recharger à chaque montage
 * multiplierait les appels sans qu'aucun n'apprenne rien de neuf. La contrepartie
 * est qu'un rôle créé à l'instant dans un autre onglet n'apparaît pas tout de
 * suite — d'où le rechargement explicite offert sur l'écran des rôles.
 */
export function useRoles() {
    return useQuery({
        queryKey: ['roles'],
        queryFn: ({ signal }) => listerRoles(signal),
        staleTime: 5 * 60_000,
    })
}

/**
 * Index id -> rôle.
 *
 * Un identifiant ABSENT de l'index est rendu abrégé plutôt que masqué : il
 * signifie qu'un compte porte un rôle qui n'existe plus dans la table, et c'est
 * exactement le genre d'incohérence qu'une console doit montrer, pas absorber.
 */
export function indexerRoles(roles: Role[] | undefined): Map<string, Role> {
    const carte = new Map<string, Role>()
    for (const r of roles ?? []) carte.set(r.id, r)
    return carte
}
