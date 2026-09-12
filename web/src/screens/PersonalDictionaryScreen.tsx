import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type PersonalDictionary, type PersonalWord, type TrainingStarted } from '../api/client'
import { LeitnerScale } from '../components/LeitnerScale'
import { formatInt, wordsLabel } from '../lib/format'
import './PersonalDictionaryScreen.css'

interface Props {
  onTrain: (dictionaryId: number) => void
  onReview: (started: TrainingStarted, dictionaryId: number) => void
  /** After a successful add or remove — the sidebar's word count lives outside this screen. */
  onChanged?: () => void
}

/// The user's own word list: type a word, take or fix the suggested translation, add it. The
/// server shelves it "don't know" at once, so "Start exercise" picks it up without sorting.
export function PersonalDictionaryScreen({ onTrain, onReview, onChanged }: Props) {
  const [detail, setDetail] = useState<PersonalDictionary | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [word, setWord] = useState('')
  const [translation, setTranslation] = useState('')
  const [lookupBusy, setLookupBusy] = useState(false)
  const [lookupNote, setLookupNote] = useState<string | null>(null)
  const [addBusy, setAddBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)
  const [removeError, setRemoveError] = useState<string | null>(null)

  const wordInput = useRef<HTMLInputElement>(null)
  // What the last lookup put into the translation field. A later lookup may replace exactly
  // that (or an empty field) — never something the user typed by hand.
  const suggested = useRef<string | null>(null)

  const reload = useCallback(() => api.getPersonalDictionary().then(setDetail), [])

  useEffect(() => {
    let cancelled = false

    api
      .getPersonalDictionary()
      .then((d) => {
        if (!cancelled) setDetail(d)
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })

    return () => {
      cancelled = true
    }
  }, [])

  const lookup = async () => {
    const query = word.trim()

    if (!query || lookupBusy) return

    setLookupBusy(true)
    setLookupNote(null)
    setFormError(null)

    try {
      const result = await api.translate(query)

      if (result.translation) {
        const next = result.translation
        // Snapshot before mutating: the updater below runs on React's own schedule, by which
        // point suggested.current has already moved on to `next` — comparing against a plain
        // captured value (not the ref) keeps the check accurate regardless of timing.
        const previous = suggested.current
        setTranslation((current) => (current.trim() === '' || current === previous ? next : current))
        suggested.current = next
      } else {
        setLookupNote('Nothing found — type the translation yourself.')
      }
    } catch (e) {
      setLookupNote(String(e))
    } finally {
      setLookupBusy(false)
    }
  }

  const onWordKey = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void lookup()
    }
  }

  const canAdd = word.trim() !== '' && translation.trim() !== '' && !addBusy

  const add = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!canAdd) return

    setAddBusy(true)
    setFormError(null)

    try {
      await api.addPersonalWord(word.trim(), translation.trim())
      setWord('')
      setTranslation('')
      suggested.current = null
      setLookupNote(null)
      wordInput.current?.focus()
      onChanged?.()
      await reload()
    } catch (e) {
      setFormError(e instanceof Error ? e.message : String(e))
    } finally {
      setAddBusy(false)
    }
  }

  const remove = async (item: PersonalWord) => {
    setRemoveError(null)

    try {
      await api.removePersonalWord(item.wordPairId)
      onChanged?.()
      await reload()
    } catch (e) {
      setRemoveError(String(e))
    }
  }

  // Same shape as DictionaryScreen's review: a 204 means the due words closed elsewhere meanwhile.
  const startReview = async () => {
    if (!detail) return

    setReviewBusy(true)
    setReviewNotice(null)

    try {
      const started = await api.startReview({ dictionaryId: detail.id, chapterIds: null })

      if (!started) {
        setReviewNotice('Nothing to review today.')
        return
      }

      onReview(started, detail.id)
    } catch (e) {
      setReviewNotice(String(e))
    } finally {
      setReviewBusy(false)
    }
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!detail) {
    return <p className="footnote">Loading…</p>
  }

  return (
    <>
      <header className="personal-header">
        <h1 className="large-title">{detail.name}</h1>
        <p className="personal-meta num">{wordsLabel(detail.wordsCount)}</p>
        {detail.learning.total > 0 && (
          <div className="personal-learning">
            <span className="footnote">Learned</span>
            <LeitnerScale progress={detail.learning} caption />
          </div>
        )}
      </header>

      <form className="add-word" onSubmit={(e) => void add(e)}>
        <label className="field">
          <input
            ref={wordInput}
            name="word"
            value={word}
            placeholder="English word"
            aria-label="English word"
            autoComplete="off"
            autoFocus
            onChange={(e) => setWord(e.target.value)}
            onKeyDown={onWordKey}
          />
        </label>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={word.trim() === '' || lookupBusy}
          onClick={() => void lookup()}
        >
          Translate
        </button>
        <label className="field">
          <input
            name="translation"
            value={translation}
            placeholder="Translation"
            aria-label="Translation"
            aria-busy={lookupBusy || undefined}
            autoComplete="off"
            onChange={(e) => setTranslation(e.target.value)}
          />
        </label>
        <button type="submit" className="btn btn-primary" disabled={!canAdd}>
          Add
        </button>
        <p className="footnote add-word-hint">
          <kbd>Enter</kbd> in the word looks up a translation · <kbd>Enter</kbd> in the translation adds the word
        </p>
        {lookupNote && <p className="footnote add-word-note">{lookupNote}</p>}
        {formError && <p className="footnote error add-word-note">{formError}</p>}
      </form>

      <div className="personal-actions">
        <button
          type="button"
          className="btn btn-primary"
          disabled={detail.learnableCount === 0}
          onClick={() => onTrain(detail.id)}
        >
          Start exercise
        </button>
        {detail.dueCount > 0 && (
          <button type="button" className="btn btn-secondary" disabled={reviewBusy} onClick={() => void startReview()}>
            Review ({formatInt(detail.dueCount)})
          </button>
        )}
      </div>

      {detail.learnableCount === 0 && <p className="footnote">Add a word to start an exercise.</p>}
      {reviewNotice && <p className="footnote">{reviewNotice}</p>}

      <section className="section">
        <h2 className="title">Words</h2>
        {detail.words.length === 0 ? (
          <p className="footnote">Nothing here yet — add your first word above.</p>
        ) : (
          <ul className="personal-words">
            {detail.words.map((item) => (
              <li key={item.wordPairId} className="personal-word">
                <span className="word">{item.word}</span>
                <span className="translation">{item.translation}</span>
                <span className="state footnote">{wordState(item)}</span>
                <button
                  type="button"
                  className="btn btn-quiet"
                  aria-label={`Remove ${item.word}`}
                  onClick={() => void remove(item)}
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>
        )}
        {removeError && <p className="footnote error">{removeError}</p>}
      </section>
    </>
  )
}

function wordState(item: PersonalWord): string {
  if (item.isLearned) return 'Learned'
  if (item.box === null) return 'New'
  return `Box ${item.box}`
}
