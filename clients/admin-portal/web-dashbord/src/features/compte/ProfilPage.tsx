import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../../components/tableau/Etats'
import { ApiError } from '../../api/errors'
import { formaterDate } from '../../lib/format'
import { useRoles, indexerRoles } from '../identite/useRoles'
import {
    changerMonMotDePasse,
    confirmerMfa,
    demarrerMfa,
    desactiverMfa,
    lireMonCompte,
    modifierMonProfil,
    type MiseEnPlaceMfa,
} from '../../auth/api'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * MON COMPTE.
 *
 * LE SEUL ÉCRAN DU PORTAIL QUI PARLE DE CELUI QUI LE REGARDE. Tous les autres
 * administrent les comptes des AUTRES ; celui-ci n'appelle que
 * `/api/identity/account/me`, dont chaque route lit l'identifiant dans le jeton
 * et ne peut donc toucher que son propre titulaire.
 *
 * TROIS GESTES, ET TROIS ABSENCES ASSUMÉES — voir l'encadré de `auth/api.ts` :
 * l'adresse e-mail ne se change pas (aucune route, et c'est l'identifiant de
 * connexion), la suppression de compte n'est pas offerte sur une console
 * d'administration, et « déconnecter tous mes appareils » n'existe pas côté
 * serveur.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function ProfilPage() {
    const client = useQueryClient()
    const roles = useRoles()

    const compte = useQuery({
        queryKey: ['moncompte'],
        queryFn: ({ signal }) => lireMonCompte(signal),
    })

    function recharger() {
        void client.invalidateQueries({ queryKey: ['moncompte'] })
    }

    const c = compte.data
    const parId = indexerRoles(roles.data)

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Mon compte</h1>
                {compte.isFetching && <VoileChargement />}
            </header>

            {compte.isError && (
                <EtatErreur erreur={compte.error} onReessayer={() => void compte.refetch()} />
            )}

            {c && (
                <>
                    <h2>Identité</h2>
                    <Identite compte={c} apres={recharger} />

                    <h2>Rôles et vérifications</h2>
                    <div className="fiche">
                        <Ligne
                            nom="Rôles"
                            valeur={
                                c.roleIds.length === 0
                                    ? 'aucun'
                                    : c.roleIds.map(id => parId.get(id)?.name ?? id).join(', ')
                            }
                        />
                        <Ligne nom="Statut du compte" valeur={c.status} />
                        {/*
                          * « VÉRIFIÉ » ET « VÉRIFIÉ SUR PAROLE » NE VALENT PAS LA
                          * MÊME CHOSE, et le contrat le dit explicitement. Afficher
                          * un simple « oui » effacerait la distinction que
                          * `EmailVerifiedByAdminOnUtc` existe pour porter.
                          */}
                        <Ligne
                            nom="Adresse vérifiée"
                            valeur={
                                !c.emailVerified
                                    ? 'non'
                                    : c.emailVerifiedByAdminOnUtc
                                      ? `attestée par un administrateur le ${formaterDate(c.emailVerifiedByAdminOnUtc)}`
                                      : 'oui, par son titulaire'
                            }
                        />
                        <Ligne
                            nom="Conditions acceptées"
                            valeur={
                                c.acceptedTermsVersion
                                    ? `${c.acceptedTermsVersion}${c.acceptedTermsOnUtc ? ` le ${formaterDate(c.acceptedTermsOnUtc)}` : ''}`
                                    : 'aucune version acceptée'
                            }
                        />
                    </div>
                    <p className="indice">
                        L'acceptation des conditions se pose depuis l'application qui AFFICHE le
                        texte : le serveur enregistre la version qu'on lui donne, il ne connaît
                        pas la rédaction en vigueur. Ce portail n'en affiche aucune, il ne
                        propose donc pas de l'accepter.
                    </p>

                    <h2>Mot de passe</h2>
                    <MotDePasse />

                    <h2>Double authentification</h2>
                    <Mfa active={c.mfaEnabled} apres={recharger} />
                </>
            )}
        </section>
    )
}

function Ligne({ nom, valeur }: { nom: string; valeur: string }) {
    return (
        <div className="fiche__ligne">
            <span className="fiche__nom">{nom}</span>
            <span className="fiche__valeur">{valeur}</span>
        </div>
    )
}

function Erreur({ erreur }: { erreur: unknown }) {
    if (!erreur) return null
    return (
        <p className="erreur-en-ligne" role="alert">
            {erreur instanceof ApiError ? erreur.messageLisible : "L'opération a échoué."}
        </p>
    )
}

/**
 * LE FORMULAIRE PART DES VALEURS DU SERVEUR, ET S'Y RESYNCHRONISE.
 *
 * `useState` seul figerait la première réponse : après un rechargement de la
 * fiche, les champs afficheraient encore l'ancien nom. La comparaison au rendu
 * — et non un effet — est le motif documenté de React pour un état dérivé d'une
 * prop ; c'est celui que `BarreRecherche` emploie déjà.
 */
