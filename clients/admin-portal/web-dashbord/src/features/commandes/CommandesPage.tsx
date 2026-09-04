import { keepPreviousData, useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger, formaterDate, formaterMontant } from '../../lib/format'
import {
    STATUTS_A_TRAITER,
    libelleStatutCommande,
    listerCommandes,
    type Commande,
} from './api'

/**
 * CONSOLE DES COMMANDES — `/api/admin/orders`.
 *
 * `keepPreviousData` : en changeant de page, le tableau précédent RESTE à
 * l'écran, atténué, au lieu d'être remplacé par un vide. Sans cela la mise en
 * page saute à chaque clic, la barre de défilement remonte, et l'attente d'une
 * demi-seconde se lit comme un écran qui se casse.
 *
 * CET ÉCRAN EST EN LECTURE SEULE, alors que le service expose deux gestes
 * d'exploitation — `POST /{id}/review/resume` et `POST /{id}/review/refund`.
 * Ils sortent une commande de l'arbitrage, et l'un des deux REND DE L'ARGENT :
 * le service dit lui-même que « l'argent rendu ne se reprend pas ». Les brancher
 * demande une confirmation explicite, un champ de motif obligatoire sur le
 * remboursement, et de savoir ce que l'écran fait quand la reprise échoue —
 * le service répond alors 409 avec le motif à jour, ce qui n'est PAS une erreur
 * à afficher en rouge mais un résultat à lire. Cela mérite d'être fait
 * délibérément, pas glissé dans un écran de liste.
 */
export default function CommandesPage() {
    const { etat, modifier } = useListeUrl('createdAtUtc')

    const requete = useQuery({
        queryKey: ['commandes', etat],
        queryFn: ({ signal }) =>
            listerCommandes(
                {
                    page: etat.page,
                    taille: etat.taille,
                    recherche: etat.recherche || undefined,
                    statut: etat.statut,
                    tri: etat.tri,
                    sens: etat.sens,
                },
                signal,
            ),
        placeholderData: keepPreviousData,
    })

    const page = requete.data
    const filtre = Boolean(etat.recherche || etat.statut)

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Commandes</h1>
                <BarreRecherche
                    valeur={etat.recherche}
                    onChange={q => modifier({ recherche: q })}
                    placeholder="Rechercher une commande"
                />
            </header>

            <Facettes
                facettes={page?.facettes ?? null}
                actif={etat.statut}
                onChoisir={s => modifier({ statut: s })}
                libelle={libelleStatutCommande}
            />

            {requete.isError && (
                <EtatErreur erreur={requete.error} onReessayer={() => void requete.refetch()} />
            )}

            {!requete.isError && (
                <div className="tableau-enveloppe">
                    {requete.isFetching && <VoileChargement />}

                    {page && page.items.length === 0 ? (
                        <EtatVide filtre={filtre} />
                    ) : (
                        <table className={`tableau ${requete.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Commandes de la plateforme, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Commande</th>
                                    <th scope="col">Créée le</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Type</th>
                                    <th scope="col">Lignes</th>
                                    <th scope="col" className="au-bout">Total</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(c => (
                                    <Ligne key={c.id} commande={c} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}

            {page && (
                <Pagination
                    page={page.page}
                    taille={page.pageSize}
                    total={page.total}
                    onPage={p => modifier({ page: p })}
                    onTaille={t => modifier({ taille: t })}
                    desactive={requete.isFetching}
                />
            )}
        </section>
    )
}

function Ligne({ commande }: { commande: Commande }) {
    const aTraiter = STATUTS_A_TRAITER.has(commande.status)

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <code title={commande.id}>{abreger(commande.id)}</code>
                {/*
                  * LE MOTIF D'ARBITRAGE EST AFFICHÉ, PAS SEULEMENT LE STATUT.
                  * « En arbitrage » ne dit pas quoi faire ; « course annulée »
                  * ou « deux lieux d'expédition » le dit. C'est le champ que le
                  * service remplit exactement pour cela.
                  */}
                {commande.reviewReason && (
                    <div className="indice">{commande.reviewReason}</div>
                )}
            </td>
            <td>{formaterDate(commande.createdAtUtc)}</td>
            <td>
                <span className={`pastille pastille--${commande.status.toLowerCase()}`}>
                    {libelleStatutCommande(commande.status)}
                </span>
            </td>
            <td>{commande.kind === 'Food' ? 'Repas' : 'Marchandise'}</td>
            <td>{commande.lines?.length ?? 0}</td>
            <td className="au-bout">
                {formaterMontant(commande.grandTotal, commande.currency)}
            </td>
        </tr>
    )
}
