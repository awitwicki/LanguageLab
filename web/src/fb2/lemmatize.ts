import nlp from 'compromise'
import { adjective, noun, verb } from 'wink-lemmatizer'
import { cleanWord, isRejected, splitCompoundWord } from './tokenize'

/**
 * The same two-step scheme as extract.py: first a POS tag in the context of
 * the sentence, then lemmatization for exactly that tag. Tagging a word on its
 * own defaults to noun and leaves gerunds like `surrounding` untouched.
 */
export function lemmatizeText(text: string): string[] {
  const document = nlp(text)
  const result: string[] = []

  for (const term of document.json({ terms: { tags: true } }).flatMap((s: any) => s.terms)) {
    const raw = cleanWord(String(term.text ?? ''))

    if (raw === '') {
      continue
    }

    const tags: string[] = term.tags ?? []

    // Compound words yield only their parts: the joined spelling (`xray`)
    // never appears in the text and must not end up in the dictionary.
    const candidates = raw.includes('-') ? splitCompoundWord(raw) : [raw]

    for (const candidate of candidates) {
      const lemma = lemmatizeWord(candidate, tags)

      if (!isRejected(lemma)) {
        result.push(lemma)
      }
    }
  }

  return result
}

function lemmatizeWord(word: string, tags: string[]): string {
  if (tags.includes('Verb')) {
    return verb(word)
  }

  if (tags.includes('Adjective')) {
    return adjective(word)
  }

  if (tags.includes('Noun')) {
    return noun(word)
  }

  // No tag — try as a noun, which is the default in extract.py too.
  return noun(word)
}
