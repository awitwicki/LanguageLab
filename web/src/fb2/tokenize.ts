import { verb } from 'wink-lemmatizer'

/** Порт stopwords з NLTK — той самий набір, що використовує extract.py. */
const STOP_WORDS = new Set([
  'i', 'me', 'my', 'myself', 'we', 'our', 'ours', 'ourselves', 'you', "you're", "you've",
  "you'll", "you'd", 'your', 'yours', 'yourself', 'yourselves', 'he', 'him', 'his', 'himself',
  'she', "she's", 'her', 'hers', 'herself', 'it', "it's", 'its', 'itself', 'they', 'them',
  'their', 'theirs', 'themselves', 'what', 'which', 'who', 'whom', 'this', 'that', "that'll",
  'these', 'those', 'am', 'is', 'are', 'was', 'were', 'be', 'been', 'being', 'have', 'has',
  'had', 'having', 'do', 'does', 'did', 'doing', 'a', 'an', 'the', 'and', 'but', 'if', 'or',
  'because', 'as', 'until', 'while', 'of', 'at', 'by', 'for', 'with', 'about', 'against',
  'between', 'into', 'through', 'during', 'before', 'after', 'above', 'below', 'to', 'from',
  'up', 'down', 'in', 'out', 'on', 'off', 'over', 'under', 'again', 'further', 'then', 'once',
  'here', 'there', 'when', 'where', 'why', 'how', 'all', 'any', 'both', 'each', 'few', 'more',
  'most', 'other', 'some', 'such', 'no', 'nor', 'not', 'only', 'own', 'same', 'so', 'than',
  'too', 'very', 's', 't', 'can', 'will', 'just', 'don', "don't", 'should', "should've", 'now',
  'd', 'll', 'm', 'o', 're', 've', 'y', 'ain', 'aren', "aren't", 'couldn', "couldn't", 'didn',
  "didn't", 'doesn', "doesn't", 'hadn', "hadn't", 'hasn', "hasn't", 'haven', "haven't", 'isn',
  "isn't", 'ma', 'mightn', "mightn't", 'mustn', "mustn't", 'needn', "needn't", 'shan', "shan't",
  'shouldn', "shouldn't", 'wasn', "wasn't", 'weren', "weren't", 'won', "won't", 'wouldn',
  "wouldn't",
])

/** З extract.py: числівники, з яких складаються порядкові форми. */
const NUMBER_PREFIXES = ['one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten']
const ORDINAL_SUFFIXES = new Set(['th', 'st', 'nd', 'rd'])

/**
 * Порядкові форми, які не зводяться до `prefix + suffix` через випадання
 * голосної (nine → ninth, а не nineth). extract.py має той самий
 * prefix/suffix алгоритм і той самий пробіл — там він просто ніколи не
 * зустрічається, бо застосовується лише до частин складених слів.
 */
const IRREGULAR_ORDINALS = new Set(['ninth'])

/**
 * З extract.py: -ing форми, які є самостійними іменниками й не мають
 * згортатись у дієслово, навіть коли база присутня в тексті.
 */
const ING_HOMOGRAPH_KEEP = new Set([
  'building', 'ceiling', 'clothing', 'evening', 'feeling', 'meaning', 'morning', 'nothing',
  'painting', 'setting', 'something', 'string', 'thing', 'wedding', 'writing',
])

/**
 * Апостроф лишається в слові (не вирізається як звичайна пунктуація):
 * інакше "don't"/"wasn't" стають "dont"/"wasnt" — валідними на вигляд
 * словами, і `isRejected` (тільки [a-z]) уже не впізнає в них скорочення.
 * Книги здебільшого пишуть апостроф типографським символом (’), тож він
 * спершу нормалізується до звичайного '.
 */
export function cleanWord(word: string): string {
  return word
    .toLowerCase()
    .replace(/[‘’]/g, "'")
    .replace(/[^\w\s'-]/g, '')
    .replace(/^[-_'"]+|[-_'"]+$/g, '')
}

export function splitCompoundWord(word: string): string[] {
  const parts: string[] = []

  for (const rawPart of word.split('-')) {
    const part = cleanWord(rawPart).replace(/[0-9]+/g, '')

    if (part.length > 2 && /^[a-z]+$/.test(part) && !isOrdinalNumberWord(part)) {
      parts.push(part)
    }
  }

  return parts
}

export function isRejected(word: string): boolean {
  if (word.length < 3) {
    return true
  }

  if (!/^[a-z]+$/.test(word)) {
    return true
  }

  if (STOP_WORDS.has(word)) {
    return true
  }

  return isOrdinalNumberWord(word)
}

/**
 * Якщо в тексті є і дієслово, і його -ing форма — лишаємо дієслово.
 * Без цього ти сортував би `surround` і `surrounding` як два різні слова.
 */
export function consolidateIngForms(vocabulary: Set<string>): Set<string> {
  const result = new Set(vocabulary)

  for (const word of vocabulary) {
    if (ING_HOMOGRAPH_KEEP.has(word)) {
      continue
    }

    if (word.endsWith('ings') && word.length >= 7) {
      const stem = word.slice(0, -4)

      if (vocabulary.has(stem) || vocabulary.has(`${stem}e`)) {
        result.delete(word)
        continue
      }
    }

    if (word.endsWith('ing') && word.length >= 5) {
      const base = verb(word)

      if (base !== word && vocabulary.has(base)) {
        result.delete(word)
      }
    }
  }

  return result
}

function isOrdinalNumberWord(word: string): boolean {
  if (IRREGULAR_ORDINALS.has(word)) {
    return true
  }

  for (const prefix of NUMBER_PREFIXES) {
    if (!word.startsWith(prefix)) {
      continue
    }

    const rest = word.slice(prefix.length)

    if (rest === '' || ORDINAL_SUFFIXES.has(rest)) {
      return true
    }
  }

  return false
}
