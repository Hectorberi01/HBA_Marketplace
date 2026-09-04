import { useEffect, useState } from 'react'
import { NavLink } from 'react-router-dom'
import './SideBar.css'
import type { SidebarProps } from './SideBarType'

/**
 * L'IMPORT DU CSS RESPECTE LA CASSE DU FICHIER.
 *
 * Il disait `./Sidebar.css` alors que le fichier s'appelle `SideBar.css`. Sur
 * macOS le système de fichiers ignore la casse, donc cela fonctionnait sur le
 * poste de développement — et aurait échoué à la construction sur une image
 * Linux, avec un « Failed to resolve import » qui ne pointe vers aucune faute
 * visible dans le code.
 */

const CLE_REPLI = 'hba.admin.sidebar.repliee'

export default function SideBar({ sections, brand, footer, defaultCollapsed = false }: SidebarProps) {
    /*
     * L'ÉTAT REPLIÉ SURVIT AU RECHARGEMENT.
     *
     * Replier la barre est un geste qu'on fait une fois, pour de bon — sur un
     * portable, pour gagner la largeur d'un tableau. La redéployer à chaque
     * chargement oblige à le refaire indéfiniment.
     *
     * `localStorage` peut lever : navigation privée stricte, stockage désactivé
     * par une politique d'entreprise. Le confort disparaît alors, le portail
     * fonctionne.
     */
    const [repliee, setRepliee] = useState(() => {
        try {
            const garde = localStorage.getItem(CLE_REPLI)
            return garde === null ? defaultCollapsed : garde === '1'
        } catch {
            return defaultCollapsed
        }
    })

    useEffect(() => {
        try {
            localStorage.setItem(CLE_REPLI, repliee ? '1' : '0')
        } catch {
            // Sans stockage, le choix ne vaut que pour cette page.
        }
    }, [repliee])

    return (
        <nav
            className={`sidebar ${repliee ? 'sidebar--collapsed' : ''}`}
            aria-label="Navigation principale"
        >
            {brand && <div className="sidebar__brand">{brand}</div>}

            <div className="sidebar__scroll">
                {sections.map((section, i) => (
                    <div className="sidebar__section" key={section.title ?? i}>
                        {/*
                          * Le titre reste dans le DOM quand la barre est repliée,
                          * masqué visuellement. Le retirer changerait la structure
                          * annoncée aux lecteurs d'écran selon un état purement
                          * visuel : la même page n'aurait plus les mêmes repères
                          * selon la largeur choisie.
                          */}
                        {section.title && (
                            <h2 className={`sidebar__section-title ${repliee ? 'visuellement-cache' : ''}`}>
                                {section.title}
                            </h2>
                        )}

                        <ul className="sidebar__list">
                            {section.items.map(item => (
                                <li key={item.to}>
                                    <NavLink
                                        to={item.to}
                                        end={item.exact}
                                        className={({ isActive }) =>
                                            `sidebar__item ${isActive ? 'is-active' : ''}`
                                        }
                                        title={repliee ? item.label : undefined}
                                    >
                                        <span className="sidebar__icon" aria-hidden="true">
                                            {item.icon}
                                        </span>
                                        <span className="sidebar__label">{item.label}</span>
                                        {item.badge != null && item.badge > 0 && (
                                            <span className="sidebar__badge">{item.badge}</span>
                                        )}
                                    </NavLink>
                                </li>
                            ))}
                        </ul>
                    </div>
                ))}
            </div>

            {footer && !repliee && <div className="sidebar__footer">{footer}</div>}

            <button
                type="button"
                className="sidebar__toggle"
                aria-expanded={!repliee}
                onClick={() => setRepliee(c => !c)}
            >
                <span className="sidebar__icon" aria-hidden="true">
                    <Chevron />
                </span>
                <span className="sidebar__label">Replier</span>
            </button>
        </nav>
    )
}

function Chevron() {
    return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M15 6l-6 6 6 6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
    )
}
