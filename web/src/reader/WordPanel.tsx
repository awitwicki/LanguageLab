import { useEffect, useRef, useState } from 'react'
import { api, type ReaderWord, type ReaderWordStatus } from '../api/client'
import { formatInt } from '../lib/format'
import './WordPanel.css'

interface Props {
  lemma: string
  /** The word as it stands in the text. */
  form: string
  /** Occurrences of the lemma in the whole book. */
  count: number
  onClose: () => void
  onStatusChange: (lemma: string, status: 'learning' | 'known') => void
}

const STATUS_LABEL: Record<ReaderWordStatus, string> = { new: 'New', learning: 'Learning', known: 'Known' }

/** A swipe down this far closes the panel. */
const SWIPE_CLOSE_PX = 60

export function WordPanel({ lemma, form, count, onClose, onStatusChange }: Props) {
  const [word, setWord] = useState<ReaderWord | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const touchStart = useRef<number | null>(null)

  useEffect(() => {
    let cancelled = false

    api
      .getReaderWord(lemma)
      .then((found) => {
        if (!cancelled) setWord(found)
      })
      .catch(() => {
        if (!cancelled) setError("Couldn't look this word up. Check the connection and tap it again.")
      })

    return () => {
      cancelled = true
    }
  }, [lemma])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const act = async (status: 'learning' | 'known') => {
    if (!word) return

    setBusy(true)
    setError(null)

    try {
      if (status === 'learning') {
        await api.learnWord(lemma, word.translation ? undefined : typed.trim())
      } else {
        await api.knowWord(lemma)
      }

      setWord({ ...word, status })
      onStatusChange(lemma, status)
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

      <div className="word-panel-meta">
        <span className={`word-panel-status word-panel-status-${status}`}>{STATUS_LABEL[status]}</span>
        <span className="num">Count: {formatInt(count)}</span>
      </div>

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
          onClick={() => void act('learning')}
        >
          Learn
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={!word || busy || !word.inSharedVocabulary}
          onClick={() => void act('known')}
        >
          I know it
        </button>
      </div>
    </section>
  )
}
