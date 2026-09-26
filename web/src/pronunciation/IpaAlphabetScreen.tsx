import { useEffect, useMemo, useState } from 'react'
import { api, type IpaAlphabet } from '../api/client'
import { formatInt } from '../lib/format'
import { countEntries, filterAlphabet, groupRuns } from './ipaSearch'
import { IpaSymbolCard } from './IpaSymbolCard'
import { isSpeechRecognitionSupported } from './speechRecognition'
import './IpaAlphabetScreen.css'

interface Props {
  onBack: () => void
  onOpenFamily: (key: string) => void
}

/**
 * The whole alphabet, to look a symbol up in — the reference half of the pronunciation
 * mode. It reads no progress and records nothing, so unlike the trainer it works in every
 * browser; the link into practice is the one part that needs speech recognition.
 */
export function IpaAlphabetScreen({ onBack, onOpenFamily }: Props) {
  const [alphabet, setAlphabet] = useState<IpaAlphabet | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [englishOnly, setEnglishOnly] = useState(false)

  useEffect(() => {
    api.getIpaAlphabet().then(setAlphabet).catch((e) => setError(String(e)))
  }, [])

  const sections = useMemo(
    () => (alphabet ? filterAlphabet(alphabet.sections, query, englishOnly) : []),
    [alphabet, query, englishOnly],
  )

  const onPractice = isSpeechRecognitionSupported() ? onOpenFamily : null
  const shown = countEntries(sections)
  const total = alphabet ? countEntries(alphabet.sections) : 0

  return (
    <>
      <button type="button" className="btn btn-quiet ipa-back" onClick={onBack}>
        Back
      </button>
      <h1 className="large-title">The phonetic alphabet</h1>
      <p className="ipa-intro">
        Every symbol of the IPA, how it is read, and a word to hear it in. Search by the symbol itself, by the
        letters it stands for, or by the name of the sound.
      </p>

      {error && <p className="error">{error}</p>}
      {!error && !alphabet && <p className="footnote">Loading…</p>}

      {alphabet && (
        <>
          <div className="ipa-controls">
            <label className="field ipa-search">
              <input
                name="symbol"
                value={query}
                placeholder="Search a symbol, a sound or a word"
                aria-label="Search the alphabet"
                autoComplete="off"
                onChange={(e) => setQuery(e.target.value)}
              />
            </label>
            <label className="ipa-toggle">
              <input
                type="checkbox"
                checked={englishOnly}
                onChange={(e) => setEnglishOnly(e.target.checked)}
              />
              Only the sounds English uses
            </label>
          </div>

          <p className="footnote num ipa-count">
            {shown === total
              ? `${formatInt(total)} symbols`
              : `${formatInt(shown)} of ${formatInt(total)} symbols`}
          </p>

          {shown === 0 && <p className="ipa-empty">Nothing matches “{query.trim()}”.</p>}

          {sections.map((section) => {
            const runs = groupRuns(section.entries)
            return (
              <section key={section.key} className="ipa-section">
                <h2 className="ipa-section-title">{section.title}</h2>
                <p className="ipa-section-note">{section.note}</p>
                {runs.map((run, index) => (
                  <div key={`${run.group}-${index}`} className="ipa-group">
                    {runs.length > 1 && <h3 className="ipa-group-title">{run.group}</h3>}
                    <ul className="ipa-cards">
                      {run.entries.map((entry) => (
                        <IpaSymbolCard key={entry.symbol} entry={entry} onPractice={onPractice} />
                      ))}
                    </ul>
                  </div>
                ))}
              </section>
            )
          })}
        </>
      )}
    </>
  )
}
