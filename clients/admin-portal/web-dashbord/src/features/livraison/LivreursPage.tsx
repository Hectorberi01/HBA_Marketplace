import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { formaterDate } from '../../lib/format'
import {
    STATUTS_LIVREUR,
    libelleStatutLivreur,
    listerLivreurs,
    type Livreur,
} from './api'

const TAILLES = [100, 200, 500]

/**
 * DOSSIERS LIVREURS — `GET /api/v1/admin/drivers`.
 *
 * LE STATUT EST TOUJOURS ENVOYÉ, ET TOUJOURS AFFICHÉ.
 *
 * La route retombe sur `UnderReview` quand le paramètre manque. Une console qui
 * l'ignorerait montrerait « 3 livreurs » sur une plateforme qui en compte deux
 * cents, sans qu'aucun message ne dise qu'un filtre est actif — l'exploitation
 * en conclurait que la flotte est vide. Le sélecteur part donc de « À examiner »
 * comme le serveur, mais la sélection est VISIBLE et dans l'URL.
 *
 * IL N'Y A PAS D'OPTION « TOUS ». `status` n'est pas nullable côté service : il
 * se lie à une valeur de l'énumération, et une valeur vide serait refusée. Voir
 * l'ensemble des livreurs demande cinq requêtes, une par statut — ce que la
 * route n'invite pas à faire, et que ce n'est pas à l'écran de contourner en
 * silence.
 */
export default function LivreursPage() {
    const { etat, modifier } = useListeUrl('registeredAtUtc')
    const statut = etat.statut ?? 'UnderReview'
    const [take, setTake] = useState(100)
    const [filtre, setFiltre] = useState('')

    const requete = useQuery({
        queryKey: ['livreurs', statut, take],
        queryFn: ({ signal }) => listerLivreurs(statut, take, signal),
    })

    const affiches = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        if (!q) return requete.data ?? []
        return (requete.data ?? []).filter(
            l => l.fullName.toLowerCase().includes(q) || l.phone.includes(q),
        )
    }, [requete.data, filtre])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Livreurs</h1>
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

            {/*
              * Le statut est un GROUPE DE BOUTONS et non une liste déroulante :
              * c'est le filtre structurant de cet écran, et il doit se voir sans
              * qu'on l'ouvre. Pas de compteurs — le service n'en rend pas, et les
              * calculer sur la liste reçue donnerait le compte d'un seul statut.
              */}
            <div className="facettes" role="group" aria-label="Statut du dossier">
                {STATUTS_LIVREUR.map(s => (
                    <button
                        key={s}
                        type="button"
                        className={`facette ${statut === s ? 'is-active' : ''}`}
                        aria-pressed={statut === s}
                        onClick={() => modifier({ statut: s })}
                    >
                        {libelleStatutLivreur(s)}
                    </button>
                ))}
            </div>

            <p className="indice">
                Un seul statut à la fois : la route l'exige et retombe sur « À examiner » si
                on ne le précise pas. Le filtre de recherche porte sur les {take} dossiers
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
                                Dossiers livreurs au statut {libelleStatutLivreur(statut)},
                                {' '}{affiches.length} affichés
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Livreur</th>
                                    <th scope="col">Inscrit le</th>
                                    <th scope="col">Dossier</th>
                                    <th scope="col">Pièces</th>
                                    <th scope="col">Véhicules</th>
                                </tr>
                            </thead>
                            <tbody>
                                {affiches.map(l => (
                                    <Ligne key={l.driverId} livreur={l} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}
        </section>
    )
}

function Ligne({ livreur }: { livreur: Livreur }) {
    const incomplet = livreur.missingDocuments.length > 0

    /*
     * « VÉRIFIÉ » ET « PEUT RECEVOIR DES COURSES » NE SONT PAS LA MÊME CHOSE.
     *
     * `Dispatchable` est calculé par le service et tient compte d'autre chose
     * que du statut — un véhicule actif, notamment. Un dossier vérifié sans
     * véhicule ne prendra aucune course, et la seule façon de s'en apercevoir
     * est de regarder ce drapeau. Le service prévient d'ailleurs qu'il ne dit
     * rien de la disponibilité ni de la position : c'est un droit, pas une
     * présence.
     */
    const anomalie = livreur.verificationStatus === 'Verified' && !livreur.dispatchable

    return (
        <tr className={incomplet || anomalie ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{livreur.fullName}</div>
                <div className="indice">{livreur.phone}</div>
            </td>
            <td>
                {formaterDate(livreur.registeredAtUtc)}
                {livreur.submittedAtUtc && (
                    <div className="indice">déposé {formaterDate(livreur.submittedAtUtc)}</div>
                )}
            </td>
            <td>
                <span
                    className={`pastille pastille--${livreur.verificationStatus.toLowerCase()}`}
                >
                    {libelleStatutLivreur(livreur.verificationStatus)}
                </span>
                {/*
                  * LE MOTIF DE REFUS OU DE SUSPENSION EST AFFICHÉ. « Refusé »
                  * n'appelle aucun geste ; « permis expiré » en appelle un.
                  */}
                {livreur.statusReason && <div className="indice">{livreur.statusReason}</div>}
                {anomalie && (
                    <div className="indice erreur-en-ligne">
                        vérifié mais ne reçoit pas de course
                    </div>
                )}
            </td>
            <td>
                <div className="jetons">
                    {livreur.documents.map(d => (
                        <span
                            key={d.id}
                            className={`jeton ${d.status === 'Rejected' ? 'jeton--attention' : ''}`}
                            title={d.rejectionReason ?? d.status}
                        >
                            {d.type}
                        </span>
                    ))}
                    {/*
                      * LES PIÈCES MANQUANTES SONT L'INFORMATION LA PLUS UTILE DE
                      * CET ÉCRAN, et le service les calcule déjà. Sans elles, on
                      * lit « 2 pièces » sans savoir laquelle attendre.
                      */}
                    {livreur.missingDocuments.map(t => (
                        <span key={t} className="jeton jeton--inconnu" title="Pièce manquante">
                            {t} ?
                        </span>
                    ))}
                    {livreur.documents.length === 0 && livreur.missingDocuments.length === 0 && (
                        <span className="indice">aucune</span>
                    )}
                </div>
            </td>
            <td>
                {livreur.vehicles.length === 0 ? (
                    <span className="indice">aucun</span>
                ) : (
                    <div className="jetons">
                        {livreur.vehicles.map(v => (
                            <span
                                key={v.id}
                                className={`jeton ${v.active ? '' : 'jeton--inconnu'}`}
                                title={
                                    [v.make, v.model, v.plate].filter(Boolean).join(' ') ||
                                    undefined
                                }
                            >
                                {v.type}
                                {!v.active && ' (inactif)'}
                            </span>
                        ))}
                    </div>
                )}
            </td>
        </tr>
    )
}
