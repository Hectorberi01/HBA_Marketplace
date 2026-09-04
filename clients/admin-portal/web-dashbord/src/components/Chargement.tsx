/**
 * Écran d'attente.
 *
 * Il DIT CE QU'IL ATTEND. Un rond qui tourne sans texte est indistinguable
 * d'une page figée : au bout de quelques secondes, l'utilisateur ne sait pas
 * s'il doit patienter ou recharger.
 */
export default function Chargement({ message = 'Chargement…' }: { message?: string }) {
    return (
        <div className="ecran-centre" role="status" aria-live="polite">
            <div className="rond-attente" aria-hidden="true" />
            <p>{message}</p>
        </div>
    )
}
