import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../../components/tableau/Etats'
import { Geste } from '../../components/Geste'
import { abreger, formaterDate, formaterMontant } from '../../lib/format'
import {
    libelleCondition,
    libelleStatutOffre,
    listerOffresBoutique,
    type Offre,
} from '../catalogue/api'
import {
    leverSuspensionBoutique,
    libelleStatutBoutique,
    lireBoutique,
    suspendreBoutique,
} from './api'

const JOURS = ['Dimanche', 'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi']

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * FICHE D'UNE BOUTIQUE : CE QU'ELLE EST, CE QU'ELLE VEND, ET CE QUE ÇA DIT.
 *
 * DEUX SERVICES, DEUX LECTURES.
 *
 *   seller-service  GET /api/v1/merchants/{sellerId}/stores/{storeId}
 *   catalog-service GET /api/v1/catalog/seller/stores/{storeId}/offers
 *
 * La première décrit la vitrine — nom, contact, horaires, état. La seconde dit
 * ce qui y est en vente. Aucune ne connaît l'autre, et c'est cette page qui les
 * rapproche.
 *
 * LES STATISTIQUES SONT CALCULÉES SUR LA LISTE COMPLÈTE, PAS SUR UNE PAGE.
 *
 * `ListStoreOffersQuery(storeId)` ne prend ni page ni borne : ce que le service
 * rend EST tout ce que la boutique vend. Les comptes ci-dessous sont donc
 * exacts, pas des planchers — c'est la différence avec les écrans de stock et de
 * livreurs, où le portail affiche « au moins N ».
 *
 * CE QUI N'EST PAS AFFICHÉ, ET POURQUOI.
 *
 *   PAS DE CHIFFRE D'AFFAIRES DE LA BOUTIQUE. Les commandes se lisent par
 *   VENDEUR (`GET /api/sellers/{sellerId}/orders`, ouverte à l'administration),
 *   jamais par boutique, et cette route rend une liste bornée par `take` sans
 *   total. Un montant calculé dessus serait un plancher présenté comme un
 *   chiffre d'affaires.
 *
 *   PAS DE STOCK. inventory-service ne joint pas les offres : le rapprochement
 *   se ferait offre par offre, et une rupture est déjà dite par le statut
 *   `OutOfStock`, que le service pose lui-même.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function BoutiquePage() {
    const { sellerId = '', storeId = '' } = useParams()
    const client = useQueryClient()

    const boutique = useQuery({
        queryKey: ['boutique', sellerId, storeId],
        queryFn: ({ signal }) => lireBoutique(sellerId, storeId, signal),
        enabled: sellerId !== '' && storeId !== '',
    })

    const offres = useQuery({
        queryKey: ['boutique', storeId, 'offres'],
        queryFn: ({ signal }) => listerOffresBoutique(storeId, signal),
        enabled: storeId !== '',
    })

    function recharger() {
        void client.invalidateQueries({ queryKey: ['boutique', sellerId, storeId] })
        void client.invalidateQueries({ queryKey: ['vendeur', sellerId] })
    }

    const stats = useMemo(() => calculer(offres.data ?? []), [offres.data])
    const b = boutique.data

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <div>
                    <p className="indice">
                        <Link to={`/vendeurs/${sellerId}`}>← Fiche vendeur</Link>
                    </p>
                    <h1>{b?.name ?? 'Boutique'}</h1>
                </div>
                {(boutique.isFetching || offres.isFetching) && <VoileChargement />}
            </header>

            {boutique.isError && (
                <EtatErreur erreur={boutique.error} onReessayer={() => void boutique.refetch()} />
            )}

            {b && (
                <>
                    <div className="tuiles">
                        <div className="tuile">
                            <span className="tuile__titre">État</span>
                            <span className={`pastille pastille--${b.status.toLowerCase()}`}>
                                {libelleStatutBoutique(b.status)}
                            </span>
                            <span className="indice">
                                {b.isSelling ? 'Vend actuellement' : 'Ne vend pas'}
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Contact</span>
                            <span className="fiche__valeur">{b.contactPhone}</span>
                            <span className="indice">{b.contactEmail ?? 'pas de courriel'}</span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Ouverte le</span>
                            <span className="tuile__valeur">
                                {formaterDate(b.createdOnUtc).split(' ')[0]}
                            </span>
                            <span className="indice">
                                <code title={b.id}>{abreger(b.id)}</code>
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Lieu logistique</span>
                            <span className="fiche__valeur">
                                {b.fulfillmentLocationId ? (
                                    <code>{abreger(b.fulfillmentLocationId)}</code>
                                ) : (
                                    <span className="indice">aucun</span>
                                )}
                            </span>
                            <span className="indice">
                                Sans lieu rattaché, aucune expédition n'est planifiable.
                            </span>
                        </div>
                    </div>

                    {b.statusReason && <p className="erreur-en-ligne">{b.statusReason}</p>}

                    <h2>Gouvernance</h2>
                    <div className="gestes">
                        {b.status === 'Suspended' ? (
                            <Geste
                                libelle="Lever la suspension"
                                confirmation="La boutique redevient ouverte. Le compte du vendeur n'est pas touché."
                                executer={() => leverSuspensionBoutique(sellerId, b.id)}
                                apres={recharger}
                            />
                        ) : (
                            <Geste
                                libelle="Suspendre la boutique"
                                danger
                                motif
                                placeholderMotif="Motif de la suspension"
                                aide="Ferme cette vitrine seule — les autres boutiques du vendeur continuent."
                                executer={m => suspendreBoutique(sellerId, b.id, m)}
                                apres={recharger}
                            />
                        )}
                    </div>

                    <h2>Horaires</h2>
                    {b.openingHours.length === 0 ? (
                        <p className="indice">Aucun horaire déclaré.</p>
                    ) : (
                        <div className="fiche">
                            {b.openingHours.map((h, i) => (
                                <div className="fiche__ligne" key={i}>
                                    <span className="fiche__nom">
                                        {typeof h.dayOfWeek === 'number'
                                            ? (JOURS[h.dayOfWeek] ?? `Jour ${h.dayOfWeek}`)
                                            : h.dayOfWeek}
                                    </span>
                                    <span className="fiche__valeur">
                                        {h.isClosed || !h.opensAt
                                            ? 'Fermé'
                                            : `${h.opensAt} — ${h.closesAt ?? '?'}`}
                                    </span>
                                </div>
                            ))}
                        </div>
                    )}
                </>
            )}

            <h2>Ce que la boutique vend</h2>

            {offres.isError ? (
                <EtatErreur erreur={offres.error} onReessayer={() => void offres.refetch()} />
            ) : (
                <>
                    <div className="tuiles">
                        <div className="tuile">
                            <span className="tuile__titre">Mises en vente</span>
                            <span className="tuile__valeur">{stats.total}</span>
                            <span className="indice">
                                Une offre par variante : deux tailles font deux offres.
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">En vente</span>
                            <span className="tuile__valeur">{stats.parStatut.Active ?? 0}</span>
                            <span className="indice">
                                {stats.parStatut.OutOfStock ?? 0} en rupture ·{' '}
                                {stats.parStatut.Paused ?? 0} en pause
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">En promotion</span>
                            <span className="tuile__valeur">{stats.enPromotion}</span>
                            <span className="indice">
                                Offres dont le prix promotionnel s'applique aujourd'hui.
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Préparation</span>
                            <span className="tuile__valeur">
                                {stats.total > 0 ? `${stats.delaiMedian} j` : '—'}
                            </span>
                            <span className="indice">
                                Délai MÉDIAN, pas moyen : une offre à trente jours ne doit pas
                                déplacer le chiffre de toute la boutique.
                            </span>
                        </div>
                    </div>

                    {/*
                      * UNE TUILE DE PRIX PAR DEVISE, JAMAIS UNE SOMME.
                      *
                      * L'offre porte sa propre devise. Additionner ou moyenner des
                      * XOF avec des EUR donnerait un nombre faux d'un facteur six
                      * cent cinquante, et qui aurait l'air plausible.
                      */}
                    {stats.devises.length > 0 && (
                        <div className="tuiles">
                            {stats.devises.map(d => (
                                <div className="tuile" key={d.devise}>
                                    <span className="tuile__titre">Prix · {d.devise}</span>
                                    <span className="fiche__valeur">
                                        {formaterMontant(d.min, d.devise)} —{' '}
                                        {formaterMontant(d.max, d.devise)}
                                    </span>
                                    <span className="indice">
                                        {d.nombre} offre{d.nombre > 1 ? 's' : ''} · commission
                                        encaissée {formaterMontant(d.commission, d.devise)}
                                    </span>
                                </div>
                            ))}
                        </div>
                    )}

                    <div className="tableau-enveloppe">
                        {(offres.data ?? []).length === 0 ? (
                            <div className="etat-liste">
                                <p>Cette boutique ne vend rien.</p>
                                <p className="indice">
                                    Une boutique ouverte sans offre est invisible pour les
                                    acheteurs. Les fiches produit du vendeur peuvent exister sans
                                    être mises en vente ici.
                                </p>
                            </div>
                        ) : (
                            <table className="tableau">
                                <caption className="visuellement-cache">
                                    Offres de la boutique, {stats.total} au total
                                </caption>
                                <thead>
                                    <tr>
                                        <th scope="col">Produit</th>
                                        <th scope="col">SKU</th>
                                        <th scope="col">Prix</th>
                                        <th scope="col">État</th>
                                        <th scope="col">Condition</th>
                                        <th scope="col">Préparation</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {(offres.data ?? []).map(o => (
                                        <LigneOffre key={o.id} offre={o} />
                                    ))}
                                </tbody>
                            </table>
                        )}
                    </div>
                </>
            )}
        </section>
    )
}

