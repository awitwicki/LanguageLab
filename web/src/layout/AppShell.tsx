import type { ReactNode } from 'react'
import { TopBar } from './TopBar'
import './AppShell.css'

interface Props {
  sidebar: ReactNode
  onHome: () => void
  children: ReactNode
}

export function AppShell({ sidebar, onHome, children }: Props) {
  return (
    <div className="shell">
      <div className="shell-topbar">
        <TopBar onHome={onHome} />
      </div>
      <div className="shell-sidebar">{sidebar}</div>
      <main className="shell-content">
        <div className="content">{children}</div>
      </main>
    </div>
  )
}
