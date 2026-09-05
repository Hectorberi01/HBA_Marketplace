import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError } from '../../api/errors'
import { listerUtilisateurs, type Utilisateur } from '../identite/api'
import { inscrireVendeur } from './api'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * INSCRIRE UN VENDEUR.
 *
 * DEUX FAITS QU'IL FAUT AVOIR EN TÊTE POUR COMPRENDRE CET ÉCRAN.
 *
 * 1. UN VENDEUR EST RATTACHÉ À UN COMPTE EXISTANT. Il n'y a pas de « création
 *    de vendeur » : `RegisterSellerCommand` prend un `UserId` et le résout par
 *    gRPC chez identity. On choisit donc d'abord une personne, ensuite on lui
 *    donne une boutique. Créer le compte, c'est l'autre écran.
 *
 * 2. SON ADRESSE DOIT ÊTRE ATTESTÉE, ET L'APPROBATION DU COMPTE NE SUFFIT PAS.
 *    Le gestionnaire refuse `sellers.seller.email_unverified` tant que
 *    `EmailVerified` est faux — ce qu'il est pour tout le monde, faute
 *    d'e-mailing déployé. L'écran le dit AVANT l'envoi, en désactivant le
 *    bouton, plutôt que de laisser découvrir un 403 après coup.
 *
 * LA RECHERCHE NE PORTE QUE SUR LE PRÉNOM ET LE NOM. C'est ce que
 * `ListUsersQuery` interroge ; l'adresse et le téléphone n'y sont pas. Le dire
 * évite de conclure qu'un compte n'existe pas parce qu'on l'a cherché par
 * courriel.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function NouveauVendeurPage() {
    const naviguer = useNavigate()
    const [recherche, setRecherche] = useState('')
    const [choisi, setChoisi] = useState<Utilisateur | null>(null)
    const [nomBoutique, setNomBoutique] = useState('')
    const [taux, setTaux] = useState('10')

    const comptes = useQuery({
        queryKey: ['utilisateurs', 'choix', recherche],
        queryFn: ({ signal }) =>
            listerUtilisateurs({ page: 1, taille: 10, recherche: recherche || undefined }, signal),
        enabled: recherche.trim().length >= 2,
    })

    const inscription = useMutation({
        mutationFn: () =>
            inscrireVendeur({
                userId: choisi!.id,
                shopName: nomBoutique.trim(),
                // Le service attend une FRACTION. L'écran saisit des pour cent,
                // parce que c'est ainsi qu'on en parle, et divise ici — une
                // seule fois, à un seul endroit.
                commissionRate: Number(taux) / 100,
            }),
        onSuccess: resultat => naviguer(`/vendeurs/${resultat.id}`),
    })

    const tauxValide = Number.isFinite(Number(taux)) && Number(taux) >= 0 && Number(taux) <= 100
    const pret =
        choisi !== null && choisi.emailVerified && nomBoutique.trim().length >= 2 && tauxValide

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <div>
                    <p className="indice">
                        <Link to="/vendeurs">← Vendeurs</Link>
                    </p>
                    <h1>Inscrire un vendeur</h1>
                </div>
            </header>

            <p className="indice">
                Un vendeur se rattache à un compte qui existe déjà. Pour créer le compte lui-même,
                passez par <Link to="/administration/utilisateurs/nouveau">Nouveau compte</Link>.
            </p>

            <h2>1. Le compte</h2>

            {choisi ? (
                <div className="fiche">
                    <div className="fiche__ligne">
                        <span className="fiche__nom">
                            {`${choisi.firstName} ${choisi.lastName}`.trim() || choisi.email}
                        </span>
                        <span className="fiche__valeur">
                            {choisi.email}
                            <button
                                type="button"
                                className="lien-deconnexion"
                                onClick={() => setChoisi(null)}
                            >
                                Changer
                            </button>
                        </span>
                    </div>
                </div>
            ) : (
                <>
                    <div className="barre-recherche">
                        <input
                            type="search"
                            value={recherche}
                            placeholder="Prénom ou nom"
                            onChange={e => setRecherche(e.target.value)}
                        />
                    </div>
                    <p className="indice">
                        La recherche porte sur le prénom et le nom uniquement : l'adresse et le
                        téléphone ne sont pas interrogeables côté service.
                    </p>

                    {comptes.data && comptes.data.items.length > 0 && (
                        <div className="tableau-enveloppe">
                            <table className="tableau">
                                <tbody>
                                    {comptes.data.items.map(u => (
                                        <tr key={u.id}>
                                            <td>
                                                <div className="cellule-titre">
                                                    {`${u.firstName} ${u.lastName}`.trim() ||
                                                        u.email}
                                                </div>
                                                <div className="indice">{u.email}</div>
                                            </td>
                                            <td>
                                                {u.emailVerified ? (
                                                    <span className="indice">
                                                        adresse attestée
                                                    </span>
                                                ) : (
                                                    <span className="indice erreur-en-ligne">
                                                        adresse non attestée
                                                    </span>
                                                )}
                                            </td>
                                            <td>
                                                <button
                                                    type="button"
                                                    className="bouton"
                                                    onClick={() => setChoisi(u)}
                                                >
                                                    Choisir
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}

                    {comptes.data && comptes.data.items.length === 0 && (
                        <p className="indice">Aucun compte pour cette recherche.</p>
                    )}
                </>
            )}

            {choisi && !choisi.emailVerified && (
                <p className="erreur-en-ligne">
                    L'adresse de ce compte n'est pas attestée, et le service refusera l'inscription
                    — <code>sellers.seller.email_unverified</code>. Attester est un geste distinct
                    de l'approbation du compte : il se pose depuis l'écran{' '}
                    <Link to="/administration/utilisateurs">Utilisateurs</Link>.
                </p>
            )}

            <h2>2. La boutique</h2>

            <div className="formulaire">
                <label>
                    Nom de la boutique
                    <input
                        type="text"
                        value={nomBoutique}
                        onChange={e => setNomBoutique(e.target.value)}
                        placeholder="Le nom que verront les acheteurs"
                    />
                </label>

                <label>
                    Commission, en pour cent
                    <input
                        type="number"
                        min={0}
                        max={100}
                        step={0.5}
                        value={taux}
                        onChange={e => setTaux(e.target.value)}
                    />
                </label>
            </div>
            <p className="indice">
                Le domaine stocke ce taux en FRACTION — dix pour cent s'y écrit 0,1. La conversion
                se fait ici, une seule fois : saisir 10 pose bien dix pour cent.
            </p>

            <div className="gestes">
                <button
                    type="button"
                    className="bouton"
                    disabled={!pret || inscription.isPending}
                    onClick={() => inscription.mutate()}
                >
                    {inscription.isPending ? 'Inscription…' : 'Inscrire le vendeur'}
                </button>
            </div>

            {inscription.isError && (
                <p className="erreur-en-ligne" role="alert">
                    {inscription.error instanceof ApiError
                        ? inscription.error.messageLisible
                        : "L'inscription a échoué."}
                </p>
            )}
        </section>
    )
}
