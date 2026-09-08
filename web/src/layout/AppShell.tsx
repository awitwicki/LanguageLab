import type { ReactNode } from 'react'
import type { CurrentUser } from '../api/client'
import { TopBar } from './TopBar'
import './AppShell.css'

interface Props {
  sidebar: ReactNode
  user: CurrentUser
  onHome: () => void
  onAdmin: () => void
  onSignOut: () => void
  children: ReactNode
}

export function AppShell({ sidebar, user, onHome, onAdmin, onSignOut, children }: Props) {
  return (
    <div className="shell">
      <div className="shell-topbar">
        <TopBar user={user} onHome={onHome} onAdmin={onAdmin} onSignOut={onSignOut} />
      </div>
      <div className="shell-sidebar">{sidebar}</div>
      <main className="shell-content">
        <div className="content">{children}</div>
      </main>
    </div>
  )
}
