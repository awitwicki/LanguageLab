import { Fragment, memo } from 'react'
import type { ReaderSentence } from './readerBook'
import { resolveWord, type KnownStatuses, type ResolvedWord } from './wordStatus'
import './Sentence.css'

export type SentenceTranslation =
  | { state: 'loading' }
  | { state: 'open'; text: string }
  | { state: 'error'; message: string }

interface Props {
  sentence: ReaderSentence
  /** "chapter.paragraph.sentence" — the key of the position and of the translation cache. */
  positionKey: string
  /** The first sentence of a paragraph gets a little more room above. */
  paragraphStart: boolean
  statuses: KnownStatuses
  /** The tapped token's index in this sentence, or null. */
  selectedToken: number | null
  /** The saved reading position is here. */
  isBookmark: boolean
  canTranslate: boolean
  translation: SentenceTranslation | undefined
  onWordTap: (positionKey: string, tokenIndex: number, resolved: ResolvedWord, form: string, element: HTMLElement) => void
  /** Opens, closes or retries — the caller decides from the current state. */
  onToggleTranslation: (positionKey: string, text: string) => void
}

function TranslateIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
      <path
        d="M2 3h6M5 2v1m1.5 0C6 6 4.5 8 2.5 9M4 5.5c.8 1.5 2 2.7 3.5 3.3M9 14l2.5-6 2.5 6m-4.2-1.8h3.4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.3"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export const Sentence = memo(function Sentence({
  sentence,
  positionKey,
  paragraphStart,
  statuses,
  selectedToken,
  isBookmark,
  canTranslate,
  translation,
  onWordTap,
  onToggleTranslation,
}: Props) {
  const toggle = () => onToggleTranslation(positionKey, sentence.text)

  return (
    <div className={paragraphStart ? 'reader-sentence reader-paragraph-start' : 'reader-sentence'} data-pos={positionKey}>
      <div className="reader-gutter" aria-hidden="true">
        {isBookmark && <span className="reader-bookmark" />}
      </div>

      <p className="reader-text">
        {sentence.tokens.map((token, index) => {
          const resolved = token.isWord ? resolveWord(token.text, statuses) : null

          if (!resolved) {
            return <Fragment key={index}>{token.text}</Fragment>
          }

          const classes = ['reader-word']

          if (resolved.status === 'new' || resolved.status === 'learning') {
            classes.push(`reader-word-${resolved.status}`)
          }

          if (selectedToken === index) {
            classes.push('reader-word-selected')
          }

          return (
            <span
              key={index}
              className={classes.join(' ')}
              onClick={(event) => onWordTap(positionKey, index, resolved, token.text, event.currentTarget)}
            >
              {token.text}
            </span>
          )
        })}
      </p>

      {canTranslate && (
        <button
          type="button"
          className="reader-strip"
          aria-label={translation ? 'Hide translation' : 'Translate sentence'}
          aria-expanded={translation !== undefined}
          onClick={toggle}
        >
          <span className="reader-strip-icon" aria-hidden="true">
            {translation?.state === 'loading' ? '…' : translation ? '⌃' : <TranslateIcon />}
          </span>
        </button>
      )}

      {translation?.state === 'open' && (
        <p className="reader-translation" lang="uk">
          {translation.text}
        </p>
      )}

      {translation?.state === 'error' && (
        <p className="reader-translation reader-translation-error">
          {translation.message}{' '}
          <button type="button" className="btn btn-quiet" onClick={toggle}>
            Retry
          </button>
        </p>
      )}
    </div>
  )
})
