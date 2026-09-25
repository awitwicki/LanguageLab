import { useCallback, useEffect, useState } from 'react'
import { api, type BatchCandidate, type BatchPreview } from '../api/client'

/** How many candidates to fetch at once — the segmented control's maximum. Changing the batch size is just a slice, no request. */
export const PREVIEW_TAKE = 20

export interface Row {
  candidate: BatchCandidate
  /** Struck during this visit ("know"): stays in place until "Bring back". */
  struck: boolean
}

/**
 * Reconciles the previous rows with a fresh preview: struck ones stay in place (the server no longer
 * returns them), active ones are only those still in the top batchSize, replacements append at the bottom. Pure, tested on its own.
 */
export function reconcileRows(
  prev: Row[],
  fresh: BatchCandidate[],
  batchSize: number,
  crossedOut: ReadonlySet<number>,
): Row[] {
  const active = fresh.slice(0, batchSize)
  const activeIds = new Set(active.map((c) => c.wordPairId))

  const kept = prev
    .filter((r) => crossedOut.has(r.candidate.wordPairId) || activeIds.has(r.candidate.wordPairId))
    .map((r) => ({ candidate: r.candidate, struck: crossedOut.has(r.candidate.wordPairId) }))
  const present = new Set(kept.map((r) => r.candidate.wordPairId))

  return [...kept, ...active.filter((c) => !present.has(c.wordPairId)).map((c) => ({ candidate: c, struck: false }))]
}

interface Options {
  dictionaryId: number
  chapterIds: number[] | null
  initialBatchSize: number
}

export function useBatchPreview({ dictionaryId, chapterIds, initialBatchSize }: Options) {
  const [preview, setPreview] = useState<BatchPreview | null>(null)
  const [rows, setRows] = useState<Row[]>([])
  const [batchSize, setBatchSizeState] = useState(initialBatchSize)
  const [crossedOut, setCrossedOut] = useState<ReadonlySet<number>>(() => new Set())
  const [pendingId, setPendingId] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(
    async (crossed: ReadonlySet<number>, size: number) => {
      const next = await api.previewBatch(dictionaryId, chapterIds, PREVIEW_TAKE)
      setPreview(next)
      setRows((prev) => reconcileRows(prev, next.candidates, size, crossed))
    },
    [dictionaryId, chapterIds],
  )

  // The scope only changes with the route (the screen remounts), so the state is not reset —
  // and setState here happens only in .then, never synchronously (see lint react/set-state-in-effect).
  useEffect(() => {
    let cancelled = false

    api
      .previewBatch(dictionaryId, chapterIds, PREVIEW_TAKE)
      .then((next) => {
        if (cancelled) {
          return
        }

        setPreview(next)
        setRows((prev) => reconcileRows(prev, next.candidates, initialBatchSize, new Set()))
      })
      .catch((e) => {
        if (!cancelled) {
          setError(String(e))
        }
      })

    return () => {
      cancelled = true
    }
  }, [dictionaryId, chapterIds, initialBatchSize])

  const setBatchSize = useCallback(
    (size: number) => {
      setBatchSizeState(size)
      setRows((prev) => (preview ? reconcileRows(prev, preview.candidates, size, crossedOut) : prev))
    },
    [preview, crossedOut],
  )

  const toggle = useCallback(
    async (id: number, status: 'known' | 'unknown') => {
      if (pendingId !== null) {
        return
      }

      setPendingId(id)
      setError(null)

      try {
        await api.mark(id, status)

        const next = new Set(crossedOut)

        if (status === 'known') {
          next.add(id)
        } else {
          next.delete(id)
        }

        setCrossedOut(next)
        // The row changes right after the server confirms, before the preview is refetched.
        setRows((prev) => prev.map((r) => (r.candidate.wordPairId === id ? { ...r, struck: status === 'known' } : r)))
        await reload(next, batchSize)
      } catch (e) {
        setError(String(e))
      } finally {
        setPendingId(null)
      }
    },
    [pendingId, crossedOut, batchSize, reload],
  )

  const crossOut = useCallback((id: number) => toggle(id, 'known'), [toggle])
  const bringBack = useCallback((id: number) => toggle(id, 'unknown'), [toggle])

  const batchIds = rows.filter((r) => !r.struck).map((r) => r.candidate.wordPairId)

  return { preview, rows, batchIds, batchSize, setBatchSize, crossOut, bringBack, pendingId, error }
}
