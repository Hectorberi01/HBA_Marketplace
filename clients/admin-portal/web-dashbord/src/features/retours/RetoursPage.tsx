import { keepPreviousData, useQuery } from '@tanstack/react-query'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { formaterDate, formaterMontant } from '../../lib/format'
import {
    STATUTS_A_TRAITER,
    libelleStatutRetour,
    listerRetours,
    nomStatut,
    type Retour,
} from './api'

/**
 * DOSSIERS DE RETOUR — `/api/v1/admin/returns`.
 *
 * AUCUN CHAMP DE RECHERCHE : la route n'accepte que `page`, `pageSize` et
 * `status`. En poser un qui ne filtrerait que les vingt lignes chargées
 * ressemblerait en tout point à une recherche complète et raterait le reste en
 * silence.
 *
 * LES FACETTES SONT CALCULÉES AVANT LE FILTRE, côté serveur — le dépôt le dit :
 * « elles disent ce qu'il y a ailleurs ». Les compteurs restent donc stables
 * quand on clique d'un statut à l'autre, ce qui est exactement ce qu'on attend
 * d'une vue d'ensemble.
 */
export default function RetoursPage() {
    const { etat, modifier } = useListeUrl('createdAtUtc')

    const requete = useQuery({
        queryKey: ['retours', etat.page, etat.taille, etat.statut],
        queryFn: ({ signal }) =>
            listerRetours(
                { page: etat.page, taille: etat.taille, statut: etat.statut },
                signal,
            ),
        placeholderData: keepPreviousData,
    })

    const page = requete.data

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Retours</h1>
            </header>

            <Facettes
                facettes={page?.facettes ?? null}
                actif={etat.statut}
                onChoisir={s => modifier({ statut: s })}
                libelle={libelleStatutRetour}
            />

            {requete.isError && (
                <EtatErreur erreur={requete.error} onReessayer={() => void requete.refetch()} />
            )}

            {!requete.isError && (
                <div className="tableau-enveloppe">
                    {requete.isFetching && <VoileChargement />}

                    {page && page.items.length === 0 ? (
                        <EtatVide filtre={Boolean(etat.statut)} />
                    ) : (
                        <table className={`tableau ${requete.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Dossiers de retour, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Dossier</th>
                                    <th scope="col">Ouvert le</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Articles</th>
                                    <th scope="col" className="au-bout">Remboursement</th>
                                    <th scope="col">Échéance</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(r => (
                                    <Ligne key={r.id} retour={r} maintenant={requete.dataUpdatedAt} />
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

function Ligne({ retour, maintenant }: { retour: Retour; maintenant: number }) {
    const nom = nomStatut(retour.status)
    const aTraiter = STATUTS_A_TRAITER.has(nom)

    /*
     * LE MONTANT APPROUVÉ PRIME SUR L'ESTIMATION.
     *
     * Tant que personne n'a tranché, `approvedRefund` est nul et l'estimation
     * est la seule information disponible. Une fois le dossier arbitré, c'est
     * le montant approuvé qui sera versé — afficher l'estimation à sa place
     * montrerait un chiffre que personne ne paiera.
     */
    const montant = retour.approvedRefund ?? retour.estimatedRefund
    const estime = retour.approvedRefund == null

    /*
     * L'ÉCHÉANCE EST DÉPASSÉE : on le dit, on ne le calcule pas en silence. Un
     * dossier expiré non traité est exactement ce qu'une console d'exploitation
     * existe pour faire remonter.
     *
     * L'INSTANT DE RÉFÉRENCE EST CELUI DE LA DONNÉE, PAS `Date.now()`.
     *
     * Appeler `Date.now()` pendant le rendu rend le composant impur : deux
     * rendus successifs des mêmes données peuvent donner deux résultats, et
     * oxlint le signale (`react(purity)`). `dataUpdatedAt` est l'horodatage de
     * la réponse elle-même — c'est aussi la bonne référence sur le fond, parce
     * que la question posée est « à la date de ces données, ce dossier
     * était-il échu ».
     */
    const echu =
        new Date(retour.expiresAtUtc).getTime() < maintenant && retour.resolvedAtUtc == null

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{retour.returnNumber}</div>
                <div className="indice">
                    commande <code title={retour.orderId}>{retour.orderId.slice(0, 8)}</code>
                </div>
            </td>
            <td>{formaterDate(retour.createdAtUtc)}</td>
            <td>
                <span className={`pastille pastille--${nom.toLowerCase()}`}>
                    {libelleStatutRetour(retour.status)}
                </span>
            </td>
            <td>{retour.items?.length ?? 0}</td>
            <td className="au-bout">
                {formaterMontant(montant.amount, montant.currency)}
                {estime && <div className="indice">estimé</div>}
            </td>
            <td>
                {formaterDate(retour.expiresAtUtc)}
                {echu && <div className="indice erreur-en-ligne">échéance dépassée</div>}
            </td>
        </tr>
    )
}
