import { useMemo, useState } from 'react'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useRoles } from './useRoles'

/**
 * RÔLES — `/api/identity/roles`.
 *
 * LISTE NUE, SANS PAGINATION NI RECHERCHE : `ListRolesQuery` n'a aucun
 * paramètre et le gestionnaire rend tous les rôles. Ce n'est pas une limite
 * gênante ici — une plateforme en compte une poignée, pas des milliers — mais le
 * filtre ci-dessous reste LOCAL, et l'écran le dit, par la même règle que sur le
 * stock : un champ muet sur sa portée se lit comme une recherche complète.
 *
 * LES PERMISSIONS SONT LA VRAIE INFORMATION DE CET ÉCRAN.
 *
 * Le nom d'un rôle ne dit rien de ce qu'il autorise — « Support » peut tout ou
 * presque rien. Les afficher toutes, en clair, est ce qui permet de répondre à
 * la seule question qu'on se pose en ouvrant cette page : qui peut faire quoi.
 */
export default function RolesPage() {
    const roles = useRoles()
    const [filtre, setFiltre] = useState('')

    const filtres = useMemo(() => {
        const q = filtre.trim().toLowerCase()
        if (!q) return roles.data ?? []
        return (roles.data ?? []).filter(
            r =>
                r.name.toLowerCase().includes(q) ||
                (r.description ?? '').toLowerCase().includes(q) ||
                // On cherche AUSSI dans les permissions : « qui peut gérer les
                // utilisateurs » se pose plus souvent que « où est le rôle
                // Support », et c'est la question à laquelle cet écran répond.
                r.permissions.some(p => p.toLowerCase().includes(q)),
        )
    }, [roles.data, filtre])

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Rôles</h1>
                <div className="filtres">
                    <BarreRecherche
                        valeur={filtre}
                        onChange={setFiltre}
                        placeholder="Filtrer par nom ou permission"
                    />
                    <button
                        type="button"
                        className="lien-deconnexion"
                        onClick={() => void roles.refetch()}
                    >
                        Recharger
                    </button>
                </div>
            </header>

            <p className="indice">
                Le filtre porte sur les rôles déjà chargés : l'API rend la liste entière en
                une fois, sans recherche ni pagination.
            </p>

            {roles.isError && (
                <EtatErreur erreur={roles.error} onReessayer={() => void roles.refetch()} />
            )}

            {!roles.isError && (
                <div className="tableau-enveloppe">
                    {roles.isFetching && <VoileChargement />}

                    {roles.data && filtres.length === 0 ? (
                        <EtatVide filtre={Boolean(filtre)} />
                    ) : (
                        <table className={`tableau ${roles.isFetching ? 'est-en-attente' : ''}`}>
                            <caption className="visuellement-cache">
                                Rôles de la plateforme, {filtres.length} affichés
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Rôle</th>
                                    <th scope="col">Origine</th>
                                    <th scope="col">Permissions</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filtres.map(r => (
                                    <tr key={r.id}>
                                        <td>
                                            <div className="cellule-titre">{r.name}</div>
                                            {r.description && (
                                                <div className="indice">{r.description}</div>
                                            )}
                                        </td>
                                        <td>
                                            {/*
                                              * UN RÔLE DU SOCLE NE SE SUPPRIME PAS.
                                              * `IsSystem` est la seule chose qui distingue
                                              * un rôle qu'on peut retirer d'un rôle dont la
                                              * suppression casserait l'amorçage — et cette
                                              * distinction n'apparaît nulle part ailleurs.
                                              */}
                                            {r.isSystem ? (
                                                <span className="jeton">Socle</span>
                                            ) : (
                                                <span className="indice">créé à la main</span>
                                            )}
                                        </td>
                                        <td>
                                            {r.permissions.length === 0 ? (
                                                /*
                                                 * Un rôle SANS permission n'autorise rien.
                                                 * Le signaler évite qu'on l'attribue en
                                                 * croyant donner un accès.
                                                 */
                                                <span className="indice erreur-en-ligne">
                                                    aucune permission
                                                </span>
                                            ) : (
                                                <div className="jetons">
                                                    {[...r.permissions].sort().map(p => (
                                                        <span key={p} className="jeton">
                                                            <code>{p}</code>
                                                        </span>
                                                    ))}
                                                </div>
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
