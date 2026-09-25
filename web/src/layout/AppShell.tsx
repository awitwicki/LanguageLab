import { useEffect, useRef, type ReactNode } from 'react'
import type { CurrentUser } from '../api/client'
import type { AppMode } from './mode'
import { TopBar } from './TopBar'
import './AppShell.css'

interface Props {
  /** null collapses the shell to a single column — the modes without a sidebar. */
  sidebar: ReactNode | null
  user: CurrentUser
  mode: AppMode | null
  onSelectMode: (mode: AppMode) => void
  /** Identifies the screen on show; a change moves focus to the main region. */
  screenKey: string
  onHome: () => void
  onAdmin: () => void
  onSignOut: () => void
  onDeleteAccount: () => Promise<void>
  children: ReactNode
}

export function AppShell({
  sidebar,
  user,
  mode,
  onSelectMode,
  screenKey,
  onHome,
  onAdmin,
  onSignOut,
  onDeleteAccount,
  children,
}: Props) {
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
    <div className={sidebar === null ? 'shell shell-no-sidebar' : 'shell'}>
      <div className="shell-topbar">
        <TopBar
          user={user}
          mode={mode}
          onSelectMode={onSelectMode}
          onHome={onHome}
          onAdmin={onAdmin}
          onSignOut={onSignOut}
          onDeleteAccount={onDeleteAccount}
        />
      </div>
      {sidebar !== null && <div className="shell-sidebar">{sidebar}</div>}
      <main ref={mainRef} className="shell-content" tabIndex={-1}>
        <div className="content">{children}</div>
      </main>
    </div>
  )
}
