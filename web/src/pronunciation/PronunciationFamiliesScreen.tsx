import { useEffect, useState } from 'react'
import { api, type PronunciationProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import { isSpeechRecognitionSupported } from './speechRecognition'
import './PronunciationFamiliesScreen.css'

interface Props {
  onOpenFamily: (key: string) => void
}

export function PronunciationFamiliesScreen({ onOpenFamily }: Props) {
  const [progress, setProgress] = useState<PronunciationProgress | null>(null)
  const [error, setError] = useState<string | null>(null)
  const supported = isSpeechRecognitionSupported()

  useEffect(() => {
    if (!supported) {
      return
    }
    api.getPronunciationProgress().then(setProgress).catch((e) => setError(String(e)))
  }, [supported])

  if (!supported) {
    return (
      <>
        <h1 className="large-title">Pronunciation</h1>
        <p className="unsupported-message">
          Pronunciation practice needs a browser with speech recognition, like Chrome or Edge.
        </p>
      </>
    )
  }

  if (error) return <p className="error">{error}</p>
  if (!progress) return <p className="footnote">Loading…</p>

  return (
    <>
      <h1 className="large-title">
        Pronunciation <span className="beta-badge">Beta</span>
      </h1>
      <p className="pronunciation-intro">Real recordings, real feedback — a handful of well-chosen words per sound.</p>
      <p className="pronunciation-beta-note">
        This feature is in beta and speech recognition may not always work reliably. For best results, use Google
        Chrome.
      </p>
      <div className="family-tiles">
        {progress.families.map((family) => (
          <section key={family.key} className="family-tile">
            <p className="family-tile-title">{family.title}</p>
            <p className="family-tile-sounds">{family.targetSounds.join(' · ')}</p>
            <ProgressBar sorted={family.mastered} total={family.total} showLabel={false} />
            <p className="footnote num family-tile-caption">
              {formatInt(family.mastered)} of {formatInt(family.total)} mastered
            </p>
            <button type="button" className="btn btn-secondary" onClick={() => onOpenFamily(family.key)}>
              Open
            </button>
          </section>
        ))}
      </div>
    </>
  )
}