function LigneOffre({ offre }: { offre: Offre }) {
    const enPromo =
        offre.promotionalPrice != null && offre.effectivePrice < offre.buyerPrice

    return (
        <tr>
            <td>
                <div className="cellule-titre">{offre.productName}</div>
                <div className="indice">
                    <code title={offre.productId}>{abreger(offre.productId)}</code>
                </div>
            </td>
            <td>{offre.sku ?? <span className="indice">—</span>}</td>
            <td>
                {formaterMontant(offre.effectivePrice, offre.currency)}
                {enPromo && (
                    <div className="indice">
                        au lieu de {formaterMontant(offre.buyerPrice, offre.currency)}
                        {offre.promotionEndsOnUtc
                            ? ` jusqu'au ${formaterDate(offre.promotionEndsOnUtc).split(' ')[0]}`
                            : ''}
                    </div>
                )}
            </td>
            <td>
                <span className={`pastille pastille--${offre.status.toLowerCase()}`}>
                    {libelleStatutOffre(offre.status)}
                </span>
                {offre.statusReason && <div className="indice">{offre.statusReason}</div>}
            </td>
            <td>{libelleCondition(offre.condition)}</td>
            <td>{offre.handlingTimeDays} j</td>
        </tr>
    )
}

type ParDevise = {
    devise: string
    nombre: number
    min: number
    max: number
    commission: number
}

