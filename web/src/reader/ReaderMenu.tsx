import { useState } from 'react'
import type { ReaderChapter } from './readerBook'
import { MAX_DIM, TEXT_SIZES, type ReaderSettings, type ReaderTheme } from './readerSettings'
import './ReaderMenu.css'

interface Props {
  chapters: ReaderChapter[]
  currentChapter: number
  settings: ReaderSettings
  dictionaryId: number | null
  onJump: (chapterIndex: number) => void
  onSettings: (next: ReaderSettings) => void
  onOpenDictionary: (dictionaryId: number) => void
  onClose: () => void
}

const THEMES: { theme: ReaderTheme; label: string }[] = [
  { theme: 'light', label: 'Light' },
  { theme: 'dark', label: 'Dark' },
  { theme: 'system', label: 'System' },
]

export function ReaderMenu({
  chapters,
  currentChapter,
  settings,
  dictionaryId,
  onJump,
  onSettings,
  onOpenDictionary,
  onClose,
}: Props) {
  const [tab, setTab] = useState<'contents' | 'display'>('contents')

  return (
    <>
      <div className="reader-sheet-backdrop" onClick={onClose} />
      <section className="reader-sheet" role="dialog" aria-label="Contents and display">
        <div className="reader-sheet-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'contents'} onClick={() => setTab('contents')}>
            Contents
          </button>
          <button type="button" role="tab" aria-selected={tab === 'display'} onClick={() => setTab('display')}>
            Display
          </button>
        </div>

        {tab === 'contents' && (
          <>
            <ol className="reader-contents">
              {chapters.map((chapter, index) => (
                <li key={index}>
                  <button
                    type="button"
                    aria-current={index === currentChapter ? 'true' : undefined}
                    onClick={() => onJump(index)}
                  >
                    {chapter.title || `Chapter ${index + 1}`}
                  </button>
                </li>
              ))}
            </ol>
            {dictionaryId !== null && (
              <button type="button" className="btn btn-secondary reader-sheet-wide" onClick={() => onOpenDictionary(dictionaryId)}>
                Open the book's dictionary
              </button>
            )}
          </>
        )}

        {tab === 'display' && (
          <div className="reader-display">
            <div className="reader-display-row" role="group" aria-label="Theme">
              {THEMES.map(({ theme, label }) => (
                <button
                  key={theme}
                  type="button"
                  className="reader-choice"
                  aria-pressed={settings.theme === theme}
                  onClick={() => onSettings({ ...settings, theme })}
                >
                  {label}
                </button>
              ))}
            </div>

            <label className="reader-display-row reader-dim">
              <span>Dimming</span>
              <input
                type="range"
                min={0}
                max={Math.round(MAX_DIM * 100)}
                step={5}
                value={Math.round(settings.dim * 100)}
                onChange={(event) => onSettings({ ...settings, dim: Number(event.target.value) / 100 })}
              />
            </label>

            <div className="reader-display-row" role="group" aria-label="Text size">
              <button
                type="button"
                className="reader-choice"
                aria-label="Smaller text"
                disabled={settings.textSize === 0}
                onClick={() => onSettings({ ...settings, textSize: settings.textSize - 1 })}
              >
                A−
              </button>
              <span className="num">
                {settings.textSize + 1} of {TEXT_SIZES.length}
              </span>
              <button
                type="button"
                className="reader-choice"
                aria-label="Larger text"
                disabled={settings.textSize === TEXT_SIZES.length - 1}
                onClick={() => onSettings({ ...settings, textSize: settings.textSize + 1 })}
              >
                A+
              </button>
            </div>
          </div>
        )}
      </section>
    </>
  )
}
