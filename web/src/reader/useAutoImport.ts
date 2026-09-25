import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import { parseBook } from '../fb2/chapters'
import { decodeFb2 } from '../fb2/decode'
import { createWordExtractor } from '../fb2/wordExtractor'

export type AutoImport =
  | { status: 'idle' }
  | { status: 'running'; done: number; total: number }
  | { status: 'failed' }
  | { status: 'done'; dictionaryId: number }

interface Options {
  /** Outside Telegram, reading a registered book that has no dictionary yet. */
  enabled: boolean
  hash: string
  title: string
  bytes: ArrayBuffer | null
  onImported: (dictionaryId: number) => void
}

// Books whose import started in this page session: a failure is not retried on its own, and
// coming back to the reader does not start a second import of the same book.
const started = new Set<string>()

/**
 * Builds the dictionary of a book opened in the reader: the import screen's pipeline (leaf
 * chapters, the word-extraction worker, importDictionary) in the background, as a private
 * dictionary named after the book. Reading goes on meanwhile.
 */
export function useAutoImport({ enabled, hash, title, bytes, onImported }: Options): AutoImport {
  const [state, setState] = useState<AutoImport>({ status: 'idle' })
  const imported = useRef(onImported)

  useEffect(() => {
    imported.current = onImported
  }, [onImported])

  useEffect(() => {
    if (!enabled || !bytes || started.has(hash)) return

    started.add(hash)

    let cancelled = false
    let finished = false
    const extractor = createWordExtractor()
    setState({ status: 'running', done: 0, total: 0 })

    const run = async () => {
      const { sections } = parseBook(decodeFb2(bytes))
      const chapters = await extractor.extract(sections, 'leaf', (done, total) => {
        if (!cancelled) setState({ status: 'running', done, total })
      })

      return api.importDictionary({
        name: title,
        requestPublication: false,
        fileHash: hash,
        chapters: chapters.map((c) => ({ order: c.order, title: c.title, words: c.words })),
      })
    }

    run()
      .then((result) => {
        finished = true
        if (cancelled) return
        setState({ status: 'done', dictionaryId: result.dictionaryId })
        imported.current(result.dictionaryId)
      })
      .catch(() => {
        finished = true
        if (!cancelled) setState({ status: 'failed' })
      })
      .finally(() => extractor.dispose())

    return () => {
      cancelled = true

      // Left before it finished: the worker goes with the screen, so a later visit may try again.
      if (!finished) {
        started.delete(hash)
        extractor.dispose()
      }
    }
  }, [enabled, bytes, hash, title])

  return state
}

/** For tests: forget which books were imported in this "session". */
export function resetAutoImportsForTests() {
  started.clear()
}