function Identite({
    compte,
    apres,
}: {
    compte: { firstName: string; lastName: string; email: string; phoneNumber: string }
    apres: () => void
}) {
    const [reference, setReference] = useState(compte)
    const [prenom, setPrenom] = useState(compte.firstName)
    const [nom, setNom] = useState(compte.lastName)
    const [telephone, setTelephone] = useState(compte.phoneNumber)

    if (reference !== compte) {
        setReference(compte)
        setPrenom(compte.firstName)
        setNom(compte.lastName)
        setTelephone(compte.phoneNumber)
    }

    const mutation = useMutation({
        mutationFn: () =>
            modifierMonProfil({
                firstName: prenom.trim(),
                lastName: nom.trim(),
                phoneNumber: telephone.trim(),
            }),
        onSuccess: apres,
    })

    const modifie =
        prenom.trim() !== compte.firstName ||
        nom.trim() !== compte.lastName ||
        telephone.trim() !== compte.phoneNumber

    const pret = prenom.trim() !== '' && nom.trim() !== '' && telephone.trim() !== ''

    return (
        <>
            <div className="formulaire">
                <label>
                    Prénom
                    <input value={prenom} onChange={e => setPrenom(e.target.value)} />
                </label>
                <label>
                    Nom
                    <input value={nom} onChange={e => setNom(e.target.value)} />
                </label>
                <label>
                    Téléphone
                    <input value={telephone} onChange={e => setTelephone(e.target.value)} />
                </label>
                <label>
                    Adresse e-mail
                    <input value={compte.email} readOnly disabled />
                </label>
            </div>
            <p className="indice">
                L'adresse n'est pas modifiable : aucune route ne la change, et c'est
                l'identifiant de connexion. La changer sans re-vérification ouvrirait une prise
                de compte.
            </p>

            <div className="gestes">
                <button
                    type="button"
                    className="bouton"
                    disabled={!modifie || !pret || mutation.isPending}
                    onClick={() => mutation.mutate()}
                >
                    {mutation.isPending ? 'Enregistrement…' : 'Enregistrer'}
                </button>
                {mutation.isSuccess && !modifie && <span className="indice">Enregistré.</span>}
            </div>
            <Erreur erreur={mutation.error} />
        </>
    )
}

/**
 * LE NOUVEAU MOT DE PASSE EST SAISI DEUX FOIS, ET LA COMPARAISON EST LOCALE.
 *
 * Le serveur ne reçoit qu'une valeur : il ne peut pas détecter une faute de
 * frappe. La détecter ici évite de verrouiller son propre compte sur un
 * caractère qu'on n'a jamais voulu taper — et sur cette plateforme, où aucun
 * courriel de réinitialisation ne part, ce serait sans retour.
 */
function MotDePasse() {
    const [actuel, setActuel] = useState('')
    const [nouveau, setNouveau] = useState('')
    const [confirmation, setConfirmation] = useState('')

    const mutation = useMutation({
        mutationFn: () =>
            changerMonMotDePasse({ currentPassword: actuel, newPassword: nouveau }),
        onSuccess: () => {
            setActuel('')
            setNouveau('')
            setConfirmation('')
        },
    })

    const concordent = nouveau !== '' && nouveau === confirmation
    const pret = actuel !== '' && concordent && nouveau !== actuel

    return (
        <>
            <div className="formulaire">
                <label>
                    Mot de passe actuel
                    <input
                        type="password"
                        autoComplete="current-password"
                        value={actuel}
                        onChange={e => setActuel(e.target.value)}
                    />
                </label>
                <label>
                    Nouveau mot de passe
                    <input
                        type="password"
                        autoComplete="new-password"
                        value={nouveau}
                        onChange={e => setNouveau(e.target.value)}
                    />
                </label>
                <label>
                    Répéter le nouveau
                    <input
                        type="password"
                        autoComplete="new-password"
                        value={confirmation}
                        onChange={e => setConfirmation(e.target.value)}
                    />
                </label>
            </div>

            {confirmation !== '' && !concordent && (
                <p className="erreur-en-ligne">Les deux saisies diffèrent.</p>
            )}
            {nouveau !== '' && nouveau === actuel && (
                <p className="erreur-en-ligne">Le nouveau mot de passe est identique à l'actuel.</p>
            )}

            <div className="gestes">
                <button
                    type="button"
                    className="bouton"
                    disabled={!pret || mutation.isPending}
                    onClick={() => mutation.mutate()}
                >
                    {mutation.isPending ? 'Changement…' : 'Changer le mot de passe'}
                </button>
                {mutation.isSuccess && <span className="indice">Mot de passe changé.</span>}
            </div>
            <p className="indice">
                Changer son mot de passe RÉVOQUE les jetons de rafraîchissement du compte. Les
                autres sessions ouvertes tomberont à leur prochain renouvellement.
            </p>
            <Erreur erreur={mutation.error} />
        </>
    )
}

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * DOUBLE AUTHENTIFICATION.
 *
 * LE SECRET N'EST RENDU QU'UNE FOIS, PAR `POST /mfa/setup`. Il n'est plus
 * lisible ensuite. L'écran le garde donc affiché tant que la confirmation n'a
 * pas abouti, et ne le fait pas disparaître sur un clic à côté.
 *
 * PAS DE QR CODE, ET C'EST UN CHOIX DE DÉPENDANCE. Le dessiner suppose un
 * encodeur Reed-Solomon — une bibliothèque, pas quinze lignes. Pour un geste
 * qu'un administrateur fait une fois, la saisie manuelle du secret suffit :
 * toutes les applications d'authentification l'acceptent. Le jour où le portail
 * servira plusieurs dizaines d'administrateurs, la bibliothèque se chargera à la
 * demande sur ce seul panneau.
 * ═══════════════════════════════════════════════════════════════════════════
 */
