import { useEffect, useRef, useState } from 'react'
import { api, type LearnTarget, type ReaderWord, type ReaderWordShelf } from '../api/client'
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
  onStatusChange: (lemma: string, status: 'learning' | 'known' | 'new') => void
}

type Action = 'learn' | 'know' | 'ignore'

interface ActionButton {
  action: Action
  label: string
  /** The shelf this button puts the word on — so also the button a word on it is marked under. */
  shelf: Exclude<ReaderWordShelf, 'new'>
  className: string
}

/**
 * The three buttons are one picker: the word's current shelf is marked, another button moves it
 * there, and the marked one tapped again undoes it.
 */
const BUTTONS: ActionButton[] = [
  { action: 'learn', label: 'Add to training', shelf: 'learning', className: 'btn-primary' },
  { action: 'know', label: 'I know it', shelf: 'known', className: 'btn-secondary' },
  { action: 'ignore', label: 'Ignore', shelf: 'ignored', className: 'btn-quiet word-panel-ignore' },
]

const SHELF_LABEL: Record<ReaderWordShelf, string> = {
  new: 'New',
  learning: 'Learning',
  known: 'Known',
  ignored: 'Ignored',
}

const TARGET_HINT: Record<LearnTarget, string> = {
  book: "Goes to this book's words",
  personal: 'Goes to My words',
}

/** A swipe down this far closes the panel. */
const SWIPE_CLOSE_PX = 60

/** Matches --dur-sheet in index.css. */
export const SHEET_OUT_MS = 220

const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

interface ShelfButtonProps {
  className: string
  /** This button's own request is out: it spins, the others merely wait. */
  busy: boolean
  /** The word sits on this button's shelf: it wears the ring, and undoes on the next tap. */
  marked: boolean
  disabled: boolean
  onClick: () => void
  children: string
}

function ShelfButton({ className, busy, marked, disabled, onClick, children }: ShelfButtonProps) {
  const classes = [className]

  if (busy) classes.push('btn-busy')
  if (marked) classes.push('word-panel-marked')

  return (
    <button
      type="button"
      className={classes.join(' ')}
      disabled={disabled}
      aria-busy={busy}
      aria-pressed={marked}
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

  const act = async ({ action, shelf: target }: ActionButton) => {
    if (!word) return

    const undo = target === word.shelf

    setBusy(action)
    setError(null)

    try {
      if (undo) {
        await api.resetWord(lemma)
      } else if (action === 'learn') {
        await api.learnWord(lemma, word.translation ? undefined : typed.trim(), dictionaryId)
      } else if (action === 'know') {
        await api.knowWord(lemma)
      } else {
        await api.ignoreWord(lemma)
      }

      const shelf = undo ? 'new' : target
      setWord({ ...word, shelf, canReset: !undo })
      onStatusChange(lemma, shelf === 'ignored' ? 'known' : shelf)
      dismiss()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  const shelf = word?.shelf ?? 'new'
  const needsTranslation = word !== null && !word.translation
  const markedButton = BUTTONS.find((b) => b.shelf === shelf)

  /**
   * New word: where the primary button sends it. Shelved word: which tap undoes it, or why none
   * does. The row is there from the start: it sits under the buttons, and growing into it later
   * would shove them upwards under the reader's finger.
   */
  const hint = () => {
    if (!word) return null
    if (!markedButton) return TARGET_HINT[word.learnTarget]

    return word.canReset ? `Tap ${markedButton.label} again to undo` : 'In training — progress is kept'
  }

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
        <span className={`word-panel-status word-panel-status-${shelf}`}>{SHELF_LABEL[shelf]}</span>
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
        {BUTTONS.map((entry) => {
          const marked = entry === markedButton
          // A marked button only undoes, so the typed translation is beside the point there.
          const blocked = marked
            ? !word?.canReset
            : entry.action === 'learn' && needsTranslation && typed.trim() === ''

          return (
            <ShelfButton
              key={entry.action}
              className={`btn ${entry.className}`}
              busy={busy === entry.action}
              marked={marked}
              disabled={!word || busy !== null || blocked}
              onClick={() => void act(entry)}
            >
              {entry.label}
            </ShelfButton>
          )
        })}
      </div>

      <p className="word-panel-hint">
        {hint() ?? (error === null && <span className="skeleton skeleton-line" style={{ width: '52%' }} />)}
      </p>
    </section>
  )
}
