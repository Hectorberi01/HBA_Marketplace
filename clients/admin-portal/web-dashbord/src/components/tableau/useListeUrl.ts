import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'

/**
 * L'ÉTAT DE LA LISTE VIT DANS L'URL, PAS DANS `useState`.
 *
 * Recherche, statut, tri, page et taille de page sont des paramètres de
 * requête. Quatre choses en découlent, et aucune n'est gratuite autrement :
 *
 *   — le bouton « précédent » du navigateur défait le dernier filtre, au lieu
 *     de quitter l'écran ;
 *   — un lien copié rouvre exactement la même vue chez un collègue, ce qui est
 *     tout l'intérêt d'une console d'exploitation à plusieurs ;
 *   — un rechargement ne perd rien ;
 *   — TanStack Query voit changer sa clé de requête sans qu'on ait à la
 *     synchroniser à la main avec un état local.
 *
 * `replace` sur les filtres, PAS sur la pagination : taper « samsu » entrée par
 * entrée créerait cinq entrées d'historique, et cinq retours en arrière pour
 * revenir à l'écran précédent. Changer de page, en revanche, est un geste
 * délibéré qu'on veut pouvoir défaire.
 */

export type EtatListe = {
    recherche: string
    statut: string | null
    tri: string | null
    sens: 'asc' | 'desc'
    page: number
    taille: number
}

const TAILLE_DEFAUT = 20

export function useListeUrl(triDefaut: string) {
    const [params, setParams] = useSearchParams()

    const etat: EtatListe = useMemo(() => {
        const page = Number(params.get('page') ?? '1')
        const taille = Number(params.get('taille') ?? String(TAILLE_DEFAUT))
        return {
            recherche: params.get('q') ?? '',
            statut: params.get('statut'),
            tri: params.get('tri') ?? triDefaut,
            sens: params.get('sens') === 'asc' ? 'asc' : 'desc',
            // Une URL bricolée à la main peut porter `page=0` ou `page=abc`.
            // Sans ces gardes, la requête partirait avec une valeur que le
            // serveur refuserait, et l'écran afficherait une erreur de
            // validation pour un paramètre que l'utilisateur n'a jamais saisi.
            page: Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1,
            taille: [20, 50, 100].includes(taille) ? taille : TAILLE_DEFAUT,
        }
    }, [params, triDefaut])

    const modifier = useCallback(
        (bribe: Partial<EtatListe>, options?: { remplacer?: boolean }) => {
            const suivant = new URLSearchParams(params)

            const poser = (cle: string, valeur: string | null | undefined) => {
                if (valeur === null || valeur === undefined || valeur === '') suivant.delete(cle)
                else suivant.set(cle, valeur)
            }

            if ('recherche' in bribe) poser('q', bribe.recherche)
            if ('statut' in bribe) poser('statut', bribe.statut)
            if ('tri' in bribe) poser('tri', bribe.tri)
            if ('sens' in bribe) poser('sens', bribe.sens)
            if ('taille' in bribe) poser('taille', bribe.taille ? String(bribe.taille) : null)

            // TOUT CHANGEMENT DE FILTRE REMET À LA PREMIÈRE PAGE.
            // Sans cela, filtrer depuis la page 4 d'une liste qui n'a plus que
            // deux pages rend un tableau vide, et l'écran dit « aucun résultat »
            // alors qu'il y en a — le filtre est accusé à tort.
            const filtreChange =
                'recherche' in bribe || 'statut' in bribe || 'tri' in bribe ||
                'sens' in bribe || 'taille' in bribe
            if ('page' in bribe) poser('page', bribe.page ? String(bribe.page) : null)
            else if (filtreChange) suivant.delete('page')

            setParams(suivant, { replace: options?.remplacer ?? filtreChange })
        },
        [params, setParams],
    )

    return { etat, modifier }
}
