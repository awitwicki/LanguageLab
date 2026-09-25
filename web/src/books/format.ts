import { decodeXml } from './decode'
import { parseEpub, type EpubBook } from './epub'
import { BookFormatError } from './formatError'
import { looksZipped, readZip, type ZipEntries } from './zip'

/**
 * The two algorithms the epub spec and Adobe's own extension to it use to obfuscate embedded font
 * files — not the book's text. Very common in DRM-free retail and InDesign-built epubs, which
 * would otherwise be wrongly refused as protected. Any other algorithm, or an encryption.xml that
 * cannot be read at all, is treated as real DRM: better a false refusal than showing garbage text.
 */
const FONT_OBFUSCATION_ALGORITHMS = new Set([
  'http://www.idpf.org/2008/embedding',
  'http://ns.adobe.com/pdf/enc#RC',
])

/**
 * A book file reduced to its content: fb2 XML, or a parsed epub. The one place in the app that
 * decides which — and it decides from the bytes, so a book with a wrong or missing extension
 * still opens.
 */
export type BookSource =
  | { format: 'fb2'; xml: string; fallbackTitle: string }
  | { format: 'epub'; book: EpubBook }

/** The file name without its book extension — a title for a book whose metadata names none. */
export function stripBookExtension(fileName: string): string {
  return fileName.replace(/\.(?:fb2|epub)\.zip$/i, '').replace(/\.(?:fb2|epub|zip)$/i, '')
}

export function readBookSource(bytes: ArrayBuffer, fileName: string): BookSource {
  const fallbackTitle = stripBookExtension(fileName)

  if (!looksZipped(bytes)) {
    return { format: 'fb2', xml: decodeXml(bytes), fallbackTitle }
  }

  let entries: ZipEntries

  try {
    entries = readZip(bytes)
  } catch {
    throw new BookFormatError('invalid')
  }

  // Adobe's and everyone else's DRM: the text is there but encrypted, so say so rather than
  // let the parser report an unreadable book. An encryption.xml that only obfuscates fonts is
  // not DRM on the text and is let through.
  const encryption = entries.get('META-INF/encryption.xml')

  if (encryption && !isFontObfuscationOnly(decodeXml(encryption))) {
    throw new BookFormatError('encrypted')
  }

  if (entries.has('META-INF/container.xml') || mimetypeOf(entries) === 'application/epub+zip') {
    return { format: 'epub', book: parseEpub(entries, fallbackTitle) }
  }

  const fb2 = [...entries.keys()].filter((path) => /\.fb2$/i.test(path))

  // Exactly one: with several there is no telling which book the learner meant.
  if (fb2.length === 1) {
    return { format: 'fb2', xml: decodeXml(entries.get(fb2[0])!), fallbackTitle }
  }

  throw new BookFormatError('invalid')
}

function mimetypeOf(entries: ZipEntries): string {
  const bytes = entries.get('mimetype')

  return bytes ? new TextDecoder('ascii').decode(bytes).trim() : ''
}

/** True only when every EncryptedData in the file targets font obfuscation, never the text. */
function isFontObfuscationOnly(xml: string): boolean {
  const doc = new DOMParser().parseFromString(xml, 'application/xml')

  if (doc.getElementsByTagName('parsererror').length > 0) {
    return false
  }

  const methods = Array.from(doc.getElementsByTagName('*')).filter(
    (element) => element.localName.toLowerCase() === 'encryptionmethod',
  )

  return methods.length > 0 && methods.every((method) => FONT_OBFUSCATION_ALGORITHMS.has(method.getAttribute('Algorithm') ?? ''))
}
