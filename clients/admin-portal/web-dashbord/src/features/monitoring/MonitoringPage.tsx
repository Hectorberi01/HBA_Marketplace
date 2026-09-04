import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { EtatErreur, VoileChargement } from '../../components/tableau/Etats'
import { formaterDate } from '../../lib/format'
import { lireFiles, sonderPasserelle, type File, type Sonde } from './api'

/** Intervalle de rafraîchissement, en millisecondes. */
const RAFRAICHISSEMENT = 60_000

/**
 * SUPERVISION.
 *
 * ELLE NE SURVEILLE PAS L'INFRASTRUCTURE. La pile d'observabilité existe dans
 * `docker-compose.observability.yml` et n'est pas déployée en production ;
 * aucun service n'expose `/metrics` ; les sondes `/health/ready` des dix-neuf
 * services vivent sur le réseau interne, que la passerelle ne relaie pas. Cette
 * page le dit en haut, pour qu'on ne s'y fie pas pour ce qu'elle ne fait pas.
 *
 * ELLE SURVEILLE LA PLATEFORME VUE PAR SON API : la passerelle répond-elle, et
 * combien de dossiers attendent un humain.
 *
 * LE RAFRAÎCHISSEMENT EST VISIBLE ET INTERRUPTIBLE. Une page qui se recharge
 * toute seule sans le dire fait douter de la fraîcheur de ce qu'on lit ; l'heure
 * de la dernière lecture est affichée, et le rechargement manuel existe.
 */
export default function MonitoringPage() {
    const sondes = useQuery({
        queryKey: ['supervision', 'sondes'],
        queryFn: ({ signal }) => sonderPasserelle(signal),
        refetchInterval: RAFRAICHISSEMENT,
        // Une sonde ne se rejoue pas : son échec EST le résultat qu'on veut
        // voir. Réessayer deux fois masquerait une panne intermittente.
        retry: false,
    })

    const files = useQuery({
        queryKey: ['supervision', 'files'],
        queryFn: ({ signal }) => lireFiles(signal),
        refetchInterval: RAFRAICHISSEMENT,
    })

    const urgentes = (files.data?.files ?? []).filter(f => f.urgent)
    const autres = (files.data?.files ?? []).filter(f => !f.urgent && f.nombre > 0)
    const vides = (files.data?.files ?? []).filter(f => f.nombre === 0)

    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Supervision</h1>
                <div className="filtres">
                    <span className="indice">
                        {files.dataUpdatedAt
                            ? `Lu à ${formaterDate(new Date(files.dataUpdatedAt).toISOString())}`
                            : '—'}
                    </span>
                    <button
                        type="button"
                        className="lien-deconnexion"
                        onClick={() => {
                            void sondes.refetch()
                            void files.refetch()
                        }}
                    >
                        Recharger
                    </button>
                </div>
            </header>

            <p className="indice">
                Cette page regarde la plateforme <strong>par son API</strong>, pas son
                infrastructure : la pile d'observabilité n'est pas déployée en production,
                aucun service n'expose de métriques, et les sondes de santé des dix-neuf
                services vivent sur le réseau interne. Rafraîchissement automatique toutes
                les minutes.
            </p>

            <h2>Passerelle</h2>
            <div className="tuiles">
                {(sondes.data ?? []).map(s => (
                    <TuileSonde key={s.chemin} sonde={s} />
                ))}
                {sondes.isError && (
                    <EtatErreur erreur={sondes.error} onReessayer={() => void sondes.refetch()} />
                )}
            </div>

            <h2>Dossiers en attente</h2>

            {files.isError ? (
                <EtatErreur erreur={files.error} onReessayer={() => void files.refetch()} />
            ) : (
                <div className="tableau-enveloppe">
                    {files.isFetching && <VoileChargement />}

                    {/*
                      * LES DOMAINES QUI N'ONT PAS REPONDU SONT NOMMES.
                      * Sans cette ligne, un service indisponible ferait
                      * simplement disparaitre ses compteurs : on lirait « rien
                      * n'attend » alors qu'on ne sait pas.
                      */}
                    {files.data && files.data.echecs.length > 0 && (
                        <p className="erreur" role="alert">
                            Sans réponse : {files.data.echecs.join(', ')}. Leurs compteurs sont
                            absents du tableau — ce n'est pas « zéro », c'est « inconnu ».
                        </p>
                    )}

                    <table className={`tableau ${files.isFetching ? 'est-en-attente' : ''}`}>
                        <caption className="visuellement-cache">
                            Dossiers en attente d'un traitement humain, par domaine
                        </caption>
                        <thead>
                            <tr>
                                <th scope="col">Domaine</th>
                                <th scope="col">File</th>
                                <th scope="col" className="au-bout">En attente</th>
                                <th scope="col"> </th>
                            </tr>
                        </thead>
                        <tbody>
                            {[...urgentes, ...autres, ...vides].map(f => (
                                <LigneFile key={f.cle} file={f} />
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </section>
    )
}

function TuileSonde({ sonde }: { sonde: Sonde }) {
    return (
        <div className={`tuile ${sonde.ok ? '' : 'tuile--alerte'}`}>
            <div className="tuile__titre">{sonde.nom}</div>
            <div className="tuile__valeur">
                {/*
                 * `0` N'EST PAS UN CODE HTTP. C'est ce que rend une requête qui
                 * n'a jamais abouti — hors ligne, DNS, TLS, CORS. L'écrire « — »
                 * le distingue d'un vrai code de réponse.
                 */}
                {sonde.code === 0 ? '—' : sonde.code}
            </div>
            <div className="indice">
                {sonde.duree} ms · <code>{sonde.chemin}</code>
            </div>
        </div>
    )
}

function LigneFile({ file }: { file: File }) {
    return (
        <tr className={file.urgent ? 'a-traiter' : undefined}>
            <td>{file.domaine}</td>
            <td>
                <div className="cellule-titre">{file.libelle}</div>
                {!file.exact && (
                    /*
                     * LA BORNE EST DITE. Ces routes rendent une liste limitée par
                     * `take` et n'offrent ni facettes ni total : quand la liste
                     * atteint la borne, le nombre affiché est un PLANCHER. Le
                     * présenter comme un total serait faux d'autant qu'il en
                     * reste.
                     */
                    <div className="indice">
                        liste bornée par l'API : au moins ce nombre, pas le total
                    </div>
                )}
            </td>
            <td className="au-bout">
                <span className="compteur">
                    {file.exact ? '' : '≥ '}
                    {file.nombre}
                </span>
            </td>
            <td>
                {file.nombre > 0 && (
                    <Link to={file.lien} className="lien-file">
                        Traiter
                    </Link>
                )}
            </td>
        </tr>
    )
}
