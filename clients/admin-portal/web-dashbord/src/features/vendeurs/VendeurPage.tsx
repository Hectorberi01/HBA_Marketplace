import { Link, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../../components/tableau/Etats'
import { Geste } from '../../components/Geste'
import { abreger, formaterDate, formaterTaux } from '../../lib/format'
import {
    activerVendeur,
    approuverKyb,
    approuverReactivation,
    leverSuspensionVendeur,
    libelleKyb,
    libelleStatutBoutique,
    libelleStatutVendeur,
    lireVendeur,
    refuserKyb,
    suspendreVendeur,
    type Boutique,
} from './api'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * FICHE D'UN VENDEUR — `GET /api/v1/merchants/{sellerId}`.
 *
 * ELLE PORTE CE QUE LA LISTE REFUSE DE PORTER, ET C'EST VOULU DES DEUX CÔTÉS.
 *
 * `SellerListItem` omet le compte de reversement, le RCCM, l'IFU et le
 * téléphone du gérant : « une console a le droit d'afficher ces données sur la
 * fiche qu'un humain ouvre, pas dans un listing qu'un écran charge au réveil ».
 * Cette page EST la fiche qu'un humain ouvre.
 *
 * DEUX ÉTATS INDÉPENDANTS, JAMAIS FONDUS EN UN SEUL.
 *
 * Le COMPTE (Pending, Active, Suspended, Closed) et le DOSSIER KYB (NotStarted,
 * InReview, Verified, Rejected) évoluent séparément. Un compte actif au dossier
 * refusé existe ; le contraire aussi. Les afficher côte à côte, avec leurs
 * gestes propres, est la seule façon de ne pas faire disparaître ces cas.
 *
 * LES GESTES NE DEVINENT RIEN. Les routes rendent 204 sans l'état d'après :
 * chacun invalide la fiche et la relit. Deviner marcherait presque toujours.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function VendeurPage() {
    const { sellerId = '' } = useParams()
    const client = useQueryClient()

    const requete = useQuery({
        queryKey: ['vendeur', sellerId],
        queryFn: ({ signal }) => lireVendeur(sellerId, signal),
        enabled: sellerId !== '',
    })

    function recharger() {
        void client.invalidateQueries({ queryKey: ['vendeur', sellerId] })
        // La liste porte le même statut : la laisser en cache montrerait
        // « En revue » sur un dossier qu'on vient d'approuver.
        void client.invalidateQueries({ queryKey: ['vendeurs'] })
    }

    const v = requete.data

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <div>
                    <p className="indice">
                        <Link to="/vendeurs">← Vendeurs</Link>
                    </p>
                    <h1>{v?.shopName ?? 'Vendeur'}</h1>
                </div>
                {requete.isFetching && <VoileChargement />}
            </header>

            {requete.isError && (
                <EtatErreur erreur={requete.error} onReessayer={() => void requete.refetch()} />
            )}

            {v && (
                <>
                    <div className="tuiles">
                        <div className="tuile">
                            <span className="tuile__titre">Compte</span>
                            <span className={`pastille pastille--${v.status.toLowerCase()}`}>
                                {libelleStatutVendeur(v.status)}
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Dossier KYB</span>
                            <span className={`pastille pastille--${v.kybStatus.toLowerCase()}`}>
                                {libelleKyb(v.kybStatus)}
                            </span>
                            {v.kybRejectionReason && (
                                <span className="indice">{v.kybRejectionReason}</span>
                            )}
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Commission</span>
                            <span className="tuile__valeur">{formaterTaux(v.commissionRate)}</span>
                            <span className="indice">
                                Le domaine la stocke en fraction : 0,1 vaut dix pour cent.
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Ventes</span>
                            <span className="tuile__valeur">
                                {v.salesCount.toLocaleString('fr-FR')}
                            </span>
                            <span className="indice">
                                Note {v.rating ? v.rating.toFixed(1) : '—'} · alimentée par le
                                module Avis
                            </span>
                        </div>
                        <div className="tuile">
                            <span className="tuile__titre">Inscrit le</span>
                            <span className="tuile__valeur">
                                {formaterDate(v.createdOnUtc).split(' ')[0]}
                            </span>
                            <span className="indice">
                                <code title={v.id}>{abreger(v.id)}</code>
                            </span>
                        </div>
                    </div>

                    <h2>Gouvernance</h2>
                    <div className="gestes">
                        {/*
                          * LES GESTES OFFERTS SUIVENT L'ÉTAT, PAS L'INVENTAIRE DES
                          * ROUTES. Proposer « lever la suspension » sur un compte
                          * actif ferait découvrir la transition interdite par une
                          * erreur — après le clic, et sans que rien n'explique
                          * pourquoi le bouton existait.
                          */}
                        {v.kybStatus === 'InReview' && (
                            <>
                                <Geste
                                    libelle="Valider le dossier KYB"
                                    aide="Les pièces sont conformes."
                                    confirmation="Le dossier passe en Vérifié. Le vendeur peut alors être activé."
                                    executer={() => approuverKyb(v.id)}
                                    apres={recharger}
                                />
                                <Geste
                                    libelle="Refuser le dossier"
                                    danger
                                    motif
                                    placeholderMotif="Ce que le vendeur doit corriger"
                                    aide="Le motif lui est renvoyé tel quel."
                                    executer={m => refuserKyb(v.id, m)}
                                    apres={recharger}
                                />
                            </>
                        )}

                        {v.status === 'Pending' && (
                            <Geste
                                libelle="Activer le compte"
                                aide="Le vendeur peut ouvrir ses boutiques et vendre."
                                confirmation="Le compte passe en Actif."
                                executer={() => activerVendeur(v.id)}
                                apres={recharger}
                            />
                        )}

                        {v.status === 'Active' && (
                            <Geste
                                libelle="Suspendre le compte"
                                danger
                                motif
                                placeholderMotif="Motif de la suspension"
                                aide="Coupe la vente sur toutes ses boutiques."
                                executer={m => suspendreVendeur(v.id, m)}
                                apres={recharger}
                            />
                        )}

                        {v.status === 'Suspended' && (
                            <>
                                <Geste
                                    libelle="Lever la suspension"
                                    confirmation="Le compte redevient actif."
                                    executer={() => leverSuspensionVendeur(v.id)}
                                    apres={recharger}
                                />
                                <Geste
                                    libelle="Approuver la réactivation"
                                    aide="À utiliser quand le vendeur a lui-même demandé sa réactivation."
                                    confirmation="Répond à une demande du vendeur, ce que « lever la suspension » ne fait pas."
                                    executer={() => approuverReactivation(v.id)}
                                    apres={recharger}
                                />
                            </>
                        )}
                    </div>
                    <p className="indice">
                        La SUPPRESSION d'un vendeur existe côté service et n'est pas offerte ici :
                        elle est définitive et n'a pas d'inverse. Elle se fait à la main, en
                        connaissance de cause.
                    </p>

                    <h2>Boutiques</h2>
                    {v.stores.length === 0 ? (
                        <p className="indice">
                            Ce vendeur n'a ouvert aucune boutique. Un compte actif sans boutique ne
                            vend rien — c'est un état normal juste après l'inscription, et un signal
                            s'il dure.
                        </p>
                    ) : (
                        <div className="tuiles">
                            {v.stores.map(b => (
                                <CarteBoutique key={b.id} sellerId={v.id} boutique={b} />
                            ))}
                        </div>
                    )}

                    <h2>Dossier</h2>
                    <div className="fiche">
                        <Champ nom="Raison sociale" valeur={v.metadata?.legalName} />
                        <Champ nom="RCCM" valeur={v.metadata?.rccm} />
                        <Champ nom="IFU" valeur={v.metadata?.ifu} />
                        <Champ nom="Téléphone du gérant" valeur={v.metadata?.managerPhone} />
                        <Champ
                            nom="Adresse"
                            valeur={[v.metadata?.addressLine, v.metadata?.city]
                                .filter(Boolean)
                                .join(', ')}
                        />
                        <Champ nom="Pièces déposées" valeur={String(v.kybDocuments.length)} />
                    </div>

                    <h2>Compte de reversement</h2>
                    {v.payout ? (
                        <div className="fiche">
                            <Champ nom="Fournisseur" valeur={v.payout.provider} />
                            <Champ nom="Intitulé" valeur={v.payout.accountName} />
                            <Champ nom="Établissement" valeur={v.payout.bankName} />
                            <Champ nom="Numéro" valeur={v.payout.accountNumber} />
                            <Champ nom="Devise" valeur={v.payout.currency} />
                        </div>
                    ) : (
                        <p className="indice erreur-en-ligne">
                            Aucun compte de reversement. Les règlements de ce vendeur échoueront :
                            wallet-service lit ce champ et refuse la demande quand il est vide.
                        </p>
                    )}
                </>
            )}
        </section>
    )
}

