import { API_BASE_URL } from '../../config/env'
import { requete } from '../../api/client'
import { lireListe, lirePage, versQuery } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * SUPERVISION — CE QUE CETTE PAGE PEUT VOIR, ET CE QU'ELLE NE PEUT PAS.
 *
 * ELLE NE SURVEILLE PAS L'INFRASTRUCTURE, ET NE LE PEUT PAS DEPUIS UN
 * NAVIGATEUR.
 *
 *   — La pile d'observabilité — OpenTelemetry, Prometheus, Tempo, Loki,
 *     Grafana — existe dans `docker-compose.observability.yml` et n'est PAS
 *     dans `docker-compose.prod.yml`. Elle ne tourne pas en production.
 *   — Aucun service n'expose `/metrics` : `MapPrometheusScrapingEndpoint`
 *     n'apparaît nulle part dans le dépôt.
 *   — Les sondes `/health/ready` des dix-neuf services vivent sur le réseau
 *     interne. La passerelle ne relaie que `/api/...` ; un navigateur ne les
 *     atteint pas. Seule la passerelle elle-même répond sur `/health` et
 *     `/health/ready`, à la racine du domaine.
 *
 * CE QU'ELLE SURVEILLE DONC : LA PLATEFORME VUE PAR SON API.
 *
 * Deux choses, et elles sont exactes :
 *
 *   1. La passerelle répond-elle, et en combien de temps.
 *   2. COMBIEN DE DOSSIERS ATTENDENT UN HUMAIN, domaine par domaine. Ces
 *      nombres viennent des facettes calculées CÔTÉ SERVEUR sur la table
 *      entière — pas d'un comptage de page. Là où le service n'en rend pas, on
 *      demande une page filtrée et on lit son `total`, qui est tout aussi
 *      exact.
 *
 * LES TROIS COMPTEURS INEXACTS SONT MARQUÉS COMME TELS. Stock, livreurs et
 * établissements n'offrent ni facettes ni total : leur route rend une liste
 * bornée par `take`. Quand la liste atteint la borne, on affiche « au moins N »
 * plutôt qu'un nombre qui aurait l'air d'un total.
 * ═══════════════════════════════════════════════════════════════════════════
 */

/** Sonde HTTP simple, avec le temps mesuré côté navigateur. */
export type Sonde = {
    nom: string
    chemin: string
    code: number
    /** Millisecondes, arrondies. Mesure client : réseau compris. */
    duree: number
    ok: boolean
}

async function sonder(nom: string, chemin: string, signal?: AbortSignal): Promise<Sonde> {
    const debut = performance.now()
    try {
        const reponse = await fetch(`${API_BASE_URL}${chemin}`, { signal })
        return {
            nom,
            chemin,
            code: reponse.status,
            duree: Math.round(performance.now() - debut),
            ok: reponse.ok,
        }
    } catch {
        // Une requête qui n'aboutit pas n'a pas de code. `0` le dit, et la
        // durée mesurée reste utile : elle distingue un refus immédiat d'un
        // silence de plusieurs secondes.
        return {
            nom,
            chemin,
            code: 0,
            duree: Math.round(performance.now() - debut),
            ok: false,
        }
    }
}

export function sonderPasserelle(signal?: AbortSignal): Promise<Sonde[]> {
    return Promise.all([
        sonder('Passerelle · vivante', '/health', signal),
        sonder('Passerelle · apte', '/health/ready', signal),
    ])
}

/*
 * ═══════════════════════════════════════════════════════════════════════════
 * CE PARAGRAPHE AFFIRMAIT QUE LES STATISTIQUES DE PAIEMENT ÉTAIENT
 *     INJOIGNABLES. C'ÉTAIT FAUX, ET L'ERREUR MÉRITE DE RESTER ÉCRITE.
 *
 * Il concluait : « la passerelle ne route, sous /api/financial, que commissions,
 * invoices et settlements […] la route rend donc 404 ». Le raisonnement était
 * juste ; la prémisse ne l'était pas.
 *
 * L'entrée `payments` de la passerelle fait correspondre
 * `/api/payments/{**catch-all}` et le RÉÉCRIT en
 * `/api/financial/payments/{**catch-all}` — le groupe que monte réellement le
 * service. `GET /api/payments/stats` arrive donc sur `GetPaymentStatsAsync`,
 * gardé par `.RequireAdmin()`.
 *
 * L'ERREUR ÉTAIT DE CHERCHER LE CHEMIN DE SORTIE DANS UNE TABLE QUI INDEXE LES
 * CHEMINS D'ENTRÉE. Six routes du fichier portent une réécriture, et son propre
 * commentaire prévient : « PRÉFIXE PUBLIC ≠ PRÉFIXE DU SERVICE […] sans aucune
 * erreur de configuration pour l'expliquer, puisque le cluster et la destination
 * sont corrects ». Une absence dans une table de routage ne prouve rien tant
 * qu'on n'a pas lu les transformations.
 *
 * LA SUPERVISION NE L'APPELLE TOUJOURS PAS, MAIS POUR UNE AUTRE RAISON. Cette
 * page compte ce qui ATTEND UN GESTE ; `/stats` rend des volumes. Le graphe
 * qu'ils alimentent est sur l'accueil — voir `features/accueil/api.ts`.
 * ═══════════════════════════════════════════════════════════════════════════
 */

