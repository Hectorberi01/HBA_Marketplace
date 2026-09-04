import { Link, useLocation } from 'react-router-dom'

export default function IntrouvablePage() {
    const { pathname } = useLocation()
    return (
        <div className="ecran-centre">
            <div className="carte-message">
                <h1>Page introuvable</h1>
                <p>
                    Aucun écran ne correspond à <code>{pathname}</code>.
                </p>
                <Link to="/">Retour à l'accueil</Link>
            </div>
        </div>
    )
}
