import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../components/tableau/Etats'
import { NAVIGATION } from '../layout/navigation'
import { useAuth } from '../auth/useAuth'
import { formaterDate } from '../lib/format'
import { lireVolumes } from '../features/accueil/api'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * ACCUEIL.
 *
 * CE QU'IL FAIT : dire à qui l'on est connecté, ce que la plateforme porte, et
 * où aller. Trois choses, toutes exactes.
 *
 * CE QU'IL NE FAIT PAS, ET POURQUOI.
 *
 *   PAS DE CHIFFRE D'AFFAIRES. `payment-service` monte bien
 *   `GET /api/financial/payments/stats` — total, montant capturé, en attente,
 *   échoué, remboursé, c'est-à-dire exactement le bandeau qu'on attend d'un
 *   accueil. La passerelle ne route rien sous `/api/financial/payments` : la
 *   route rend 404 depuis n'importe quel client. Une tuile en erreur permanente
 *   apprend à ignorer les erreurs ; on ne l'appelle pas.
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