/**
 * Une file d'attente : un nombre, ce qu'il compte, et où aller le traiter.
 *
 * `exact` DISTINGUE UN TOTAL D'UN PLANCHER. Sans ce drapeau, « 200 » venu d'une
 * liste bornée à 200 se lirait comme le compte de la plateforme.
 */
export type File = {
    cle: string
    libelle: string
    domaine: string
    nombre: number
    exact: boolean
    lien: string
    /** Ce compteur mérite-t-il un regard immédiat. */
    urgent: boolean
}

/**
 * Lit les facettes d'une liste paginée SANS rapatrier ses lignes.
 *
 * `pageSize=1` : les facettes sont calculées sur la table entière, la page
 * demandée n'a aucune influence dessus. Demander vingt lignes pour n'en lire
 * aucune serait du gaspillage à chaque rafraîchissement.
 */
async function facettes(
    chemin: string,
    signal?: AbortSignal,
): Promise<Record<string, number>> {
    const corps = await requete<unknown>(`${chemin}${versQuery({ page: 1, pageSize: 1 })}`, {
        signal,
    })
    return lirePage<unknown>(corps, 1, 1).facettes ?? {}
}

/** Total d'une liste filtrée — exact, comme les facettes. */
async function total(chemin: string, signal?: AbortSignal): Promise<number> {
    const corps = await requete<unknown>(chemin, { signal })
    return lirePage<unknown>(corps, 1, 1).total
}

/** Longueur d'une liste bornée : exacte seulement si la borne n'est pas atteinte. */
async function bornee(
    chemin: string,
    borne: number,
    signal?: AbortSignal,
): Promise<{ nombre: number; exact: boolean }> {
    const corps = await requete<unknown>(chemin, { signal })
    const items = lireListe<unknown>(corps)
    return { nombre: items.length, exact: items.length < borne }
}

const BORNE = 200

/**
 * Chaque file est lue INDÉPENDAMMENT.
 *
 * Une seule requête qui échoue ne doit pas vider le tableau de bord : un
 * service indisponible fait disparaître SA ligne, pas les onze autres. C'est
 * `allSettled` et non `all` — et l'écran affiche ce qui a répondu.
 */
