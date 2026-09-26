import { useRef } from 'react'
import type { IpaEntry } from '../api/client'

interface Props {
  entry: IpaEntry
  /** Offered only where practice can actually run; null hides the link. */
  onPractice: ((familyKey: string) => void) | null
}

/**
 * One row of the chart: the symbol, how it is read, an example word, and whatever
 * recordings exist for it. Both clips share one audio element — playing one stops the
 * other, which is what a learner comparing them expects.
 */
export function IpaSymbolCard({ entry, onPractice }: Props) {
  const audio = useRef<HTMLAudioElement>(null)

  const play = (src: string) => {
    const element = audio.current
    if (!element) return

    if (element.src.endsWith(src)) {
      element.currentTime = 0
    } else {
      element.src = src
    }
    void element.play().catch(() => {})
  }

  return (
    <li className="ipa-card">
      <p className="ipa-symbol" lang="und-fonipa">
        {entry.symbol}
      </p>
      <div className="ipa-body">
        <p className="ipa-name">{entry.name}</p>
        <p className="ipa-hint">{entry.hint}</p>
        {entry.exampleWord && (
          <p className="ipa-example">
            <span className="ipa-example-word">{entry.exampleWord}</span>
            <span className="ipa-example-ipa">{entry.exampleIpa}</span>
            {entry.exampleLanguage !== 'English' && (
              <span className="ipa-example-language">{entry.exampleLanguage}</span>
            )}
          </p>
        )}
      </div>
      <div className="ipa-actions">
        {entry.soundAudio && (
          <button type="button" className="btn btn-secondary" onClick={() => play(entry.soundAudio!)}>
            Play the sound
          </button>
        )}
        {entry.wordAudio && (
          <button type="button" className="btn btn-quiet" onClick={() => play(entry.wordAudio!)}>
            Play “{entry.exampleWord}”
          </button>
        )}
        {!entry.soundAudio && !entry.wordAudio && <p className="footnote ipa-no-audio">No recording yet</p>}
        {entry.familyKey && onPractice && (
          <button type="button" className="btn btn-quiet" onClick={() => onPractice(entry.familyKey!)}>
            Practice it
          </button>
        )}
        {/* eslint-disable-next-line jsx-a11y/media-has-caption -- a single spoken sound has nothing to caption */}
        <audio ref={audio} preload="none" />
      </div>
    </li>
  )
}
