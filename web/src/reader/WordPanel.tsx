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

export function WordPanel({ lemma, form, count, dictionaryId, onClose, onStatusChange }: Props) {
  const [word, setWord] = useState<ReaderWord | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const touchStart = useRef<number | null>(null)

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
      if (event.key === 'Escape') onClose()
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const act = async (action: Action) => {
    if (!word) return

    setBusy(true)
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

      if (action === 'ignore') onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  const status = word?.status ?? 'new'
  const needsTranslation = word !== null && !word.translation

  return (
    <section
      className="word-panel"
      role="dialog"
      aria-label={`Word: ${lemma}`}
      onTouchStart={(event) => {
        touchStart.current = event.touches[0]?.clientY ?? null
      }}
      onTouchEnd={(event) => {
        const start = touchStart.current
        const end = event.changedTouches[0]?.clientY

        if (start !== null && end !== undefined && end - start > SWIPE_CLOSE_PX) onClose()
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
        <button type="button" className="word-panel-close" aria-label="Close" onClick={onClose}>
          ×
        </button>
      </div>

      <p className="word-panel-meta">
        <span className={`word-panel-status word-panel-status-${status}`}>{STATUS_LABEL[status]}</span>
        {' · '}
        <span className="num">seen {formatInt(count)}× in this book</span>
      </p>

      {word === null && error === null && <p className="word-panel-loading">Looking up…</p>}

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
        <button
          type="button"
          className="btn btn-primary"
          disabled={!word || busy || (needsTranslation && typed.trim() === '')}
          onClick={() => void act('learn')}
        >
          Add to training
        </button>
        <button type="button" className="btn btn-secondary" disabled={!word || busy} onClick={() => void act('know')}>
          I know it
        </button>
        <button
          type="button"
          className="btn btn-quiet word-panel-ignore"
          disabled={!word || busy}
          onClick={() => void act('ignore')}
        >
          Ignore
        </button>
      </div>

      {word && <p className="word-panel-hint">{TARGET_HINT[word.learnTarget]}</p>}
    </section>
  )
}
