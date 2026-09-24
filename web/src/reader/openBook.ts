import type { BookStore } from './bookStore'
import { sha256Hex } from './hash'
import { BookFormatError, readBookFile, type ReaderBook } from './readerBook'

export type OpenResult =
  | { kind: 'opened'; hash: string }
  /** The file is not the one the learner was continuing: nothing was kept. */
  | { kind: 'mismatch'; hash: string }
  | { kind: 'error'; message: string }

/**
 * A chosen file → a book on this device. With expectedHash (continuing a book read elsewhere)
 * a different file stops here, before anything is kept, so the library can ask first. The
 * server learns about the book when the reader opens it.
 */
export async function openBookFile(file: File, store: BookStore, expectedHash: string | null): Promise<OpenResult> {
  let bytes: ArrayBuffer

  try {
    bytes = await file.arrayBuffer()
  } catch {
    return { kind: 'error', message: "Couldn't read the file." }
  }

  const hash = await sha256Hex(bytes)

  if (expectedHash !== null && hash !== expectedHash) {
    return { kind: 'mismatch', hash }
  }

  let book: ReaderBook

  try {
    book = readBookFile(bytes, file.name)
  } catch (e) {
    return {
      kind: 'error',
      message: e instanceof BookFormatError ? e.message : "This file isn't a readable fb2 book.",
    }
  }

  try {
    await store.put(
      { hash, title: book.title, author: book.author, fileName: file.name, addedAt: new Date().toISOString() },
      bytes,
    )
  } catch {
    return { kind: 'error', message: "There's no room left to keep this book on the device." }
  }

  return { kind: 'opened', hash }
}
