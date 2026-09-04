/**
 * Icônes en ligne.
 *
 * Sorties de `App.tsx`, où elles étaient déclarées APRÈS le tableau qui les
 * utilise. Cela fonctionnait — les déclarations de fonction sont hissées — mais
 * la constante `stroke`, elle, ne l'est pas : elle était lue avant sa ligne de
 * déclaration à chaque rendu des icônes. Le code marchait par accident d'ordre
 * d'exécution, pas par construction.
 */

const trait = {
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.6,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
}

export function IconeAccueil() {
    return (
        <svg viewBox="0 0 24 24" {...trait}>
            <path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />
        </svg>
    )
}

export function IconeColis() {
    return (
        <svg viewBox="0 0 24 24" {...trait}>
            <path d="M21 8 12 3 3 8v8l9 5 9-5z" />
            <path d="M3 8l9 5 9-5M12 13v8" />
        </svg>
    )
}

export function IconeUtilisateurs() {
    return (
        <svg viewBox="0 0 24 24" {...trait}>
            <circle cx="9" cy="8" r="3.5" />
            <path d="M2.5 20a6.5 6.5 0 0 1 13 0M17 5.5a3.5 3.5 0 0 1 0 7M18 14.5a6 6 0 0 1 3.5 5.5" />
        </svg>
    )
}

export function IconeReglages() {
    return (
        <svg viewBox="0 0 24 24" {...trait}>
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 7.5 19.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0-1.1-2.7H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 7.5l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1A1.6 1.6 0 0 0 10 3.6V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 2.5 1.1l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0 1.1 2.5H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z" />
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
