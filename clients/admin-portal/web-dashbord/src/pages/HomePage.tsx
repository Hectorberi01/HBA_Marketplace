import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../components/tableau/Etats'
import { NAVIGATION } from '../layout/navigation'
import { useAuth } from '../auth/useAuth'
import { formaterDate, formaterMontant, formaterPeriode } from '../lib/format'
import {
    lireFacettesCommandes,
    lireStatsPaiements,
    lireVolumes,
} from '../features/accueil/api'
import { listerLots, type LotReglement } from '../features/finance/api'
import { libelleStatutCommande, STATUTS_A_TRAITER } from '../features/commandes/api'
import {
    BarreEmpilee,
    BarresHorizontales,
    Colonnes,
    type Colonne,
    type Part,
} from '../components/graphes/Graphes'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * ACCUEIL.
 *
 * CE QU'IL FAIT : dire à qui l'on est connecté, ce que la plateforme porte, et
 * où aller. Trois choses, toutes exactes.
 *
 * CE QU'IL NE FAIT PAS, ET POURQUOI.
 *
 *   LES PAIEMENTS SONT LÀ, ET J'AVAIS ÉCRIT LE CONTRAIRE. Ce paragraphe
 *   affirmait que la passerelle ne route rien sous `/api/financial/payments` et
 *   que `/stats` rendait 404. C'est faux : l'entrée `payments` fait correspondre
 *   `/api/payments/{**catch-all}` et le RÉÉCRIT vers le préfixe du service.
 *   J'avais cherché le chemin de SORTIE dans une table qui indexe les chemins
 *   d'ENTRÉE. Voir `features/accueil/api.ts` pour le détail.
 *
 *   PAS DE CHIFFRE D'AFFAIRES POUR AUTANT. `PaymentStatsSummary` rend
 *   `CapturedAmount` SANS DEVISE, alors que chaque paiement porte la sienne. Le
 *   montant est donc affiché avec sa réserve, et ce sont les NOMBRES — non
 *   ambigus — qui portent le graphe.
 *
 *   PAS DE COURBE, PAS DE « CETTE SEMAINE ». Aucun endpoint d'administration
 *   n'accepte de fenêtre temporelle ni ne rend d'agrégat par période. Le calculer
 *   côté navigateur supposerait de rapatrier toutes les commandes de la
 *   plateforme pour les compter — et le chiffre serait faux dès que la
 *   pagination borne le rapatriement, sans que rien ne le signale.
 *
 *   PAS DE FILE D'ATTENTE. C'est `/supervision`, et le faire deux fois
 *   garantirait que les deux écrans finissent par ne plus dire la même chose.
 *   L'accueil y renvoie, il ne le recopie pas.
 *
 * LES RACCOURCIS SONT ENGENDRÉS DEPUIS `NAVIGATION`.
 *
 * Recopier les seize entrées ici en ferait une seconde liste à tenir à jour, et
 * elle divergerait au premier écran ajouté — l'accueil montrerait alors une
 * plateforme qui n'existe plus. La barre latérale reste la seule source.
 * ═══════════════════════════════════════════════════════════════════════════
 */
