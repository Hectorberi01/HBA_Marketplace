import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { formaterMontant } from '../../lib/format'
import {
    libelleJour,
    libelleStatutEtablissement,
    listerEnAttente,
    type Etablissement,
} from './api'

const TAILLES = [100, 200]

/**
 * FILE DE VALIDATION DES ÉTABLISSEMENTS —
 * `GET /api/food/admin/restaurants/pending`.
 *
 * CE N'EST PAS UN ANNUAIRE, ET LE TITRE SEUL POURRAIT LE FAIRE CROIRE.
 *
 * C'est la seule lecture que le groupe d'administration expose, et elle ne rend
 * que les dossiers en attente. Aucune route ne liste les établissements en
 * activité, suspendus ou refusés. Un écran muet là-dessus laisserait penser que
 * la plateforme compte trois restaurants ; la phrase sous le titre l'énonce.
 *
 * CE QUE L'ÉCRAN REGARDE VRAIMENT : LA COMPLÉTUDE DU DOSSIER.
 *
 * Approuver un établissement le met en vente. Trois champs décident s'il pourra
 * réellement travailler, et aucun n'apparaît dans son statut :
 *
 *   `payoutSellerId`        nul -> il vend et n'est jamais payé ;
 *   `fulfillmentLocationId` nul -> aucun livreur ne sait où retirer ;
 *   `serviceHours` vides    -> il n'ouvre jamais.
 *
 * Ce sont exactement les questions qu'un validateur doit se poser, et les
 * chercher une par une dans un écran de détail les ferait oublier.
 */
export default function EtablissementsPage() {
    const [take, setTake] = useState(100)
    const [filtre, setFiltre] = useState('')

    const requete = useQuery({
        queryKey: ['etablissements', take],
        queryFn: ({ signal }) => listerEnAttente(take, signal),
    })

    const affiches = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        if (!q) return requete.data ?? []
        return (requete.data ?? []).filter(
            e => e.name.toLowerCase().includes(q) || e.phone.includes(q),
        )
    }, [requete.data, filtre])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Établissements</h1>
                <div className="filtres">
                    <BarreRecherche
                        valeur={filtre}
                        onChange={setFiltre}
                        placeholder="Filtrer par nom ou téléphone"
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
                Dossiers <strong>en attente de validation</strong> uniquement. Le service
                n'expose aucune route listant les établissements en activité, suspendus ou
                refusés : ce n'est pas un annuaire. Le filtre porte sur les {take} dossiers
                chargés.
            </p>

            {requete.isError ? (
                <EtatErreur erreur={requete.error} onReessayer={() => void requete.refetch()} />
            ) : (
                <div className="tableau-enveloppe">
                    {requete.isFetching && <VoileChargement />}

                    {requete.data && affiches.length === 0 ? (
                        <EtatVide filtre={Boolean(filtre)} />
                    ) : (
                        <table className={`tableau ${requete.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Établissements en attente de validation, {affiches.length} affichés
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Établissement</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Dossier</th>
                                    <th scope="col">Service</th>
                                    <th scope="col" className="au-bout">Panier minimum</th>
                                </tr>
                            </thead>
                            <tbody>
                                {affiches.map(e => (
                                    <Ligne key={e.id} etablissement={e} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}
        </section>
    )
}

function Ligne({ etablissement }: { etablissement: Etablissement }) {
    const manques: string[] = []
    if (!etablissement.payoutSellerId) manques.push('aucun dossier de paiement')
    if (!etablissement.fulfillmentLocationId) manques.push('aucun point de retrait')
    if (etablissement.serviceHours.length === 0) manques.push('aucune heure de service')

    return (
        <tr className={manques.length > 0 ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{etablissement.name}</div>
                <div className="indice">{etablissement.phone}</div>
                {etablissement.description && (
                    <div className="indice">{etablissement.description}</div>
                )}
            </td>
            <td>
                <span
                    className={`pastille pastille--${etablissement.status.toLowerCase()}`}
                >
                    {libelleStatutEtablissement(etablissement.status)}
                </span>
                {/*
                  * `BlockedReason` est rendu par le service à côté de
                  * `AcceptsOrdersNow` : c'est lui qui dit POURQUOI un
                  * établissement ne prend pas de commande à cet instant. Le
                  * masquer laisserait un « non » sans explication.
                  */}
                {!etablissement.acceptsOrdersNow && etablissement.blockedReason && (
                    <div className="indice">{etablissement.blockedReason}</div>
                )}
                {etablissement.specialClosureReason && (
                    <div className="indice erreur-en-ligne">
                        {etablissement.specialClosureReason}
                    </div>
                )}
            </td>
            <td>
                {manques.length === 0 ? (
                    <span className="jeton">complet</span>
                ) : (
                    <div className="jetons">
                        {manques.map(m => (
                            <span key={m} className="jeton jeton--attention">
                                {m}
                            </span>
                        ))}
                    </div>
                )}
            </td>
            <td>
                <div className="jetons">
                    <span className="jeton">{etablissement.acceptanceMode}</span>
                    <span className="jeton">{etablissement.preparationMinutes} min</span>
                    {etablissement.extraWaitMinutes > 0 && (
                        <span className="jeton jeton--attention">
                            +{etablissement.extraWaitMinutes} min ({etablissement.loadLevel})
                        </span>
                    )}
                </div>
                {etablissement.serviceHours.length > 0 && (
                    <div className="indice">
                        {etablissement.serviceHours
                            .map(h => `${libelleJour(h.day)} ${h.opensAt}–${h.closesAt}`)
                            .join(' · ')}
                    </div>
                )}
            </td>
            <td className="au-bout">
                {etablissement.minimumOrderAmount == null ? (
                    <span className="indice">aucun</span>
                ) : (
                    // LA DEVISE N'EST PAS DANS LE CONTRAT. `MinimumOrderAmount` est
                    // un `decimal?` nu — le service ne dit pas en quelle monnaie.
                    // On affiche en francs CFA, faute de mieux, et le commentaire
                    // existe pour que personne ne le prenne pour une certitude.
                    formaterMontant(etablissement.minimumOrderAmount, 'XOF')
                )}
            </td>
        </tr>
    )
}
