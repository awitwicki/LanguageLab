import { useCallback, useEffect, useRef, useState } from 'react'
import type { PronunciationAttemptResult } from '../api/client'
import { analyzeClip, type ClipAnalysis } from './audio/spectrogram'
import { useClipAnalysis, type ClipView } from './audio/useClipAnalysis'
import { isMicSupported, useMicCapture } from './audio/useMicCapture'
import { createSpeechRecognition, isSpeechRecognitionSupported, transcriptOf, type SpeechRecognitionLike } from './speechRecognition'
import { SpectrogramStrip } from './SpectrogramStrip'
import { usePronunciationFamily } from './usePronunciationFamily'
import './PronunciationFamilyScreen.css'

interface Props {
  familyKey: string
  onBack: () => void
}

/** Mirrors PronunciationStateMachine.MasteryStreak on the server. */
const MASTERY_STREAK = 3

/** The recognizer's error codes are a short fixed set; only the permission ones are actionable. */
function messageForRecognitionError(code: string): string {
  if (code === 'not-allowed' || code === 'permission-denied' || code === 'service-not-allowed') {
    return "Microphone access was denied — check your browser's permission settings and try again."
  }
  return "Couldn't hear you — try again."
}

function streakLabel(feedback: PronunciationAttemptResult): string {
  if (feedback.state === 'mastered') return 'Mastered.'
  return `${feedback.streak} of ${MASTERY_STREAK} in a row.`
}

function recordLabel(status: string, listening: boolean, micStarting: boolean): string {
  if (micStarting) return 'Starting…'
  if (listening) return 'Listening…'
  if (status === 'scoring') return 'Scoring…'
  return 'Record'
}

function referencePlaceholder(status: ClipView['status'] | undefined): string {
  if (status === 'unsupported') return 'Spectrogram needs a newer browser'
  if (status === 'failed') return 'No preview'
  return 'Loading…'
}

type AttemptView = { kind: 'idle' } | { kind: 'live' } | { kind: 'ready'; analysis: ClipAnalysis } | { kind: 'empty' }

const IDLE: AttemptView = { kind: 'idle' }

function messageForMicError(error: unknown): string {
  const name = error instanceof Error ? error.name : ''
  if (name === 'NotAllowedError' || name === 'PermissionDeniedError' || name === 'SecurityError') {
    return "Microphone access was denied — check your browser's permission settings and try again."
  }
  return "Couldn't start recording — try again."
}

function attemptPlaceholder(attempt: AttemptView, micSupported: boolean): string {
  if (!micSupported) return 'Spectrogram needs a newer browser'
  return attempt.kind === 'empty' ? 'Nothing captured' : 'Press Record'
}

