import './LoginScreen.css'

interface Props {
  /** True when the last attempt failed — the OIDC callback's /?error=login, or a refused Mini App sign-in. */
  loginFailed: boolean
  /**
   * Set when the page runs as a Telegram Mini App. The OIDC redirect has no way back into
   * Telegram's web view, so there the button repeats the launch-parameter sign-in the boot
   * made, and a failure asks for a fresh launch from the bot.
   */
  onTelegramSignIn: (() => void) | null
}

export function LoginScreen({ loginFailed, onTelegramSignIn }: Props) {
  return (
    <main className="login">
      <div className="login-card">
        <img src="/favicon.svg" alt="" width={44} height={44} />
        <h1 className="title">LanguageLab</h1>
        <p className="footnote">Learn the words of the books you read. Sign in with Telegram to start.</p>

        {loginFailed && (
          <p className="error">
            {onTelegramSignIn
              ? 'Could not sign in through Telegram. Close this window and open the app from the bot again.'
              : 'Sign-in did not complete. Please try again.'}
          </p>
        )}

        {onTelegramSignIn ? (
          <button type="button" className="btn btn-primary btn-lg sign-in" onClick={onTelegramSignIn}>
            Sign in with Telegram
          </button>
        ) : (
          /* A link, not a button: this leaves the SPA for Telegram and comes back through
             the callback, so it must be a real navigation. */
          <a className="btn btn-primary btn-lg sign-in" href="/api/auth/telegram/start">
            Sign in with Telegram
          </a>
        )}

        {/* Not a runtime check: Vite replaces import.meta.env.DEV with a literal false in
            `npm run build`, so this branch is dropped from the production bundle. The
            endpoint behind it is fenced off independently — see LanguageLab.Api/Auth/DevLogin.cs. */}
        {import.meta.env.DEV && (
          <a className="btn btn-secondary btn-lg sign-in dev-sign-in" href="/api/auth/dev-login">
            Sign in as local dev
          </a>
        )}
      </div>
    </main>
  )
}
