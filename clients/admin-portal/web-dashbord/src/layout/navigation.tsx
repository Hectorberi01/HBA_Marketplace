import type { SidebarSection } from '../components/SideBar/SideBarType'
import {
    IconeAccueil,
    IconeCatalogue,
    IconeCommandes,
    IconeCommissions,
    IconeFactures,
    IconeLivreurs,
    IconeReglements,
    IconeRepas,
    IconeRestaurants,
    IconeRetours,
    IconeRoles,
    IconeStock,
    IconeTarification,
    IconeUtilisateurs,
    IconeVendeurs,
} from '../components/Icones'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * NAVIGATION DU PORTAIL D'ADMINISTRATION.
 *
 * CHAQUE ENTRÉE CORRESPOND À UN GROUPE `MapAdminGroup` QUI EXISTE VRAIMENT.
 *
 * Ce n'est pas une maquette : les quinze écrans ci-dessous sont exactement les
 * quinze surfaces d'administration montées par les services, et le chemin d'API
 * de chacune est écrit en commentaire. Une barre latérale inventée mènerait à
 * des écrans qu'on ne pourrait jamais brancher, et personne ne saurait dire
 * lesquels avant d'essayer.
 *
 * DEUX GROUPES ADMIN SONT DÉLIBÉRÉMENT ABSENTS, ET LA RAISON EST LA MÊME :
 * LA PASSERELLE NE LES ROUTE PAS.
 *
 *   Modération des avis — review-service monte `/api/engagement/reviews` et
 *   `/api/engagement/recommendations`. La passerelle ne connaît que
 *   `/api/reviews` et `/api/recommendations` ; aucune route ne commence par
 *   `/api/engagement`. Ces endpoints sont donc injoignables depuis n'importe
 *   quel client, pas seulement depuis ce portail.
 *
 *   Écriture sur les règlements — payment-service monte l'administration des
 *   règlements sur `/api/financial/settlements`, mais la route `settlements` de
 *   la passerelle n'accepte que GET, HEAD et OPTIONS. La lecture passe, les
 *   gestes d'administration rendent 404. L'entrée « Règlements » figure donc
 *   ci-dessous en LECTURE SEULE.
 *
 * Ces deux constats sont des défauts de la passerelle, pas du portail. Les
 * inscrire ici les rend visibles au lieu de les laisser se découvrir écran par
 * écran.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export const NAVIGATION: SidebarSection[] = [
    {
        items: [
            // Pas d'API dédiée : l'accueil agrège, il n'a pas de groupe à lui.
            { to: '/', label: 'Accueil', icon: <IconeAccueil />, exact: true },
        ],
    },
    {
        title: 'Marketplace',
        items: [
            // order-service          /api/admin/orders
            { to: '/commandes', label: 'Commandes', icon: <IconeCommandes /> },
            // catalog-service        /api/v1/catalog/admin
            { to: '/catalogue', label: 'Catalogue', icon: <IconeCatalogue /> },
            // inventory-service      /api/inventory
            { to: '/stock', label: 'Stock', icon: <IconeStock /> },
            // seller-service         /api/v1/merchants  (gouvernance)
            { to: '/vendeurs', label: 'Vendeurs', icon: <IconeVendeurs /> },
            // return-refund-service  /api/v1/admin/returns
            { to: '/retours', label: 'Retours', icon: <IconeRetours /> },
        ],
    },
    {
        title: 'Restauration',
        items: [
            // restaurant-service     /api/food/admin
            { to: '/restauration/etablissements', label: 'Établissements', icon: <IconeRestaurants /> },
            // food-order-service     /api/admin/food/orders
            { to: '/restauration/commandes', label: 'Commandes repas', icon: <IconeRepas /> },
        ],
    },
    {
        title: 'Livraison',
        items: [
            // driver-service           /api/v1/admin/drivers
            { to: '/livraison/livreurs', label: 'Livreurs', icon: <IconeLivreurs /> },
            // delivery-pricing-service /api/v1/admin/delivery-pricing
            { to: '/livraison/tarification', label: 'Tarification', icon: <IconeTarification /> },
        ],
    },
    {
        title: 'Finance',
        items: [
            // payment-service  /api/financial/settlements   (LECTURE SEULE, voir l'encadré)
            { to: '/finance/reglements', label: 'Règlements', icon: <IconeReglements /> },
            // payment-service  /api/financial/commissions
            { to: '/finance/commissions', label: 'Commissions', icon: <IconeCommissions /> },
            // payment-service  /api/financial/invoices
            { to: '/finance/factures', label: 'Factures', icon: <IconeFactures /> },
        ],
    },
    {
        title: 'Administration',
        items: [
            // identity-service /api/identity/users
            { to: '/administration/utilisateurs', label: 'Utilisateurs', icon: <IconeUtilisateurs /> },
            // identity-service /api/identity/roles
            { to: '/administration/roles', label: 'Rôles', icon: <IconeRoles /> },
        ],
    },
]
