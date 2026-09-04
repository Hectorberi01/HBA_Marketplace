import { useContext } from 'react'
import { AuthContext, type Auth } from './context'

export function useAuth(): Auth {
    const auth = useContext(AuthContext)
    if (!auth) {
        // Un contexte absent rend `null`, et le composant échouerait plus loin
        // sur « cannot read property etat of null », sans dire qu'il manque un
        // fournisseur. On le dit ici.
        throw new Error("useAuth doit être appelé sous <AuthProvider>.")
    }
    return auth
}
