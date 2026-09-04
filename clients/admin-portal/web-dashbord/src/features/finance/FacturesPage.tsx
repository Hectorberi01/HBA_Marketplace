import { keepPreviousData, useQuery } from '@tanstack/react-query'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger, formaterMontant, formaterPeriode } from '../../lib/format'
import { libelleFacture, listerFactures, type Facture } from './api'

const CLES_EXTRA = ['vendeur'] as const

/** Forme d'un GUID, telle que la route l'exige (`Guid? sellerId`). */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * FACTURES VENDEUR — `GET /api/financial/invoices`.
 *
 * Paginée, enveloppée, avec facettes par statut. Deux filtres seulement :
 * `status` et `sellerId`. AUCUNE RECHERCHE TEXTUELLE — la route n'en accepte
 * pas, et le filtre vendeur prend un GUID, pas un nom de boutique.
 *
 * C'EST INCONFORTABLE, ET LE MASQUER SERAIT PIRE.
 *
 * Un champ qui accepterait « Chez Fatou » et ne trouverait rien laisserait
 * croire que cette boutique n'a pas de facture. Le champ annonce donc ce qu'il
 * attend, et refuse d'envoyer une valeur qui n'est pas un identifiant — sans
 * quoi la requête partirait avec un `sellerId` invalide et le serveur rendrait
 * une erreur de liage pour une saisie que l'utilisateur croyait légitime.
 *
 * L'identifiant se copie depuis l'écran Vendeurs, où il est affiché sous chaque
 * boutique. C'est le chemin, faute de mieux.
 */
export default function FacturesPage() {
    const { etat, modifier } = useListeUrl('periodEndUtc', CLES_EXTRA)
    const vendeur = etat.extra.vendeur ?? null
    const vendeurValide = vendeur === null || GUID.test(vendeur)

    const requete = useQuery({
        queryKey: ['factures', etat.page, etat.taille, etat.statut, vendeur],
        queryFn: ({ signal }) =>
            listerFactures(
                {
                    page: etat.page,
                    taille: etat.taille,
                    statut: etat.statut,
                    // Une valeur mal formée n'est PAS envoyée : le serveur
                    // rendrait une erreur de liage, illisible pour qui a
                    // simplement colle un identifiant tronque.
                    vendeur: vendeurValide ? vendeur : null,
                },
                signal,
            ),
        placeholderData: keepPreviousData,
    })

    const page = requete.data
    const filtre = Boolean(etat.statut || vendeur)

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Factures</h1>
                <div className="filtres">
                    <label>
                        Vendeur
                        <input
                            className="champ-guid"
                            value={vendeur ?? ''}
                            placeholder="identifiant du vendeur"
                            aria-label="Identifiant du vendeur"
                            aria-invalid={!vendeurValide}
                            onChange={e => modifier({ extra: { vendeur: e.target.value || null } })}
                        />
                    </label>
                    {filtre && (
                        <button
                            type="button"
                            className="lien-deconnexion"
                            onClick={() => modifier({ statut: null, extra: { vendeur: null } })}
                        >
                            Tout effacer
                        </button>
                    )}
                </div>
            </header>

            {!vendeurValide && (
                <p className="indice erreur-en-ligne">
                    Ce n'est pas un identifiant valide — le filtre vendeur attend le GUID
                    affiché sous chaque boutique dans l'écran Vendeurs. Le filtre n'est pas
                    appliqué tant que la saisie n'est pas complète.
                </p>
            )}

            <Facettes
                facettes={page?.facettes ?? null}
                actif={etat.statut}
                onChoisir={s => modifier({ statut: s })}
                libelle={libelleFacture}
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
                                Factures vendeur, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Période</th>
                                    <th scope="col">Vendeur</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col" className="au-bout">Montant</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(f => (
                                    <Ligne key={f.id} facture={f} />
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

function Ligne({ facture }: { facture: Facture }) {
    /*
     * UNE FACTURE ÉMISE ET NON PAYÉE EST CE QU'ON VIENT CHERCHER ICI.
     * `Draft` n'engage rien, `Paid` est clos ; entre les deux, quelqu'un doit
     * encaisser.
     */
    const aTraiter = facture.status === 'Issued'

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">
                    {formaterPeriode(facture.periodStartUtc, facture.periodEndUtc)}
                </div>
                <div className="indice">
                    <code title={facture.id}>{abreger(facture.id)}</code>
                </div>
            </td>
            <td>
                <code title={facture.sellerId}>{abreger(facture.sellerId)}</code>
            </td>
            <td>
                <span className={`pastille pastille--${facture.status.toLowerCase()}`}>
                    {libelleFacture(facture.status)}
                </span>
            </td>
            <td className="au-bout">{formaterMontant(facture.totalAmount, facture.currency)}</td>
        </tr>
    )
}
