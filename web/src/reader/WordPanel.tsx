import { useEffect, useRef, useState } from 'react'
import { api, type LearnTarget, type ReaderWord, type ReaderWordStatus } from '../api/client'
import { formatInt } from '../lib/format'
import './WordPanel.css'

interface Props {
  lemma: string
  /** The word as it stands in the text. */
  form: string
  /** Occurrences of the lemma in the whole book. */
  count: number
  /** The dictionary of the book being read, if it has one: it decides where Add to training goes. */
  dictionaryId: number | null
  onClose: () => void
  /** Ignore reports 'known': an ignored word is not highlighted, the same as a known one. */
  onStatusChange: (lemma: string, status: 'learning' | 'known') => void
}

type Action = 'learn' | 'know' | 'ignore'

const STATUS_LABEL: Record<ReaderWordStatus, string> = { new: 'New', learning: 'Learning', known: 'Known' }

const TARGET_HINT: Record<LearnTarget, string> = {
  book: "Goes to this book's words",
  personal: 'Goes to My words',
}

/** A swipe down this far closes the panel. */
const SWIPE_CLOSE_PX = 60

/** Matches --dur-sheet in index.css. */
export const SHEET_OUT_MS = 220

const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

interface ActionButtonProps {
  className: string
  /** This button's own request is out: it spins, the others merely wait. */
  busy: boolean
  disabled: boolean
  onClick: () => void
  children: string
}

function ActionButton({ className, busy, disabled, onClick, children }: ActionButtonProps) {
  return (
    <button
      type="button"
      className={busy ? `${className} btn-busy` : className}
      disabled={disabled}
      aria-busy={busy}
      onClick={onClick}
    >
      {busy && <span className="btn-spinner" aria-hidden="true" />}
      <span className="btn-label">{children}</span>
    </button>
  )
}

export function WordPanel({ lemma, form, count, dictionaryId, onClose, onStatusChange }: Props) {
  const [word, setWord] = useState<ReaderWord | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [typed, setTyped] = useState('')
  /** Which of the three actions is waiting on the server, if any. */
  const [busy, setBusy] = useState<Action | null>(null)
  const [closing, setClosing] = useState(false)
  const touchStart = useRef<number | null>(null)
  // The parent passes a fresh arrow each render; a ref keeps the close timer from restarting.
  const onCloseRef = useRef(onClose)

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    if (!closing) return

    const timer = window.setTimeout(() => onCloseRef.current(), prefersReducedMotion() ? 0 : SHEET_OUT_MS)
    return () => window.clearTimeout(timer)
  }, [closing])

  const dismiss = () => setClosing(true)

  useEffect(() => {
    let cancelled = false

    api
      .getReaderWord(lemma, dictionaryId)
      .then((found) => {
        if (!cancelled) setWord(found)
      })
      .catch(() => {
        if (!cancelled) setError("Couldn't look this word up. Check the connection and tap it again.")
      })

    return () => {
      cancelled = true
    }
  }, [lemma, dictionaryId])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setClosing(true)
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const act = async (action: Action) => {
    if (!word) return

    setBusy(action)
    setError(null)

    try {
      if (action === 'learn') {
        await api.learnWord(lemma, word.translation ? undefined : typed.trim(), dictionaryId)
      } else if (action === 'know') {
        await api.knowWord(lemma)
      } else {
        await api.ignoreWord(lemma)
      }

      const status = action === 'learn' ? 'learning' : 'known'
      setWord({ ...word, status })
      onStatusChange(lemma, status)
      dismiss()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  const status = word?.status ?? 'new'
  const needsTranslation = word !== null && !word.translation
  // The row is there from the start: it sits under the buttons, and growing into it later
  // would shove them upwards under the reader's finger.
  const hint = word ? TARGET_HINT[word.learnTarget] : null

  return (
    <section
      className={closing ? 'word-panel word-panel-closing' : 'word-panel'}
      role="dialog"
      aria-label={`Word: ${lemma}`}
      onTouchStart={(event) => {
        touchStart.current = event.touches[0]?.clientY ?? null
      }}
      onTouchEnd={(event) => {
        const start = touchStart.current
        const end = event.changedTouches[0]?.clientY

        if (start !== null && end !== undefined && end - start > SWIPE_CLOSE_PX) dismiss()
        touchStart.current = null
      }}
    >
      <div className="word-panel-head">
        <h2 className="word-panel-lemma">{lemma}</h2>
        {form.toLowerCase() !== lemma && (
          <span className="word-panel-form">
            {form} → {lemma}
          </span>
        )}
        <button type="button" className="word-panel-close" aria-label="Close" onClick={dismiss}>
          ×
        </button>
      </div>

      <p className="word-panel-meta">
        <span className={`word-panel-status word-panel-status-${status}`}>{STATUS_LABEL[status]}</span>
        {' · '}
        <span className="num">seen {formatInt(count)}× in this book</span>
      </p>

      {word === null && error === null && (
        <p className="word-panel-skeleton" role="status" aria-busy="true" aria-label="Looking this word up…">
          <span className="skeleton skeleton-line" style={{ width: '58%' }} />
        </p>
      )}

      {word?.translation && (
        <p className="word-panel-translation" lang="uk">
          {word.translation}
        </p>
      )}

      {needsTranslation && (
        <label className="word-panel-typed">
          <span>No translation found</span>
          <input
            className="word-panel-input"
            value={typed}
            placeholder="Type a translation"
            lang="uk"
            onChange={(event) => setTyped(event.target.value)}
          />
        </label>
      )}

      {error && <p className="word-panel-error">{error}</p>}

      <div className="word-panel-actions">
        <ActionButton
          className="btn btn-primary"
          busy={busy === 'learn'}
          disabled={!word || busy !== null || (needsTranslation && typed.trim() === '')}
          onClick={() => void act('learn')}
        >
          Add to training
        </ActionButton>
        <ActionButton
          className="btn btn-secondary"
          busy={busy === 'know'}
          disabled={!word || busy !== null}
          onClick={() => void act('know')}
        >
          I know it
        </ActionButton>
        <ActionButton
          className="btn btn-quiet word-panel-ignore"
          busy={busy === 'ignore'}
          disabled={!word || busy !== null}
          onClick={() => void act('ignore')}
        >
          Ignore
        </ActionButton>
      </div>

      <p className="word-panel-hint">
        {hint ?? (error === null && <span className="skeleton skeleton-line" style={{ width: '52%' }} />)}
      </p>
    </section>
  )
}
