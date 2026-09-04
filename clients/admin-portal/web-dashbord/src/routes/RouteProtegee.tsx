import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import Chargement from '../components/Chargement'

/**
 * GARDE DE ROUTE.
 *
 * CE N'EST PAS UN CONTRÔLE D'ACCÈS. C'est du confort d'affichage : on évite de
 * montrer un écran qui rendra de toute façon des 401 et des 403. Quiconque
 * modifie le jeton dans son navigateur passe cette garde — et se heurte au
 * serveur, qui est l'autorité. Aucune décision de sécurité ne doit dépendre de
 * ce fichier.
 *
 * L'état « inconnu » NE REDIRIGE PAS. Rediriger pendant la restauration de la
 * session enverrait vers l'écran de connexion un utilisateur déjà connecté, et
 * lui ferait perdre la page qu'il demandait.
 */
export default function RouteProtegee() {
    const { etat } = useAuth()
    const emplacement = useLocation()

    if (etat.statut === 'inconnu') {
        return <Chargement message="Vérification de la session…" />
    }

    if (etat.statut === 'anonyme') {
        // `state.de` porte la destination demandée : après connexion, on y
        // revient au lieu de retomber bêtement sur l'accueil.
        return <Navigate to="/connexion" replace state={{ de: emplacement }} />
    }

    if (etat.statut === 'interdit') {
        return <Navigate to="/interdit" replace />
    }

    return <Outlet />
}
