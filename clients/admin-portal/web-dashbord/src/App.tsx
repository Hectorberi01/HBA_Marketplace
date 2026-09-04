import { Route, Routes } from 'react-router-dom'
import Coquille from './layout/Coquille'
import RouteProtegee from './routes/RouteProtegee'
import ConnexionPage from './pages/ConnexionPage'
import InterditPage from './pages/InterditPage'
import IntrouvablePage from './pages/IntrouvablePage'
import HomePage from './pages/HomePage'
import './App.css'

/**
 * TABLE DES ROUTES.
 *
 * Deux zones : ce qui est accessible sans session — la connexion et l'écran
 * 403 — et tout le reste, derrière `RouteProtegee`.
 *
 * `/interdit` est PUBLIQUE À DESSEIN. La placer derrière la garde créerait une
 * boucle : la garde renvoie vers /interdit, qui déclenche la garde, qui renvoie
 * vers /interdit.
 *
 * Les trois écrans métier sont des espaces réservés. Ils existent pour que la
 * navigation soit complète et que rien ne mène à un écran blanc ; ils ne
 * lisent aucune donnée.
 */
export default function App() {
    return (
        <Routes>
            <Route path="/connexion" element={<ConnexionPage />} />
            <Route path="/interdit" element={<InterditPage />} />

            <Route element={<RouteProtegee />}>
                <Route element={<Coquille />}>
                    <Route index element={<HomePage />} />
                    <Route path="commandes" element={<EnConstruction titre="Commandes" />} />
                    <Route path="utilisateurs" element={<EnConstruction titre="Utilisateurs" />} />
                    <Route path="parametres" element={<EnConstruction titre="Paramètres" />} />
                </Route>
            </Route>

            <Route path="*" element={<IntrouvablePage />} />
        </Routes>
    )
}

function EnConstruction({ titre }: { titre: string }) {
    return (
        <section>
            <h1>{titre}</h1>
            <p className="indice">
                Cet écran n'est pas encore branché sur l'API. La coquille et
                l'authentification sont en place ; la lecture des données vient
                ensuite.
            </p>
        </section>
    )
}
