import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ApiError } from '../api/errors'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * UN GESTE D'ADMINISTRATION, ET CE QUI L'ENTOURE.
 *
 * TROIS CHOSES QUE CHAQUE BOUTON D'ÉCRITURE DOIT FAIRE, ET QU'ON OUBLIE UNE
 *     FOIS SUR DEUX QUAND ON LES RÉÉCRIT À CHAQUE ÉCRAN.
 *
 *   1. SE DÉSACTIVER PENDANT L'ENVOI. Sans cela, deux clics valent deux
 *      suspensions — et la seconde échoue sur une transition interdite, ce qui
 *      affiche une erreur pour un geste qui a pourtant réussi.
 *   2. DIRE CE QUI A ÉCHOUÉ, à l'endroit du bouton. Une erreur affichée en haut
 *      de page ne se rattache à rien quand l'écran porte huit gestes.
 *   3. RECHARGER LA FICHE APRÈS COUP. Les routes rendent 204 : elles ne
 *      renvoient pas l'état d'après. Deviner marcherait presque toujours, et
 *      afficherait un état faux le jour où le domaine impose une autre
 *      transition que celle qu'on croyait déclencher.
 *
 * LE MOTIF EST EXIGÉ ICI, PAS DÉCOUVERT PAR UN 422.
 *
 * Les agrégats refusent un motif vide. Le contrat, lui, le déclare nullable pour
 * rendre une erreur lisible plutôt qu'un 400 sur corps mal formé. Laisser
 * partir un motif vide ferait donc un aller-retour réseau pour apprendre ce
 * qu'on sait déjà.
 * ═══════════════════════════════════════════════════════════════════════════
 */

type ProprietesGeste = {
    libelle: string
    /** Ce que le geste fait, en une ligne — affiché sous le bouton. */
    aide?: string
    /** Un geste lourd demande confirmation ; un geste anodin, non. */
    confirmation?: string
    /** Le geste exige-t-il un motif écrit. */
    motif?: boolean
    /** Texte d'aide du champ de motif. */
    placeholderMotif?: string
    danger?: boolean
    executer: (motif: string) => Promise<unknown>
    apres: () => void
}

export function Geste({
    libelle,
    aide,
    confirmation,
    motif = false,
    placeholderMotif,
    danger = false,
    executer,
    apres,
}: ProprietesGeste) {
    const [ouvert, setOuvert] = useState(false)
    const [texte, setTexte] = useState('')

    const mutation = useMutation({
        mutationFn: (valeur: string) => executer(valeur),
        onSuccess: () => {
            setOuvert(false)
            setTexte('')
            apres()
        },
    })

    const besoinDeSaisie = motif || Boolean(confirmation)

    function lancer() {
        if (motif && !texte.trim()) return
        mutation.mutate(texte.trim())
    }

    return (
        <div className="geste">
            {!ouvert ? (
                <>
                    <button
                        type="button"
                        className={danger ? 'bouton bouton--danger' : 'bouton'}
                        disabled={mutation.isPending}
                        onClick={() => (besoinDeSaisie ? setOuvert(true) : lancer())}
                    >
                        {mutation.isPending ? 'En cours…' : libelle}
                    </button>
                    {aide && <span className="indice">{aide}</span>}
                </>
            ) : (
                <div className="geste__saisie">
                    {confirmation && <p className="indice">{confirmation}</p>}
                    {motif && (
                        <label>
                            <span className="visuellement-cache">Motif</span>
                            <input
                                type="text"
                                autoFocus
                                value={texte}
                                placeholder={placeholderMotif ?? 'Motif, lu par le vendeur'}
                                onChange={e => setTexte(e.target.value)}
                                onKeyDown={e => {
                                    if (e.key === 'Enter') lancer()
                                    if (e.key === 'Escape') setOuvert(false)
                                }}
                            />
                        </label>
                    )}
                    <div className="geste__boutons">
                        <button
                            type="button"
                            className={danger ? 'bouton bouton--danger' : 'bouton'}
                            disabled={mutation.isPending || (motif && !texte.trim())}
                            onClick={lancer}
                        >
                            {mutation.isPending ? 'En cours…' : `Confirmer — ${libelle}`}
                        </button>
                        <button
                            type="button"
                            className="lien-deconnexion"
                            disabled={mutation.isPending}
                            onClick={() => {
                                setOuvert(false)
                                mutation.reset()
                            }}
                        >
                            Annuler
                        </button>
                    </div>
                    {motif && !texte.trim() && (
                        <p className="indice">
                            Le motif est obligatoire : le service refuse un refus sans raison, et
                            un vendeur qui ne sait pas quoi corriger resoumet à l'identique.
                        </p>
                    )}
                </div>
            )}

            {mutation.isError && (
                <p className="erreur-en-ligne" role="alert">
                    {mutation.error instanceof ApiError
                        ? mutation.error.messageLisible
                        : 'Le geste a échoué.'}
                    {mutation.error instanceof ApiError && mutation.error.requestId
                        ? ` (requête ${mutation.error.requestId})`
                        : ''}
                </p>
            )}
        </div>
    )
}
