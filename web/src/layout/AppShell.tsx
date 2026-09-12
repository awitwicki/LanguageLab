import { useEffect, useRef, type ReactNode } from 'react'
import type { CurrentUser } from '../api/client'
import { TopBar } from './TopBar'
import './AppShell.css'

interface Props {
  sidebar: ReactNode
  user: CurrentUser
  /** Identifies the screen on show; a change moves focus to the main region. */
  screenKey: string
  onHome: () => void
  onAdmin: () => void
  onSignOut: () => void
  onDeleteAccount: () => Promise<void>
  children: ReactNode
}

export function AppShell({ sidebar, user, screenKey, onHome, onAdmin, onSignOut, onDeleteAccount, children }: Props) {
  const mainRef = useRef<HTMLElement>(null)
  const shownKey = useRef(screenKey)

  // Without a router nothing tells a screen reader the page changed: focus would stay on the
  // clicked sidebar item (or fall to <body> once it re-renders). Moving it to the main region
  // announces the new screen and puts the next Tab at its start. Not on the first screen —
  // that one is the page load, where focus belongs at the top of the document.
  useEffect(() => {
    if (shownKey.current === screenKey) {
      return
    }

    shownKey.current = screenKey
    mainRef.current?.focus()
  }, [screenKey])

  return (
    <div className="shell">
      <div className="shell-topbar">
        <TopBar
          user={user}
          onHome={onHome}
          onAdmin={onAdmin}
          onSignOut={onSignOut}
          onDeleteAccount={onDeleteAccount}
        />
      </div>
      <div className="shell-sidebar">{sidebar}</div>
      <main ref={mainRef} className="shell-content" tabIndex={-1}>
        <div className="content">{children}</div>
      </main>
    </div>
  )
}
