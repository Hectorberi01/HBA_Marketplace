import { keepPreviousData, useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger, formaterMontant } from '../../lib/format'
import {
    STATUTS_A_TRAITER,
    libelleStatutProduit,
    listerProduits,
    type Produit,
} from './api'

/**
 * CONSOLE DU CATALOGUE — `/api/v1/catalog/admin/products`.
 *
 * CET ÉCRAN EST EN LECTURE SEULE, alors que le service expose quatre gestes de
 * validation : approve, reject, suspend, restore. Ils sont volontairement
 * absents pour l'instant.
 *
 * La raison n'est pas la difficulté, c'est ce qu'ils exigent autour : le refus
 * demande un motif que le vendeur lira, la suspension bloque une fiche que son
 * vendeur ne pourra pas relancer lui-même, et le journal `product_reviews` —
 * dont le service dit qu'il n'a « aucune autre raison d'exister » que l'audit —
 * doit rester lisible. Un bouton « Refuser » posé dans une ligne de tableau,
 * sans champ de motif ni confirmation, produirait exactement l'inverse.
 *
 * L'écran de validation a d'ailleurs son propre endpoint,
 * `GET /products/reviews`, qui rend les fiches en attente : c'est là que ces
 * gestes ont leur place, pas ici.
 */
export default function CataloguePage() {
    const { etat, modifier } = useListeUrl('name')

    const requete = useQuery({
        queryKey: ['produits', etat],
        queryFn: ({ signal }) =>
            listerProduits(
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
                <h1>Catalogue</h1>
                <BarreRecherche
                    valeur={etat.recherche}
                    onChange={q => modifier({ recherche: q })}
                    placeholder="Rechercher un produit"
                />
            </header>

            <Facettes
                facettes={page?.facettes ?? null}
                actif={etat.statut}
                onChoisir={s => modifier({ statut: s })}
                libelle={libelleStatutProduit}
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
                                Produits du catalogue, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Produit</th>
                                    <th scope="col">Vendeur</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Variantes</th>
                                    <th scope="col" className="au-bout">À partir de</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(p => (
                                    <Ligne key={p.id} produit={p} />
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

/**
 * PRIX D'APPEL : le plus bas des variantes.
 *
 * Un produit n'a pas de prix, ses variantes en ont. Afficher celui de la
 * première variante donnerait un chiffre qui dépend de l'ordre de la liste —
 * donc instable, et faux dès qu'une variante moins chère existe.
 */
function prixDAppel(produit: Produit): { montant: number; devise: string } | null {
    const prix = produit.variants
        .filter(v => typeof v.price === 'number')
        .map(v => ({ montant: v.price as number, devise: v.currency ?? 'XOF' }))
    if (prix.length === 0) return null
    return prix.reduce((bas, v) => (v.montant < bas.montant ? v : bas))
}

function Ligne({ produit }: { produit: Produit }) {
    const aTraiter = STATUTS_A_TRAITER.has(produit.status)
    const prix = prixDAppel(produit)

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{produit.name}</div>
                <div className="indice">
                    <code title={produit.id}>{abreger(produit.id)}</code>
                    {produit.slug && <> · {produit.slug}</>}
                </div>
            </td>
            <td>
                <code title={produit.sellerId}>{abreger(produit.sellerId)}</code>
            </td>
            <td>
                <span className={`pastille pastille--${produit.status.toLowerCase()}`}>
                    {libelleStatutProduit(produit.status)}
                </span>
            </td>
            <td>{produit.variants?.length ?? 0}</td>
            <td className="au-bout">
                {prix ? formaterMontant(prix.montant, prix.devise) : <span className="indice">—</span>}
            </td>
        </tr>
    )
}