export default function HomePage() {
    const { etat } = useAuth()
    const jeton = etat.statut === 'connecte' ? etat.jeton : null

    const volumes = useQuery({
        queryKey: ['accueil', 'volumes'],
        queryFn: ({ signal }) => lireVolumes(signal),
    })

    const facettes = useQuery({
        queryKey: ['accueil', 'facettes-commandes'],
        queryFn: ({ signal }) => lireFacettesCommandes(signal),
    })

    const paiements = useQuery({
        queryKey: ['accueil', 'paiements'],
        queryFn: ({ signal }) => lireStatsPaiements(signal),
        // Une seule tentative : un 403 ou un 404 sur cette route est un fait de
        // configuration, pas un aléa réseau. Le rejouer trois fois retarde
        // l'affichage des deux autres graphes sans rien changer au résultat.
        retry: false,
    })

    const lots = useQuery({
        queryKey: ['accueil', 'reglements'],
        queryFn: ({ signal }) => listerLots(signal),
    })

    // Le prénom seul quand le jeton porte un nom complet : « Bonjour Hector »
    // se lit, « Bonjour Hector Adjakpa » sonne comme un courrier administratif.
    const prenom = jeton?.nom?.trim().split(/\s+/)[0] ?? null

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>{prenom ? `Bonjour ${prenom}` : 'Console d’administration'}</h1>
                <div className="filtres">
                    <span className="indice">
                        {volumes.dataUpdatedAt
                            ? `Lu à ${formaterDate(new Date(volumes.dataUpdatedAt).toISOString())}`
                            : '—'}
                    </span>
                    <button
                        type="button"
                        className="lien-deconnexion"
                        onClick={() => void volumes.refetch()}
                    >
                        Recharger
                    </button>
                </div>
            </header>

            <p className="indice">
                {jeton?.email ?? 'Session'}
                {jeton && jeton.roles.length > 0 ? ` · ${jeton.roles.join(', ')}` : ''}
                {' — '}
                <Link to="/supervision">la supervision</Link> montre ce qui attend un geste ;
                cette page montre ce que la plateforme porte.
            </p>

            <h2>Volumes</h2>

            {volumes.isError ? (
                <EtatErreur erreur={volumes.error} onReessayer={() => void volumes.refetch()} />
            ) : (
                <div className={volumes.isFetching ? 'tuiles est-en-attente' : 'tuiles'}>
                    {(volumes.data?.volumes ?? []).map(volume => (
                        <Link key={volume.cle} to={volume.lien} className="tuile tuile--lien">
                            <span className="tuile__titre">{volume.libelle}</span>
                            <span className="tuile__valeur">
                                {volume.nombre.toLocaleString('fr-FR')}
                            </span>
                            <span className="indice">{volume.precision}</span>
                        </Link>
                    ))}
                    {volumes.isLoading && <VoileChargement />}
                </div>
            )}

            {volumes.data && volumes.data.echecs.length > 0 && (
                <p className="erreur-en-ligne">
                    Sans réponse : {volumes.data.echecs.join(', ')}. Les autres chiffres
                    restent exacts.
                </p>
            )}

            <p className="indice">
                Ces totaux sont calculés par les services sur la table entière, pas sur une
                page. Stock, livreurs, établissements, règlements, commissions et
                tarification n'apparaissent pas ici : leurs routes rendent une liste bornée,
                sans total — un chiffre y serait un plancher déguisé en total.
            </p>

            <h2>Ce que disent les chiffres</h2>

            {facettes.isError ? (
                <EtatErreur erreur={facettes.error} onReessayer={() => void facettes.refetch()} />
            ) : (
                facettes.data && (
                    <BarresHorizontales
                        titre="Commandes par statut"
                        parts={partsCommandes(facettes.data)}
                    />
                )
            )}

            {/*
              * LES PAIEMENTS PEUVENT MANQUER, ET L'ÉCRAN LE DIT SANS S'EFFONDRER.
              *
              * Cette route est gardée par `.RequireAdmin()` côté service et
              * traverse une réécriture de passerelle. Un échec ici n'empêche pas
              * les deux autres graphes : chaque requête est indépendante.
              */}
            {paiements.isError ? (
                <p className="erreur-en-ligne">
                    Les statistiques de paiement n'ont pas répondu. Les deux autres graphes
                    restent exacts.
                </p>
            ) : (
                paiements.data && (
                    <BarreEmpilee
                        titre={`Paiements — ${paiements.data.total.toLocaleString('fr-FR')} au total`}
                        parts={partsPaiements(paiements.data)}
                        note={
                            <>
                                Montant encaissé :{' '}
                                <strong>
                                    {paiements.data.capturedAmount.toLocaleString('fr-FR')}
                                </strong>{' '}
                                — <em>sans devise</em>. Le contrat rend ce total nu alors que
                                chaque paiement porte la sienne : le chiffre est juste tant que
                                la plateforme n'encaisse qu'en francs CFA, et deviendra faux
                                sans le dire au premier paiement en euro.
                            </>
                        }
                    />
                )
            )}

            {lots.isError ? (
                <EtatErreur erreur={lots.error} onReessayer={() => void lots.refetch()} />
            ) : (
                lots.data && <ReglementsParPeriode lots={lots.data} />
            )}

            <p className="indice">
                Trois graphes, pas dix. Ce sont les seuls que l'API peut adosser exactement :
                les facettes sont calculées côté serveur sur la table entière, et la liste des
                lots de règlement est la SEULE réponse complète, datée et chiffrée du dépôt —
                elle ne prend ni page ni borne. Une courbe de commandes par mois, un chiffre
                d'affaires par vendeur ou un entonnoir de conversion demanderaient des
                endpoints qui n'existent pas ; les dessiner sur une page rapatriée donnerait
                des chiffres faux d'allure plausible.
            </p>

            <h2>Raccourcis</h2>

            {NAVIGATION.filter(section => section.title).map(section => (
                <div key={section.title} className="raccourcis">
                    <h3 className="raccourcis__titre">{section.title}</h3>
                    <div className="raccourcis__liens">
                        {section.items.map(item => (
                            <Link key={item.to} to={item.to} className="raccourci">
                                {item.icon}
                                <span>{item.label}</span>
                            </Link>
                        ))}
                    </div>
                </div>
            ))}
        </section>
    )
}

