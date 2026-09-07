import nlp from 'compromise'
import { adjective, noun, verb } from 'wink-lemmatizer'
import { cleanWord, isRejected, splitCompoundWord } from './tokenize'

/**
 * Та сама двоступенева схема, що в extract.py: спершу POS-тег у контексті
 * речення, потім лематизація саме під цей тег. Тегування слова окремо
 * дефолтиться на іменник і лишає герундії на кшталт `surrounding` недоторканими.
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

    // Складені слова дають лише свої частини: злите написання (`xray`)
    // ніколи не з'являється в тексті і не має потрапляти у словник.
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

  // Немає тега — пробуємо як іменник, це дефолт і в extract.py.
  return noun(word)
}