export function PronunciationFamilyScreen({ familyKey, onBack }: Props) {
  const {
    status,
    error,
    attemptError,
    title,
    word,
    accent,
    feedback,
    setAccent,
    submitTranscript,
    reportAttemptError,
    next,
    practiceAgain,
    resetWord,
  } = usePronunciationFamily(familyKey)
  const [listening, setListening] = useState(false)
  const active = useRef<SpeechRecognitionLike | null>(null)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  const audioRef = useRef<HTMLAudioElement>(null)
  const [playing, setPlaying] = useState(false)
  const clipUrl = word ? (accent === 'uk' ? word.audioUk : word.audioUs) : null
  const reference = useClipAnalysis(clipUrl)
  const referenceAnalysis = reference?.status === 'ready' ? reference.analysis : null

  const referencePlayhead = useCallback((): number | null => {
    const audio = audioRef.current
    if (!audio || audio.paused || !referenceAnalysis) return null
    const fraction = (audio.currentTime - referenceAnalysis.trimStart) / referenceAnalysis.duration
    return Math.min(1, Math.max(0, fraction))
  }, [referenceAnalysis])

  const play = () => {
    void audioRef.current?.play().catch(() => {})
  }

  const mic = useMicCapture()
  const micSupported = isMicSupported()
  const [attempt, setAttempt] = useState<AttemptView>(IDLE)
  const [micStarting, setMicStarting] = useState(false)

  const finishCapture = useCallback(async () => {
    const clip = await mic.stop()
    setAttempt(clip ? { kind: 'ready', analysis: analyzeClip(clip.samples, clip.sampleRate) } : { kind: 'empty' })
  }, [mic])

  const record = useCallback(async () => {
    if (!isSpeechRecognitionSupported()) {
      reportAttemptError('Recording needs Chrome or Edge.')
      return
    }

    audioRef.current?.pause()
    reportAttemptError(null)

    if (micSupported) {
      setMicStarting(true)
      try {
        await mic.start()
      } catch (error) {
        setMicStarting(false)
        reportAttemptError(messageForMicError(error))
        return
      }
      setMicStarting(false)
    }

    // mic.start() awaits the permission prompt, which can stay open long enough for the
    // learner to navigate away. Don't create a recognizer for a screen that's gone.
    if (!mounted.current) return

    const recognition = createSpeechRecognition(accent === 'uk' ? 'en-GB' : 'en-US')!
    setListening(true)
    setAttempt(micSupported ? { kind: 'live' } : IDLE)
    active.current = recognition

    recognition.onresult = (event) => {
      void submitTranscript(transcriptOf(event))
    }
    recognition.onerror = (event) => {
      setListening(false)
      reportAttemptError(messageForRecognitionError(event.error))
    }
    // Always fires, after onresult or onerror alike — the one signal that the
    // recognizer has released the microphone and the button can be pressed again.
    recognition.onend = () => {
      setListening(false)
      active.current = null
      if (micSupported) void finishCapture()
    }

    try {
      recognition.start()
    } catch {
      // start() throws InvalidStateError when a previous recognition is still running.
      setListening(false)
      active.current = null
      if (micSupported) {
        void mic.stop()
        setAttempt(IDLE)
      }
      reportAttemptError("Couldn't start recording — try again.")
    }
  }, [accent, mic, micSupported, finishCapture, submitTranscript, reportAttemptError])

  // Leaving mid-recording must not post an attempt for a word that is no longer on
  // screen: abort discards the capture, and the detached handlers make sure nothing
  // fires even from a recognizer that reports after abort.
  useEffect(
    () => () => {
      const recognition = active.current
      if (!recognition) return
      recognition.onresult = null
      recognition.onerror = null
      recognition.onend = null
      recognition.abort()
      active.current = null
    },
    [],
  )

  useEffect(() => {
    if (status !== 'family-complete') return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onBack()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [status, onBack])

  if (error) return <p className="error">{error}</p>
  if (status === 'loading' && !word) return <p className="footnote">Loading…</p>

  return (
    <>
      <button type="button" className="btn btn-quiet" onClick={onBack}>
        Back
      </button>
      <h1 className="large-title">{title}</h1>

      <div className="accent-toggle">
        <button type="button" aria-pressed={accent === 'us'} onClick={() => setAccent('us')}>
          US
        </button>
        <button type="button" aria-pressed={accent === 'uk'} onClick={() => setAccent('uk')}>
          UK
        </button>
      </div>

      {word && (
        <div className="pronunciation-word-card">
          <p className="pronunciation-word">{word.word}</p>
          <p className="pronunciation-ipa">{word.ipa}</p>
          <audio
            ref={audioRef}
            src={clipUrl ?? undefined}
            preload="auto"
            onPlay={() => setPlaying(true)}
            onPause={() => setPlaying(false)}
            onEnded={() => setPlaying(false)}
          />
          <div className="pronunciation-strips">
            <SpectrogramStrip
              label="Reference"
              placeholder={referencePlaceholder(reference?.status)}
              spectrogram={referenceAnalysis?.spectrogram ?? null}
              duration={referenceAnalysis?.duration ?? null}
              playhead={referencePlayhead}
              action={
                <button type="button" className="btn btn-secondary" onClick={play} disabled={playing || listening || micStarting}>
                  {playing ? 'Playing…' : 'Play'}
                </button>
              }
            />
            <SpectrogramStrip
              label="Your attempt"
              placeholder={attemptPlaceholder(attempt, micSupported)}
              spectrogram={attempt.kind === 'ready' ? attempt.analysis.spectrogram : null}
              duration={attempt.kind === 'ready' ? attempt.analysis.duration : null}
              live={attempt.kind === 'live' ? mic.liveFrame : undefined}
            />
          </div>
          <div className="pronunciation-controls">
            <button
              type="button"
              className="btn btn-primary"
              onClick={record}
              disabled={status !== 'ready' || listening || micStarting}
            >
              {recordLabel(status, listening, micStarting)}
            </button>
            {/* Only worth offering once there is something to undo — a New word is already reset. */}
            {word.state !== 'new' && (
              <button
                type="button"
                className="btn btn-quiet"
                onClick={() => {
                  setAttempt(IDLE)
                  void resetWord()
                }}
                disabled={listening || micStarting || status === 'scoring'}
              >
                Reset progress
              </button>
            )}
          </div>

          {attemptError && (
            <p className="pronunciation-attempt-error" role="alert">
              {attemptError}
            </p>
          )}

          {feedback && (
            <div className={`pronunciation-feedback ${feedback.outcome}`}>
              <p>
                {feedback.outcome === 'correct' ? 'Nice — that landed.' : 'Not quite — try again.'} Score:{' '}
                {feedback.score}
              </p>
              <p className="footnote pronunciation-streak">{streakLabel(feedback)}</p>
              <button type="button" className="btn btn-secondary" onClick={() => { setAttempt(IDLE); next() }}>
                Next
              </button>
            </div>
          )}
        </div>
      )}

      {status === 'family-complete' && (
        <div className="pronunciation-complete-backdrop" onClick={onBack}>
          <div
            className="pronunciation-complete-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="pronunciation-complete-title"
            onClick={(event) => event.stopPropagation()}
          >
            <button type="button" className="btn btn-quiet dialog-close" aria-label="Close" onClick={onBack}>
              ✕
            </button>
            <h2 id="pronunciation-complete-title">All mastered!</h2>
            <p>You've mastered every word in this family.</p>
            <div className="pronunciation-complete-actions">
              <button type="button" className="btn btn-secondary" onClick={() => { setAttempt(IDLE); practiceAgain() }}>
                Practice again
              </button>
              <button type="button" className="btn btn-primary" onClick={onBack}>
                Back to lessons
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
