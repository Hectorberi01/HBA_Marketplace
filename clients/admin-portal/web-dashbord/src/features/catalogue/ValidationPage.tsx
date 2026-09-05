import { useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, VoileChargement } from '../../components/tableau/Etats'
import { Geste } from '../../components/Geste'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger, formaterDate, formaterMontant } from '../../lib/format'
import {
    MOTIFS_REFUS,
    approuverFiche,
    libelleStatutProduit,
    lireDecisions,
    listerFichesAValider,
    refuserFiche,
    type MotifRefus,
    type Produit,
} from './api'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * VALIDATION DES FICHES PRODUIT (§16).
 *
 * CES ROUTES EXISTAIENT ET N'ÉTAIENT APPELÉES PAR PERSONNE.
 *
 * `Product.Approve`, `Reject`, `Suspend` et `Restore` sont dans le domaine
 * depuis le lot 1, testés. Le service l'écrit : « appelés par personne. Le
 * parcours du §28 s'arrêtait à l'étape 4, et `ChangeProductStatusCommandHandler`
 * renvoyait le vendeur vers « l'API admin » — c'est-à-dire vers rien. » Une
 * fiche soumise ne pouvait donc pas être approuvée autrement qu'en base.
 *
 * LA FILE EST SERVIE PAR LE SERVEUR, PAS FILTRÉE ICI.
 *
 * `GET /admin/products/reviews` rend exactement les fiches en attente, paginées,
 * avec leur total. Filtrer `PendingReview` sur la liste générale donnerait le
 * même contenu au prix d'un contrat de plus à tenir — et le service a déjà
 * décidé ce qu'« en attente » veut dire.
 *
 * LE RELECTEUR VIENT DU JETON. Le portail n'envoie jamais d'identifiant de
 * relecteur : « un relecteur pris dans la requête permettrait d'attribuer sa
 * propre approbation à quelqu'un d'autre. Le journal `product_reviews` n'aurait
 * alors plus aucune valeur d'audit, ce qui est sa seule raison d'exister. »
 *
 * LE REFUS EXIGE AU MOINS UN MOTIF, IMPOSÉ AVANT L'ENVOI. L'agrégat rend
 * `catalog.review.reason_required` sur une liste vide ; le découvrir par un 422
 * serait un aller-retour pour apprendre ce qu'on sait déjà.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function ValidationPage() {
    const { etat, modifier } = useListeUrl('createdAtUtc')
    const client = useQueryClient()
    const [ouverte, setOuverte] = useState<string | null>(null)

    const requete = useQuery({
        queryKey: ['validation', etat.page, etat.taille],
        queryFn: ({ signal }) => listerFichesAValider(etat.page, etat.taille, signal),
        placeholderData: keepPreviousData,
    })

    function recharger() {
        void client.invalidateQueries({ queryKey: ['validation'] })
        // La liste générale du catalogue porte le même statut.
        void client.invalidateQueries({ queryKey: ['catalogue'] })
        void client.invalidateQueries({ queryKey: ['supervision'] })
        setOuverte(null)
    }

    const page = requete.data

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Fiches à valider</h1>
                <div className="filtres">
                    <Link to="/catalogue" className="lien-file">
                        Tout le catalogue
                    </Link>
                </div>
            </header>

            <p className="indice">
                Chaque décision est journalisée avec son auteur et le NUMÉRO DE RÉVISION de la
                fiche jugée. Un vendeur corrige souvent avant de lire le refus : sans ce numéro, il
                ne sait pas si les motifs portent sur ce qu'il voit ou sur ce qu'il a soumis trois
                jours plus tôt.
            </p>

            {requete.isError && (
                <EtatErreur erreur={requete.error} onReessayer={() => void requete.refetch()} />
            )}

            {!requete.isError && (
                <div className="tableau-enveloppe">
                    {requete.isFetching && <VoileChargement />}

                    {page && page.items.length === 0 ? (
                        <div className="etat-liste">
                            <p>Aucune fiche n'attend de décision.</p>
                            <p className="indice">
                                La file se remplit quand un vendeur soumet une fiche à la
                                validation — pas quand il la crée.
                            </p>
                        </div>
                    ) : (
                        <table className={`tableau ${requete.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Fiches en attente de validation, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Produit</th>
                                    <th scope="col">Vendeur</th>
                                    <th scope="col">Variantes</th>
                                    <th scope="col">État</th>
                                    <th scope="col">Décision</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(p => (
                                    <LigneFiche
                                        key={p.id}
                                        produit={p}
                                        ouverte={ouverte === p.id}
                                        onOuvrir={() => setOuverte(ouverte === p.id ? null : p.id)}
                                        apres={recharger}
                                    />
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

function LigneFiche({
    produit,
    ouverte,
    onOuvrir,
    apres,
}: {
    produit: Produit
    ouverte: boolean
    onOuvrir: () => void
    apres: () => void
}) {
    const prix = produit.variants.find(v => v.price != null)

    return (
        <>
            <tr className="a-traiter">
                <td>
                    <div className="cellule-titre">{produit.name}</div>
                    <div className="indice">
                        <code title={produit.id}>{abreger(produit.id)}</code>
                        {prix?.price != null &&
                            ` · ${formaterMontant(prix.price, prix.currency ?? 'XOF')}`}
                    </div>
                </td>
                <td>
                    {/*
                      * LE LIEN MÈNE À LA FICHE VENDEUR, PAS À UN NOM.
                      *
                      * `ProductSummary` ne porte que `sellerId` : le catalogue ne
                      * connaît pas le nom de la boutique, et l'inventer par un
                      * second appel par ligne ferait vingt requêtes pour une page.
                      * L'identifiant abrégé est cliquable, ce qui suffit à aller
                      * voir qui c'est.
                      */}
                    <Link to={`/vendeurs/${produit.sellerId}`}>
                        <code title={produit.sellerId}>{abreger(produit.sellerId)}</code>
                    </Link>
                </td>
                <td>{produit.variants.length}</td>
                <td>
                    <span className={`pastille pastille--${produit.status.toLowerCase()}`}>
                        {libelleStatutProduit(produit.status)}
                    </span>
                </td>
                <td>
                    <button type="button" className="lien-deconnexion" onClick={onOuvrir}>
                        {ouverte ? 'Fermer' : 'Examiner'}
                    </button>
                </td>
            </tr>

            {ouverte && (
                <tr>
                    <td colSpan={5}>
                        <Examen produit={produit} apres={apres} />
                    </td>
                </tr>
            )}
        </>
    )
}

function Examen({ produit, apres }: { produit: Produit; apres: () => void }) {
    const [motifs, setMotifs] = useState<MotifRefus[]>([])
    const [commentaire, setCommentaire] = useState('')

    const historique = useQuery({
        queryKey: ['validation', produit.id, 'decisions'],
        queryFn: ({ signal }) => lireDecisions(produit.id, signal),
    })

    function basculer(motif: MotifRefus) {
        setMotifs(actuels =>
            actuels.some(m => m.code === motif.code)
                ? actuels.filter(m => m.code !== motif.code)
                : [...actuels, motif],
        )
    }

    return (
        <div className="examen">
            <div className="examen__fiche">
                <h3>{produit.name}</h3>
                <p>{produit.description || <span className="indice">aucune description</span>}</p>
                <p className="indice">
                    {produit.media.length} image{produit.media.length > 1 ? 's' : ''} ·{' '}
                    {produit.variants.length} variante{produit.variants.length > 1 ? 's' : ''} ·{' '}
                    {produit.tags.length > 0 ? produit.tags.join(', ') : 'aucune étiquette'}
                </p>
                {produit.media.length === 0 && (
                    <p className="erreur-en-ligne">
                        Aucune image : la fiche ne sera pas vendable telle quelle.
                    </p>
                )}
            </div>

            <div className="examen__decision">
                <Geste
                    libelle="Approuver"
                    aide="La fiche devient publiable par le vendeur."
                    confirmation="Approbation journalisée à votre nom."
                    executer={() => approuverFiche(produit.id, commentaire)}
                    apres={apres}
                />

                <fieldset className="motifs">
                    <legend>Motifs de refus</legend>
                    {MOTIFS_REFUS.map(m => (
                        <label key={m.code} className="motif">
                            <input
                                type="checkbox"
                                checked={motifs.some(x => x.code === m.code)}
                                onChange={() => basculer(m)}
                            />
                            <span>{m.message}</span>
                        </label>
                    ))}
                </fieldset>

                <label className="commentaire">
                    Commentaire, lu par le vendeur
                    <textarea
                        rows={2}
                        value={commentaire}
                        onChange={e => setCommentaire(e.target.value)}
                        placeholder="Optionnel sur une approbation, utile sur un refus."
                    />
                </label>

                {motifs.length === 0 ? (
                    <p className="indice">
                        Cochez au moins un motif pour pouvoir refuser. Le service rend
                        <code> catalog.review.reason_required</code> sur un refus sans motif.
                    </p>
                ) : (
                    <Geste
                        libelle="Refuser"
                        danger
                        confirmation={`${motifs.length} motif${motifs.length > 1 ? 's' : ''} envoyé${motifs.length > 1 ? 's' : ''} au vendeur.`}
                        executer={() => refuserFiche(produit.id, motifs, commentaire)}
                        apres={apres}
                    />
                )}
            </div>

            <div className="examen__historique">
                <h4>Décisions précédentes</h4>
                {historique.isLoading && <p className="indice">Lecture…</p>}
                {historique.isError && (
                    <p className="indice">Historique indisponible.</p>
                )}
                {historique.data?.length === 0 && (
                    <p className="indice">Première décision sur cette fiche.</p>
                )}
                {(historique.data ?? []).map(d => (
                    <div key={d.id} className="decision">
                        <span className="cellule-titre">
                            {d.decision} · révision {d.revisionVersion}
                        </span>
                        <span className="indice">{formaterDate(d.reviewedAtUtc)}</span>
                        {d.comment && <p>{d.comment}</p>}
                        {d.reasons.length > 0 && (
                            <ul className="indice">
                                {d.reasons.map((r, i) => (
                                    <li key={i}>{r.message}</li>
                                ))}
                            </ul>
                        )}
                    </div>
                ))}
            </div>
        </div>
    )
}
