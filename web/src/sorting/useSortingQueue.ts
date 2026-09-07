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
  const [buffer, setBuffer] = useState<QueueWord[]>([])
  const [known, setKnown] = useState<RecentWord[]>([])
  const [unknown, setUnknown] = useState<RecentWord[]>([])
  const [total, setTotal] = useState(0)
  const [sorted, setSorted] = useState(0)
  const [error, setError] = useState<string | null>(null)
  // Поки перша черга не приїхала, екран не має казати «усе посортовано»: total=0 — це ще не порожньо.
  const [loaded, setLoaded] = useState(false)

  // Одна активна відправка за раз. Це не перестраховка: undo на сервері знімає
  // «найсвіжішу позначку», і якщо mark ще летить — undo зніме не те слово.
  const chain = useRef<Promise<unknown>>(Promise.resolve())

  // Слова, чия позначка вже накладена оптимістично, але сервер її ще не підтвердив.
  // Без цього refill (він приходить із серверною відповіддю) відкочував би такі
  // слова назад у буфер і в лічильник — вони ж у тій відповіді ще не враховані.
  const pendingIds = useRef<Set<number>>(new Set())

  const enqueue = useCallback(<T,>(work: () => Promise<T>): Promise<T> => {
    const next = chain.current.then(work, work)
    chain.current = next.catch(() => undefined)
    return next
  }, [])

  const refill = useCallback(async () => {
    const queue = await api.getQueue(dictionaryId, chapterIds, BUFFER_SIZE)

    // Слова «в польоті» сервер ще вважає непосортованими й віддає їх знову —
    // локальний стан тут свіжіший, тож викидаємо їх із відповіді, а їхні
    // позначки додаємо назад у лічильник, щоб прогрес не стрибав назад.
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

      // Оптимістично: картка міняється зараз, запит летить у фоні.
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
          await api.mark(word.wordPairId, status)
        } finally {
          // Знімаємо «в польоті» саме тут, до refill: щойно сервер відповів,
          // слово вже враховане в його queue.sorted, і рахувати його вдруге
          // (як pending) означало б завищити прогрес.
          pendingIds.current.delete(word.wordPairId)
        }

        if (buffer.length - 1 <= REFILL_AT) {
          await refill()
        }
      }).catch((e) => {
        // Відкочуємо тільки це слово, а не знімок усього стану: наступні позначки
        // вже наклали свій оптимістичний стан, їхні запити ще летять, і повний
        // відкат стер би їх разом із цією невдачею.
        setBuffer((current) => [word, ...current])
        setSorted((current) => Math.max(0, current - 1))

        if (status === 'known') {
          setKnown((current) => current.filter((w) => w.wordPairId !== word.wordPairId))
        } else if (status === 'unknown') {
          setUnknown((current) => current.filter((w) => w.wordPairId !== word.wordPairId))
        }

        setError(`Не збереглося: ${e}. Онови сторінку.`)
      })
    },
    [buffer, enqueue, refill],
  )

  const undo = useCallback(() => {
    enqueue(async () => {
      const undone = await api.undo()

      if (!undone) {
        return
      }

      // Повернуте слово стає поточною карткою — так завжди видно, що саме
      // відкотилось, навіть якщо це позначка з попередньої сесії.
      setBuffer((current) => [
        { wordPairId: undone.wordPairId, word: undone.word, frequency: 0 },
        ...current,
      ])
      setSorted((current) => Math.max(0, current - 1))
      setKnown((current) => current.filter((w) => w.wordPairId !== undone.wordPairId))
      setUnknown((current) => current.filter((w) => w.wordPairId !== undone.wordPairId))
    }).catch((e) => setError(String(e)))
  }, [enqueue])

  return { current: buffer[0] ?? null, known, unknown, total, sorted, loaded, error, mark, undo }
}