function CarteBoutique({ sellerId, boutique }: { sellerId: string; boutique: Boutique }) {
    return (
        <Link
            to={`/vendeurs/${sellerId}/boutiques/${boutique.id}`}
            className="tuile tuile--lien"
        >
            <span className="tuile__titre">{boutique.name}</span>
            <span className={`pastille pastille--${boutique.status.toLowerCase()}`}>
                {libelleStatutBoutique(boutique.status)}
            </span>
            <span className="indice">
                {/*
                  * `isSelling` N'EST PAS `status === 'Open'`. Une boutique ouverte
                  * dont le vendeur est suspendu ne vend pas, et c'est ce booléen —
                  * calculé par le service — qui le dit. Recalculer ici « ouverte
                  * donc en vente » afficherait le contraire de la réalité.
                  */}
                {boutique.isSelling ? 'Vend actuellement' : 'Ne vend pas'}
                {boutique.statusReason ? ` · ${boutique.statusReason}` : ''}
            </span>
        </Link>
    )
}

function Champ({ nom, valeur }: { nom: string; valeur?: string | null }) {
    return (
        <div className="fiche__ligne">
            <span className="fiche__nom">{nom}</span>
            <span className="fiche__valeur">
                {valeur && valeur.trim() ? valeur : <span className="indice">non renseigné</span>}
            </span>
        </div>
    )
}
