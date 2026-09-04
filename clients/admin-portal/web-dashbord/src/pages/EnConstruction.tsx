/**
 * ÉCRAN NON ENCORE BRANCHÉ.
 *
 * IL DIT SUR QUEL ENDPOINT IL DEVRA TAPER, ET CE QU'IL NE FAIT PAS ENCORE.
 *
 * Un « bientôt disponible » sans détail se lit comme une panne : l'utilisateur
 * recharge, essaie un autre navigateur, finit par écrire. Nommer le chemin
 * d'API transforme la page en information utile — et, pour qui développe, en
 * rappel de ce qui reste à faire.
 */
export default function EnConstruction({
    titre,
    api,
    note,
}: {
    titre: string
    api: string
    note?: string
}) {
    return (
        <section>
            <h1>{titre}</h1>
            <p className="indice">
                Cet écran n'est pas encore branché. La coquille, l'authentification et la
                navigation sont en place ; la lecture des données vient ensuite.
            </p>
            <p className="indice">
                Endpoint prévu : <code>{api}</code>
            </p>
            {note && <p className="indice">{note}</p>}
        </section>
    )
}
