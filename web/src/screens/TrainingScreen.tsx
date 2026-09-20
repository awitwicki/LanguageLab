import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type NextQuestion, type TrainingStarted, type TrainingSummary } from '../api/client'
import { SortingProgress } from '../components/SortingProgress'
import { formatDue, percentOf } from '../lib/format'
import './TrainingScreen.css'

type Phase = 'cards' | 'quiz' | 'summary'

/** The verdict on the current question, worked out in the browser the moment an option is picked. */
interface Verdict {
  isCorrect: boolean
  word: string
  translation: string
}

interface Props {
  /** null for a review across every book: it starts from the home screen and goes back there. */
  dictionaryId: number | null
  dictionaryName: string
  scopeTitle: string
  chapterIds: number[] | null
  /** null for a review — then "One more batch" is not shown. */
  batchSize: number | null
  started: TrainingStarted
  onBack: () => void
}

export function TrainingScreen({ dictionaryId, dictionaryName, scopeTitle, chapterIds, batchSize, started, onBack }: Props) {
  const backLabel = dictionaryId === null ? 'Home' : dictionaryName
  // The session lives here, not in the route: "Retry mistakes" and "One more batch" start
  // a new session in place, without jumping between screens.
  const [session, setSession] = useState(started)
  // Every mode opens on the word list: a review's words may be long forgotten, and
  // Enter skips the list at once for anyone who wants pure recall.
  const [phase, setPhase] = useState<Phase>('cards')
  const [next, setNext] = useState<NextQuestion | null>(null)
  const [pickedId, setPickedId] = useState<number | null>(null)
  const [answer, setAnswer] = useState<Verdict | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [summary, setSummary] = useState<TrainingSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const nextButtonRef = useRef<HTMLButtonElement>(null)
  // The question after the one on screen, fetched as soon as the answer is recorded; null
  // once that fetch failed, so "Next" knows to ask the server itself.
  const pendingNext = useRef<Promise<NextQuestion | null> | null>(null)

  const question = next?.question ?? null

  // Puts a fetched question on screen, or ends the session once the queue is empty.
  const show = useCallback(
    async (n: NextQuestion) => {
      if (n.question === null) {
        // Do not keep the note ("Know" / "won't show up again") from the previous question —
        // it referred to the quiz card, not the summary screen.
        setNotice(null)
        setSummary(await api.finish(session.trainingId))
        setPhase('summary')
        return
      }

      setNext(n)
      setPickedId(null)
      setAnswer(null)
      setError(null)
    },
    [session.trainingId],
  )

  const loadNext = useCallback(async () => {
    setBusy(true)

    try {
      await show(await api.nextQuestion(session.trainingId))
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }, [session.trainingId, show])

  const restart = (nextSession: TrainingStarted) => {
    setSession(nextSession)
    setPhase('cards')
    setNext(null)
    setPickedId(null)
    setAnswer(null)
    setNotice(null)
    setSummary(null)
    setError(null)
    pendingNext.current = null
  }

  // Only switches the phase: the effect below fetches the first question — so "Start quiz"
  // and starting a review take one path and do not double the request.
  const startQuiz = useCallback(() => setPhase('quiz'), [])

  // The same effect loads the first question after "Start quiz" and after a session restart.
  useEffect(() => {
    if (phase === 'quiz' && next === null && summary === null) {
      void Promise.resolve().then(() => loadNext())
    }
  }, [phase, next, summary, loadNext])

  // The options become disabled right after the answer, so focus falls off the chosen button
  // onto <body>; move it to "Next" — that is where the next keyboard action goes.
  useEffect(() => {
    if (answer) {
      nextButtonRef.current?.focus()
    }
  }, [answer])

  const pick = useCallback(
    (wordPairId: number) => {
      if (!question || answer || busy) {
        return
      }

      const enToUa = question.direction === 'enToUa'
      const correctLabel = question.options.find((o) => o.wordPairId === question.wordPairId)?.label ?? ''

      // Graded right here: the question carries its own answer, and a round trip between the
      // click and the colour is exactly the delay a learner feels on a slow link.
      setNotice(null)
      setPickedId(wordPairId)
      setAnswer({
        isCorrect: wordPairId === question.wordPairId,
        word: enToUa ? question.prompt : correctLabel,
        translation: enToUa ? correctLabel : question.prompt,
      })

      // The server still records the answer — it grades the session at the end — and the
      // following question is fetched right behind it, so "Next" finds it ready. A failure is
      // shown at once, and "Next" then asks the server afresh instead of trusting a prefetch
      // that never came. A 204 (the question was already gone) changes nothing here.
      pendingNext.current = api
        .answer(session.trainingId, question.id, wordPairId)
        .then(() => api.nextQuestion(session.trainingId))
        .catch((e) => {
          setError(String(e))
          return null
        })
    },
    [question, answer, busy, session.trainingId],
  )

  const goNext = useCallback(async () => {
    if (!answer || busy) {
      return
    }

    setNotice(null)
    setBusy(true)

    try {
      // Usually resolved long ago — the learner has been reading the result.
      const prefetched = await pendingNext.current
      pendingNext.current = null
      await show(prefetched ?? (await api.nextQuestion(session.trainingId)))
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }, [answer, busy, session.trainingId, show])

  const markKnown = async () => {
    if (!question || busy) {
      return
    }

    setBusy(true)

    try {
      const known = await api.markKnown(session.trainingId, question.id)
      setNotice(known ? `${known.word} won’t come up again` : null)
      await loadNext()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  const retry = async () => {
    setBusy(true)
    setNotice(null)

    try {
      const nextSession = await api.retry(session.trainingId)

      if (!nextSession) {
        setNotice('No mistakes to review.')
        return
      }

      restart(nextSession)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  const anotherBatch = async () => {
    if (batchSize === null || dictionaryId === null) {
      return
    }

    setBusy(true)
    setNotice(null)

    try {
      const nextSession = await api.startNewBatch(dictionaryId, chapterIds, batchSize)

      if (!nextSession) {
        setNotice('No more words in this set.')
        return
      }

      restart(nextSession)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // preventDefault() only when the branch actually does something — otherwise in a real browser
      // (unlike jsdom) it cancels the native Enter activation of the focused button.
      let handled = true

      if (phase === 'cards' && event.key === 'Enter') {
        startQuiz()
      } else if (phase === 'quiz' && event.key === 'Enter' && answer && !busy) {
        void goNext()
      } else if (phase === 'quiz' && !answer && !busy && event.key >= '1' && event.key <= '6') {
        const option = question?.options[Number(event.key) - 1]
        if (option) pick(option.wordPairId)
        else handled = false
      } else {
        handled = false
      }

      if (handled) {
        event.preventDefault()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [phase, question, answer, busy, startQuiz, goNext, pick])

  return (
    <>
      <div className="training-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ {backLabel}
        </button>
      </div>

      {error && <p className="error">{error}</p>}

      {phase === 'cards' && (
        <section className="cards">
          <p className="footnote">
            {dictionaryName} · {scopeTitle}
          </p>
          <h1 className="large-title">{session.mode === 'review' ? 'Words to review' : 'New words'}</h1>
          <ul className="cards-list">
            {session.words.map((w) => (
              <li key={w.wordPairId} className="cards-row">
                <span className="cards-word">{w.word}</span>
                <span className="cards-translation">{w.translation}</span>
              </li>
            ))}
          </ul>
          <div>
            <button type="button" className="btn btn-primary btn-lg" onClick={startQuiz}>
              Start quiz <kbd>Enter</kbd>
            </button>
          </div>
        </section>
      )}

      {phase === 'quiz' && next && question && (
        <>
          <SortingProgress
            scope={dictionaryName}
            title={scopeTitle}
            sorted={next.answered}
            total={next.total}
            counts={`Question ${next.answered + 1} of ${next.total}`}
          />

          {notice && <p className="footnote quiz-notice">{notice}</p>}

          <div className="quiz-card">
            <p key={question.id} className="quiz-prompt">
              {question.prompt}
            </p>

            <div className="options">
              {question.options.map((option, index) => {
                const isCorrect = answer !== null && option.wordPairId === question.wordPairId
                const isWrong = answer !== null && option.wordPairId === pickedId && !answer.isCorrect
                const state = isCorrect ? ' is-correct' : isWrong ? ' is-wrong' : ''

                return (
                  <button
                    key={option.wordPairId}
                    type="button"
                    className={`btn btn-secondary option${state}`}
                    disabled={answer !== null || busy}
                    onClick={() => pick(option.wordPairId)}
                  >
                    <kbd>{index + 1}</kbd>
                    <span className="option-label">{option.label}</span>
                  </button>
                )
              })}
            </div>

            {answer ? (
              <>
                <p className={`quiz-result${answer.isCorrect ? ' is-correct' : ' is-wrong'}`} aria-live="polite">
                  {answer.isCorrect ? 'Correct' : `${answer.word} — ${answer.translation}`}
                </p>
                <div className="quiz-actions">
                  <button
                    ref={nextButtonRef}
                    type="button"
                    className="btn btn-primary btn-lg"
                    disabled={busy}
                    onClick={() => void goNext()}
                  >
                    Next <kbd>Enter</kbd>
                  </button>
                </div>
              </>
            ) : (
              <div className="quiz-actions">
                <button type="button" className="btn btn-quiet" disabled={busy} onClick={markKnown}>
                  Know
                </button>
              </div>
            )}
          </div>
        </>
      )}

      {phase === 'quiz' && !question && !error && <p className="footnote">Loading…</p>}

      {phase === 'summary' && summary && (
        <section className="summary">
          <p className="footnote">
            {dictionaryName} · {scopeTitle}
          </p>

          {summary.total === 0 ? (
            <h1 className="large-title">No questions left</h1>
          ) : (
            <>
              <h1 className="large-title summary-score num">
                {summary.correct} of {summary.total} · {percentOf(summary.correct, summary.total)}%
              </h1>
              <p className={`summary-badge${summary.passed ? ' pass' : ' fail'}`}>
                {summary.passed ? 'Passed' : 'Not passed'}
              </p>

              <ul className="summary-table">
                {[...summary.words]
                  .sort((a, b) => {
                    const cleanA = a.correct === a.total ? 0 : 1
                    const cleanB = b.correct === b.total ? 0 : 1
                    return cleanA - cleanB || a.word.localeCompare(b.word, 'en')
                  })
                  .map((w) => (
                    <li key={w.word} className={`summary-row${w.correct === w.total ? ' clean' : ''}`}>
                      <span className="summary-word">{w.word}</span>
                      <span className="summary-translation">{w.translation}</span>
                      <span className="summary-score-cell num">{w.correct === w.total ? '' : `${w.correct}/${w.total}`}</span>
                      <span className="summary-box num">box {w.box}</span>
                      <span className="summary-due num">{formatDue(w.dueAt, w.isLearned, new Date())}</span>
                    </li>
                  ))}
              </ul>
            </>
          )}

          {notice && <p className="footnote">{notice}</p>}

          <div className="summary-actions">
            {summary.words.some((w) => w.correct < w.total) && (
              <button type="button" className="btn btn-primary" disabled={busy} onClick={retry}>
                Review mistakes
              </button>
            )}
            {batchSize !== null && (
              <button type="button" className="btn btn-secondary" disabled={busy} onClick={anotherBatch}>
                Another batch
              </button>
            )}
            <button type="button" className="btn btn-quiet" onClick={onBack}>
              {dictionaryId === null ? 'Back home' : 'Back to dictionary'}
            </button>
          </div>
        </section>
      )}
    </>
  )
}
