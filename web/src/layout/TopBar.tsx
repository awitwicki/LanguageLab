import type { CurrentUser } from '../api/client'
import { AccountMenu } from './AccountMenu'
import './TopBar.css'

interface Props {
  user: CurrentUser
  onHome: () => void
  onAdmin: () => void
  onSignOut: () => void
}

export function TopBar({ user, onHome, onAdmin, onSignOut }: Props) {
  return (
    <header className="topbar">
      <button type="button" className="brand" onClick={onHome}>
        <img src="/favicon.svg" alt="" width={22} height={22} />
        LanguageLab
      </button>

      <AccountMenu user={user} onAdmin={onAdmin} onSignOut={onSignOut} />
    </header>
  )
}
