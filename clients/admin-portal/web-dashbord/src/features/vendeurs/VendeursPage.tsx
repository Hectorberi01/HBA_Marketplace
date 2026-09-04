import { keepPreviousData, useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger, formaterDate } from '../../lib/format'
import {
    A_TRAITER_KYB,
    STATUTS_KYB,
    STATUTS_VENDEUR,
    libelleKyb,
    libelleStatutVendeur,
    listerVendeurs,
    type Vendeur,
} from './api'

const CLES_EXTRA = ['kyb'] as const

/**
 * GOUVERNANCE DES VENDEURS — `/api/v1/merchants`.
 *
 * DEUX FILTRES, PAS UN. `status` porte sur le COMPTE, `kybStatus` sur le
 * DOSSIER de vérification, et le service les traite séparément. Un vendeur
 * actif dont le KYB a été refusé existe, un compte en attente dont le KYB est
 * vérifié aussi — ce sont précisément les cas qu'une console doit faire
 * remonter, et les fondre en un seul filtre les ferait disparaître.
 *
 * CE SERVICE NE REND PAS DE FACETTES. Les deux filtres sont donc des listes
 * déroulantes, alimentées par les énumérations du domaine, et non des pastilles
 * chiffrées comme sur les autres écrans. Inventer des compteurs à partir de la
 * page affichée donnerait des nombres faux.
 *
 * ÉCRAN DE LECTURE. Le service expose sept gestes de gouvernance : approuver ou
 * refuser un KYB, activer, suspendre, lever une suspension, approuver une
 * réactivation, supprimer. Trois d'entre eux exigent un motif, et la
 * suppression est définitive. Ils appartiennent à une fiche vendeur, pas à une
 * ligne de tableau.
 */
export default function VendeursPage() {
    const { etat, modifier } = useListeUrl('createdOnUtc', CLES_EXTRA)
    const kyb = etat.extra.kyb ?? null

    const requete = useQuery({
        queryKey: ['vendeurs', etat.page, etat.taille, etat.recherche, etat.statut, kyb],
        queryFn: ({ signal }) =>
            listerVendeurs(
                {
                    page: etat.page,
                    taille: etat.taille,
                    recherche: etat.recherche || undefined,
                    statut: etat.statut,
                    statutKyb: kyb,
                },
                signal,
            ),
        placeholderData: keepPreviousData,
    })

    const page = requete.data
    const filtre = Boolean(etat.recherche || etat.statut || kyb)

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Vendeurs</h1>
                <BarreRecherche
                    valeur={etat.recherche}
                    onChange={q => modifier({ recherche: q })}
                    placeholder="Rechercher une boutique"
                />
            </header>

            <div className="filtres">
                <label>
                    Compte
                    <select
                        value={etat.statut ?? ''}
                        onChange={e => modifier({ statut: e.target.value || null })}
                    >
                        <option value="">Tous</option>
                        {STATUTS_VENDEUR.map(s => (
                            <option key={s} value={s}>
                                {libelleStatutVendeur(s)}
                            </option>
                        ))}
                    </select>
                </label>

                <label>
                    Dossier KYB
                    <select
                        value={kyb ?? ''}
                        onChange={e => modifier({ extra: { kyb: e.target.value || null } })}
                    >
                        <option value="">Tous</option>
                        {STATUTS_KYB.map(s => (
                            <option key={s} value={s}>
                                {libelleKyb(s)}
                            </option>
                        ))}
                    </select>
                </label>

                {filtre && (
                    <button
                        type="button"
                        className="lien-deconnexion"
                        onClick={() =>
                            modifier({ recherche: '', statut: null, extra: { kyb: null } })
                        }
                    >
                        Tout effacer
                    </button>
                )}
            </div>

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
                                Vendeurs de la plateforme, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Boutique</th>
                                    <th scope="col">Inscrit le</th>
                                    <th scope="col">Compte</th>
                                    <th scope="col">Dossier KYB</th>
                                    <th scope="col">Pièces</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(v => (
                                    <Ligne key={v.id} vendeur={v} />
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

function Ligne({ vendeur }: { vendeur: Vendeur }) {
    const aTraiter = A_TRAITER_KYB.has(vendeur.kybStatus)

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{vendeur.shopName}</div>
                <div className="indice">
                    <code title={vendeur.id}>{abreger(vendeur.id)}</code>
                </div>
            </td>
            <td>{formaterDate(vendeur.createdOnUtc)}</td>
            <td>
                <span className={`pastille pastille--${vendeur.status.toLowerCase()}`}>
                    {libelleStatutVendeur(vendeur.status)}
                </span>
            </td>
            <td>
                <span className={`pastille pastille--${vendeur.kybStatus.toLowerCase()}`}>
                    {libelleKyb(vendeur.kybStatus)}
                </span>
                {/*
                  * LE MOTIF DE REFUS EST AFFICHÉ. « Refusé » n'appelle aucun
                  * geste ; « pièce illisible » en appelle un, et c'est le champ
                  * que le service remplit exactement pour cela.
                  */}
                {vendeur.kybRejectionReason && (
                    <div className="indice">{vendeur.kybRejectionReason}</div>
                )}
            </td>
            <td>
                {vendeur.kybDocumentCount}
                {vendeur.kybDocumentCount === 0 && vendeur.kybStatus !== 'NotStarted' && (
                    <div className="indice erreur-en-ligne">aucune pièce</div>
                )}
            </td>
        </tr>
    )
}
