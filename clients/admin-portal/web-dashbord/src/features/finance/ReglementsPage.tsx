import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { abreger, formaterDate, formaterMontant, formaterPeriode } from '../../lib/format'
import {
    LOTS_A_TRAITER,
    libelleLot,
    libelleVersement,
    listerLots,
    type LotReglement,
} from './api'

/**
 * LOTS DE RÈGLEMENT — `GET /api/financial/settlements`.
 *
 * Liste NUE : `ListSettlementBatchesQuery` ne prend aucun paramètre. Ni page,
 * ni filtre, ni recherche. Le filtre ci-dessous est LOCAL et l'écran le dit.
 *
 * LECTURE SEULE, ET PAS PAR PRUDENCE DE MA PART.
 *
 * Les quatre gestes du lot — lancer, marquer payé, marquer échoué, annuler —
 * ne sont pas relayés par la passerelle : sa route `settlements` est GET
 * seulement, délibérément. Le service l'écrit : « elle n'est donc atteignable
 * que depuis le réseau interne ». Ces gestes déplacent de l'argent dans les
 * deux sens, `MarkPayoutFailed` recréditant un vendeur. Tant que cette décision
 * tient, aucun bouton ne peut exister ici, et en poser un donnerait un 404.
 */
export default function ReglementsPage() {
    const lots = useQuery({ queryKey: ['reglements'], queryFn: ({ signal }) => listerLots(signal) })
    const [filtre, setFiltre] = useState<string | null>(null)

    /*
     * LA RÉPARTITION EST CALCULÉE SUR LES VERSEMENTS RÉELLEMENT REÇUS.
     *
     * Chaque lot porte ses `payouts` dans la réponse : compter dedans n'est pas
     * une extrapolation, c'est lire ce qui est là. La nuance avec les facettes
     * des autres écrans est réelle — là-bas, compter les lignes affichées aurait
     * donné la répartition d'une PAGE présentée comme celle de la plateforme.
     * Ici, la liste est complète par construction.
     */
    const parStatut = useMemo(() => {
        const compte: Record<string, number> = {}
        for (const lot of lots.data ?? []) compte[lot.status] = (compte[lot.status] ?? 0) + 1
        return compte
    }, [lots.data])

    const filtres = useMemo(
        () => (filtre ? (lots.data ?? []).filter(l => l.status === filtre) : lots.data ?? []),
        [lots.data, filtre],
    )

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Règlements</h1>
                <button
                    type="button"
                    className="lien-deconnexion"
                    onClick={() => void lots.refetch()}
                >
                    Recharger
                </button>
            </header>

            <p className="indice">
                Lots de règlement, du plus récent au plus ancien. L'API rend la liste entière
                en une fois : le filtre ci-dessous porte sur ce qui est déjà chargé. Les gestes
                de règlement — lancer, marquer payé ou échoué, annuler — ne passent pas par la
                passerelle, qui n'expose cette route qu'en lecture.
            </p>

            {Object.keys(parStatut).length > 0 && (
                <div className="facettes" role="group" aria-label="Filtrer par statut">
                    <button
                        type="button"
                        className={`facette ${filtre === null ? 'is-active' : ''}`}
                        aria-pressed={filtre === null}
                        onClick={() => setFiltre(null)}
                    >
                        Tous <span className="facette__compte">{lots.data?.length ?? 0}</span>
                    </button>
                    {Object.entries(parStatut)
                        .sort((a, b) => b[1] - a[1])
                        .map(([statut, n]) => (
                            <button
                                key={statut}
                                type="button"
                                className={`facette ${filtre === statut ? 'is-active' : ''}`}
                                aria-pressed={filtre === statut}
                                onClick={() => setFiltre(filtre === statut ? null : statut)}
                            >
                                {libelleLot(statut)} <span className="facette__compte">{n}</span>
                            </button>
                        ))}
                </div>
            )}

            {lots.isError ? (
                <EtatErreur erreur={lots.error} onReessayer={() => void lots.refetch()} />
            ) : (
                <div className="tableau-enveloppe">
                    {lots.isFetching && <VoileChargement />}

                    {lots.data && filtres.length === 0 ? (
                        <EtatVide filtre={Boolean(filtre)} />
                    ) : (
                        <table className={`tableau ${lots.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Lots de règlement, {filtres.length} affichés
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Période</th>
                                    <th scope="col">Créé le</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Versements</th>
                                    <th scope="col" className="au-bout">Net total</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filtres.map(l => (
                                    <Ligne key={l.id} lot={l} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}
        </section>
    )
}

function Ligne({ lot }: { lot: LotReglement }) {
    const aTraiter = LOTS_A_TRAITER.has(lot.status)

    /*
     * LES VERSEMENTS ÉCHOUÉS SONT COMPTÉS À PART.
     *
     * « 47 versements » sur un lot dont trois ont été refusés par l'opérateur
     * cache exactement ce qu'il faut voir : trois vendeurs débités et jamais
     * payés, en attente d'une compensation manuelle.
     */
    const parStatut = lot.payouts.reduce<Record<string, number>>((acc, v) => {
        acc[v.status] = (acc[v.status] ?? 0) + 1
        return acc
    }, {})
    const echoues = parStatut.Failed ?? 0

    return (
        <tr className={aTraiter || echoues > 0 ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">
                    {formaterPeriode(lot.periodStartUtc, lot.periodEndUtc)}
                </div>
                <div className="indice">
                    <code title={lot.id}>{abreger(lot.id)}</code>
                </div>
            </td>
            <td>{formaterDate(lot.createdAtUtc)}</td>
            <td>
                <span className={`pastille pastille--${lot.status.toLowerCase()}`}>
                    {libelleLot(lot.status)}
                </span>
            </td>
            <td>
                <div className="jetons">
                    {Object.entries(parStatut).map(([statut, n]) => (
                        <span
                            key={statut}
                            className={`jeton ${statut === 'Failed' ? 'jeton--attention' : ''}`}
                        >
                            {libelleVersement(statut)} {n}
                        </span>
                    ))}
                    {lot.payouts.length === 0 && <span className="indice">aucun</span>}
                </div>
            </td>
            <td className="au-bout">{formaterMontant(lot.totalNet, lot.currency)}</td>
        </tr>
    )
}
