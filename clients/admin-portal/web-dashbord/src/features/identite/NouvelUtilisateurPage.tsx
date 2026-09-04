import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../api/errors'
import {
    attribuerRole,
    creerCompte,
    engendrerMotDePasse,
    envoyerReinitialisation,
} from './api'
import { useRoles } from './useRoles'

/**
 * CRÉER UN COMPTE.
 *
 * PREMIER ÉCRAN D'ÉCRITURE DU PORTAIL, et il porte tout ce que l'API impose :
 * il n'existe pas de route d'administration pour créer un utilisateur, seule
 * l'inscription publique le permet. Voir l'encadré de `features/identite/api.ts`.
 *
 * L'OPÉRATION EST EN DEUX TEMPS, ET ELLE PEUT ÉCHOUER AU MILIEU.
 *
 * `register` crée le compte, puis `POST /users/{id}/roles` pose le rôle. Si le
 * second échoue, le compte EXISTE quand même. Rendre « la création a échoué »
 * serait faux et ferait recommencer — sur un e-mail désormais pris, donc sur un
 * 409 incompréhensible. L'écran distingue donc les deux étapes et dit
 * exactement où l'on en est.
 */
export default function NouvelUtilisateurPage() {
    const naviguer = useNavigate()
    const cache = useQueryClient()
    const roles = useRoles()

    const [prenom, setPrenom] = useState('')
    const [nom, setNom] = useState('')
    const [email, setEmail] = useState('')
    const [telephone, setTelephone] = useState('')
    const [roleId, setRoleId] = useState('')
    const [motDePasse] = useState(() => engendrerMotDePasse())

    /** Compte créé mais rôle non posé : l'écran reste, avec quoi reprendre. */
    const [rolePartiel, setRolePartiel] = useState<{ id: string; erreur: string } | null>(null)

    const creation = useMutation({
        mutationFn: async () => {
            const { id } = await creerCompte({
                firstName: prenom.trim(),
                lastName: nom.trim(),
                email: email.trim(),
                phoneNumber: telephone.trim(),
                password: motDePasse,
            })

            if (roleId) {
                try {
                    await attribuerRole(id, roleId)
                } catch (cause) {
                    // ON NE RELANCE PAS : le compte est créé, et le signaler
                    // comme un échec global le ferait recréer sur un e-mail déjà
                    // pris. On remonte l'état réel.
                    setRolePartiel({
                        id,
                        erreur:
                            cause instanceof ApiError
                                ? cause.messageLisible
                                : "raison inconnue",
                    })
                    return { id, roleAttribue: false }
                }
            }
            return { id, roleAttribue: Boolean(roleId) }
        },
        onSuccess: () => {
            // La liste des comptes est périmée : elle en contient un de plus.
            void cache.invalidateQueries({ queryKey: ['utilisateurs'] })
        },
    })

    const reinitialisation = useMutation({
        mutationFn: () => envoyerReinitialisation(email.trim()),
    })

    const erreurCreation = creation.error instanceof ApiError ? creation.error : null
    const cree = creation.isSuccess

    function soumettre(evenement: FormEvent) {
        evenement.preventDefault()
        creation.mutate()
    }

    if (cree) {
        return (
            <section className="ecran-liste">
                <header className="ecran-liste__tete">
                    <h1>Compte créé</h1>
                </header>

                <div className="carte-explication">
                    <p>
                        Le compte de <strong>{prenom} {nom}</strong> ({email}) est créé. Son
                        statut est <strong>À vérifier</strong> : il naît comme une inscription
                        ordinaire, et un courriel de vérification part à cette adresse.
                    </p>

                    {rolePartiel ? (
                        <>
                            <h2>Le rôle n'a pas été posé</h2>
                            <p className="erreur">{rolePartiel.erreur}</p>
                            <p>
                                Le compte existe — ne le recréez pas, l'adresse est désormais
                                prise. Le rôle s'attribue depuis la fiche du compte.
                            </p>
                        </>
                    ) : null}

                    <h2>Le mot de passe</h2>
                    <p>
                        Il a fallu en fournir un : la route d'inscription l'exige et il
                        n'existe pas de variante sans. Celui-ci a été tiré au hasard et{' '}
                        <strong>n'est affiché qu'ici</strong>.
                    </p>
                    <p>
                        <code className="mot-de-passe">{motDePasse}</code>
                    </p>
                    <p className="indice">
                        Il a été vu sur votre écran, pas sur celui de la personne concernée.
                        Le plus propre est de lui envoyer un lien de réinitialisation pour
                        qu'elle pose le sien.
                    </p>

                    <p>
                        <button
                            type="button"
                            onClick={() => reinitialisation.mutate()}
                            disabled={reinitialisation.isPending || reinitialisation.isSuccess}
                        >
                            {reinitialisation.isSuccess
                                ? 'Lien envoyé'
                                : reinitialisation.isPending
                                  ? 'Envoi…'
                                  : 'Envoyer un lien de réinitialisation'}
                        </button>
                    </p>
                    {reinitialisation.error instanceof ApiError && (
                        <p className="erreur">{reinitialisation.error.messageLisible}</p>
                    )}

                    <p className="liens-utiles">
                        <Link to="/administration/utilisateurs">Retour aux comptes</Link>
                    </p>
                </div>
            </section>
        )
    }

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Nouveau compte</h1>
            </header>

            <form className="formulaire" onSubmit={soumettre}>
                <p className="indice">
                    L'API n'expose aucune route d'administration pour créer un compte : celui-ci
                    passe par l'inscription publique. Il naîtra donc au statut
                    <strong> À vérifier</strong>, avec un courriel de vérification envoyé à
                    l'adresse indiquée.
                </p>

                <div className="formulaire__paire">
                    <label>
                        Prénom
                        <input
                            value={prenom}
                            onChange={e => setPrenom(e.target.value)}
                            required
                            maxLength={100}
                            autoComplete="off"
                        />
                    </label>
                    <label>
                        Nom
                        <input
                            value={nom}
                            onChange={e => setNom(e.target.value)}
                            required
                            maxLength={100}
                            autoComplete="off"
                        />
                    </label>
                </div>

                <label>
                    Adresse électronique
                    <input
                        type="email"
                        value={email}
                        onChange={e => setEmail(e.target.value)}
                        required
                        maxLength={320}
                        autoComplete="off"
                    />
                </label>

                <label>
                    Téléphone
                    <input
                        type="tel"
                        value={telephone}
                        onChange={e => setTelephone(e.target.value)}
                        required
                        autoComplete="off"
                    />
                    {/*
                      * LA RÈGLE VIENT DU DOMAINE, PAS D'UNE SUPPOSITION.
                      * `PhoneNumber.Create` refuse avec « 8 à 15 chiffres,
                      * indicatif optionnel ». L'écrire évite de faire découvrir
                      * la contrainte par un refus après envoi.
                      */}
                    <span className="indice">8 à 15 chiffres, indicatif optionnel</span>
                </label>

                <label>
                    Rôle
                    <select value={roleId} onChange={e => setRoleId(e.target.value)}>
                        <option value="">Aucun</option>
                        {(roles.data ?? []).map(r => (
                            <option key={r.id} value={r.id}>
                                {r.name}
                            </option>
                        ))}
                    </select>
                    <span className="indice">
                        L'inscription n'accepte pas de rôle : il est posé par un second appel,
                        juste après la création.
                    </span>
                </label>

                {erreurCreation && (
                    <div className="erreur" role="alert">
                        <p>{erreurCreation.messageLisible}</p>
                        {/*
                          * LES ERREURS DE CHAMP SONT DÉTAILLÉES. L'enveloppe du
                          * paragraphe 5 porte `error.details` avec un `field` par
                          * entrée — les afficher évite de faire deviner lequel des
                          * cinq champs le serveur a refusé.
                          */}
                        {erreurCreation.details.length > 0 && (
                            <ul>
                                {erreurCreation.details.map((d, i) => (
                                    <li key={i}>
                                        {d.field ? `${d.field} : ` : ''}
                                        {d.message}
                                    </li>
                                ))}
                            </ul>
                        )}
                        {erreurCreation.requestId && (
                            <p className="indice">
                                Requête : <code>{erreurCreation.requestId}</code>
                            </p>
                        )}
                    </div>
                )}

                <div className="formulaire__actions">
                    <button type="submit" disabled={creation.isPending}>
                        {creation.isPending ? 'Création…' : 'Créer le compte'}
                    </button>
                    <button
                        type="button"
                        className="lien-deconnexion"
                        onClick={() => naviguer('/administration/utilisateurs')}
                        disabled={creation.isPending}
                    >
                        Annuler
                    </button>
                </div>
            </form>
        </section>
    )
}
