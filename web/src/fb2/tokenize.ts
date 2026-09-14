import { verb } from 'wink-lemmatizer'

/** A port of NLTK's stopwords — the same set extract.py uses. */
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

/** From extract.py: the numerals ordinal forms are built from. */
const NUMBER_PREFIXES = ['one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten']
const ORDINAL_SUFFIXES = new Set(['th', 'st', 'nd', 'rd'])

/**
 * Ordinal forms that do not reduce to `prefix + suffix` because a vowel
 * drops out (nine → ninth, not nineth). extract.py has the same
 * prefix/suffix algorithm and the same gap — there it simply never comes
 * up, since it applies only to the parts of compound words.
 */
const IRREGULAR_ORDINALS = new Set(['ninth'])

/**
 * From extract.py: -ing forms that are nouns in their own right and must
 * not collapse into the verb, even when the base is present in the text.
 */
const ING_HOMOGRAPH_KEEP = new Set([
  'building', 'ceiling', 'clothing', 'evening', 'feeling', 'meaning', 'morning', 'nothing',
  'painting', 'setting', 'something', 'string', 'thing', 'wedding', 'writing',
])

/**
 * The apostrophe stays in the word (it is not cut out like ordinary punctuation):
 * otherwise "don't"/"wasn't" become "dont"/"wasnt" — valid-looking words that
 * `isRejected` (only [a-z]) no longer recognizes as contractions.
 * Books mostly write the apostrophe as the typographic character (’), so it
 * is normalized to a plain ' first.
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
 * When the text has both a verb and its -ing form, keep the verb.
 * Without this you would sort `surround` and `surrounding` as two different words.
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
