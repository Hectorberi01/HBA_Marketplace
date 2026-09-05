import { Route, Routes } from 'react-router-dom'
import Coquille from './layout/Coquille'
import RouteProtegee from './routes/RouteProtegee'
import ConnexionPage from './pages/ConnexionPage'
import InterditPage from './pages/InterditPage'
import IntrouvablePage from './pages/IntrouvablePage'
import HomePage from './pages/HomePage'
import MonitoringPage from './features/monitoring/MonitoringPage'
import CommandesPage from './features/commandes/CommandesPage'
import CataloguePage from './features/catalogue/CataloguePage'
import ValidationPage from './features/catalogue/ValidationPage'
import StockPage from './features/stock/StockPage'
import VendeursPage from './features/vendeurs/VendeursPage'
import VendeurPage from './features/vendeurs/VendeurPage'
import NouveauVendeurPage from './features/vendeurs/NouveauVendeurPage'
import BoutiquePage from './features/vendeurs/BoutiquePage'
import RetoursPage from './features/retours/RetoursPage'
import UtilisateursPage from './features/identite/UtilisateursPage'
import RolesPage from './features/identite/RolesPage'
import NouvelUtilisateurPage from './features/identite/NouvelUtilisateurPage'
import ReglementsPage from './features/finance/ReglementsPage'
import CommissionsPage from './features/finance/CommissionsPage'
import FacturesPage from './features/finance/FacturesPage'
import LivreursPage from './features/livraison/LivreursPage'
import TarificationPage from './features/livraison/TarificationPage'
import EtablissementsPage from './features/restauration/EtablissementsPage'
import CommandesRepasPage from './features/restauration/CommandesRepasPage'
import './App.css'

/**
 * TABLE DES ROUTES.
 *
 * ELLE SUIT `layout/navigation.tsx`, ENTRÉE POUR ENTRÉE. Un chemin qui figure
 * dans la barre latérale sans route correspondante donne une page « introuvable »
 * atteinte par un clic dans le menu — le pire des deux mondes.
 *
 * `/interdit` est PUBLIQUE À DESSEIN. La placer derrière la garde créerait une
 * boucle : la garde renvoie vers /interdit, qui déclenche la garde, qui renvoie
 * vers /interdit.
 */
export default function App() {
    return (
        <Routes>
            <Route path="/connexion" element={<ConnexionPage />} />
            <Route path="/interdit" element={<InterditPage />} />

            <Route element={<RouteProtegee />}>
                <Route element={<Coquille />}>
                    <Route index element={<HomePage />} />
                    <Route path="supervision" element={<MonitoringPage />} />

                    <Route path="commandes" element={<CommandesPage />} />
                    <Route path="catalogue" element={<CataloguePage />} />
                    <Route path="catalogue/validation" element={<ValidationPage />} />
                    <Route path="stock" element={<StockPage />} />
                    <Route path="vendeurs" element={<VendeursPage />} />
                    <Route path="vendeurs/nouveau" element={<NouveauVendeurPage />} />
                    <Route path="vendeurs/:sellerId" element={<VendeurPage />} />
                    <Route
                        path="vendeurs/:sellerId/boutiques/:storeId"
                        element={<BoutiquePage />}
                    />
                    <Route path="retours" element={<RetoursPage />} />

                    <Route path="restauration/etablissements" element={<EtablissementsPage />} />
                    <Route path="restauration/commandes" element={<CommandesRepasPage />} />

                    <Route path="livraison/livreurs" element={<LivreursPage />} />
                    <Route path="livraison/tarification" element={<TarificationPage />} />

                    <Route path="finance/reglements" element={<ReglementsPage />} />
                    <Route path="finance/commissions" element={<CommissionsPage />} />
                    <Route path="finance/factures" element={<FacturesPage />} />

                    <Route path="administration/utilisateurs" element={<UtilisateursPage />} />
                    <Route
                        path="administration/utilisateurs/nouveau"
                        element={<NouvelUtilisateurPage />}
                    />
                    <Route path="administration/roles" element={<RolesPage />} />
                </Route>
            </Route>

            <Route path="*" element={<IntrouvablePage />} />
        </Routes>
    )
}
