import { createContext } from 'react'
import type { ContenuJeton } from './jwt'

/**
 * LE CONTEXTE VIT DANS SON PROPRE FICHIER, SANS COMPOSANT.
 *
 * Le rafraîchissement à chaud de Vite ne sait remplacer un module que si celui
 * -ci n'exporte QUE des composants. Un fichier qui exporte à la fois
 * `AuthProvider` et l'objet de contexte force un rechargement complet de la
 * page à chaque édition — et, la page rechargée, la session repart de zéro.
 * L'inconfort tombe précisément sur le code qu'on est en train d'écrire.
 */

export type EtatAuth =
    | { statut: 'inconnu' }
    | { statut: 'anonyme' }
    | { statut: 'connecte'; jeton: ContenuJeton }
    /** Authentifié, mais sans le rôle exigé pour ce portail. */
    | { statut: 'interdit'; jeton: ContenuJeton }

export type Auth = {
    etat: EtatAuth
    /** Rend `true` si un code MFA est encore attendu. */
    connexion: (email: string, motDePasse: string, codeMfa?: string) => Promise<boolean>
    deconnexion: () => Promise<void>
}

export const AuthContext = createContext<Auth | null>(null)
