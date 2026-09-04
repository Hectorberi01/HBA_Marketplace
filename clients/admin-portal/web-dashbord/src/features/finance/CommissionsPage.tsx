import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { abreger, formaterDate, formaterMontant, formaterTaux } from '../../lib/format'
import { libellePortee, listerRegles, type RegleCommission } from './api'

/**
 * GRILLE DE COMMISSION — `GET /api/financial/commissions`.
 *
 * Liste NUE : `ListCommissionRulesQuery` ne prend aucun paramètre. Filtre local,
 * annoncé.
 *
 * TROIS PORTÉES, ET LEUR ORDRE EST LE FOND DU SUJET.
 *
 * `CommissionScope` vaut Global, Category ou Seller. Une règle « Seller » prime
 * sur une règle « Category », qui prime sur « Global » — c'est ainsi que se
 * négocie un taux particulier. L'écran trie donc du plus général au plus
 * spécifique, pour que la grille se lise comme elle s'applique.
 *
 * LES RÈGLES INACTIVES SONT AFFICHÉES, PAS MASQUÉES. Une règle désactivée
 * explique pourquoi un taux a changé le mois dernier ; la cacher rendrait cette
 * question sans réponse.
 */
export default function CommissionsPage() {
    const regles = useQuery({
        queryKey: ['commissions'],
        queryFn: ({ signal }) => listerRegles(signal),
    })
    const [filtre, setFiltre] = useState('')
    const [inactives, setInactives] = useState(true)

    const affichees = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        const rang: Record<string, number> = { Global: 0, Category: 1, Seller: 2 }
        return (regles.data ?? [])
            .filter(r => (inactives ? true : r.isActive))
            .filter(
                r =>
                    !q ||
                    libellePortee(r.scope).toLowerCase().includes(q) ||
                    r.scope.toLowerCase().includes(q) ||
                    (r.targetId ?? '').toLowerCase().includes(q),
            )
            .sort((a, b) => {
                const p = (rang[a.scope] ?? 9) - (rang[b.scope] ?? 9)
                if (p !== 0) return p
                // À portée égale, la plus récemment entrée en vigueur d'abord :
                // c'est celle qui s'applique aujourd'hui.
                return b.effectiveFromUtc.localeCompare(a.effectiveFromUtc)
            })
    }, [regles.data, filtre, inactives])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Commissions</h1>
                <div className="filtres">
                    <BarreRecherche
                        valeur={filtre}
                        onChange={setFiltre}
                        placeholder="Filtrer par portée ou cible"
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
                Triées du plus général au plus spécifique : une règle vendeur prime sur une
                règle catégorie, qui prime sur la règle plateforme. L'API rend la grille
                entière en une fois — le filtre porte sur ce qui est déjà chargé.
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
                                Règles de commission, {affichees.length} affichées
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Portée</th>
                                    <th scope="col" className="au-bout">Taux</th>
                                    <th scope="col" className="au-bout">Part fixe</th>
                                    <th scope="col">Bornes</th>
                                    <th scope="col">En vigueur depuis</th>
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

function Ligne({ regle }: { regle: RegleCommission }) {
    return (
        <tr className={regle.isActive ? undefined : 'ligne-eteinte'}>
            <td>
                <div className="cellule-titre">
                    {libellePortee(regle.scope)}
                    {!regle.isActive && <span className="jeton"> inactive</span>}
                </div>
                {regle.targetId ? (
                    <div className="indice">
                        <code title={regle.targetId}>{abreger(regle.targetId)}</code>
                    </div>
                ) : (
                    // Une portée Global n'a pas de cible : ce n'est pas une donnée
                    // manquante, c'est ce que « toute la plateforme » veut dire.
                    <div className="indice">toute la plateforme</div>
                )}
            </td>
            <td className="au-bout">{formaterTaux(regle.rate)}</td>
            <td className="au-bout">
                {regle.fixedFee === 0 ? (
                    <span className="indice">—</span>
                ) : (
                    formaterMontant(regle.fixedFee, regle.currency)
                )}
            </td>
            <td>
                {/*
                  * LES BORNES CHANGENT LE MONTANT RÉELLEMENT PRÉLEVÉ.
                  * Un taux de 10 % avec un minimum de 500 F prélève 500 F sur une
                  * vente à 1 000 F, soit 50 %. Les masquer donnerait un taux
                  * affiché qui n'est pas celui qui s'applique.
                  */}
                {regle.minFee == null && regle.maxFee == null ? (
                    <span className="indice">aucune</span>
                ) : (
                    <div className="jetons">
                        {regle.minFee != null && (
                            <span className="jeton">
                                min {formaterMontant(regle.minFee, regle.currency)}
                            </span>
                        )}
                        {regle.maxFee != null && (
                            <span className="jeton">
                                max {formaterMontant(regle.maxFee, regle.currency)}
                            </span>
                        )}
                    </div>
                )}
            </td>
            <td>{formaterDate(regle.effectiveFromUtc)}</td>
        </tr>
    )
}
