import { Fragment, memo } from 'react'
import type { ReaderSentence } from './readerBook'
import { resolveWord, type KnownStatuses, type ResolvedWord } from './wordStatus'
import './Sentence.css'

export type SentenceTranslation =
  | { state: 'loading' }
  | { state: 'open'; text: string }
  | { state: 'error'; message: string }

/** Word lengths the placeholder cycles through — uneven, so the bars read as text, not as a bar chart. */
const SKELETON_WORDS = [5, 8, 3, 6, 10, 4, 7, 5, 9, 4, 6, 3, 8, 5, 7, 4]

/**
 * Characters of source text per character of bar. A `ch` is the width of a "0", which runs wider
 * than an average letter, so a bar of N ch takes more room than N characters of the translation
 * would; 0.9 is what measuring real translations against their sentences gave, from phone widths
 * up to the reader's full 720px.
 */
const BAR_CHARS_PER_SOURCE_CHAR = 0.9

/** A sentence this long is past what the translator takes anyway — no need to stand in for more. */
const MAX_SKELETON_CHARS = 400

/**
 * The lengths, in characters, of the words the translation is still bringing. Laid out inline
 * they wrap exactly where its text will, so the bubble is already the right height at any width
 * — which a fixed number of full-width bars could only guess at.
 */
function skeletonWords(text: string): number[] {
  const budget = Math.min(text.length * BAR_CHARS_PER_SOURCE_CHAR, MAX_SKELETON_CHARS)
  const words: number[] = []
  let used = 0

  while (used < budget) {
    const length = SKELETON_WORDS[words.length % SKELETON_WORDS.length]

    words.push(length)
    // The space after the word counts too — it is what the next bar may break at.
    used += length + 1
  }

  return words
}

interface Props {
  sentence: ReaderSentence
  /** "chapter.paragraph.sentence" — the key of the position and of the translation cache. */
  positionKey: string
  /** The first sentence of a paragraph gets a little more room above. */
  paragraphStart: boolean
  statuses: KnownStatuses
  /** The tapped token's index in this sentence, or null. */
  selectedToken: number | null
  canTranslate: boolean
  translation: SentenceTranslation | undefined
  onWordTap: (positionKey: string, tokenIndex: number, resolved: ResolvedWord, form: string, element: HTMLElement) => void
  /** Opens, closes or retries — the caller decides from the current state. */
  onToggleTranslation: (positionKey: string, text: string) => void
}

export const Sentence = memo(function Sentence({
  sentence,
  positionKey,
  paragraphStart,
  statuses,
  selectedToken,
  canTranslate,
  translation,
  onWordTap,
  onToggleTranslation,
}: Props) {
  const toggle = () => onToggleTranslation(positionKey, sentence.text)
  const stripClasses = ['reader-strip']

  if (translation) stripClasses.push('reader-strip-open')
  if (translation?.state === 'loading') stripClasses.push('reader-strip-loading')

  return (
    <div className={paragraphStart ? 'reader-sentence reader-paragraph-start' : 'reader-sentence'} data-pos={positionKey}>
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
          className={stripClasses.join(' ')}
          aria-label={translation ? 'Hide translation' : 'Translate sentence'}
          aria-expanded={translation !== undefined}
          onClick={toggle}
        >
          <span className="reader-strip-line" aria-hidden="true" />
        </button>
      )}

      {translation?.state === 'loading' && (
        <p className="reader-translation reader-translation-loading" role="status" aria-busy="true" aria-label="Translating…">
          {skeletonWords(sentence.text).map((length, index) => (
            <Fragment key={index}>
              <span className="skeleton skeleton-word" style={{ width: `${length}ch` }} />{' '}
            </Fragment>
          ))}
        </p>
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
