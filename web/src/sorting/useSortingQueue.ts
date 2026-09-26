import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type QueueWord, type RecentWord, type SortStatus } from '../api/client'

const BUFFER_SIZE = 50
const REFILL_AT = 20
const COLUMN_SIZE = 10

interface Options {
  dictionaryId: number
  chapterIds: number[] | null
}

export function useSortingQueue({ dictionaryId, chapterIds }: Options) {
  // The chapter the server records as the visit behind this screen, so the home screen can
  // offer a way back. Several chapters at once have no single scope to return to, so they
  // count as the whole book — the same rule the server applies to a training session.
  const scopeChapterId = chapterIds?.length === 1 ? chapterIds[0] : null

  const [buffer, setBuffer] = useState<QueueWord[]>([])
  const [known, setKnown] = useState<RecentWord[]>([])
  const [unknown, setUnknown] = useState<RecentWord[]>([])
  const [total, setTotal] = useState(0)
  const [sorted, setSorted] = useState(0)
  const [error, setError] = useState<string | null>(null)
  // Until the first queue arrives the screen must not say "all sorted": total=0 is not yet empty.
  const [loaded, setLoaded] = useState(false)

  // One active submission at a time. Not over-caution: undo on the server removes
  // "the most recent mark", and if a mark is still in flight, undo removes the wrong word.
  const chain = useRef<Promise<unknown>>(Promise.resolve())

  // Words whose mark is applied optimistically but not yet confirmed by the server.
  // Without this a refill (it comes with the server's response) would roll such words
  // back into the buffer and the counter — they are not counted in that response yet.
  const pendingIds = useRef<Set<number>>(new Set())

  const enqueue = useCallback(<T,>(work: () => Promise<T>): Promise<T> => {
    const next = chain.current.then(work, work)
    chain.current = next.catch(() => undefined)
    return next
  }, [])

  const refill = useCallback(async () => {
    const queue = await api.getQueue(dictionaryId, chapterIds, BUFFER_SIZE)

    // Words "in flight" the server still considers unsorted and returns again —
    // the local state is fresher here, so drop them from the response and add
    // their marks back into the counter so the progress does not jump backwards.
    setBuffer(queue.words.filter((w) => !pendingIds.current.has(w.wordPairId)))
    setTotal(queue.total)
    setSorted(queue.sorted + pendingIds.current.size)
  }, [dictionaryId, chapterIds])

  useEffect(() => {
    setError(null)
    Promise.all([refill(), api.getRecent(COLUMN_SIZE)])
      .then(([, recent]) => {
        setKnown(recent.known)
        setUnknown(recent.unknown)
      })
      .catch((e) => setError(String(e)))
      .finally(() => setLoaded(true))
  }, [refill])

  const mark = useCallback(
    (status: SortStatus) => {
      const word = buffer[0]

      if (!word) {
        return
      }

      // Optimistic: the card changes now, the request flies in the background.
      pendingIds.current.add(word.wordPairId)
      setBuffer((current) => current.slice(1))
      setSorted((current) => current + 1)

      if (status === 'known') {
        setKnown((current) => [{ wordPairId: word.wordPairId, word: word.word }, ...current].slice(0, COLUMN_SIZE))
      } else if (status === 'unknown') {
        setUnknown((current) => [{ wordPairId: word.wordPairId, word: word.word }, ...current].slice(0, COLUMN_SIZE))
      }

      enqueue(async () => {
        try {
          await api.mark(word.wordPairId, status, { dictionaryId, chapterId: scopeChapterId })
        } finally {
          // Clear "in flight" right here, before the refill: once the server answered,
          // the word is already counted in its queue.sorted, and counting it a second
          // time (as pending) would inflate the progress.
          pendingIds.current.delete(word.wordPairId)
        }

        if (buffer.length - 1 <= REFILL_AT) {
          await refill()
        }
      }).catch((e) => {
        // Roll back only this word, not a snapshot of the whole state: later marks
        // have already applied their optimistic state, their requests are still in
        // flight, and a full rollback would wipe them along with this failure.
        setBuffer((current) => [word, ...current])
        setSorted((current) => Math.max(0, current - 1))

        if (status === 'known') {
          setKnown((current) => current.filter((w) => w.wordPairId !== word.wordPairId))
        } else if (status === 'unknown') {
          setUnknown((current) => current.filter((w) => w.wordPairId !== word.wordPairId))
        }

        setError(`Could not save: ${e}. Reload the page.`)
      })
    },
    [buffer, dictionaryId, enqueue, refill, scopeChapterId],
  )

  const undo = useCallback(() => {
    enqueue(async () => {
      const undone = await api.undo()

      if (!undone) {
        return
      }

      // The returned word becomes the current card — so it is always visible what
      // exactly was rolled back, even if it is a mark from a previous session.
      setBuffer((current) => [
        { wordPairId: undone.wordPairId, word: undone.word, translation: undone.translation, frequency: 0 },
        ...current,
      ])
      setSorted((current) => Math.max(0, current - 1))
      setKnown((current) => current.filter((w) => w.wordPairId !== undone.wordPairId))
      setUnknown((current) => current.filter((w) => w.wordPairId !== undone.wordPairId))
    }).catch((e) => setError(String(e)))
  }, [enqueue])

  return { current: buffer[0] ?? null, known, unknown, total, sorted, loaded, error, mark, undo }
}
