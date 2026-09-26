import { useCallback, useRef, useState } from 'react'
import type { CurrentUser } from '../api/client'
import { roleLabel } from '../auth/roles'
import { useDismiss } from '../lib/useDismiss'
import './AccountMenu.css'

interface Props {
  user: CurrentUser
  onAdmin: () => void
  onSignOut: () => void
  /** Rejects with the server's reason when refused — the last-admin rule, in practice. */
  onDeleteAccount: () => Promise<void>
}

export function AccountMenu({ user, onAdmin, onSignOut, onDeleteAccount }: Props) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)

  // A menu that stays open after you click away or press Escape feels broken, and the
  // panel overlaps the content underneath it.
  useDismiss(open, useCallback(() => setOpen(false), []), root)

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
        <AccountPanel
          user={user}
          onAdmin={() => run(onAdmin)}
          onSignOut={() => run(onSignOut)}
          onDeleteAccount={onDeleteAccount}
        />
      )}
    </div>
  )
}

/**
 * The open panel is its own component so that its state — an armed delete, a refusal
 * message — lives exactly as long as the panel is on screen. Closing the menu by any route
 * unmounts it, and the next open starts clean without any reset bookkeeping.
 */
function AccountPanel({ user, onAdmin, onSignOut, onDeleteAccount }: Props) {
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Irreversible, so one click only arms the second. The panel deliberately does not close
  // on either: on success the session ends and the shell unmounts the whole menu; on a
  // refusal the reason has to stay readable.
  const remove = async () => {
    if (!confirming) {
      setConfirming(true)
      return
    }

    setConfirming(false)
    setError(null)

    try {
      await onDeleteAccount()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <div className="account-panel" role="menu">
      <div className="account-identity">
        <p className="account-name headline">{user.displayName}</p>
        {user.username && <p className="caption">@{user.username}</p>}
        {user.role !== 'user' && <span className="role-badge caption">{roleLabel(user.role)}</span>}
      </div>

      {user.role === 'admin' && (
        <button type="button" role="menuitem" className="account-action to-admin" onClick={onAdmin}>
          Admin panel
        </button>
      )}

      <button type="button" role="menuitem" className="account-action sign-out" onClick={onSignOut}>
        Sign out
      </button>

      <button
        type="button"
        role="menuitem"
        className="account-action delete-account"
        onClick={() => void remove()}
      >
        {confirming ? 'Confirm deletion' : 'Delete account'}
      </button>

      {confirming && (
        <p className="account-delete-warning caption">This removes your progress and cannot be undone.</p>
      )}
      {error && <p className="error caption">{error}</p>}
    </div>
  )
}
