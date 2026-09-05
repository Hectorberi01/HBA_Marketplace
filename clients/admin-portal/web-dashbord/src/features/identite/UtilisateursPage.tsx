import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import BarreRecherche from '../../components/tableau/BarreRecherche'
import Facettes from '../../components/tableau/Facettes'
import Pagination from '../../components/tableau/Pagination'
import { EtatErreur, EtatVide, VoileChargement } from '../../components/tableau/Etats'
import { useListeUrl } from '../../components/tableau/useListeUrl'
import { abreger } from '../../lib/format'
import {
    STATUTS_A_TRAITER,
    approuverUtilisateur,
    libelleStatutUtilisateur,
    listerUtilisateurs,
    type Role,
    type Utilisateur,
} from './api'
import { Geste } from '../../components/Geste'
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

    const client = useQueryClient()

    function recharger() {
        void client.invalidateQueries({ queryKey: ['utilisateurs'] })
    }

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
                <div className="filtres">
                    <BarreRecherche
                        valeur={etat.recherche}
                        onChange={q => modifier({ recherche: q })}
                        placeholder="Rechercher un compte"
                    />
                    <Link className="bouton-lien" to="/administration/utilisateurs/nouveau">
                        Nouveau compte
                    </Link>
                </div>
            </header>

            {/*
              * LA RECHERCHE NE PORTE QUE SUR LE PRENOM ET LE NOM, et le service
              * explique pourquoi : « ILike uniquement sur des colonnes string
              * simples : Email/PhoneNumber sont des value objects convertis, non
              * traduisibles ». C'est exactement la facon dont on cherche un compte
              * en support — par son adresse. Le dire vaut mieux que de laisser
              * conclure que le compte n'existe pas.
              */}
            <p className="indice">
                La recherche porte sur le prénom et le nom uniquement : l'adresse et le
                téléphone ne sont pas interrogeables côté service.
            </p>

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
                                    <th scope="col">Action</th>
                                </tr>
                            </thead>
                            <tbody>
                                {(page?.items ?? []).map(u => (
                                    <Ligne
                                        key={u.id}
                                        compte={u}
                                        parId={parId}
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

function Ligne({
    compte,
    parId,
    apres,
}: {
    compte: Utilisateur
    parId: Map<string, Role>
    apres: () => void
}) {
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
            <td>
                {/*
                  * LE SEUL GESTE OFFERT ICI, ET SEULEMENT QUAND IL S'APPLIQUE.
                  *
                  * Suspendre, réactiver, attribuer un rôle sont adressés par
                  * identifiant et se décident sur un dossier, pas sur une ligne.
                  * L'approbation, elle, est le geste qui DÉBLOQUE : sans elle le
                  * compte ne peut pas se connecter, et rien d'autre ne le peut.
                  *
                  * `Reactivate` n'est PAS proposé sur un compte en attente : il
                  * rend 409 `identity.user.not_suspended`. Offrir un bouton qui
                  * échoue toujours vaut moins que pas de bouton.
                  */}
                {compte.status === 'PendingVerification' ? (
                    <Geste
                        libelle="Approuver"
                        confirmation="Le compte devient actif et peut se connecter. L'adresse reste non vérifiée : approuver n'est pas vérifier."
                        executer={() => approuverUtilisateur(compte.id)}
                        apres={apres}
                    />
                ) : (
                    <span className="indice">—</span>
                )}
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
