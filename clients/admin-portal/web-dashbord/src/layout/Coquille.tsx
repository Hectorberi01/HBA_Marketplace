import { Outlet } from 'react-router-dom'
import SideBar from '../components/SideBar/SideBar'
import { Logo } from '../components/Icones'
import { NAVIGATION } from './navigation'
import { useAuth } from '../auth/useAuth'

/**
 * COQUILLE DE L'APPLICATION : barre latérale + zone de contenu.
 *
 * ELLE NE CALCULE PLUS L'ÉLÉMENT ACTIF.
 *
 * Les deux versions précédentes s'en chargeaient : d'abord un `useState`, qui
 * ignorait le bouton « précédent » du navigateur, puis un calcul de plus long
 * préfixe à partir du chemin. Le second était juste, mais il dupliquait ce que
 * `NavLink` fait déjà — et un jour les deux auraient divergé.
 */
export default function Coquille() {
    const { etat, deconnexion } = useAuth()
    const jeton = etat.statut === 'connecte' ? etat.jeton : null

    return (
        <div className="App">
            <SideBar
                sections={NAVIGATION}
                brand={
                    <>
                        <Logo />
                        <span>HBAExpress</span>
                    </>
                }
                footer={
                    <div className="pied-lateral">
                        <span title={jeton?.email ?? undefined}>
                            {jeton?.email ?? jeton?.nom ?? 'Session'}
                        </span>
                        <button
                            type="button"
                            className="lien-deconnexion"
                            onClick={() => void deconnexion()}
                        >
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
