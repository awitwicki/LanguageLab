import './LoginScreen.css'

interface Props {
  /** True when the last attempt came back as /?error=login — cancelled consent, or a provider error. */
  loginFailed: boolean
}

export function LoginScreen({ loginFailed }: Props) {
  return (
    <main className="login">
      <div className="login-card">
        <img src="/favicon.svg" alt="" width={44} height={44} />
        <h1 className="title">LanguageLab</h1>
        <p className="footnote">Learn the words of the books you read. Sign in with Telegram to start.</p>

        {loginFailed && <p className="error">Sign-in did not complete. Please try again.</p>}

        {/* A link, not a button: this leaves the SPA for Telegram and comes back through
            the callback, so it must be a real navigation. */}
        <a className="btn btn-primary btn-lg sign-in" href="/api/auth/telegram/start">
          Sign in with Telegram
        </a>
      </div>
    </main>
  )
}
