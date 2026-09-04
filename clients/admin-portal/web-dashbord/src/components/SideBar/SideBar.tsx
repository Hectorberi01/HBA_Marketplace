import { useState } from 'react'
import './Sidebar.css'
import type { SidebarProps } from './SideBarType'

export default function Sidebar({sections, activeId, onSelect, brand, footer, defaultCollapsed = false,}: SidebarProps) {
    const [collapsed, setCollapsed] = useState(defaultCollapsed)

    return (
        <nav
            className={`sidebar ${collapsed ? 'sidebar--collapsed' : ''}`}
            aria-label="Navigation principale"
        >
            {brand && <div className="sidebar__brand">{brand}</div>}

            <div className="sidebar__scroll">
                {sections.map((section, i) => (
                    <div className="sidebar__section" key={section.title ?? i}>
                        {section.title && !collapsed && (
                            <h2 className="sidebar__section-title">{section.title}</h2>
                        )}

                        <ul className="sidebar__list">
                            {section.items.map((item) => {
                                const isActive = item.id === activeId

                                return (
                                    <li key={item.id}>
                                        <button
                                            type="button"
                                            className={`sidebar__item ${isActive ? 'is-active' : ''}`}
                                            aria-current={isActive ? 'page' : undefined}
                                            title={collapsed ? item.label : undefined}
                                            onClick={() => onSelect(item.id)}
                                        >
                      <span className="sidebar__icon" aria-hidden="true">
                        {item.icon}
                      </span>
                                            <span className="sidebar__label">{item.label}</span>
                                            {item.badge != null && item.badge > 0 && (
                                                <span className="sidebar__badge">{item.badge}</span>
                                            )}
                                        </button>
                                    </li>
                                )
                            })}
                        </ul>
                    </div>
                ))}
            </div>

            {footer && !collapsed && <div className="sidebar__footer">{footer}</div>}

            <button
                type="button"
                className="sidebar__toggle"
                aria-expanded={!collapsed}
                onClick={() => setCollapsed((c) => !c)}
            >
        <span className="sidebar__icon" aria-hidden="true">
          <ChevronIcon />
        </span>
                <span className="sidebar__label">Replier</span>
            </button>
        </nav>
    )
}

function ChevronIcon() {
    return (
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M15 6l-6 6 6 6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
    )
}