import { useEffect, useMemo, useState } from 'react'
import { api, type Language } from '../api/client'
import './LanguagePicker.css'

interface Props {
  /** The code selected when the picker opens. */
  initial: string
  /** First sign-in: no way back, and the button reads "Continue". */
  firstRun: boolean
  onSaved: () => void
  onBack?: () => void
}

/**
 * The learner's main language — what every translation they see is in. Shown full-screen on
 * first sign-in, and as a screen of its own from the account menu afterwards.
 */
export function LanguagePicker({ initial, firstRun, onSaved, onBack }: Props) {
  const [languages, setLanguages] = useState<Language[] | null>(null)
  const [selected, setSelected] = useState(initial)
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.listLanguages().then(setLanguages, (e) => setError(String(e)))
  }, [])

  const shown = useMemo(() => {
    const term = query.trim().toLowerCase()
    return (languages ?? []).filter(
      (l) => term === '' || l.nativeName.toLowerCase().includes(term) || l.englishName.toLowerCase().includes(term),
    )
  }, [languages, query])

  const save = async () => {
    setBusy(true)
    setError(null)

    try {
      await api.setLanguage(selected)
      onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setBusy(false)
    }
  }

  // On first sign-in the picker is the whole page; from the menu it sits inside the shell,
  // which already has the page's <main>.
  const Root = firstRun ? 'main' : 'section'

  return (
    <Root className={firstRun ? 'language-picker language-picker-first-run' : 'language-picker'}>
      <h1 className="title">{firstRun ? 'Choose your language' : 'Change language'}</h1>
      <p className="footnote">Translations of English words will be shown in this language.</p>

      <input
        type="search"
        className="language-search"
        placeholder="Search languages"
        aria-label="Search languages"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
      />

      <ul className="language-list" role="radiogroup" aria-label="Languages" aria-busy={languages === null}>
        {shown.map((l) => (
          <li key={l.code}>
            <button
              type="button"
              role="radio"
              aria-checked={l.code === selected}
              data-code={l.code}
              className="language-option"
              onClick={() => setSelected(l.code)}
            >
              <span className="language-native" lang={l.code}>
                {l.nativeName}
              </span>
              {l.nativeName !== l.englishName && <span className="caption">{l.englishName}</span>}
            </button>
          </li>
        ))}
      </ul>

      {languages !== null && shown.length === 0 && <p className="footnote">No language matches “{query.trim()}”.</p>}
      {error && <p className="error caption">{error}</p>}

      <div className="language-actions">
        {!firstRun && onBack && (
          <button type="button" className="btn btn-quiet" onClick={onBack}>
            Back
          </button>
        )}
        <button
          type="button"
          className="btn btn-primary btn-lg"
          disabled={busy || languages === null}
          onClick={() => void save()}
        >
          {firstRun ? 'Continue' : 'Save'}
        </button>
      </div>
    </Root>
  )
}
