import {
    useCallback,
    useEffect,
    useMemo,
    useState,
    type ReactNode,
} from 'react'
import { ROLE_REQUIS } from '../config/env'
import { brancherDeconnexion } from '../api/client'
import { ApiError } from '../api/errors'
import { lireJeton } from './jwt'
import { AuthContext, type Auth, type EtatAuth } from './context'
import { seConnecter, seDeconnecter } from './api'
import {
    effacerJetons,
    enregistrerJetons,
    lireJetonAcces,
    lireJetonRafraichissement,
} from './tokens'
import { requete } from '../api/client'
import type { AuthTokens } from './tokens'

/*
 * ÉTAT D'AUTHENTIFICATION DU PORTAIL.
 *
 * Les types et l'objet de contexte sont dans `./context` — voir l'encadré de ce
 * fichier pour la raison. Ici : la mécanique.
 *
 * L'état a QUATRE valeurs, pas deux. « connecté » et « déconnecté » ne
 * suffisent pas : au tout premier rendu on ne sait pas encore, parce qu'un
 * jeton de rafraîchissement peut dormir dans sessionStorage et qu'il faut un
 * aller-retour réseau pour savoir s'il vaut encore quelque chose.
 *
 * Confondre « on ne sait pas encore » avec « déconnecté » produit le défaut
 * classique : à chaque rechargement, l'utilisateur voit passer l'écran de
 * connexion une fraction de seconde avant d'être renvoyé où il était. Pire, une
 * garde de route qui redirige pendant cette fraction perd la page demandée.
 */

function classer(jetonBrut: string): EtatAuth {
    const contenu = lireJeton(jetonBrut)
    if (!contenu) return { statut: 'anonyme' }
    return contenu.roles.includes(ROLE_REQUIS)
        ? { statut: 'connecte', jeton: contenu }
        : { statut: 'interdit', jeton: contenu }
}

export function AuthProvider({ children }: { children: ReactNode }) {
    /*
     * L'ÉTAT INITIAL EST CALCULÉ, PAS CORRIGÉ APRÈS COUP.
     *
     * Première version : on partait toujours de « inconnu », puis un effet
     * basculait aussitôt en « anonyme » quand aucun jeton n'était stocké. Cela
     * provoquait un rendu supplémentaire dont le seul effet visible était un
     * passage par l'écran d'attente pour un visiteur qui n'a jamais eu de
     * session — et oxlint le signalait (`react(set-state-in-effect)`).
     *
     * S'il n'y a rien à restaurer, la réponse est connue dès le premier rendu.
     * L'effet ne sert plus qu'au cas qui demande vraiment un aller-retour.
     */
    const [etat, setEtat] = useState<EtatAuth>(() =>
        lireJetonRafraichissement() ? { statut: 'inconnu' } : { statut: 'anonyme' },
    )

    const oublier = useCallback(() => {
        effacerJetons()
        setEtat({ statut: 'anonyme' })
    }, [])

    // Le client HTTP ne connaît pas React. Quand il constate qu'une session est
    // définitivement perdue, il appelle ce rappel, qui est le seul chemin par
    // lequel l'interface passe en « anonyme ».
    useEffect(() => {
        brancherDeconnexion(oublier)
    }, [oublier])

    // RESTAURATION AU DÉMARRAGE.
    // Un rafraîchissement, une fois, au montage. S'il réussit on est connecté ;
    // s'il échoue on est anonyme. Dans les deux cas on quitte « inconnu », ce
    // qui débloque les gardes de route.
    useEffect(() => {
        const refresh = lireJetonRafraichissement()
        if (!refresh) return

        let annule = false
        void (async () => {
            try {
                const jetons = await requete<AuthTokens>('/api/v1/auth/refresh', {
                    methode: 'POST',
                    anonyme: true,
                    corps: { refreshToken: refresh },
                })
                if (annule) return
                enregistrerJetons(jetons)
                setEtat(classer(jetons.accessToken))
            } catch {
                if (annule) return
                effacerJetons()
                setEtat({ statut: 'anonyme' })
            }
        })()

        // `annule` protège du double montage de StrictMode et d'un démontage
        // pendant l'attente : sans lui, la réponse arrivée trop tard écrirait
        // dans un composant qui n'est plus là.
        return () => {
            annule = true
        }
    }, [])

    const connexion = useCallback(
        async (email: string, motDePasse: string, codeMfa?: string) => {
            const reponse = await seConnecter(email, motDePasse, codeMfa)

            // MFA DEMANDÉ : le serveur ne rend aucun jeton et attend qu'on
            // rappelle /login avec le code. Ce n'est pas une erreur, et le
            // traiter comme telle afficherait « identifiants invalides » à
            // quelqu'un qui vient de les saisir correctement.
            if (reponse.mfaRequired || !reponse.tokens) return true

            enregistrerJetons(reponse.tokens)
            setEtat(classer(reponse.tokens.accessToken))
            return false
        },
        [],
    )

    const deconnexion = useCallback(async () => {
        try {
            if (lireJetonAcces()) await seDeconnecter()
        } catch (cause) {
            // La révocation a échoué. On efface quand même côté navigateur :
            // laisser l'utilisateur connecté parce que le serveur n'a pas
            // répondu serait le pire des deux mondes. Le jeton de
            // rafraîchissement reste valide jusqu'à son expiration — c'est le
            // prix, et il vaut la peine d'être tracé.
            if (cause instanceof ApiError) {
                console.warn('révocation refusée par le serveur :', cause.messageLisible)
            }
        } finally {
            oublier()
        }
    }, [oublier])

    const valeur = useMemo<Auth>(
        () => ({ etat, connexion, deconnexion }),
        [etat, connexion, deconnexion],
    )

    return <AuthContext.Provider value={valeur}>{children}</AuthContext.Provider>
}
