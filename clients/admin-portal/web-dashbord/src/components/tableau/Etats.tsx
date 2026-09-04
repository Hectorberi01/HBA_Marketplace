import { ApiError } from '../../api/errors'

/**
 * LES TROIS ÉTATS QU'UNE LISTE PEUT AVOIR EN DEHORS DU CAS NORMAL.
 *
 * Ils sont écrits une fois et partagés, parce que c'est exactement le genre de
 * détail qu'on rédige avec soin sur le premier écran puis qu'on bâcle sur les
 * quatorze suivants.
 */

/** Aucun résultat — et la raison n'est pas la même selon qu'un filtre est posé. */
export function EtatVide({ filtre }: { filtre: boolean }) {
    return (
        <div className="etat-liste">
            <p>{filtre ? 'Aucun résultat pour ces critères.' : 'Rien à afficher pour le moment.'}</p>
            {filtre && (
                <p className="indice">
                    Élargissez la recherche ou retirez le filtre de statut.
                </p>
            )}
        </div>
    )
}

/**
 * L'ÉCHEC MONTRE LE MESSAGE DU SERVEUR ET SON `requestId`.
 *
 * Le requestId est ce qui permet de retrouver la requête dans les traces. Sans
 * lui, un utilisateur qui envoie une capture d'écran ne transmet rien
 * d'exploitable — et c'est précisément ce que l'enveloppe du paragraphe 5
 * existe pour éviter.
 */
export function EtatErreur({ erreur, onReessayer }: { erreur: unknown; onReessayer: () => void }) {
    const api = erreur instanceof ApiError ? erreur : null
    return (
        <div className="etat-liste" role="alert">
            <p className="erreur">{api ? api.messageLisible : 'Le chargement a échoué.'}</p>
            {api?.details.length ? (
                <ul className="indice">
                    {api.details.map((d, i) => (
                        <li key={i}>
                            {d.field ? `${d.field} : ` : ''}
                            {d.message}
                        </li>
                    ))}
                </ul>
            ) : null}
            {api?.requestId && <p className="indice">Requête : <code>{api.requestId}</code></p>}
            <button type="button" onClick={onReessayer}>
                Réessayer
            </button>
        </div>
    )
}

/**
 * Chargement d'une LISTE DÉJÀ AFFICHÉE.
 *
 * Vider le tableau à chaque changement de page fait sauter la mise en page et
 * perd la position de lecture. On garde donc les lignes précédentes, atténuées,
 * et on annonce la mise à jour aux outils d'assistance.
 */
export function VoileChargement() {
    return (
        <div className="voile-chargement" role="status" aria-live="polite">
            <span className="rond-attente" aria-hidden="true" />
            <span>Chargement…</span>
        </div>
    )
}