export async function lireFiles(signal?: AbortSignal): Promise<{
    files: File[]
    echecs: string[]
}> {
    const lectures: Array<{ domaine: string; lire: () => Promise<File[]> }> = [
        {
            domaine: 'Commandes',
            lire: async () => {
                const f = await facettes('/api/admin/orders', signal)
                return [
                    file('cmd-arbitrage', 'En arbitrage', 'Commandes', f.UnderReview ?? 0, true,
                        '/commandes?statut=UnderReview', true),
                    file('cmd-echec', 'Échouées', 'Commandes', f.Failed ?? 0, true,
                        '/commandes?statut=Failed', true),
                ]
            },
        },
        {
            domaine: 'Catalogue',
            lire: async () => {
                const f = await facettes('/api/v1/catalog/admin/products', signal)
                return [
                    file('cat-valider', 'Fiches à valider', 'Catalogue', f.PendingReview ?? 0, true,
                        '/catalogue?statut=PendingReview', true),
                ]
            },
        },
        {
            domaine: 'Retours',
            lire: async () => {
                const f = await facettes('/api/v1/admin/returns', signal)
                return [
                    file('ret-arbitrage', 'Arbitrage manuel', 'Retours', f.ManualReview ?? 0, true,
                        '/retours?statut=ManualReview', true),
                    file('ret-inspection', 'Inspection à faire', 'Retours',
                        f.InspectionPending ?? 0, true, '/retours?statut=InspectionPending', false),
                    file('ret-rembours', 'Remboursement à faire', 'Retours',
                        f.RefundPending ?? 0, true, '/retours?statut=RefundPending', true),
                ]
            },
        },
        {
            domaine: 'Comptes',
            lire: async () => {
                const f = await facettes('/api/identity/users', signal)
                return [
                    file('usr-verif', 'Comptes à vérifier', 'Comptes',
                        f.PendingVerification ?? 0, true,
                        '/administration/utilisateurs?statut=PendingVerification', false),
                ]
            },
        },
        {
            domaine: 'Factures',
            lire: async () => {
                const f = await facettes('/api/financial/invoices', signal)
                return [
                    file('fac-emises', 'Émises, non payées', 'Factures', f.Issued ?? 0, true,
                        '/finance/factures?statut=Issued', false),
                ]
            },
        },
        {
            domaine: 'Vendeurs',
            lire: async () => {
                // seller-service ne rend pas de facettes : on demande une page
                // filtrée et on lit son `total`, qui est exact.
                const n = await total(
                    `/api/v1/merchants${versQuery({ page: 1, pageSize: 1, kybStatus: 'InReview' })}`,
                    signal,
                )
                return [
                    file('ven-kyb', 'Dossiers KYB en revue', 'Vendeurs', n, true,
                        '/vendeurs?kyb=InReview', true),
                ]
            },
        },
        {
            domaine: 'Livreurs',
            lire: async () => {
                const r = await bornee(
                    `/api/v1/admin/drivers${versQuery({ status: 'UnderReview', take: BORNE })}`,
                    BORNE,
                    signal,
                )
                return [
                    file('liv-examen', 'Dossiers à examiner', 'Livreurs', r.nombre, r.exact,
                        '/livraison/livreurs?statut=UnderReview', false),
                ]
            },
        },
        {
            domaine: 'Restauration',
            lire: async () => {
                const r = await bornee(
                    `/api/food/admin/restaurants/pending${versQuery({ take: BORNE })}`,
                    BORNE,
                    signal,
                )
                return [
                    file('res-valider', 'Établissements à valider', 'Restauration',
                        r.nombre, r.exact, '/restauration/etablissements', false),
                ]
            },
        },
        {
            domaine: 'Stock',
            lire: async () => {
                const corps = await requete<unknown>(
                    `/api/inventory/low-stock${versQuery({ take: BORNE })}`,
                    { signal },
                )
                const articles = lireListe<{ available: number }>(corps)
                const ruptures = articles.filter(a => a.available <= 0).length
                return [
                    file('stk-seuil', 'Articles sous seuil', 'Stock', articles.length,
                        articles.length < BORNE, '/stock', false),
                    // La rupture est comptée sur les articles reçus : elle est
                    // donc exacte SI la liste n'est pas bornée, comme la
                    // précédente.
                    file('stk-rupture', 'En rupture', 'Stock', ruptures,
                        articles.length < BORNE, '/stock', ruptures > 0),
                ]
            },
        },
        {
            domaine: 'Règlements',
            lire: async () => {
                const corps = await requete<unknown>('/api/financial/settlements', { signal })
                const lots = lireListe<{
                    status: string
                    payouts: Array<{ status: string }>
                }>(corps)
                // La liste est complète — aucune borne sur cette route — donc
                // ces deux comptes sont exacts.
                const echoues = lots.reduce(
                    (n, l) => n + l.payouts.filter(p => p.status === 'Failed').length,
                    0,
                )
                const partiels = lots.filter(l => l.status === 'PartiallyFailed').length
                return [
                    file('reg-versements', 'Versements refusés', 'Règlements', echoues, true,
                        '/finance/reglements', echoues > 0),
                    file('reg-lots', 'Lots partiellement échoués', 'Règlements', partiels, true,
                        '/finance/reglements?statut=PartiallyFailed', partiels > 0),
                ]
            },
        },
    ]

    const resultats = await Promise.allSettled(lectures.map(l => l.lire()))
    const files: File[] = []
    const echecs: string[] = []

    resultats.forEach((r, i) => {
        if (r.status === 'fulfilled') files.push(...r.value)
        else echecs.push(lectures[i].domaine)
    })

    return { files, echecs }
}

function file(
    cle: string,
    libelle: string,
    domaine: string,
    nombre: number,
    exact: boolean,
    lien: string,
    urgent: boolean,
): File {
    return { cle, libelle, domaine, nombre, exact, lien, urgent: urgent && nombre > 0 }
}
