import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { formaterDate, formaterMontant } from '../../lib/format'
import { DEVISE_TARIFS, listerRegles, type RegleTarifaire } from './api'

/**
 * GRILLE TARIFAIRE DE LIVRAISON — `GET /api/v1/admin/delivery-pricing/rules`.
 *
 * Liste enveloppée mais NON paginée : `ListRulesAsync` rend tout, trié par
 * priorité décroissante. Le filtre ci-dessous est local, et l'écran le dit.
 *
 * L'ORDRE D'AFFICHAGE EST L'ORDRE DE RÉSOLUTION.
 *
 * Le magasin choisit une règle en plaçant d'abord celles qui nomment un type de
 * véhicule (`OrderBy(r => r.VehicleType == null)`), puis en départageant par
 * priorité décroissante. Trier autrement — par nom, par date — donnerait une
 * grille qui ne se lit pas comme elle s'applique, et l'on chercherait longtemps
 * pourquoi une course est facturée au tarif de la troisième ligne.
 *
 * LA DEVISE N'EST PAS DANS LA RÈGLE. `PricingRule` ne porte aucun champ de
 * devise ; c'est le DEVIS qui en pose une, avec `request.Currency ?? "XOF"`.
 * L'écran affiche donc en francs CFA et l'annonce, faute de pouvoir faire mieux
 * avec ce contrat.
 */
export default function TarificationPage() {
    const regles = useQuery({
        queryKey: ['tarification'],
        queryFn: ({ signal }) => listerRegles(signal),
    })
    const [filtre, setFiltre] = useState('')
    const [inactives, setInactives] = useState(true)

    const affichees = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        return (regles.data ?? [])
            .filter(r => (inactives ? true : r.status === 'ACTIVE'))
            .filter(
                r =>
                    !q ||
                    r.name.toLowerCase().includes(q) ||
                    r.scope.toLowerCase().includes(q) ||
                    r.serviceLevel.toLowerCase().includes(q) ||
                    (r.vehicleType ?? '').toLowerCase().includes(q),
            )
            .sort((a, b) => {
                // Même ordre que le magasin : les règles qui nomment un véhicule
                // passent avant, puis la priorité décroissante départage.
                const vA = a.vehicleType == null ? 1 : 0
                const vB = b.vehicleType == null ? 1 : 0
                if (vA !== vB) return vA - vB
                return b.priority - a.priority
            })
    }, [regles.data, filtre, inactives])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Tarification</h1>
                <div className="filtres">
                    <BarreRecherche
                        valeur={filtre}
                        onChange={setFiltre}
                        placeholder="Filtrer par nom, portée ou véhicule"
                    />
                    <label>
                        <input
                            type="checkbox"
                            checked={inactives}
                            onChange={e => setInactives(e.target.checked)}
                        />
                        Afficher les règles inactives
                    </label>
                </div>
            </header>

            <p className="indice">
                Dans l'ordre où le service les applique : une règle qui nomme un type de
                véhicule passe avant, puis la priorité la plus haute l'emporte. Montants en
                francs CFA — la règle ne porte pas de devise, c'est le devis qui en pose une.
            </p>

            {regles.isError ? (
                <EtatErreur erreur={regles.error} onReessayer={() => void regles.refetch()} />
            ) : (
                <div className="tableau-enveloppe">
                    {regles.isFetching && <VoileChargement />}

                    {regles.data && affichees.length === 0 ? (
                        <EtatVide filtre={Boolean(filtre) || !inactives} />
                    ) : (
                        <table className={`tableau ${regles.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Règles tarifaires, {affichees.length} affichées
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Règle</th>
                                    <th scope="col">S'applique à</th>
                                    <th scope="col" className="au-bout">Base</th>
                                    <th scope="col" className="au-bout">Par km</th>
                                    <th scope="col" className="au-bout">Par minute</th>
                                    <th scope="col">Bornes</th>
                                    <th scope="col">Validité</th>
                                </tr>
                            </thead>
                            <tbody>
                                {affichees.map(r => (
                                    <Ligne key={r.id} regle={r} />
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            )}
        </section>
    )
}

function Ligne({ regle }: { regle: RegleTarifaire }) {
    const active = regle.status === 'ACTIVE'
    const majoration = regle.surgeMultiplier !== 1

    return (
        <tr className={active ? undefined : 'ligne-eteinte'}>
            <td>
                <div className="cellule-titre">{regle.name}</div>
                <div className="indice">
                    priorité {regle.priority}
                    {!active && ' · inactive'}
                </div>
            </td>
            <td>
                <div className="jetons">
                    <span className="jeton">{regle.scope}</span>
                    <span className="jeton">{regle.serviceLevel}</span>
                    {regle.vehicleType ? (
                        <span className="jeton">{regle.vehicleType}</span>
                    ) : (
                        // Pas de type de véhicule : la règle vaut pour TOUS, et
                        // c'est aussi ce qui la fait passer APRÈS celles qui en
                        // nomment un. Le dire évite de lire une donnée manquante.
                        <span className="jeton jeton--inconnu">tous véhicules</span>
                    )}
                </div>
                {majoration && (
                    /*
                     * LA MAJORATION MULTIPLIE TOUT LE SOUS-TOTAL. Un facteur 1,5
                     * discret dans une colonne étroite fait passer une course de
                     * 1 000 à 1 500 F sans que personne ne l'ait décidé ce
                     * matin-là. Elle mérite sa propre ligne.
                     */
                    <div className="indice erreur-en-ligne">
                        majoration ×{regle.surgeMultiplier}
                    </div>
                )}
            </td>
            <td className="au-bout">{formaterMontant(regle.baseFee, DEVISE_TARIFS)}</td>
            <td className="au-bout">{formaterMontant(regle.perKmFee, DEVISE_TARIFS)}</td>
            <td className="au-bout">{formaterMontant(regle.perMinuteFee, DEVISE_TARIFS)}</td>
            <td>
                <div className="jetons">
                    <span className="jeton">
                        min {formaterMontant(regle.minFee, DEVISE_TARIFS)}
                    </span>
                    {regle.maxFee != null && (
                        <span className="jeton">
                            max {formaterMontant(regle.maxFee, DEVISE_TARIFS)}
                        </span>
                    )}
                </div>
            </td>
            <td>
                {formaterDate(regle.activeFrom)}
                <div className="indice">
                    {regle.activeTo ? `jusqu'au ${formaterDate(regle.activeTo)}` : 'sans fin'}
                </div>
            </td>
        </tr>
    )
}
