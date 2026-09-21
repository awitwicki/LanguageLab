import type { CurrentUser } from '../api/client'
import { appVersion } from '../lib/version'
import { AccountMenu } from './AccountMenu'
import { MODES, type AppMode } from './mode'
import './TopBar.css'

interface Props {
  user: CurrentUser
  mode: AppMode | null
  onSelectMode: (mode: AppMode) => void
  onHome: () => void
  onAdmin: () => void
  onSignOut: () => void
  onDeleteAccount: () => Promise<void>
}

export function TopBar({ user, mode, onSelectMode, onHome, onAdmin, onSignOut, onDeleteAccount }: Props) {
  return (
    <header className="topbar">
      <button type="button" className="brand" onClick={onHome}>
        <img src="/favicon.svg" alt="" width={22} height={22} />
        <span>
          LanguageLab
          <span className="brand-version num" title="Build version">
            {appVersion}
          </span>
        </span>
      </button>

      <nav className="mode-tabs" aria-label="Mode">
        {MODES.map((entry) => (
          <button
            key={entry.mode}
            type="button"
            className="mode-tab"
            aria-current={entry.mode === mode ? 'page' : undefined}
            onClick={() => onSelectMode(entry.mode)}
          >
            {entry.label}
          </button>
        ))}
      </nav>

      <AccountMenu user={user} onAdmin={onAdmin} onSignOut={onSignOut} onDeleteAccount={onDeleteAccount} />
    </header>
  )
}