/**
 * LES HUIT STATUTS DE COMMANDE, DANS L'ORDRE DU DOMAINE.
 *
 * PAS TRIÉS PAR VALEUR. Un ordre qui change à chaque rafraîchissement oblige à
 * relire les libellés à chaque fois ; un ordre fixe se mémorise, et l'œil voit
 * alors la barre qui a bougé. C'est aussi ce qui permet de repérer un statut
 * TOMBÉ À ZÉRO — un tri par valeur le renverrait en fin de liste, là où on ne
 * le cherche pas.
 *
 * Un statut inconnu du domaine est ajouté à la fin plutôt qu'ignoré : le jour où
 * le service en introduit un, il apparaît sous son nom technique au lieu de
 * disparaître.
 */
const ORDRE_STATUTS_COMMANDE = [
    'Pending',
    'AwaitingPayment',
    'Paid',
    'Confirmed',
    'Delivered',
    'Cancelled',
    'Failed',
    'UnderReview',
]

function partsCommandes(facettes: Record<string, number>): Part[] {
    const connus = new Set(ORDRE_STATUTS_COMMANDE)
    const inconnus = Object.keys(facettes).filter(c => !connus.has(c))

    return [...ORDRE_STATUTS_COMMANDE, ...inconnus].map(cle => ({
        cle,
        libelle: libelleStatutCommande(cle),
        valeur: facettes[cle] ?? 0,
        // LE TON SUIT L'ÉTAT, PAS LE RANG. Et il ne parle jamais seul : la
        // marque « à traiter » est écrite à côté de la valeur.
        ton: STATUTS_A_TRAITER.has(cle) ? ('critique' as const) : undefined,
        marque: STATUTS_A_TRAITER.has(cle) && (facettes[cle] ?? 0) > 0 ? 'à traiter' : undefined,
    }))
}

/**
 * LES CINQ SEGMENTS DE LA BARRE DE PAIEMENTS.
 *
 * « AUTRES » N'EST PAS UN REMPLISSAGE, C'EST UNE CORRECTION.
 *
 * `PaymentStatsSummary` rend un `Total` ET quatre compteurs qui ne le totalisent
 * PAS forcément : un paiement seulement initié n'est ni capturé, ni en attente
 * au sens du compteur, ni échoué, ni remboursé. Empiler les quatre sur cent pour
 * cent ferait donc une barre qui affirme une répartition complète là où il en
 * manque une part — et la part manquante est justement celle qui n'aboutit pas.
 *
 * L'ORDRE DES SEGMENTS EST CELUI QUE LE VALIDATEUR ACCEPTE, et il se trouve
 * qu'il est aussi celui du récit : encaissé, en attente, échoué, remboursé. Voir
 * l'encadré de `Graphes.tsx`.
 */
