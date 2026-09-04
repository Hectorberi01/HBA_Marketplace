import {type ReactNode } from 'react'

export type SidebarItem = {
    id: string
    label: string
    icon: ReactNode
    badge?: number
}

export type SidebarSection = {
    title?: string
    items: SidebarItem[]
}

export type SidebarProps = {
    sections: SidebarSection[]
    activeId: string
    onSelect: (id: string) => void
    brand?: ReactNode
    footer?: ReactNode
    defaultCollapsed?: boolean
}