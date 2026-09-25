import { unzipSync } from 'fflate'

/** A zip's files by their path inside the archive. */
export type ZipEntries = Map<string, Uint8Array>

/** What a zip archive made on a Mac carries besides the book. */
const JUNK = /^__MACOSX\/|(?:^|\/)\.DS_Store$/

/** The local-file-header signature every zip starts with — an epub and a zipped fb2 both do. */
export function looksZipped(bytes: ArrayBuffer): boolean {
  const head = new Uint8Array(bytes, 0, Math.min(4, bytes.byteLength))

  return head[0] === 0x50 && head[1] === 0x4b && head[2] === 0x03 && head[3] === 0x04
}

/**
 * The whole archive at once, synchronously — an epub is a few megabytes and fflate's unzipSync
 * takes tens of milliseconds over it, so nothing above this has to become async. Throws on a
 * corrupt archive; readBookSource turns that into a BookFormatError.
 */
export function readZip(bytes: ArrayBuffer): ZipEntries {
  const entries: ZipEntries = new Map()

  for (const [path, content] of Object.entries(unzipSync(new Uint8Array(bytes)))) {
    if (path.endsWith('/') || JUNK.test(path)) {
      continue
    }

    entries.set(path, content)
  }

  return entries
}
