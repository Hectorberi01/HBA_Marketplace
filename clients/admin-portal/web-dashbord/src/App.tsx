import { Route, Routes } from 'react-router-dom'
import Coquille from './layout/Coquille'
import RouteProtegee from './routes/RouteProtegee'
import ConnexionPage from './pages/ConnexionPage'
import InterditPage from './pages/InterditPage'
import IntrouvablePage from './pages/IntrouvablePage'
import EnConstruction from './pages/EnConstruction'
import HomePage from './pages/HomePage'
import CommandesPage from './features/commandes/CommandesPage'
import CataloguePage from './features/catalogue/CataloguePage'
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

                    <Route path="commandes" element={<CommandesPage />} />
                    <Route path="catalogue" element={<CataloguePage />} />
                    <Route
                        path="stock"
                        element={<EnConstruction titre="Stock" api="/api/inventory" />}
                    />
                    <Route
                        path="vendeurs"
                        element={<EnConstruction titre="Vendeurs" api="/api/v1/merchants" />}
                    />
                    <Route
                        path="retours"
                        element={<EnConstruction titre="Retours" api="/api/v1/admin/returns" />}
                    />

                    <Route
                        path="restauration/etablissements"
                        element={<EnConstruction titre="Établissements" api="/api/food/admin" />}
                    />
                    <Route
                        path="restauration/commandes"
                        element={
                            <EnConstruction titre="Commandes repas" api="/api/admin/food/orders" />
                        }
                    />

                    <Route
                        path="livraison/livreurs"
                        element={<EnConstruction titre="Livreurs" api="/api/v1/admin/drivers" />}
                    />
                    <Route
                        path="livraison/tarification"
                        element={
                            <EnConstruction
                                titre="Tarification"
                                api="/api/v1/admin/delivery-pricing"
                            />
                        }
                    />

                    <Route
                        path="finance/reglements"
                        element={
                            <EnConstruction
                                titre="Règlements"
                                api="/api/financial/settlements"
                                note="LECTURE SEULE : la route de la passerelle n'accepte que GET, HEAD et OPTIONS. Les gestes d'administration montés par payment-service rendront 404 tant que la passerelle n'ouvrira pas les autres méthodes."
                            />
                        }
                    />
                    <Route
                        path="finance/commissions"
                        element={
                            <EnConstruction titre="Commissions" api="/api/financial/commissions" />
                        }
                    />
                    <Route
                        path="finance/factures"
                        element={<EnConstruction titre="Factures" api="/api/financial/invoices" />}
                    />

                    <Route
                        path="administration/utilisateurs"
                        element={
                            <EnConstruction titre="Utilisateurs" api="/api/identity/users" />
                        }
                    />
                    <Route
                        path="administration/roles"
                        element={<EnConstruction titre="Rôles" api="/api/identity/roles" />}
                    />
                </Route>
            </Route>

            <Route path="*" element={<IntrouvablePage />} />
        </Routes>
    )
}