function Mfa({ active, apres }: { active: boolean; apres: () => void }) {
    const [miseEnPlace, setMiseEnPlace] = useState<MiseEnPlaceMfa | null>(null)
    const [code, setCode] = useState('')

    const demarrage = useMutation({
        mutationFn: demarrerMfa,
        onSuccess: setMiseEnPlace,
    })

    const confirmation = useMutation({
        mutationFn: () => confirmerMfa(code.trim()),
        // LE PANNEAU EST FERMÉ PAR LE GESTE QUI LE CONSOMME, PAS PAR UN EFFET.
        //
        // Un `useEffect` sur `active` faisait le même travail et le compilateur
        // React le refuse : « calling setState synchronously inside an effect
        // starts another render and is usually unnecessary ». Il l'était.
        //
        // CE QUE CE `null` ÉVITE : garder une mise en place PÉRIMÉE. Après une
        // confirmation, le secret affiché est consommé ; le laisser en mémoire le
        // ferait réapparaître si l'on désactivait ensuite la double
        // authentification — avec un secret que le serveur ne reconnaît plus.
        onSuccess: () => {
            setMiseEnPlace(null)
            setCode('')
            apres()
        },
    })

    const desactivation = useMutation({
        mutationFn: () => desactiverMfa(code.trim()),
        onSuccess: () => {
            setCode('')
            apres()
        },
    })

    if (active) {
        return (
            <>
                <p>
                    La double authentification est <strong>active</strong> sur ce compte.
                </p>
                <div className="formulaire">
                    <label>
                        Code de votre application, pour la désactiver
                        <input
                            inputMode="numeric"
                            autoComplete="one-time-code"
                            value={code}
                            onChange={e => setCode(e.target.value)}
                            placeholder="6 chiffres"
                        />
                    </label>
                </div>
                <div className="gestes">
                    <button
                        type="button"
                        className="bouton bouton--danger"
                        disabled={code.trim() === '' || desactivation.isPending}
                        onClick={() => desactivation.mutate()}
                    >
                        {desactivation.isPending ? 'Désactivation…' : 'Désactiver'}
                    </button>
                </div>
                <p className="indice">
                    Le code est exigé pour désactiver, pas seulement pour activer : sans lui, un
                    jeton volé suffirait à retirer la protection qu'il vient de contourner.
                </p>
                <Erreur erreur={desactivation.error} />
            </>
        )
    }

    return (
        <>
            {!miseEnPlace ? (
                <>
                    <p className="indice">
                        La double authentification n'est pas active. Sur un compte qui administre
                        la plateforme entière, c'est la seule protection qui survive à un mot de
                        passe divulgué.
                    </p>
                    <div className="gestes">
                        <button
                            type="button"
                            className="bouton"
                            disabled={demarrage.isPending}
                            onClick={() => demarrage.mutate()}
                        >
                            {demarrage.isPending ? 'Préparation…' : 'Activer'}
                        </button>
                    </div>
                    <Erreur erreur={demarrage.error} />
                </>
            ) : (
                <>
                    <p>
                        Ajoutez ce secret dans votre application d'authentification, puis entrez le
                        code qu'elle affiche.
                    </p>
                    <div className="fiche">
                        <Ligne nom="Secret" valeur={miseEnPlace.secret} />
                    </div>
                    <p className="indice">
                        Ce secret ne sera plus affiché après confirmation. L'adresse complète, si
                        votre application accepte de la coller :{' '}
                        <code className="viz__tableau">{miseEnPlace.otpAuthUri}</code>
                    </p>

                    <div className="formulaire">
                        <label>
                            Code affiché par l'application
                            <input
                                inputMode="numeric"
                                autoComplete="one-time-code"
                                value={code}
                                onChange={e => setCode(e.target.value)}
                                placeholder="6 chiffres"
                            />
                        </label>
                    </div>
                    <div className="gestes">
                        <button
                            type="button"
                            className="bouton"
                            disabled={code.trim() === '' || confirmation.isPending}
                            onClick={() => confirmation.mutate()}
                        >
                            {confirmation.isPending ? 'Vérification…' : 'Confirmer'}
                        </button>
                        <button
                            type="button"
                            className="lien-deconnexion"
                            onClick={() => setMiseEnPlace(null)}
                        >
                            Abandonner
                        </button>
                    </div>
                    <Erreur erreur={confirmation.error} />
                </>
            )}
        </>
    )
}
