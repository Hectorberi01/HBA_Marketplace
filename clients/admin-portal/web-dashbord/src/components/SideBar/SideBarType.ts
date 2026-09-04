import { type ReactNode } from 'react'

/**
 * MODÈLE DE NAVIGATION.
 *
 * Les entrées portent une DESTINATION, plus un identifiant opaque.
 *
 * La première version décrivait chaque entrée par un `id`, et la barre rendait
 * des `<button onClick>`. Trois choses s'en trouvaient perdues, toutes gratuites
 * avec un vrai lien : le clic du milieu n'ouvrait pas d'onglet, `Cmd`/`Ctrl` +
 * clic non plus, et le navigateur n'affichait aucune destination au survol.
 * Elle obligeait en plus à tenir une table `id -> chemin` ailleurs, donc à
 * pouvoir la désynchroniser.
 */

export type SidebarItem = {
    /** Chemin de destination, tel qu'il apparaît dans l'URL. */
    to: string
    label: string
    icon: ReactNode
    /**
     * Correspondance EXACTE plutôt que par préfixe.
     *
     * Nécessaire pour l'accueil : `/` est le préfixe de tout, et sans cela
     * l'entrée resterait allumée sur chaque écran du portail.
     */
    exact?: boolean
    badge?: number
}

export type SidebarSection = {
    title?: string
    items: SidebarItem[]
}

export type SidebarProps = {
    sections: SidebarSection[]
    brand?: ReactNode
    footer?: ReactNode
    defaultCollapsed?: boolean
}
