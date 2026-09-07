import './TopBar.css'

interface Props {
  onHome: () => void
}

export function TopBar({ onHome }: Props) {
  return (
    <header className="topbar">
      <button type="button" className="brand" onClick={onHome}>
        <img src="/favicon.svg" alt="" width={22} height={22} />
        LanguageLab
      </button>

      {/* Меню акаунта поки не існує — див. README «TODO». Кнопка вже на місці,
          щоб макет не поїхав, коли меню з'явиться. */}
      <button
        type="button"
        className="account"
        aria-label="Акаунт"
        title="Меню акаунта — скоро"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="8" r="4" fill="currentColor" />
          <path d="M4 20c0-4 3.6-6.5 8-6.5s8 2.5 8 6.5Z" fill="currentColor" />
        </svg>
      </button>
    </header>
  )
}
