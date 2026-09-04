/**
 * Icônes en ligne, une par entrée de navigation.
 *
 * Sorties de `App.tsx`, où elles étaient déclarées APRÈS le tableau qui les
 * utilise. Cela fonctionnait — les déclarations de fonction sont hissées — mais
 * la constante `stroke`, elle, ne l'est pas : elle était lue avant sa ligne de
 * déclaration à chaque rendu. Le code marchait par accident d'ordre
 * d'exécution, pas par construction.
 *
 * Toutes utilisent `currentColor` : la couleur vient du CSS de la barre, donc
 * l'état actif, le survol et le thème sombre s'appliquent sans qu'aucune icône
 * ait à le savoir.
 */

const t = {
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.6,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
}

export function IconeAccueil() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />
        </svg>
    )
}

export function IconeCommandes() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M21 8 12 3 3 8v8l9 5 9-5z" />
            <path d="M3 8l9 5 9-5M12 13v8" />
        </svg>
    )
}

export function IconeCatalogue() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M4 5a2 2 0 0 1 2-2h9l5 5v11a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z" />
            <path d="M15 3v5h5M8 12h8M8 16h5" />
        </svg>
    )
}

export function IconeStock() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 7h18v13H3zM3 7l2-4h14l2 4" />
            <path d="M9 12h6" />
        </svg>
    )
}

export function IconeVendeurs() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M4 9h16v11H4zM4 9 6 4h12l2 5" />
            <path d="M8 20v-5h4v5" />
        </svg>
    )
}

export function IconeRetours() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M9 14 4 9l5-5" />
            <path d="M4 9h10a6 6 0 0 1 0 12H8" />
        </svg>
    )
}

export function IconeRestaurants() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M6 3v8a2 2 0 0 0 4 0V3M8 11v10" />
            <path d="M17 3c-1.5 1.5-2 3-2 5s.5 3 2 3v10" />
        </svg>
    )
}

export function IconeRepas() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 13a9 9 0 0 1 18 0z" />
            <path d="M2 17h20M12 4v1.5" />
        </svg>
    )
}

export function IconeLivreurs() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <circle cx="6" cy="17" r="3" />
            <circle cx="18" cy="17" r="3" />
            <path d="M9 17h6M6 17l4-9h4l3 9M10 8h5" />
        </svg>
    )
}

export function IconeTarification() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 12 12 3l9 9-9 9z" />
            <circle cx="9" cy="9" r="1.3" />
        </svg>
    )
}

export function IconeReglements() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 6h18v12H3z" />
            <path d="M3 10h18M7 15h4" />
        </svg>
    )
}

export function IconeCommissions() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M5 19 19 5" />
            <circle cx="7.5" cy="7.5" r="2.5" />
            <circle cx="16.5" cy="16.5" r="2.5" />
        </svg>
    )
}

export function IconeFactures() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M6 3h12v18l-3-2-3 2-3-2-3 2z" />
            <path d="M9 8h6M9 12h6" />
        </svg>
    )
}

export function IconeUtilisateurs() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <circle cx="9" cy="8" r="3.5" />
            <path d="M2.5 20a6.5 6.5 0 0 1 13 0M17 5.5a3.5 3.5 0 0 1 0 7M18 14.5a6 6 0 0 1 3.5 5.5" />
        </svg>
    )
}

export function IconeRoles() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M12 3l7 3v6c0 4-3 7-7 9-4-2-7-5-7-9V6z" />
            <path d="M9 12l2 2 4-4" />
        </svg>
    )
}

export function IconeSupervision() {
    return (
        <svg viewBox="0 0 24 24" {...t}>
            <path d="M3 12h4l2.5-6 4 12 2.5-6h5" />
        </svg>
    )
}

export function Logo() {
    return (
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <rect x="2" y="2" width="20" height="20" rx="6" fill="#1b5e4a" />
            <path d="M8 15V9m0 3h8m0-3v6" stroke="#fff" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
    )
}
