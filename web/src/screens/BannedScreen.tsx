import './BannedScreen.css'

interface Props {
  onBack: () => void
}

export function BannedScreen({ onBack }: Props) {
  return (
    <main className="banned">
      <div className="banned-card">
        <h1 className="title">Account suspended</h1>
        <p className="footnote">
          An administrator has suspended this account. Your words and progress are kept, but you
          cannot sign in right now.
        </p>
        <button type="button" className="btn btn-secondary" onClick={onBack}>
          Back to sign in
        </button>
      </div>
    </main>
  )
}
