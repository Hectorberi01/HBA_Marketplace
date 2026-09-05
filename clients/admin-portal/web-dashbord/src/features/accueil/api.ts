import { requete } from '../../api/client'
import { lirePage, versQuery } from '../../api/pages'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * ACCUEIL — LES VOLUMES DE LA PLATEFORME.
 *
 * CE N'EST PAS LA SUPERVISION, ET LA DISTINCTION EST LE FOND DE CETTE PAGE.
 *
 * `/supervision` compte CE QUI ATTEND UN GESTE : les dossiers en arbitrage, les
 * fiches à valider, les remboursements à faire. Ce sont des files, elles doivent
 * tendre vers zéro, et un chiffre qui monte y est une alerte.
 *
 * Cette page-ci compte CE QUI EXISTE : combien de commandes, de produits, de
 * vendeurs, de comptes, de retours, de factures porte la plateforme. Ce sont des
 * stocks, ils ne descendent pas, et un chiffre qui monte y est une bonne
 * nouvelle. Confondre les deux ferait de l'accueil une seconde supervision, en
 * moins bien — et le jour d'un incident, on regarderait la mauvaise.
 *
 * LES TOTAUX SONT EXACTS, ET C'EST LA RAISON DE `pageSize=1`.
 *
 * Ces six routes rendent une page dont le `total` est calculé CÔTÉ SERVEUR sur
 * la table entière : il ne dépend ni de la page demandée ni de sa taille. On
 * demande donc la plus petite page possible et on ne lit que le total. Compter
 * les lignes reçues donnerait « 20 » sur chaque tuile.
 *
 * LES ROUTES SANS TOTAL SONT ABSENTES, ET C'EST DÉLIBÉRÉ.
 *
 * Stock, livreurs, établissements, règlements, commissions et règles de
 * tarification rendent une LISTE bornée, sans `total` ni facettes. On pourrait
 * afficher « au moins N » comme le fait la supervision, mais sur une tuile de
 * volume ce plancher se lirait comme un total — et personne ne relit la note de
 * bas de page deux semaines plus tard. Ces domaines restent accessibles par les
 * raccourcis, sans chiffre inventé.
 *
 * CHAQUE VOLUME EST LU INDÉPENDAMMENT. Un service en panne fait disparaître SA
 * tuile, pas les cinq autres : `allSettled`, et l'écran nomme ce qui manque.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Volume = {
    cle: string
    libelle: string
    /** Ce que la tuile compte, en une ligne — pour lever l'ambiguïté stock/file. */
    precision: string
    nombre: number
    lien: string
}

type Source = {
    cle: string
    libelle: string
    precision: string
    chemin: string
    lien: string
}

/**
 * L'ordre est celui de la barre latérale : marketplace, puis administration,
 * puis finance. Un accueil qui range autrement que le menu oblige à chercher
 * deux fois.
 */
const SOURCES: Source[] = [
    {
        cle: 'commandes',
        libelle: 'Commandes',
        precision: 'Tous statuts confondus, depuis l’ouverture.',
        chemin: '/api/admin/orders',
        lien: '/commandes',
    },
    {
        cle: 'produits',
        libelle: 'Produits',
        precision: 'Fiches du catalogue, brouillons et archives compris.',
        chemin: '/api/v1/catalog/admin/products',
        lien: '/catalogue',
    },
    {
        cle: 'vendeurs',
        libelle: 'Vendeurs',
        precision: 'Comptes vendeurs, quel que soit l’état du dossier KYB.',
        chemin: '/api/v1/merchants',
        lien: '/vendeurs',
    },
    {
        cle: 'retours',
        libelle: 'Retours',
        precision: 'Demandes de retour, ouvertes comme closes.',
        chemin: '/api/v1/admin/returns',
        lien: '/retours',
    },
    {
        cle: 'utilisateurs',
        libelle: 'Utilisateurs',
        precision: 'Comptes de la plateforme, tous rôles confondus.',
        chemin: '/api/identity/users',
        lien: '/administration/utilisateurs',
    },
    {
        cle: 'factures',
        libelle: 'Factures',
        precision: 'Factures émises, tous états de paiement.',
        chemin: '/api/financial/invoices',
        lien: '/finance/factures',
    },
]

async function total(chemin: string, signal?: AbortSignal): Promise<number> {
    const corps = await requete<unknown>(`${chemin}${versQuery({ page: 1, pageSize: 1 })}`, {
        signal,
    })
    return lirePage<unknown>(corps, 1, 1).total
}

export async function lireVolumes(signal?: AbortSignal): Promise<{
    volumes: Volume[]
    echecs: string[]
}> {
    const resultats = await Promise.allSettled(
        SOURCES.map(async source => ({ source, nombre: await total(source.chemin, signal) })),
    )

    const volumes: Volume[] = []
    const echecs: string[] = []

    resultats.forEach((resultat, index) => {
        if (resultat.status === 'fulfilled') {
            const { source, nombre } = resultat.value
            volumes.push({
                cle: source.cle,
                libelle: source.libelle,
                precision: source.precision,
                nombre,
                lien: source.lien,
            })
        } else {
            // Le nom du domaine, pas le message technique : la cause précise est
            // dans la console du navigateur, et l'écran doit rester lisible.
            echecs.push(SOURCES[index].libelle)
        }
    })

    return { volumes, echecs }
}
