import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { ApiError } from '../api/errors'

type Emplacement = { de?: { pathname: string } }

export default function ConnexionPage() {
    const { etat, connexion } = useAuth()
    const naviguer = useNavigate()
    const emplacement = useLocation()

    const [email, setEmail] = useState('')
    const [motDePasse, setMotDePasse] = useState('')
    const [codeMfa, setCodeMfa] = useState('')
    const [mfaAttendu, setMfaAttendu] = useState(false)
    const [erreur, setErreur] = useState<string | null>(null)
    const [enCours, setEnCours] = useState(false)

    // Déjà connecté : on ne montre pas un formulaire de connexion à quelqu'un
    // qui n'en a pas besoin, il croirait sa session perdue.
    if (etat.statut === 'connecte') {
        const de = (emplacement.state as Emplacement | null)?.de?.pathname
        return <Navigate to={de ?? '/'} replace />
    }

    async function soumettre(evenement: FormEvent) {
        evenement.preventDefault()
        setErreur(null)
        setEnCours(true)
        try {
            const encoreMfa = await connexion(email, motDePasse, codeMfa || undefined)
            if (encoreMfa) {
                setMfaAttendu(true)
                return
            }
            const de = (emplacement.state as Emplacement | null)?.de?.pathname
            naviguer(de ?? '/', { replace: true })
        } catch (cause) {
            // ON MONTRE CE QUE LE SERVEUR A DIT. « Identifiants invalides »
            // écrit en dur masquerait un compte suspendu, un mot de passe
            // expiré, une limitation de débit ou une panne — quatre situations
            // qui appellent quatre gestes différents de la part de l'utilisateur.
            setErreur(
                cause instanceof ApiError
                    ? cause.messageLisible
                    : "La connexion a échoué pour une raison inattendue.",
            )
        } finally {
            setEnCours(false)
        }
    }

    return (
        <div className="ecran-centre">
            <form className="carte-connexion" onSubmit={soumettre}>
                <h1>Portail d'administration</h1>
                <p className="sous-titre">HBAExpress</p>

                <label htmlFor="email">Adresse électronique</label>
                <input
                    id="email"
                    type="email"
                    autoComplete="username"
                    required
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                    disabled={enCours}
                />

                <label htmlFor="motdepasse">Mot de passe</label>
                <input
                    id="motdepasse"
                    type="password"
                    autoComplete="current-password"
                    required
                    value={motDePasse}
                    onChange={e => setMotDePasse(e.target.value)}
                    disabled={enCours}
                />

                {mfaAttendu && (
                    <>
                        <label htmlFor="mfa">Code de vérification</label>
                        <input
                            id="mfa"
                            inputMode="numeric"
                            autoComplete="one-time-code"
                            required
                            value={codeMfa}
                            onChange={e => setCodeMfa(e.target.value)}
                            disabled={enCours}
                            autoFocus
                        />
                        <p className="indice">
                            Ce compte est protégé par une double authentification.
                        </p>
                    </>
                )}

                {erreur && (
                    <p className="erreur" role="alert">
                        {erreur}
                    </p>
                )}

                <button type="submit" disabled={enCours}>
                    {enCours ? 'Connexion…' : 'Se connecter'}
                </button>
            </form>
        </div>
    )
}
