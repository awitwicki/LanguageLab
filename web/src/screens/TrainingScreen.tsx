import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type AnswerResult, type NextQuestion, type TrainingStarted, type TrainingSummary } from '../api/client'
import { SortingProgress } from '../components/SortingProgress'
import { formatDue, percentOf } from '../lib/format'
import './TrainingScreen.css'

type Phase = 'cards' | 'quiz' | 'summary'

interface Props {
  dictionaryId: number
  dictionaryName: string
  scopeTitle: string
  chapterIds: number[] | null
  /** null для повторення — тоді «Ще один батч» не показується. */
  batchSize: number | null
  started: TrainingStarted
  onBack: () => void
}

export function TrainingScreen({ dictionaryId, dictionaryName, scopeTitle, chapterIds, batchSize, started, onBack }: Props) {
  // Сесія живе тут, а не в маршруті: «Повторити помилки» та «Ще один батч» починають
  // нову сесію на місці, без стрибка по екранах.
  const [session, setSession] = useState(started)
  const [phase, setPhase] = useState<Phase>(started.mode === 'review' ? 'quiz' : 'cards')
  const [next, setNext] = useState<NextQuestion | null>(null)
  const [pickedId, setPickedId] = useState<number | null>(null)
  const [answer, setAnswer] = useState<AnswerResult | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [summary, setSummary] = useState<TrainingSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const nextButtonRef = useRef<HTMLButtonElement>(null)

  const loadNext = useCallback(async () => {
    setBusy(true)

    try {
      const n = await api.nextQuestion(session.trainingId)

      if (n.question === null) {
        // Не лишаємо нотатку («Знаю» / «більше не з’явиться») з попереднього питання —
        // вона стосувалась картки квізу, а не екрана підсумків.
        setNotice(null)
        setSummary(await api.finish(session.trainingId))
        setPhase('summary')
        return
      }

      setNext(n)
      setPickedId(null)
      setAnswer(null)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }, [session.trainingId])

  const restart = (nextSession: TrainingStarted) => {
    setSession(nextSession)
    setPhase(nextSession.mode === 'review' ? 'quiz' : 'cards')
    setNext(null)
    setPickedId(null)
    setAnswer(null)
    setNotice(null)
    setSummary(null)
    setError(null)
  }

  // Лише перемикає фазу: перше питання підтягне ефект нижче — так «Почати квіз»
  // і старт повторення ідуть одним шляхом і не роблять подвійного запиту.
  const startQuiz = useCallback(() => setPhase('quiz'), [])

  // Повторення стартує без карток — слова юзер уже бачив; той самий ефект підвантажує
  // перше питання після «Почати квіз» і після перезапуску сесії.
  useEffect(() => {
    if (phase === 'quiz' && next === null && summary === null) {
      void Promise.resolve().then(() => loadNext())
    }
  }, [phase, next, summary, loadNext])

  // Опції стають disabled одразу після відповіді, тож фокус з обраної кнопки випадає на
  // <body>; переносимо його на «Далі» — саме туди веде наступна клавіатурна дія.
  useEffect(() => {
    if (answer) {
      nextButtonRef.current?.focus()
    }
  }, [answer])

  const pick = useCallback(
    async (wordPairId: number) => {
      if (!next?.question || answer || busy) {
        return
      }

      setNotice(null)
      setPickedId(wordPairId)
      setBusy(true)

      try {
        const result = await api.answer(session.trainingId, next.question.id, wordPairId)

        // 204: питання вже відповідане або зникло — нічого підсвічувати, йдемо далі.
        if (!result) {
          await loadNext()
          return
        }

        setAnswer(result)
      } catch (e) {
        setError(String(e))
      } finally {
        setBusy(false)
      }
    },
    [next, answer, busy, session.trainingId, loadNext],
  )

  const goNext = useCallback(() => {
    if (!answer || busy) {
      return
    }

    setNotice(null)
    void loadNext()
  }, [answer, busy, loadNext])

  const markKnown = async () => {
    if (!next?.question || busy) {
      return
    }

    setBusy(true)

    try {
      const known = await api.markKnown(session.trainingId, next.question.id)
      setNotice(known ? `${known.word} більше не з’явиться` : null)
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
        setNotice('Помилок для повторення немає.')
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
    if (batchSize === null) {
      return
    }

    setBusy(true)
    setNotice(null)

    try {
      const nextSession = await api.startNewBatch(dictionaryId, chapterIds, batchSize)

      if (!nextSession) {
        setNotice('Слів у цьому наборі більше немає.')
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
      // preventDefault() лише коли гілка реально щось робить — інакше в реальному браузері
      // (на відміну від jsdom) воно скасовує рідну активацію фокусованої кнопки клавішею Enter.
      let handled = true

      if (phase === 'cards' && event.key === 'Enter') {
        startQuiz()
      } else if (phase === 'quiz' && event.key === 'Enter' && answer && !busy) {
        goNext()
      } else if (phase === 'quiz' && !answer && !busy && event.key >= '1' && event.key <= '6') {
        const option = next?.question?.options[Number(event.key) - 1]
        if (option) void pick(option.wordPairId)
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
  }, [phase, next, answer, busy, startQuiz, goNext, pick])

  return (
    <>
      <div className="training-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ {dictionaryName}
        </button>
      </div>

      {error && <p className="error">{error}</p>}

      {phase === 'cards' && (
        <section className="cards">
          <p className="footnote">
            {dictionaryName} · {scopeTitle}
          </p>
          <h1 className="large-title">Нові слова</h1>
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
              Почати квіз <kbd>Enter</kbd>
            </button>
          </div>
        </section>
      )}

      {phase === 'quiz' && next?.question && (
        <>
          <SortingProgress
            scope={dictionaryName}
            title={scopeTitle}
            sorted={next.answered}
            total={next.total}
            counts={`Питання ${next.answered + 1} з ${next.total}`}
          />

          {notice && <p className="footnote quiz-notice">{notice}</p>}

          <div className="quiz-card">
            <p key={next.question.id} className="quiz-prompt">
              {next.question.prompt}
            </p>

            <div className="options">
              {next.question.options.map((option, index) => {
                const isCorrect = answer !== null && option.wordPairId === answer.correctWordPairId
                const isWrong = answer !== null && option.wordPairId === pickedId && !answer.isCorrect
                const state = isCorrect ? ' is-correct' : isWrong ? ' is-wrong' : ''

                return (
                  <button
                    key={option.wordPairId}
                    type="button"
                    className={`btn btn-secondary option${state}`}
                    disabled={answer !== null || busy}
                    onClick={() => void pick(option.wordPairId)}
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
                  {answer.isCorrect ? 'Правильно' : `${answer.word} — ${answer.translation}`}
                </p>
                <div className="quiz-actions">
                  <button
                    ref={nextButtonRef}
                    type="button"
                    className="btn btn-primary btn-lg"
                    disabled={busy}
                    onClick={goNext}
                  >
                    Далі <kbd>Enter</kbd>
                  </button>
                </div>
              </>
            ) : (
              <div className="quiz-actions">
                <button type="button" className="btn btn-quiet" disabled={busy} onClick={markKnown}>
                  Знаю
                </button>
              </div>
            )}
          </div>
        </>
      )}

      {phase === 'quiz' && !next?.question && !error && <p className="footnote">Завантажую…</p>}

      {phase === 'summary' && summary && (
        <section className="summary">
          <p className="footnote">
            {dictionaryName} · {scopeTitle}
          </p>

          {summary.total === 0 ? (
            <h1 className="large-title">Жодного питання не лишилось</h1>
          ) : (
            <>
              <h1 className="large-title summary-score num">
                {summary.correct} з {summary.total} · {percentOf(summary.correct, summary.total)}%
              </h1>
              <p className={`summary-badge${summary.passed ? ' pass' : ' fail'}`}>
                {summary.passed ? 'Пройдено' : 'Не пройдено'}
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
                      <span className="summary-box num">бокс {w.box}</span>
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
                Повторити помилки
              </button>
            )}
            {batchSize !== null && (
              <button type="button" className="btn btn-secondary" disabled={busy} onClick={anotherBatch}>
                Ще один батч
              </button>
            )}
            <button type="button" className="btn btn-quiet" onClick={onBack}>
              До словника
            </button>
          </div>
        </section>
      )}
    </>
  )
}
