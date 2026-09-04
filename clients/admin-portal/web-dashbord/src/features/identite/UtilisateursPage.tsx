import { useMemo } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger } from '../../lib/format'
import {
    STATUTS_A_TRAITER,
    libelleStatutUtilisateur,
    listerUtilisateurs,
    type Role,
    type Utilisateur,
} from './api'
import { indexerRoles, useRoles } from './useRoles'

/**
 * COMPTES — `/api/identity/users`.
 *
 * CETTE LISTE N'EXISTAIT PAS JUSQU'À RÉCEMMENT, et le service explique ce que
 * son absence coûtait : les cinq gestes d'administration sont adressés par
 * identifiant, « il fallait déjà connaître le GUID d'un compte pour le
 * suspendre — c'est-à-dire qu'aucune console ne pouvait exister, et qu'on
 * suspendait un compte en interrogeant la base à la main ».
 *
 * LES RÔLES SONT RÉSOLUS CÔTÉ NAVIGATEUR. Le compte porte `roleIds`, une liste
 * de GUID ; la table des rôles est courte et déjà chargée pour l'autre écran.
 * Une jointure locale évite un appel par ligne, et un identifiant absent de la
 * table est affiché abrégé plutôt que masqué — un compte portant un rôle qui
 * n'existe plus est une incohérence à montrer, pas à absorber.
 */
export default function UtilisateursPage() {
    const { etat, modifier } = useListeUrl('createdOnUtc')
    const roles = useRoles()
    const parId = useMemo(() => indexerRoles(roles.data), [roles.data])

    const requete = useQuery({
        queryKey: ['utilisateurs', etat.page, etat.taille, etat.recherche, etat.statut, etat.tri, etat.sens],
        queryFn: ({ signal }) =>
            listerUtilisateurs(
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
                <h1>Utilisateurs</h1>
                <BarreRecherche
                    valeur={etat.recherche}
                    onChange={q => modifier({ recherche: q })}
                    placeholder="Rechercher un compte"
                />
            </header>

            <Facettes
                facettes={page?.facettes ?? null}
                actif={etat.statut}
                onChoisir={s => modifier({ statut: s })}
                libelle={libelleStatutUtilisateur}
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
                                Comptes de la plateforme, {page?.total ?? 0} au total
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Compte</th>
                                    <th scope="col">Téléphone</th>
                                    <th scope="col">Statut</th>
                                    <th scope="col">Rôles</th>
                                    <th scope="col">Sécurité</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(u => (
                                    <Ligne key={u.id} compte={u} parId={parId} />
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

function Ligne({ compte, parId }: { compte: Utilisateur; parId: Map<string, Role> }) {
    const aTraiter = STATUTS_A_TRAITER.has(compte.status)
    const nom = `${compte.firstName} ${compte.lastName}`.trim()

    return (
        <tr className={aTraiter ? 'a-traiter' : undefined}>
            <td>
                <div className="cellule-titre">{nom || compte.email}</div>
                <div className="indice">{compte.email}</div>
            </td>
            <td>{compte.phoneNumber}</td>
            <td>
                <span className={`pastille pastille--${compte.status.toLowerCase()}`}>
                    {libelleStatutUtilisateur(compte.status)}
                </span>
            </td>
            <td>
                {compte.roleIds.length === 0 ? (
                    <span className="indice">aucun</span>
                ) : (
                    <div className="jetons">
                        {compte.roleIds.map(id => {
                            const role = parId.get(id)
                            return (
                                <span
                                    key={id}
                                    className={`jeton ${role ? '' : 'jeton--inconnu'}`}
                                    title={role ? role.permissions.join(', ') : id}
                                >
                                    {role ? role.name : abreger(id)}
                                </span>
                            )
                        })}
                    </div>
                )}
            </td>
            <td>
                <Securite compte={compte} />
            </td>
        </tr>
    )
}

/**
 * COLONNE SÉCURITÉ.
 *
 * « VÉRIFIÉ » ET « VÉRIFIÉ SUR PAROLE » NE SONT PAS LA MÊME CHOSE, et c'est le
 * contrat lui-même qui insiste : `EmailVerifiedByAdminOnUtc` renseignée signifie
 * qu'un administrateur a marqué l'adresse vérifiée sur attestation, sans que le
 * titulaire ait cliqué quoi que ce soit. Les afficher pareil effacerait la seule
 * distinction qui compte le jour où l'on se demande comment un compte est entré.
 */
function Securite({ compte }: { compte: Utilisateur }) {
    const parAdmin = Boolean(compte.emailVerifiedByAdminOnUtc)

    return (
        <div className="jetons">
            {compte.emailVerified ? (
                <span
                    className={`jeton ${parAdmin ? 'jeton--attention' : ''}`}
                    title={
                        parAdmin
                            ? "Marquée vérifiée par un administrateur, sur attestation — le titulaire n'a pas cliqué de lien."
                            : 'Adresse vérifiée par son titulaire.'
                    }
                >
                    {parAdmin ? 'Courriel sur parole' : 'Courriel vérifié'}
                </span>
            ) : (
                <span className="jeton jeton--attention">Courriel non vérifié</span>
            )}
            {compte.mfaEnabled && <span className="jeton">2FA</span>}
        </div>
    )
}