/**
 * Les statistiques d'une boutique, tirées de la liste COMPLÈTE de ses offres.
 *
 * LE DÉLAI EST UNE MÉDIANE. La moyenne d'un ensemble où une offre annonce
 * trente jours et vingt autres en annoncent deux rend « 3,3 jours » — un chiffre
 * que rien ne vérifie et qui décrit une boutique qui n'existe pas.
 */
function calculer(offres: Offre[]) {
    const parStatut: Record<string, number> = {}
    const devises = new Map<string, ParDevise>()
    const delais: number[] = []
    let enPromotion = 0

    for (const o of offres) {
        parStatut[o.status] = (parStatut[o.status] ?? 0) + 1
        delais.push(o.handlingTimeDays)

        if (o.promotionalPrice != null && o.effectivePrice < o.buyerPrice) enPromotion += 1

        const courant = devises.get(o.currency)
        if (courant) {
            courant.nombre += 1
            courant.min = Math.min(courant.min, o.effectivePrice)
            courant.max = Math.max(courant.max, o.effectivePrice)
            courant.commission += o.commissionAmount
        } else {
            devises.set(o.currency, {
                devise: o.currency,
                nombre: 1,
                min: o.effectivePrice,
                max: o.effectivePrice,
                commission: o.commissionAmount,
            })
        }
    }

    const tries = [...delais].sort((a, b) => a - b)
    const delaiMedian = tries.length === 0 ? 0 : tries[Math.floor(tries.length / 2)]

    return {
        total: offres.length,
        parStatut,
        enPromotion,
        delaiMedian,
        devises: [...devises.values()].sort((a, b) => b.nombre - a.nombre),
    }
}
