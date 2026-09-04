import { useAuth } from '../auth/useAuth'
import { ROLE_REQUIS } from '../config/env'

/**
 * 403 — authentifié, mais pas administrateur.
 *
 * L'écran DIT AVEC QUEL COMPTE et QUELS RÔLES. « Accès refusé » tout seul
 * laisse croire à une panne, et la première réaction est de recharger. Voir son
 * propre courriel affiché fait immédiatement comprendre qu'on s'est connecté
 * avec le mauvais compte.
 */
export default function InterditPage() {
    const { etat, deconnexion } = useAuth()
    const jeton = etat.statut === 'interdit' || etat.statut === 'connecte' ? etat.jeton : null

    return (
        <div className="ecran-centre">
            <div className="carte-message">
                <h1>Accès refusé</h1>
                <p>
                    Ce portail est réservé au rôle <strong>{ROLE_REQUIS}</strong>.
                </p>
                {jeton && (
                    <p className="indice">
                        Compte : {jeton.email ?? jeton.nom ?? jeton.sujet ?? 'inconnu'}
                        <br />
                        Rôles : {jeton.roles.length > 0 ? jeton.roles.join(', ') : 'aucun'}
                    </p>
                )}
                <button type="button" onClick={() => void deconnexion()}>
                    Changer de compte
                </button>
            </div>
        </div>
    )
}
