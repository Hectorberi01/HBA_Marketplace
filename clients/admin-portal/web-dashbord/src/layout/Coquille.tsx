import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import SideBar from '../components/SideBar/SideBar'
import type { SidebarSection } from '../components/SideBar/SideBarType'
import { IconeAccueil, IconeColis, IconeReglages, IconeUtilisateurs, Logo } from '../components/Icones'
import { useAuth } from '../auth/useAuth'

/**
 * COQUILLE DE L'APPLICATION : barre latérale + zone de contenu.
 *
 * L'ÉLÉMENT ACTIF EST DÉRIVÉ DE L'URL, PAS D'UN `useState`.
 *
 * L'amorce gardait la sélection dans un état local. Trois conséquences : le
 * bouton « précédent » du navigateur ne changeait pas le menu, un lien copié
 * ramenait toujours sur l'accueil, et un rechargement remettait la sélection à
 * zéro alors que le contenu affiché, lui, venait de l'URL. Ici le chemin est la
 * seule source.
 */

type Entree = { id: string; chemin: string }

const ENTREES: Entree[] = [
    { id: 'accueil', chemin: '/' },
    { id: 'commandes', chemin: '/commandes' },
    { id: 'utilisateurs', chemin: '/utilisateurs' },
    { id: 'parametres', chemin: '/parametres' },
]

const SECTIONS: SidebarSection[] = [
    {
        items: [
            { id: 'accueil', label: 'Accueil', icon: <IconeAccueil /> },
            { id: 'commandes', label: 'Commandes', icon: <IconeColis /> },
            { id: 'utilisateurs', label: 'Utilisateurs', icon: <IconeUtilisateurs /> },
        ],
    },
    {
        title: 'Administration',
        items: [{ id: 'parametres', label: 'Paramètres', icon: <IconeReglages /> }],
    },
]

export default function Coquille() {
    const naviguer = useNavigate()
    const { pathname } = useLocation()
    const { etat, deconnexion } = useAuth()

    // Le plus long préfixe qui correspond : sans cela, `/commandes/42` ne
    // sélectionnerait rien, et `/` correspondrait à tout.
    const actif =
        [...ENTREES]
            .sort((a, b) => b.chemin.length - a.chemin.length)
            .find(e => (e.chemin === '/' ? pathname === '/' : pathname.startsWith(e.chemin)))?.id ??
        'accueil'

    const jeton = etat.statut === 'connecte' ? etat.jeton : null

    return (
        <div className="App">
            <SideBar
                sections={SECTIONS}
                activeId={actif}
                onSelect={id => {
                    const entree = ENTREES.find(e => e.id === id)
                    if (entree) naviguer(entree.chemin)
                }}
                brand={
                    <>
                        <Logo />
                        <span>HBAExpress</span>
                    </>
                }
                footer={
                    <div className="pied-lateral">
                        <span>{jeton?.email ?? jeton?.nom ?? 'Session'}</span>
                        <button type="button" className="lien-deconnexion" onClick={() => void deconnexion()}>
                            Se déconnecter
                        </button>
                    </div>
                }
            />
            <main className="app-main">
                <Outlet />
            </main>
        </div>
    )
}
