import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthContext'
import App from './App.tsx'
import './index.css'

/**
 * RÉGLAGES DE TANSTACK QUERY, ET POURQUOI CEUX-LÀ.
 *
 * `retry` : on NE REJOUE PAS un 4xx. Les valeurs par défaut réessaient trois
 * fois quelle que soit l'erreur ; sur un 401 ou un 403, cela transforme un
 * refus immédiat en trois secondes d'attente avant le même refus, et fait
 * croire à une lenteur du serveur. Un 5xx ou une coupure réseau, en revanche,
 * méritent un second essai.
 *
 * `refetchOnWindowFocus` : désactivé. Sur un écran d'administration, revenir
 * d'un autre onglet relancerait toutes les requêtes visibles — bruit inutile
 * sur des données qui ne changent pas à la seconde.
 */
const client = new QueryClient({
    defaultOptions: {
        queries: {
            retry: (echecs, erreur) => {
                const statut = (erreur as { statut?: number })?.statut
                if (typeof statut === 'number' && statut >= 400 && statut < 500) return false
                return echecs < 2
            },
            refetchOnWindowFocus: false,
            staleTime: 30_000,
        },
    },
})

createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <BrowserRouter>
            <QueryClientProvider client={client}>
                <AuthProvider>
                    <App />
                </AuthProvider>
            </QueryClientProvider>
        </BrowserRouter>
    </StrictMode>,
)
