import { useEffect, useRef, useState } from 'react'
import type { CurrentUser } from '../api/client'
import './AccountMenu.css'

interface Props {
  user: CurrentUser
  onAdmin: () => void
  onSignOut: () => void
}

export function AccountMenu({ user, onAdmin, onSignOut }: Props) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)

  // A menu that stays open after you click away or press Escape feels broken, and the
  // panel overlaps the content underneath it.
  useEffect(() => {
    if (!open) {
      return
    }

    const onPointerDown = (event: MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }

    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const run = (action: () => void) => {
    setOpen(false)
    action()
  }

  return (
    <div className="account-menu" ref={root}>
      <button
        type="button"
        className="account"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Account"
        onClick={() => setOpen((value) => !value)}
      >
        {user.photoUrl ? (
          <img src={user.photoUrl} alt="" width={32} height={32} />
        ) : (
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="12" cy="8" r="4" fill="currentColor" />
            <path d="M4 20c0-4 3.6-6.5 8-6.5s8 2.5 8 6.5Z" fill="currentColor" />
          </svg>
        )}
      </button>

      {open && (
        <div className="account-panel" role="menu">
          <div className="account-identity">
            <p className="account-name headline">{user.displayName}</p>
            {user.username && <p className="caption">@{user.username}</p>}
            {user.role === 'admin' && <span className="role-badge caption">Admin</span>}
          </div>

          {user.role === 'admin' && (
            <button type="button" role="menuitem" className="account-action to-admin" onClick={() => run(onAdmin)}>
              Admin panel
            </button>
          )}

          <button type="button" role="menuitem" className="account-action sign-out" onClick={() => run(onSignOut)}>
            Sign out
          </button>
        </div>
      )}
    </div>
  )
}