function partsPaiements(s: {
    total: number
    capturedCount: number
    pendingCount: number
    failedCount: number
    refundedCount: number
}): Part[] {
    const comptes = s.capturedCount + s.pendingCount + s.failedCount + s.refundedCount
    const autres = Math.max(0, s.total - comptes)

    const parts: Part[] = [
        { cle: 'captured', libelle: 'Encaissés', valeur: s.capturedCount, ton: 'bon' },
        { cle: 'pending', libelle: 'En attente', valeur: s.pendingCount, ton: 'attention' },
        { cle: 'failed', libelle: 'Échoués', valeur: s.failedCount, ton: 'critique' },
        { cle: 'refunded', libelle: 'Remboursés', valeur: s.refundedCount, ton: 'serieux' },
    ]

    if (autres > 0) {
        parts.push({ cle: 'autres', libelle: 'Autres états', valeur: autres, ton: 'neutre' })
    }

    return parts
}

/**
 * LE NET REVERSÉ PAR PÉRIODE — LA SEULE SÉRIE TEMPORELLE DE LA PLATEFORME.
 *
 * `ListSettlementBatchesQuery` ne prend ni page ni borne : ce que le service
 * rend EST tout ce qui existe. Aucun autre endpoint d'administration n'accepte
 * de fenêtre de dates ni ne rend d'agrégat par période.
 *
 * UNE SEULE DEVISE À L'ÉCRAN, JAMAIS UNE SOMME.
 *
 * Le lot porte la sienne. Additionner des XOF et des EUR donnerait un nombre
 * faux d'un facteur six cent cinquante, et parfaitement plausible. On trace donc
 * la devise la plus représentée et on ANNONCE combien de lots restent dehors —
 * les taire ferait croire à un total de plateforme.
 */
function ReglementsParPeriode({ lots }: { lots: LotReglement[] }) {
    if (lots.length === 0) {
        return (
            <figure className="viz">
                <figcaption className="viz__titre">Net reversé par période</figcaption>
                <p className="indice">
                    Aucun lot de règlement. Ce n'est pas une erreur : le graphe se remplit au
                    premier règlement lancé, et son absence dit qu'aucun vendeur n'a encore été
                    payé.
                </p>
            </figure>
        )
    }

    const parDevise = new Map<string, LotReglement[]>()
    for (const l of lots) {
        parDevise.set(l.currency, [...(parDevise.get(l.currency) ?? []), l])
    }

    const [devise, retenus] = [...parDevise.entries()].sort((a, b) => b[1].length - a[1].length)[0]
    const dehors = lots.length - retenus.length

    // L'ORDRE EST CELUI DU TEMPS. Trier par montant ferait un classement que
    // l'œil lirait comme une évolution.
    const colonnes: Colonne[] = [...retenus]
        .sort((a, b) => a.periodStartUtc.localeCompare(b.periodStartUtc))
        .map(l => ({
            cle: l.id,
            libelle: formaterPeriode(l.periodStartUtc, l.periodEndUtc).split(' → ')[0],
            valeur: l.totalNet,
            texte: formaterMontant(l.totalNet, l.currency),
        }))

    return (
        <>
            <Colonnes titre={`Net reversé par période — ${devise}`} colonnes={colonnes} />
            {dehors > 0 && (
                <p className="indice">
                    {dehors} lot{dehors > 1 ? 's' : ''} dans une autre devise{' '}
                    {dehors > 1 ? 'ne sont' : "n'est"} pas tracé{dehors > 1 ? 's' : ''} ici. Les
                    additionner donnerait un nombre faux et plausible.
                </p>
            )}
        </>
    )
}
