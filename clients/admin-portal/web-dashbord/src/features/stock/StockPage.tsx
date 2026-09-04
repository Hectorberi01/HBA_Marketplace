import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { abreger } from '../../lib/format'
import {
    libelleTypeLieu,
    listerLieux,
    listerSousSeuil,
    type ArticleStock,
    type LieuExpedition,
} from './api'

const TAILLES = [50, 100, 200]

/**
 * STOCK — deux lectures, et l'écran dit ce qu'elles ne font pas.
 *
 * `GET /api/inventory/low-stock?take=N` et `GET /api/inventory/locations`
 * rendent des listes NUES : ni pagination, ni recherche, ni tri, ni facettes.
 * Le seul levier côté serveur est `take`, qui BORNE la liste des articles sous
 * seuil — ce n'est pas une pagination, il n'existe pas de page suivante.
 *
 * LE FILTRE DE CET ÉCRAN EST LOCAL, ET C'EST ÉCRIT À L'ÉCRAN.
 *
 * Il ne cherche que dans ce qui a été chargé. Un champ de recherche muet sur ce
 * point ressemble en tout point à une recherche complète : on tape un SKU, on
 * ne le trouve pas, et l'on conclut qu'il n'existe pas — alors qu'il est
 * seulement au-delà de la limite. La phrase sous le champ évite précisément
 * cette conclusion.
 */
export default function StockPage() {
    const [take, setTake] = useState(50)
    const [filtre, setFiltre] = useState('')

    const articles = useQuery({
        queryKey: ['stock', 'sous-seuil', take],
        queryFn: ({ signal }) => listerSousSeuil(take, signal),
    })

    const lieux = useQuery({
        queryKey: ['stock', 'lieux'],
        queryFn: ({ signal }) => listerLieux(signal),
    })

    // Index des lieux, pour remplacer un GUID par un nom de commune dans le
    // tableau des articles. L'article ne porte que `locationId` ; sans cette
    // jointure locale, la colonne n'apprendrait rien à personne.
    const parLieu = useMemo(() => {
        const carte = new Map<string, LieuExpedition>()
        for (const l of lieux.data ?? []) carte.set(l.id, l)
        return carte
    }, [lieux.data])

    const filtres = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        if (!q) return articles.data ?? []
        return (articles.data ?? []).filter(a => a.sku.toLowerCase().includes(q))
    }, [articles.data, filtre])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Stock</h1>
                <div className="filtres">
                    <BarreRecherche
                        valeur={filtre}
                        onChange={setFiltre}
                        placeholder="Filtrer par SKU"
                    />
                    <label>
                        Limite
                        <select value={take} onChange={e => setTake(Number(e.target.value))}>
                            {TAILLES.map(t => (
                                <option key={t} value={t}>
                                    {t}
                                </option>
                            ))}
                        </select>
                    </label>
                </div>
            </header>

            <p className="indice">
                Articles dont la quantité disponible est passée sous le seuil de
                réapprovisionnement. Le filtre ne porte que sur les {take} articles chargés :
                l'API n'offre ni recherche ni pagination sur cette liste.
            </p>

            {articles.isError && (
                <EtatErreur erreur={articles.error} onReessayer={() => void articles.refetch()} />
            )}

            {!articles.isError && (
                <div className="tableau-enveloppe">
                    {articles.isFetching && <VoileChargement />}

                    {articles.data && filtres.length === 0 ? (
                        <EtatVide filtre={Boolean(filtre)} />
                    ) : (
                        <table className={`tableau ${articles.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Articles sous seuil, {filtres.length} affichés
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">SKU</th>
                                    <th scope="col">Lieu</th>
                                    <th scope="col" className="au-bout">En stock</th>
                                    <th scope="col" className="au-bout">Réservé</th>
                                    <th scope="col" className="au-bout">Disponible</th>
                                    <th scope="col" className="au-bout">Seuil</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filtres.map(a => (
                                    <LigneArticle key={a.id} article={a} lieu={parLieu.get(a.locationId)} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}

            <h2>Lieux d'expédition</h2>

            {lieux.isError ? (
                <EtatErreur erreur={lieux.error} onReessayer={() => void lieux.refetch()} />
            ) : (
                <div className="tableau-enveloppe">
                    {lieux.isFetching && <VoileChargement />}
                    {lieux.data && lieux.data.length === 0 ? (
                        <EtatVide filtre={false} />
                    ) : (
                        <table className={`tableau ${lieux.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Lieux d'expédition, {lieux.data?.length ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Lieu</th>
                                    <th scope="col">Type</th>
                                    <th scope="col">Commune</th>
                                    <th scope="col">Propriétaire</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(lieux.data ?? []).map(l => (
                                    <tr key={l.id}>
                                        <td>
                                            <div className="cellule-titre">
                                                {l.landmark ?? l.line ?? l.quartier ?? abreger(l.id)}
                                            </div>
                                            <div className="indice">
                                                <code title={l.id}>{abreger(l.id)}</code>
                                            </div>
                                        </td>
                                        <td>{libelleTypeLieu(l.type)}</td>
                                        <td>
                                            {l.communeName}
                                            {l.quartier && <div className="indice">{l.quartier}</div>}
                                        </td>
                                        <td>
                                            {l.ownerId ? (
                                                <code title={l.ownerId}>{abreger(l.ownerId)}</code>
                                            ) : (
                                                /*
                                                 * Un lieu SANS propriétaire appartient à la
                                                 * plateforme — c'est ce que dit `OwnerId` nul
                                                 * dans le domaine, pas une donnée manquante.
                                                 */
                                                <span className="indice">plateforme</span>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}
        </section>
    )
}

function LigneArticle({ article, lieu }: { article: ArticleStock; lieu?: LieuExpedition }) {
    /*
     * RUPTURE ET SOUS-SEUIL NE SONT PAS LA MÊME URGENCE.
     *
     * `available` à zéro veut dire qu'on ne peut plus vendre — c'est arrivé.
     * Sous le seuil veut dire que cela va arriver. Les afficher pareil noierait
     * le premier dans le second, alors que la liste est déjà, par construction,
     * entièrement composée d'articles sous seuil.
     */
    const enRupture = article.available <= 0

    return (
        <tr className={enRupture ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{article.sku}</div>
            </td>
            <td>
                {lieu ? (
                    <>
                        {lieu.communeName}
                        <div className="indice">{libelleTypeLieu(lieu.type)}</div>
                    </>
                ) : (
                    <code title={article.locationId}>{abreger(article.locationId)}</code>
                )}
            </td>
            <td className="au-bout">{article.onHand}</td>
            <td className="au-bout">{article.reserved}</td>
            <td className="au-bout">
                {article.available}
                {enRupture && <div className="indice erreur-en-ligne">rupture</div>}
            </td>
            <td className="au-bout">{article.reorderThreshold}</td>
        </tr>
    )
}
